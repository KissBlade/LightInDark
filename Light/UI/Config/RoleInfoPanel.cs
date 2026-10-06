using System;
using System.Collections.Generic;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.UI.Window;    // LayerExpansion
using Light.UI.Window;          // MenuTextTemplate2 / VanillaAsset
using TMPro;
using UnityEngine;

namespace Light.UI.Config;

/// <summary>
/// 一级 UI 的**右侧职业信息区** —— 内容跟着鼠标悬停的职业块变。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【布局】依用户 2026-10-06 第二张设计图：
/// <code>
///   职业名      Intro            ← 同一行：职业名大字，Intro 小字在它右边
///   最大数量：1                   ← 标签 + 全角冒号
///   生成概率：100%                ← 值按数值染色（越高越绿、越低越红）
///   其他配置项…                   ← 职业专属配置项，一行一个
/// </code>
///
/// ⚠️ **不要底板、不要边框**，和左边只隔**一条竖线**（用户："把右边那个框去掉，只留文字"）。
///    Nebula 里也没有"线"控件 —— MSDesigner 只有 Split/SplitVertically/SplitHorizontally，
///    资源里只有 White.png / ColorFullBase.png，所以线就是"纯色图拉成细长条"。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【字体：换成 Nebula 同款】（用户："换个字体，用 Nebula 同款的那种字体"）
///
/// 查了 Nebula 的源码，它的文字来源是：
/// <code>
/// CredentialsPatch.cs:17
///   RuntimePrefabs.TextPrefab = Object.Instantiate&lt;TextMeshPro&gt;(__instance.text);
/// // __instance 是 VersionShower —— 克隆的是**版本号那一行**的 TMP
/// Language.cs:19
///   if (font.name == "NotoSansSC-Regular SDF") FontSC = font;   // 简中走这个
/// </code>
///
/// 本工程原来的 <c>MenuTextTemplate</c> 用的是**主界面卡片字体**（从 ModeText 取），
/// 和 Nebula 不是一个；而 <c>MenuTextTemplate2</c> 正好就是
/// <c>NotoSansSC-Regular SDF</c> —— **就是 Nebula 那个**。所以这里改用它。
///
/// ⚠️ 换字体必须**同时换材质**（TMP 字形从字体自己的图集材质取），
///    <c>MenuTextTemplate2.ApplyFont</c> 内部已经处理了，别绕开它自己写 font。
/// </summary>
internal static class RoleInfoPanel
{
    // ---- 位置与尺寸（从用户截图反推，见 RoleListPage 类注释）----

    /// <summary>整个信息区的中心 x。</summary>
    private const float PanelX = 1.68f;

    /// <summary>信息区宽（也是竖线的定位基准）。</summary>
    private const float PanelW = 3.04f;

    /// <summary>信息区高（竖线长度）。</summary>
    private const float PanelH = 3.85f;

    /// <summary>第一行（职业名）的 y。</summary>
    private const float TopY = 1.62f;   // 名字往下移一点（原 1.80）

    /// <summary>文字区宽度（比信息区窄一点，两侧留白）。</summary>
    private const float TextW = PanelW - 0.26f;

    // ---- 字号 ----
    /// <summary>
    /// 职业名字号 —— **想改职业名大小就改这一个数**。
    ///
    /// ⚠️ 原来这里拆成"基号 + &lt;size=200%&gt; 标签"，结果用户把倍率调到 200% 后
    ///    **职业名整个消失了**。原因是 TMP 的 &lt;size&gt; 标签**只放大字形、不放大 rect**，
    ///    字被撑出矩形后撞上 MakeLine 里设的 `overflowMode = Truncate` → 整段被裁掉。
    ///    现在取消标签，直接用字号，一个数搞定，也不会被裁。
    /// </summary>
    private const float FsName = 3.20f;   // 原 5.00（用户反馈还是大）→ 降到 ≈ 正文的 1.8 倍
    private const float FsIntro = 1.70f;     // 职业介绍
    private const float FsLabel = 1.80f;     // 最大数量 / 生成概率
    private const float FsItem = 1.65f;      // 配置项

