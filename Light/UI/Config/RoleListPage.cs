using System;
using System.Collections.Generic;
using Light.Config;
using Light.UI.HudUI;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.UI.Window;
using Light.UI.Window;          // MenuTextTemplate2（NotoSansSC = Nebula 同款字体）
using TMPro;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace Light.UI.Config;

/// <summary>
/// 职业按钮列表页：把某分类下的所有职业块渲染成按钮，点击进入该职业的详情窗口
/// （由回调处理，见 <c>GameSettingMenuPatch.OnRoleSelected</c>）。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【2026-10-06 第三版：改用 HudUI 的按钮】
///
/// 前两版都是自己画（第一版圆角实心块、第二版四条带子拼方框/9 宫格描边），
/// 用户看过后说「依旧没到我想要的效果……这样，你就用 HudUI，染个色得了」。
/// 所以现在**直接复用 <see cref="HudUIButton.CreateColorButton"/>**：
///   · 它是工程里已有的自绘按钮（<c>GUI/ButtonNormal.png</c> 等贴图 + SortingGroup +
///     PassiveButton + TMP），悬浮/选中会自动换图、还会放 UI_Hover / UI_Select 音效；
///   · "染色"由 <c>color</c> 参数完成（内部 <c>Renderer.color</c>，悬浮时 ×1.3 提亮）；
///   · **不要再自己画框** —— 那正是前两版失败的地方。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【坐标是怎么定下来的】
///
/// 不是拍脑袋，是从用户截图反推的：
///   · 截图 1282px 宽、游戏窗口 36..1246（1210px）
///   · 当时按钮 BtnWidth=1.75 在屏幕上约 212px → **1 个本地单位 ≈ 121px**
///   · 两个按钮中心在屏幕 638 / 870 → 中点 754 → **本地 x=0 落在屏幕 754**
///     （注意：**不是窗口中心 641**，`_modPage` 的原点本身偏右）
/// 再按设计稿（1536px 宽、窗口 130..1340）量出的比例换算：
///   · 职业按钮区 = 窗口 34%..63% → 本地 **-2.54 .. +0.36**
///   · 右侧信息区 = 窗口 64%..95% → 本地 **+0.46 .. +3.56**
///
/// ⚠️ 这些都是**从截图目测反推的估算值**，用户确认后可能还要微调。
///    好在全部集中在下面的常量里，好改。
/// </summary>
internal static class RoleListPage
{
    // ---- 按钮排版 ----

    /// <summary>按钮宽。设计稿里两个按钮 + 间隙才占 2.90 个本地单位。</summary>
    private const float BtnWidth = 1.65f;

    /// <summary>按钮高。用户要求"整扁点"。</summary>
    private const float BtnHeight = 0.46f;   // 用户反馈按钮太扁 -> 加高（Nebula 原值 0.36，配粗体字太挤）

    /// <summary>列间距 = 宽 + 间隙（设计稿的间隙/宽 ≈ 0.14）。</summary>
    private const float SpacingX = 1.70f;

    private const float SpacingY = 0.60f;
    private const float StartY = 0.90f;   // ★ 用户要求：按钮放回原位置（不跟信息区上移）
    private const int Columns = 2;

    /// <summary>按钮组中心 x（见类注释的推导）。</summary>
    private const float GroupX = -1.55f;

    private static GameObject? _page;

    /// <summary>当前显示的职业块（供信息面板判定"是不是同一批"）。</summary>
    private static readonly List<ConfigBlock> _blocks = new();

    public static bool Built => _page != null;

    /// <summary>显示职业按钮列表（覆盖式：先清掉旧列表）。</summary>
    public static void Show(List<ConfigBlock> blocks, Transform parent, Action<ConfigBlock> onSelected)
    {
        Clear();
        if (blocks == null || blocks.Count == 0) return;

        try
        {
            _page = NewUIObject("LightRoleListPage", parent, new Vector3(0f, 0f, -2.5f));

            _blocks.Clear();
            _blocks.AddRange(blocks);

            for (int i = 0; i < blocks.Count; i++)
            {
                int row = i / Columns;
                int rowStart = row * Columns;
                int inRow = Math.Min(Columns, blocks.Count - rowStart);   // 末行不满也居中
                int col = i % Columns;

                float x = GroupX + (col - (inRow - 1) * 0.5f) * SpacingX;
                float y = StartY - row * SpacingY;
                var block = blocks[i];
                CreateRoleButton(_page.transform, block, new Vector2(x, y),
                    () => onSelected?.Invoke(block));
            }

            // 右侧信息面板（跟着鼠标悬停变）
            RoleInfoPanel.Show(_page.transform, blocks);

            LightLogger.Log($"[RoleListPage] 已铺 {blocks.Count} 个职业按钮" +
                            $"（按钮 {BtnWidth}×{BtnHeight}，组中心 x={GroupX}）");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[RoleListPage.Show]", ex);
        }
    }