    // ---- 行距 ----
    private const float AdvName = 0.40f;   // 名字 -> Intro 的间距。减小 = Intro 往上靠（原 0.78）
    private const float AdvIntro = 0.40f;
    private const float AdvLabel = 0.42f;
    private const float AdvItem = 0.38f;

    /// <summary>Intro 与职业名之间的间隙。</summary>
    /// <summary>介绍相对职业名的右移量（用户：intro 往右边错开，在职业名的右下方）。</summary>
    private const float IntroXOffset = 0.72f;

    /// <summary>竖线：宽 + 颜色（淡灰，不抢文字）。</summary>
    private const float DividerW = 0.018f;
    private static readonly Color DividerColor = new(0.78f, 0.78f, 0.80f, 0.30f);

    /// <summary>介绍文字色 —— Nebula 用 Normal 白字，不抢职业名。</summary>
    private static readonly Color IntroColor = new(0.86f, 0.86f, 0.86f, 1f);
    private static readonly Color LabelColor = new(0.92f, 0.92f, 0.92f, 1f);
    private static readonly Color ItemColor = new(0.78f, 0.78f, 0.78f, 1f);

    private static GameObject? _root;
    private static TextMeshPro? _name;
    private static TextMeshPro? _intro;
    private static TextMeshPro? _maxLine;
    private static TextMeshPro? _chanceLine;
    private static readonly List<TextMeshPro> _itemLines = new();

    /// <summary>立绘渲染器（半透明，在文字后面）。</summary>
    private static SpriteRenderer? _art;

    /// <summary>字体诊断只打一次。</summary>
    private static bool _loggedFont;

    // ---- 立绘 ----
    private const float ArtW = 2.55f;
    private const float ArtH = 2.55f;
    private const float ArtY = -0.55f;

    /// <summary>立绘透明度 —— "半透明立绘"（用户设计图原话）。</summary>
    private static readonly Color ArtColor = new(1f, 1f, 1f, 0.20f);   // Nebula Help.cs:492 SetBackImage(..., 0.2f)

    /// <summary>
    /// 立绘槽位：在文字**后面**（z 更大 = 同容器内更靠后）铺一张半透明大图。
    ///
    /// ⚠️ 贴图来源：<c>RoleTemplate.IconImage</c> —— 这是工程里**唯一**的职业图钩子，
    ///    但它本来是按钮用的小图标，拉到 2.55 会糊。
    ///    用户截图里 Nebula 的立绘是专门的illustration，那个资源 Nebula 的公开源码里没有
    ///    （我把 AddRolesTopic / AddRoleInfo / GenerateIndependentDialog 全读过，只有小图标），
    ///    所以这里先留槽位，等有专门的立绘图再换 <see cref="SetArt"/> 的取图来源。
    /// </summary>
    private static void AddArtSlot(Transform parent)
    {
        try
        {
            var go = NewUIObject("Art", parent, new Vector3(0f, ArtY, 0.5f));
            _art = go.AddComponent<SpriteRenderer>();
            _art.drawMode = SpriteDrawMode.Sliced;
            _art.size = new Vector2(ArtW, ArtH);
            _art.color = ArtColor;
            go.SetActive(false);          // 没图就不显示
        }
        catch (Exception ex)
        {
            LightLogger.LogDebug($"[RoleInfoPanel.AddArtSlot] {ex.Message}");
        }
    }

    /// <summary>换立绘（没有就隐藏）。</summary>
    private static void SetArt(LightInDark.Roles.RoleTemplate? role)
    {
        try
        {
            if (_art == null) return;
            var sprite = role?.RoleImage;   // ★ 职业立绘（不是按钮那个小图标）
            if (sprite == null) { _art.gameObject.SetActive(false); return; }
            _art.sprite = sprite;
            _art.gameObject.SetActive(true);
        }
        catch (Exception ex)
        {
            LightLogger.LogDebug($"[RoleInfoPanel.SetArt] {ex.Message}");
        }
    }

    public static bool Built => _root != null;