    public static void Clear()
    {
        try { RoleInfoPanel.Clear(); } catch { }
        _blocks.Clear();

        if (_page != null)
        {
            UnityEngine.Object.Destroy(_page);
            _page = null;
        }
    }

    /// <summary>
    /// 一个职业按钮 —— 用 **HudUI 的标准按钮**（<see cref="HudUIButton.Create"/>），
    /// 底色染成压暗的职业色、文字用职业亮色、悬停时切到"选中态"贴图。
    ///
    /// ═══════════════════════════════════════════════════════════════════
    /// 【读 Nebula 源码得出的配方】（用户："你去看看代码层面"）
    ///
    /// Nebula 画按钮的唯一入口是
    /// <c>MetaScreen.MSDesigner.SetUpButton(obj, size, display, color)</c>（MetaScreen.cs:376）：
    /// <code>
    /// Color normalColor = (color == null) ? Color.white : color.Value;
    /// renderer.sprite   = MetaScreen.GetButtonBackSprite();   // 游戏内 buttonClick 贴图
    /// renderer.drawMode = SpriteDrawMode.Tiled;               // 注意是 Tiled
    /// renderer.color    = normalColor;
    /// text.alignment    = Center;  enableAutoSizing;  fontSizeMax=2, Min=1
    /// text.rectTransform.sizeDelta = size - (0.15, 0.12);
    /// OnMouseOver → renderer.color = new Color(0f, 1f, 42f/255f);   // ★ #00FF2A 亮绿
    /// OnMouseOut  → renderer.color = normalColor;
    /// </code>
    ///
    /// **两个关键结论（我前两版都做错了）：**
    ///  ① 职业按钮**不传 color**（只有筛选按钮传 Color.yellow/gray）→
    ///     <c>normalColor = Color.white</c> → **底板保持贴图原样（深色），只有文字是职业色**。
    ///     我原来把底板染成 <c>职业色 × 0.42</c>，结果糊成一片、和 Nebula 差很远。
    ///  ② 悬浮高光是**写死的亮绿 <c>#00FF2A</c>**，不是"提亮职业色"，也不是换贴图。
    ///     设计稿截图里那个绿框选中效果就是它。
    ///
    /// ⚠️ 另外别再用 <c>CreateColorButton</c> —— 那是**颜色选择器**的胶囊贴图
    ///    （<c>ColorButton.png</c>），和设置界面的标准按钮不是一套。
    /// ═══════════════════════════════════════════════════════════════════
    /// </summary>
    private static void CreateRoleButton(Transform parent, ConfigBlock block, Vector2 pos, Action onClick)
    {
        try
        {
            var role = FindRole(block);

            // 文字色 = 职业高光色（阵营色；**中立取职业自己的颜色**）
            // ★ 用**职业自己的颜色**，不是阵营色（用户 2026-10-06：
            //   "这个按钮上显示的职业名和右侧显示的职业名，我要这个职业的颜色，
            //     而不是阵营的颜色！召集者的颜色明明是黄"）。
            //   ⚠️ 别再用 ResolveBlockHighlight() —— 它按阵营给色，
            //      船员一律 #8CFFFF（青），所以召集者会显示成青的，和它本身的黄色对不上。
            //      ResolveBlockHighlight 仍然保留，但**只用于"高光/选中"语义**（那里按阵营是对的）。
            Color textColor;
            if (role != null)
                textColor = LightInDark.ColorHelper.ToUnityColor(role.Color);
            else
                textColor = block.HeaderColor ?? new Color(0.85f, 0.85f, 0.85f, 1f);

            // =============================================================
            //  ★★★ 这里开始是 Nebula `MSDesigner.SetUpButton` 的**逐行移植 ★★★
            //      （MetaScreen.cs:376）。用户："你就按 Nebula 那种，复制粘贴行不。"
            //      每一行都对着原方法抄，只加了 try/catch 和 null 判断。
            // =============================================================

            var obj = NewUIObject($"LightRoleBtn_{block.Key}", parent, new Vector3(pos.x, pos.y, 0f));
            var size = new Vector2(BtnWidth, BtnHeight);

            // Color normalColor = (color == null) ? Color.white : color.Value;
            //   ⚠️ 职业按钮**不传 color** → 底板保持贴图原样
            Color normalColor = Color.white;

            obj.layer = LayerExpansion.GetUILayer();
            obj.transform.localScale = Vector3.one;

            var renderer = obj.AddComponent<SpriteRenderer>();
            var collider = obj.AddComponent<BoxCollider2D>();

            // 建立文字（Nebula 用 RuntimePrefabs.TextPrefab = VersionShower.text 的克隆；
            // 我们用工程自己的简中模板，字体 NotoSansSC = Nebula 同款）
            var text = MenuTextTemplate2.Create(obj.transform, new Vector3(0f, 0f, -1f),
                block.DisplayName, 2f, textColor);

            // renderer.sprite = MetaScreen.GetButtonBackSprite();
            // renderer.drawMode = SpriteDrawMode.Tiled;        ← ★ 注意是 **Tiled**，不是 Sliced
            // renderer.size = size;
            // renderer.color = normalColor;
            renderer.sprite = GameButtonSprite;
            renderer.drawMode = SpriteDrawMode.Tiled;
            renderer.size = size;
            renderer.color = normalColor;

            if (text != null)
            {
                // text.alignment = Center;  text.fontSizeMax = 2f;  text.fontSizeMin = 1f;
                // text.m_fontSizeBase = 3f;  text.fontSize = 2f;  text.enableAutoSizing = true;
                text.alignment = TextAlignmentOptions.Center;
                text.fontSizeMax = 2f;
                text.fontSizeMin = 1f;
                text.m_fontSizeBase = 3f;
                text.fontSize = 2f;
                // ⚠️⚠️ **必须关掉 autoSizing**，否则 fontSize 完全不生效（职业名那边踩过同一个坑）。
                //     Nebula 原版这里写的是 `enableAutoSizing = true` —— 它靠 rect 自动缩放，
                //     但我们要"字号可控"，所以显式关掉并锁死 fontSize。
                text.enableAutoSizing = false;
                text.fontSizeMin = 2f;
                text.fontSizeMax = 2f;

                // text.rectTransform.sizeDelta = new Vector2(size.x - 0.15f, size.y - 0.12f);
                // text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                text.rectTransform.sizeDelta = new Vector2(size.x - 0.15f, size.y - 0.12f);
                text.rectTransform.pivot = new Vector2(0.5f, 0.5f);

                // ★ 粗体 —— 用户 2026-10-06 要求（Nebula 的 AddRolesTopic 也是 FontStyles.Bold）
                text.fontStyle = FontStyles.Bold;
            }

            // collider.size = size;
            collider.size = size;
            collider.isTrigger = true;

            // ---- 职业图标（Nebula 没有，保留我们自己的）----
            AddIcon(obj.transform, role);

            // ---- PassiveButton ----
            var button = obj.AddComponent<PassiveButton>();
            button.OnMouseOver = new UnityEngine.Events.UnityEvent();
            button.OnMouseOut = new UnityEngine.Events.UnityEvent();
            button.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();

            var localOnClick = onClick;
            button.OnClick.AddListener((UnityEngine.Events.UnityAction)(() =>
            {
                // Nebula: SoundManager.Instance.PlaySound(MetaDialog.getSelectClip(), false, 0.8f);
                try { PlayUiSound("UI_Select"); } catch { }
                localOnClick?.Invoke();
            }));

            button.OnMouseOver.AddListener((UnityEngine.Events.UnityAction)(() =>
            {
                // ⚠️ Nebula 写死：renderer.color = new Color(0f, 1f, 42f/255f);   // #00FF2A
                renderer.color = HoverGreen;
                try { PlayUiSound("UI_Hover"); } catch { }
                try { RoleInfoPanel.ShowRole(role, block); } catch { }
            }));
            button.OnMouseOut.AddListener((UnityEngine.Events.UnityAction)(() =>
            {
                renderer.color = normalColor;
            }));
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[RoleListPage.CreateRoleButton]", ex);
        }
    }