    /// <summary>建信息区。默认先显示列表里的**第一个**职业，避免一开始空着。</summary>
    public static void Show(Transform parent, List<ConfigBlock> blocks)
    {
        Clear();

        try
        {
            _root = NewUIObject("LightRoleInfoPanel", parent, new Vector3(PanelX, -0.50f, 0.5f));

            // 一条竖线 —— 取代原来的底板 + 边框
            AddDivider(_root.transform, -PanelW * 0.5f, PanelH);

            // ---- 立绘槽位（在文字**后面**，z 更大）----
            //  用户设计图里那个红框就是这块："那里是放立绘的，半透明立绘"。
            //  ⚠️ 立绘贴图目前只能取 RoleTemplate.IconImage（工程里唯一的职业图钩子），
            //     它本来是**小图标**，放大会糊。等有了专门的立绘图再换。
            AddArtSlot(_root.transform);

            // ---- 文字行（自上而下）----
            //
            //  ★ 结构照搬 Nebula 的 AddRoleInfo（MetaDialog.cs:256）：
            //      MSString(size.x, cs(color, 职业名), TopLeft, **Bold**)      ← 名独占一行
            //      MSMultiString(size.x, 1.2f, 职业介绍, TopLeft, **Normal**)  ← 介绍另起一行，可换行
            //    原来是"名字 + Intro 同一行"，那是用户手绘稿的排法，**和 Nebula 不一样**，
            //    也正是"字号看着不对"的来源之一（两个字号挤一行，视觉上分不出主次）。
            float y = TopY;

            _name = MakeLine("Name", ref y, FsName, MenuTextTemplate.GlowWhite, AdvName, bold: true);
            // 名字独占一行，不换行（折行了下面的介绍会被挤走）
            //
            // ⚠️⚠️ `overflowMode` **必须是 Overflow**。
            //    2026-10-06 事故：用户把字号调大后**职业名整个消失**。
            //    原因是 MakeLine 里统一设了 `overflowMode = Truncate` ——
            //    字号一大，字形撑出那个固定高度的 rect，Truncate 就把整段裁掉了。
            //    职业名会跟着字号变，所以它这一行**不能裁**。
            if (_name != null)
            {
                _name.enableWordWrapping = false;
                _name.overflowMode = TextOverflowModes.Overflow;
            }

            // 职业介绍：另起一行，Normal（不粗），可换行 —— Nebula 的 MSMultiString
            _intro = MakeLine("Intro", ref y, FsIntro, IntroColor, AdvIntro, xOffset: IntroXOffset);   // 往右错开到职业名右下方

            // 数量 / 概率
            _maxLine = MakeLine("Max", ref y, FsLabel, LabelColor, AdvLabel, bold: true);
            _chanceLine = MakeLine("Chance", ref y, FsLabel, LabelColor, AdvLabel, bold: true);

            y -= 0.06f;
            for (int i = 0; i < 8; i++)
                _itemLines.Add(MakeLine($"Item{i}", ref y, FsItem, ItemColor, AdvItem));

            if (blocks != null && blocks.Count > 0)
            {
                var first = blocks[0];
                ShowRole(RoleListPage.FindRole(first), first);
            }
            else
            {
                SetEmpty();
            }

            LightLogger.LogDebug($"[RoleInfoPanel] 已建（中心 x={PanelX}，{PanelW}×{PanelH}，字体=NotoSansSC）");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[RoleInfoPanel.Show]", ex);
        }
    }

    /// <summary>切到某个职业（鼠标悬停时调用）。<paramref name="role"/> 可以为 null（走回退）。</summary>
    public static void ShowRole(LightInDark.Roles.RoleTemplate? role, ConfigBlock block)
    {
        if (_root == null) return;

        try
        {
            // ---- 立绘（半透明，在文字后面）----
            SetArt(role);

            // ---- 职业名（大字，职业色）----
            if (_name != null)
            {
                // ⚠️ **不套 <size> 标签**（原因见 FsName 的注释）：
                //    标签只放大字形、不放大 rect，撑出去会被裁掉 —— 那正是"改名号后职业名消失"的原因。
                //    现在 FsName 就是最终字号，一个数搞定。
                _name.text = role?.Name ?? block?.DisplayName ?? "?";
                if (role != null)
                    // ★ 用**职业自己的颜色**，不是阵营色（用户 2026-10-06：
                    //   "这个按钮上显示的职业名和右侧显示的职业名，我要这个职业的颜色，
                    //     而不是阵营的颜色！召集者的颜色明明是黄"）。
                    //   ResolveBlockHighlight() 对船员一律返回 #8CFFFF（青），所以召集者显示成青的 —— 不对。
                    _name.color = LightInDark.ColorHelper.ToUnityColor(role.Color);
                _name.ForceMeshUpdate();
            }

            // ---- Intro（小字，挪到职业名右边同一行）----
            if (_intro != null)
            {
                string intro = role?.IntroText ?? "";
                bool has = !string.IsNullOrEmpty(intro);
                _intro.gameObject.SetActive(has);
                if (has)
                {
                    _intro.text = intro;
                    _intro.ForceMeshUpdate();
                    // 介绍**另起一行**，不再贴到名字右边（Nebula 的 AddRoleInfo 就是两行）
                }
            }

            // ---- 最大数量 / 生成概率 ----
            int max = 0, chance = 0;
            if (role != null)
            {
                try { max = Light.Roles.Assignment.StandardRoleAllocator.GetMaxCount(role); } catch { }
                try { chance = Light.Roles.Assignment.StandardRoleAllocator.GetChance(role); } catch { }
            }

            if (_maxLine != null) _maxLine.text = $"最大数量：{max}";

            if (_chanceLine != null)
            {
                // 概率染色：越高越绿、越低越红
                string hex = ColorUtility.ToHtmlStringRGB(ChanceColor(chance));
                _chanceLine.text = $"生成概率：<color=#{hex}>{chance}%</color>";
            }

            // ---- 配置项（一行一个）----
            int used = 0;
            if (block != null)
            {
                foreach (var item in block.Items)
                {
                    if (used >= _itemLines.Count) break;

                    // ⚠️ 跳过"数量 / 概率"这两项（用户 2026-10-06 要求）——
                    //    它们已经在上面用 最大数量 / 生成概率 显示过了，再列一遍是重复。
                    //    判定用配置键而不是显示文字：键是 RoleConfigRegistrar 注册时定死的
                    //    ("role.<CodeName>.count" / ".chance")，改显示名也不会漏。
                    if (IsCountOrChanceKey(item.Key)) continue;
                    var line = _itemLines[used];
                    if (line == null) continue;
                    line.text = "· " + (item.DisplayName ?? item.Key);
                    line.gameObject.SetActive(true);
                    used++;
                }
            }
            for (int i = used; i < _itemLines.Count; i++)
                if (_itemLines[i] != null) _itemLines[i].gameObject.SetActive(false);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[RoleInfoPanel.ShowRole] {ex.Message}");
        }
    }

    private static void SetEmpty()
    {
        if (_name != null) _name.text = "";
        if (_intro != null) _intro.gameObject.SetActive(false);
        if (_maxLine != null) _maxLine.text = "";
        if (_chanceLine != null) _chanceLine.text = "";
        foreach (var l in _itemLines) if (l != null) l.gameObject.SetActive(false);
    }

    public static void Clear()
    {
        _name = null; _intro = null; _maxLine = null; _chanceLine = null;
        _art = null;
        _itemLines.Clear();

        if (_root != null)
        {
            UnityEngine.Object.Destroy(_root);
            _root = null;
        }
    }

    // =====================================================================
    //  绘制辅助
    // =====================================================================