    /// <summary>
    /// **Nebula <c>MetaScreen.GetButtonBackSprite()</c> 的等价物**：
    /// <code>
    /// if (buttonSprite == null) buttonSprite = Helpers.getSpriteFromAssets("buttonClick");
    /// </code>
    /// 而 <c>Helpers.getSpriteFromAssets(name)</c> 就是"遍历所有已加载 Sprite 按名字找"。
    /// 所以这里做同一件事 —— 捞**游戏内**那张叫 <c>buttonClick</c> 的按钮底图。
    ///
    /// ⚠️ 捞不到时退回 <c>HudUIAssets.ButtonNormal</c>（工程自带的设置界面按钮图），
    ///    不会留空、不会崩。
    /// </summary>
    private static Sprite? _gameButtonSprite;

    private static Sprite GameButtonSprite
    {
        get
        {
            if (_gameButtonSprite != null) return _gameButtonSprite;

            // ① 照抄 Nebula：按名字从游戏资源里捞
            try
            {
                foreach (var s in UnityEngine.Object.FindObjectsOfTypeIncludingAssets(
                             Il2CppInterop.Runtime.Il2CppType.Of<Sprite>()))
                {
                    if (s == null) continue;
                    var sp = s.TryCast<Sprite>();
                    if (sp == null || sp.name != "buttonClick") continue;
                    _gameButtonSprite = sp;
                    LightLogger.Log("[RoleListPage] 已从游戏资源取到按钮底图 buttonClick ✓（Nebula 同款）");
                    break;
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[RoleListPage] 查找 buttonClick 失败: {ex.Message}");
            }

            // ② 退回工程自带按钮图
            if (_gameButtonSprite == null)
            {
                try { _gameButtonSprite = HudUIAssets.ButtonNormal; } catch { }
                LightLogger.LogWarning("[RoleListPage] 没找到 buttonClick，退回 HudUIAssets.ButtonNormal");
            }

            return _gameButtonSprite;
        }
    }

    /// <summary>播 UI 音效（Nebula 用 <c>Helpers.FindSound("UI_Hover"/"UI_Select")</c>）。</summary>
    private static void PlayUiSound(string name)
    {
        try
        {
            var clip = Light.UI.Window.VanillaAsset.FindSoundClip(name);
            if (clip != null) SoundManager.Instance.PlaySound(clip, false, 0.8f);
        }
        catch { }
    }

    /// <summary>悬浮高光色 —— 照搬 Nebula <c>SetUpButton</c> 里的 <c>new Color(0f, 1f, 42f/255f)</c>（#00FF2A）。</summary>
    private static readonly Color HoverGreen = new(0f, 1f, 42f / 255f, 1f);

    /// <summary>在按钮左侧放职业图标（有才放）。</summary>
    private static void AddIcon(Transform btn, LightInDark.Roles.RoleTemplate? role)
    {
        try
        {
            var icon = role?.IconImage;
            if (icon == null) return;

            var go = new GameObject("Icon");
            go.layer = LayerExpansion.GetUILayer();
            go.transform.SetParent(btn, false);

            // 靠左内侧；按钮高 0.30，图标留点边距 → 0.22
            const float iconSize = 0.22f;
            go.transform.localPosition = new Vector3(-BtnWidth * 0.5f + iconSize * 0.5f + 0.10f, 0f, -0.1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = icon;
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = new Vector2(iconSize, iconSize);
            sr.color = Color.white;
        }
        catch (Exception ex)
        {
            LightLogger.LogDebug($"[RoleListPage.AddIcon] {ex.Message}");
        }
    }

    /// <summary>
    /// 从配置块反查职业模板。键形如 <c>lid.role.&lt;CodeName&gt;</c>
    /// （见 <c>RoleConfigRegistrar.Register</c>）。查不到返回 null，调用方走回退配色。
    /// </summary>
    internal static LightInDark.Roles.RoleTemplate? FindRole(ConfigBlock block)
    {
        try
        {
            const string Prefix = "lid.role.";
            string? key = block?.Key;
            if (string.IsNullOrEmpty(key) || !key.StartsWith(Prefix, StringComparison.Ordinal)) return null;

            string codeName = key.Substring(Prefix.Length);
            if (codeName.Length == 0) return null;

            return LightInDark.Roles.RoleRegistry.GetByName(codeName);
        }
        catch (Exception ex)
        {
            LightLogger.LogDebug($"[RoleListPage.FindRole] {ex.Message}");
            return null;
        }
    }

    private static GameObject NewUIObject(string name, Transform parent, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.layer = LayerExpansion.GetUILayer();
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one;
        return go;
    }
}