    /// <summary>
    /// 按当前 y 建一行左对齐文字，然后把 y 往下推。
    /// ⚠️ 用 <c>MenuTextTemplate2</c>（NotoSansSC = Nebula 同款字体），
    ///    **不是** <c>MenuTextTemplate</c>（那个是主界面卡片字体）。
    /// </summary>
    /// <param name="bold">
    /// 是否粗体。**默认 true** —— 用户 2026-10-06 定下的规矩：
    /// "之后我除了特殊说明所有字都 Bold"（已记入 AGENTS.md）。
    /// </param>
    private static TextMeshPro? MakeLine(string name, ref float y, float fontSize, Color color,
        float advance, bool bold = true, float xOffset = 0f)
    {
        var tmp = MenuTextTemplate2.Create(_root!.transform, new Vector3(xOffset, y, -0.1f), "", fontSize, color);
        y -= advance;
        if (tmp == null) return null;

        tmp.name = "Info_" + name;

        // 模板默认"居中 + 不换行 + 预制体自带 rect"，这里改成左对齐定宽
        try
        {
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.rectTransform.sizeDelta = new Vector2(TextW, tmp.rectTransform.sizeDelta.y);
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Overflow;   // 字号要大，绝不能裁（见职业名那段注释）

            // ⚠️⚠️⚠️ **必须关掉 autoSizing，否则 fontSize 完全不生效。**
            //
            //  2026-10-06 查到的大坑：用户连着好几轮说"改字号没反应"，
            //  根因就是 <see cref="MenuTextTemplate2.Create"/> 只写了 `tmp.fontSize = fontSize`
            //  —— 而模板预制体上 `enableAutoSizing` 是 **true**。
            //  TMP 在 autoSizing 开启时会**完全无视 fontSize**，改为按 rect 大小自己缩放；
            //  那个 rect 是固定的 → 字号永远一个样，改常量毫无效果。
            //
            //  表现极具迷惑性：**改"位置/行距"这些常量都正常生效，唯独字号纹丝不动** ——
            //  因为行距是我们自己算的，字号是交给 TMP 的。
            tmp.enableAutoSizing = false;
            tmp.fontSize = fontSize;
            tmp.fontSizeMin = fontSize;
            tmp.fontSizeMax = fontSize;

            // 粗体（默认开：用户要求"除了特殊说明所有字都 Bold"）
            tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;

            // 字体诊断：只打第一条，确认字体 + 字号 + autoSizing 的**实际值**
            if (!_loggedFont)
            {
                _loggedFont = true;
                string fn = "?";
                try { fn = tmp.font != null ? tmp.font.name : "null"; } catch { }
                LightLogger.Log($"[RoleInfoPanel] 文本字体 = {fn}（期望 NotoSansSC-Regular SDF），" +
                                $"字号={tmp.fontSize}，autoSizing={tmp.enableAutoSizing}（必须是 False），粗体={bold}");
            }
        }
        catch { }

        return tmp;
    }

    /// <summary>
    /// 是不是"数量 / 概率"这两项自带配置。
    /// 键由 <c>RoleConfigRegistrar</c> 注册时确定：<c>role.&lt;CodeName&gt;.count</c> / <c>.chance</c>。
    /// </summary>
    private static bool IsCountOrChanceKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        return key.EndsWith(".count", StringComparison.Ordinal)
            || key.EndsWith(".chance", StringComparison.Ordinal);
    }

    /// <summary>一条**竖线**（分割"职业按钮区"和"职业信息区"）。做法同 Nebula：纯色图拉成细长条。</summary>
    private static void AddDivider(Transform parent, float x, float height)
    {
        try
        {
            var go = NewUIObject("Divider", parent, new Vector3(x, 0f, 0.01f));
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Light.UI.Window.VanillaAsset.FullScreenSprite;
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = new Vector2(DividerW, height);
            sr.color = DividerColor;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[RoleInfoPanel.AddDivider] {ex.Message}");
        }
    }

    /// <summary>概率配色：0% 红 → 100% 绿。</summary>
    private static Color ChanceColor(int percent)
    {
        float t = Mathf.Clamp01(percent / 100f);
        return new Color(
            Mathf.Lerp(0.95f, 0.25f, t),   // R：红 → 暗
            Mathf.Lerp(0.28f, 0.95f, t),   // G：暗 → 绿
            Mathf.Lerp(0.28f, 0.35f, t),
            1f);
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