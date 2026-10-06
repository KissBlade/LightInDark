using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.UI.Window;             // LayerExpansion（在 LightAPI 里，不是 Light.Utilities）
using Light.UI.HudUI;                    // HudUIWindow / HudUIButton / HudUIAssets
using Object = UnityEngine.Object;

namespace Light.UI.Config;

/// <summary>
/// **预设窗口**（用户设计图：加载预设那个二级窗口）。
///
/// ═══════════════════════════════════════════════════════════════════════
///  布局（用户 2026-10-06 的设计图）：
/// <code>
///   [X]┌─────────────────────────┬──────────────────────┐
///      │ [搜索框：预设名...]      │ [ 加载此预设 ]        │
///      │ ┌─────────────────────┐ │ [ 删除此预设 ]        │
///      │ │ 预设卡片            │ │ ┌──────────────────┐ │
///      │ │ 预设卡片            │◄┼─│ 预设名：XXX       │ │
///      │ │ 预设卡片            │ │ │ 预设作者：XXX     │ │
///      │ └─────────────────────┘ │ │ 创建时间：...     │ │
///      │        （滚轮滚动）      │ │ AU版本 / MOD版本  │ │
///      │                         │ │ 匹配情况（换行）   │ │
///      │                         │ │ 状态：可用(绿)    │ │
///      │                         │ └──────────────────┘ │
///      └─────────────────────────┴──────────────────────┘
/// </code>
///
///  【选中规则】用户原话："用 HudUI 的高光，固定住高光，只能选一个，
///   如果选中状态又点了一个，那么切换选中为那个。"
///   → 单一真值来源 <see cref="_selectedPath"/>，所有卡片视觉由 <see cref="RefreshSelection"/> 统一刷。
///     **不做"每张卡片各自记状态"** —— 单选就该只有一个源头，否则迟早出现"两张都亮"。
///
///  【为什么不用 HudUI.OpenCustomWindow】它内部 `HudUIWindow.Create` 会
///   `parent ??= HudManager.Instance?.transform` 并在拿不到时抛异常 ——
///   而**规则编辑界面根本没有 HudManager**。所以这里显式给 parent（见 <see cref="ResolveParent"/>）。
/// ═══════════════════════════════════════════════════════════════════════
internal static class PresetWindow
{
    // ---------------------------------------------------------------------
    //  尺寸 / 布局常量（嫌不对直接改这里）
    // ---------------------------------------------------------------------

    private const float WinW = 9.40f;
    private const float WinH = 5.40f;

    private const float LeftCx = -2.20f;      // 左列中心（用户 2026-10-06："左边的预设按钮整体往右去一点"）
    private const float ColW = 4.20f;      // 列宽

    private const float RightCx = 2.50f;      // 右列中心
    private const float RightW = 3.60f;

    private const float SearchY = 1.95f;
    private const float ListTopY = 1.95f;     // 列表顶（搜索框删掉后上移到原来搜索框那行）
    private const float CardH = 0.62f;
    private const float CardGap = 0.72f;
    private const float CardGapX = 0.16f;

    // ★ 卡片**一行两个**（用户 2026-10-06："左边的那一排也做成一行两个"）
    private const int CardsPerRow = 2;
    private const int MaxVisibleCards = 8;      // 4 行 × 2（槽位多了，滚动上限也跟着大）

    // ---- 右侧：两个按钮**并排一行**（用户："按钮改短，一排俩按钮"）----
    private const float BtnRowY = 1.95f;
    private const float BtnW = 1.72f;      // 短按钮
    private const float BtnH = 0.52f;
    private const float BtnGap = 0.16f;

    // ---- 右侧详情区（下面那一大块）----
    private const float DetailPlateTopY = 1.30f;   // 底板顶
    private const float DetailTopY = 1.12f;        // 文字顶（比底板低一点 = 内边距，用户："字往下点"）
    private const float DetailPlateH = 3.75f;      // 底板高
    private const float DetailFont = 1.62f;        // 详情字号（用户："整体大点"）

    /// <summary>右上角 X 的缩放（用户 2026-10-06："退出按钮大一点"，后又"大点"）。</summary>
    private const float CloseButtonScale = 2.10f;

    private const float RowZ = -2f;

    private static readonly Color Highlight = new(0f, 1f, 42f / 255f);   // #00FF2A，同 RoleListPage 悬浮绿

    // ---------------------------------------------------------------------
    //  状态
    // ---------------------------------------------------------------------

    private static HudUIWindow? _window;
    private static readonly List<HudUIButton> _cardBtns = new();
    private static readonly List<PresetMeta> _shown = new();      // 当前显示的那一页

    private static TextBoxTMP? _search;
    private static string _filter = "";

    private static HudUIButton? _loadBtn;
    private static HudUIButton? _delBtn;
    private static TextMeshPro? _detail;
    private static TextMeshPro? _countText;

    private static string? _selectedPath;
    private static int _scroll;

    public static bool IsOpen => _window != null && _window.GameObject != null;

    // ---------------------------------------------------------------------
    //  打开 / 关闭
    // ---------------------------------------------------------------------

    public static void Open()
    {
        try
        {
            // ⚠️⚠️ **无条件先重置静态状态**（2026-10-06 修"第二次打开预设条目全没了"）。
            //
            //  原来的写法是 `if (IsOpen) { Close(); }`，而 IsOpen 是
            //  `_window != null && _window.GameObject != null` ——
            //  **用户点右上角 X 关窗时 UnityEngine 会把物体销毁**，于是 GameObject 变成假 null，
            //  IsOpen 返回 false → **Close() 被整个跳过** → `_cardBtns` 里那 6 个旧按钮
            //  （已经是销毁态）没被清掉。
            //
            //  接着 BuildCards 又追加 6 个新的 → 列表变成 [6 个死的][6 个活的]，
            //  而 RefreshCards 从 i=0 开始按顺序填 → **填的全是死按钮** → 界面一片空白。
            //
            //  教训：**静态可变状态不能靠"某次分支调用"来清理**。
            //  这里是每帧/每次打开都要走的主入口，就在门口无条件清一次最稳。
            ResetState();
            CloseWindowObject();

            var parent = ResolveParent();
            if (parent == null) { LightLogger.LogWarning("[PresetWindow] 找不到可挂载的父物体"); return; }

            _window = HudUIWindow.Create("预设", new Vector2(WinW, WinH), parent);
            if (_window == null || _window.Screen == null)
            {
                LightLogger.LogWarning("[PresetWindow] HudUIWindow.Create 失败");
                return;
            }

            var root = _window.Screen.transform;

            // ⚠️ 搜索框**已按用户要求删掉**（2026-10-06："不要搜索框了"）——
            //    `BuildSearch` / `TryCloneTextBox` / `ClearPlaceholder` 三个方法保留着没删，
            //    因为它们记录了"怎么克隆原版 TextBoxTMP"的完整踩坑（allowAllCharacters、
            //    占位符清理、不能碰 OnEnter）—— 以后要做**保存预设**那个输入窗口时直接复用。
            BuildCards(root);
            BuildRightButtons(root);   // 内含详情底板 + 详情文字 + 列表计数

            // 右上角那个 X 的尺寸**不在这里改** —— 2026-10-06 教训：
            //   原来这里有个 ScaleCloseButton()，用 FindDeep("CloseButton") 找子物体再改 localScale，
            //   **从来没生效过**（CloseButton 是 Screen 的**兄弟**，从 _window.GameObject 往下找不到；
            //   而且 GenerateWindow 里写死 0.57，改完也会被覆盖）。
            //   现在尺寸统一在 HudUI.DefaultCloseButtonScale 里定（用户："你去动 HudUI 吧"）。

            // ⚠️ 全部建完之后统一刷一次字体（AddButton 的标签也是内部建的 TMP）
            try { _window.ApplyCjkFont(); } catch { }

            Reload();

            // 滚轮滚动：窗口自己没滚动条，用一个每帧驱动器读 mouseScrollDelta
            try { _window.GameObject.AddComponent<PresetWindowDriver>(); } catch { }

            LightLogger.Log($"[PresetWindow] 已打开（预设 {PresetLibrary.List().Count} 个，" +
                            $"卡片槽 {_cardBtns.Count} 个）");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PresetWindow.Open]", ex);
        }
    }

    /// <summary>
    /// **清掉所有静态可变状态**。
    /// ⚠️ 必须在每次 <see cref="Open"/> 开头**无条件**调用 ——
    ///    这些字段跨窗口生命周期存活，而窗口物体随时可能被 Unity 销毁（点 X / 切场景），
    ///    靠"某个分支里顺便清一下"必然会漏（见 Open 里那段事故记录）。
    /// </summary>
    private static void ResetState()
    {
        _cardBtns.Clear();
        _shown.Clear();
        _search = null;
        _loadBtn = null;
        _delBtn = null;
        _detail = null;
        _countText = null;
        _selectedPath = null;
        _scroll = 0;
        _filter = "";
    }

    /// <summary>把窗口物体关掉（不碰静态状态 —— 那是 <see cref="ResetState"/> 的活）。</summary>
    private static void CloseWindowObject()
    {
        if (_window == null) return;
        try { _window.Close(); }
        catch (Exception ex) { LightLogger.LogWarning($"[PresetWindow.CloseWindowObject] {ex.Message}"); }
        finally { _window = null; }
    }

    public static void Close()
    {
        CloseWindowObject();   // 关窗口物体
        ResetState();          // 清静态状态（两者必须成对，见 Open 里的事故记录）
    }

    public static void Toggle()
    {
        if (IsOpen) Close(); else Open();
    }

    /// <summary>
    /// 让开着的窗口**只重刷列表**（重新读 Save/ 目录），**不动窗口开关**。
    ///
    /// ⚠️ 别用 <c>Toggle()</c> 来"刷新" —— 那是**开/关切换**：
    ///    窗口开着时调一次就把它关掉了（2026-10-06 保存预设就是这么把窗口关没的）。
    /// </summary>
    public static void RequestRefresh()
    {
        try
        {
            if (!IsOpen) return;
            RefreshCards();
            RefreshDetail();
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[PresetWindow.RequestRefresh] {ex.Message}");
        }
    }

    /// <summary>
    /// 窗口挂谁下面。<b>必须是"原点在屏幕正中"的物体</b>，否则窗口会偏
    /// （详见 RoleDetailWindow 那段教训：挂 _modPage 会偏到右上角）。
    ///
    /// ⚠️⚠️ **绝对不要用 <c>DestroyableSingleton&lt;T&gt;.Instance</c>**（2026-10-06 实测踩坑）！
    ///   `DestroyableSingleton` 的 Instance 在**找不到实例时会自己 `new GameObject + AddComponent` 造一个** ——
    ///   在规则编辑界面里 `MainMenuManager` / `HudManager` 可能不存在，
    ///   于是它凭空造出来 → `Awake()` 跑 → `SetUpControllerNav()` 里读还没建好的字段 → **NRE**。
    ///   日志实证（时间戳正好是窗口打开那一刻）：
    /// <code>
    ///   DestroyableSingleton`1[T].get_Instance ()
    ///   UnityEngine.GameObject.AddComponent[T] ()
    ///   MainMenuManager.Awake ()
    ///   MainMenuManager.SetUpControllerNav ()   ← NullReferenceException
    /// </code>
    ///   所以这里一律用 <c>FindObjectOfType</c>（**只找，不造**）。
    /// </summary>
    private static Transform? ResolveParent()
    {
        return ResolveParentCore();
    }

    /// <summary>同上，给 <see cref="PresetNotice"/> 复用。</summary>
    internal static Transform? ResolveParentCore()
    {
        try
        {
            // ★ 优先用**我们自己的**设置菜单根 —— 它一定存在，而且原点就在屏幕中心附近。
            var gsm = GameSettingMenu.Instance;
            if (gsm != null) return gsm.transform;

            // 次选：场景里已经存在的（只找不造！）
            var hud = Object.FindObjectOfType<HudManager>();
            if (hud != null) return hud.transform;

            var menu = Object.FindObjectOfType<MainMenuManager>();
            if (menu != null) return menu.transform;

            var cam = Camera.main;
            return cam != null ? cam.transform : null;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[PresetWindow.ResolveParent] {ex.Message}");
            return null;
        }
    }

    // ---------------------------------------------------------------------
    //  搜索框
    // ---------------------------------------------------------------------

    private static void BuildSearch(Transform root)
    {
        try
        {
            var go = new GameObject("PresetSearch");
            go.layer = LayerExpansion.GetUILayer();
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(LeftCx, SearchY, RowZ);
            go.transform.localScale = Vector3.one;

            // ⚠️ 这里**必须自己加一层底板**（2026-10-06 实测）：
            //   上一版我把它删了、指望克隆来的 TextBoxTMP 自带 Background 撑场面 ——
            //   结果**搜索框整个看不见了**（那个 Background 在克隆体上可能是 null 或尺寸为 0）。
            //   现在：自己画底 + 把克隆体自带的 Background 关掉，保证**只有一层**。
            var bg = go.AddComponent<SpriteRenderer>();
            bg.sprite = HudUIAssets.ButtonNormal;
            bg.drawMode = SpriteDrawMode.Sliced;
            bg.size = new Vector2(ColW, 0.56f);

            // 输入框：克隆原版的 TextBoxTMP（用户："输入框就用那个吧，HudUI 给的不好用"）
            var box = TryCloneTextBox(go.transform, new Vector3(-ColW * 0.5f + 0.16f, 0f, -0.1f), ColW - 0.32f);
            if (box != null)
            {
                // 关掉克隆体自带的那层底（我们已经画了一层，两层会叠）
                try { if (box.Background != null) box.Background.enabled = false; } catch { }
                box.allowAllCharacters = true;      // 原版那个只允许房间码字符 → 必须放开
                box.characterLimit = 24;
                box.text = "";
                try { box.outputText.text = ""; } catch { }
                ClearPlaceholder(box);              // ★ 抹掉原版的"输入代码"占位符
                _search = box;

                box.OnChange ??= new UnityEngine.UI.Button.ButtonClickedEvent();
                box.OnChange.AddListener((UnityAction)(() =>
                {
                    _filter = box.text ?? "";
                    _scroll = 0;
                    RefreshCards();
                }));
            }
            else
            {
                _search = null;
                LightLogger.LogWarning("[PresetWindow] 没找到可克隆的 TextBoxTMP，搜索框不可用");
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PresetWindow.BuildSearch]", ex);
        }
    }

    /// <summary>
    /// 抹掉原版输入框的**占位符**（"输入代码"）。
    ///
    /// ⚠️ `TextBoxTMP.placeholderText` 是 **private** 字段，interop 里虽然能读到，
    ///    但不同版本字段名不一定一致 —— 所以这里用"更笨但更稳"的办法：
    ///    找出该物体下**所有** TMP，凡是**不是** <c>outputText</c> 的一律清空。
    ///    （2026-10-06 用户截图：搜索框上方一直挂着一行"输入代码"。）
    /// </summary>
    private static void ClearPlaceholder(TextBoxTMP box)
    {
        try
        {
            var outTmp = box.outputText;
            foreach (var t in box.GetComponentsInChildren<TextMeshPro>(true))
            {
                if (t == null) continue;
                if (outTmp != null && t.Pointer == outTmp.Pointer) continue;   // 这个是真的输出文本，留着
                t.text = "";
                t.gameObject.SetActive(false);      // 连物体一起关掉，免得它自己的布局还占位
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[PresetWindow.ClearPlaceholder] {ex.Message}");
        }
    }

    /// <summary>
    /// 克隆一个原版输入框。
    /// 优先用大厅那个"输入房间码"的 <c>EnterCodeField</c>（用户点名要它），
    /// 拿不到就退回"已加载资源里第一个 TextBoxTMP"。
    /// </summary>
    private static TextBoxTMP? TryCloneTextBox(Transform parent, Vector3 pos, float width)
    {
        try
        {
            TextBoxTMP? tpl = null;

            // ① MainMenuManager.entercodeField（主界面输房间码那个）
            try
            {
                // ⚠️ 同样**不能**用 DestroyableSingleton（会凭空造一个 MainMenuManager → NRE，见 ResolveParent 注释）
                var menu = Object.FindObjectOfType<MainMenuManager>();

                // ⚠️ `entercodeField` 是 **PassiveButton**（按钮本体），
                //    真正的输入框是它子物体上的 TextBoxTMP —— 必须 GetComponentInChildren 取，
                //    直接赋值会编译不过（CS0029 无法把 PassiveButton 转成 TextBoxTMP）。
                if (menu != null && menu.entercodeField != null)
                    tpl = menu.entercodeField.GetComponentInChildren<TextBoxTMP>();
            }
            catch { }

            // ② 兜底：已加载资源里任意一个 TextBoxTMP
            if (tpl == null)
            {
                try
                {
                    var all = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<TextBoxTMP>());
                    if (all != null && all.Length > 0) tpl = all[0].TryCast<TextBoxTMP>();
                }
                catch { }
            }

            if (tpl == null) return null;

            var clone = Object.Instantiate(tpl, parent);
            clone.gameObject.name = "PresetInput";
            clone.gameObject.SetActive(true);
            clone.transform.localPosition = pos;
            clone.transform.localScale = Vector3.one;

            // 尺寸：原版那个很小（房间码 6 位），按宽度拉一下
            try
            {
                if (clone.Background != null)
                {
                    clone.Background.drawMode = SpriteDrawMode.Sliced;
                    clone.Background.size = new Vector2(width, 0.42f);
                }
                if (clone.outputText != null)
                {
                    clone.outputText.enableAutoSizing = false;     // §12.2
                    clone.outputText.fontSize = 1.5f;
                    clone.outputText.fontSizeMin = 1.5f;
                    clone.outputText.fontSizeMax = 1.5f;
                    clone.outputText.fontStyle = FontStyles.Bold;  // §12.1
                    clone.outputText.alignment = TextAlignmentOptions.Left;
                    clone.outputText.rectTransform.sizeDelta = new Vector2(width - 0.3f, 0.4f);
                }
            }
            catch { }

            // ⚠️ 克隆来的输入框自带原版行为 —— 清掉它的 Enter 回调，
            //    否则回车会触发原版的"加入房间"（§4.5 同一类坑）。
            try
            {
                clone.OnEnter = new UnityEngine.UI.Button.ButtonClickedEvent();
                clone.ClearOnFocus = false;
                clone.ForceUppercase = false;
            }
            catch { }

            return clone;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[PresetWindow.TryCloneTextBox] {ex.Message}");
            return null;
        }
    }

    // ---------------------------------------------------------------------
    //  卡片列表
    // ---------------------------------------------------------------------

    private static void BuildCards(Transform root)
    {
        try
        {
            // ★ 再清一次（ResetState 已经在 Open 开头清过，这里是双保险）——
            //   `_cardBtns` 是**静态**的，只要有一次漏清，卡片就会整体错位/消失。
            _cardBtns.Clear();

            // 一行两个：列 = i % 2，行 = i / 2
            float cardW = (ColW - CardGapX) * 0.5f;
            float halfStep = (cardW + CardGapX) * 0.5f;

            for (int i = 0; i < MaxVisibleCards; i++)
            {
                int slot = i;
                var btn = HudUIButton.Create(root, "", new Vector2(cardW, CardH), () => OnCardClicked(slot));
                if (btn == null || btn.GameObject == null) continue;

                int row = i / CardsPerRow;
                int col = i % CardsPerRow;
                float x = LeftCx + (col == 0 ? -halfStep : halfStep);
                float y = ListTopY - row * CardGap;

                btn.SetPosition(new Vector3(x, y, RowZ));
                btn.SetVisible(false);

                // ★ 选中态换成**悬停观感**（用户 2026-10-06："这个选中态也太难堪了，
                //   你就让他变成类似于鼠标悬停时的样子行不"）。
                //   默认的 ButtonSelected 是一整块高亮，铺满整张卡片显得很重；
                //   换成 ButtonHover = 和悬停同一个描边观感，轻得多。
                //   ⚠️ 只改这一个实例，不动 HudUIAssets，别的窗口照旧。
                try { btn.SetSelectedSprite(HudUIAssets.ButtonHover, HudUIAssets.ButtonSelectedHover); }
                catch { }

                _cardBtns.Add(btn);
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PresetWindow.BuildCards]", ex);
        }
    }

    /// <summary>重新拉数据并刷列表（打开窗口 / 搜索词变化 / 删了一个之后调）。</summary>
    private static void Reload()
    {
        try
        {
            RefreshCards();
            RefreshDetail();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PresetWindow.Reload]", ex);
        }
    }

    private static void RefreshCards()
    {
        try
        {
            var all = PresetLibrary.List();

            // 搜索过滤
            var filtered = string.IsNullOrWhiteSpace(_filter)
                ? all
                : all.Where(p => (p.Name ?? "").IndexOf(_filter, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();

            // 滚动窗口夹紧
            int maxScroll = Math.Max(0, filtered.Count - MaxVisibleCards);
            _scroll = Math.Clamp(_scroll, 0, maxScroll);

            _shown.Clear();
            _shown.AddRange(filtered.Skip(_scroll).Take(MaxVisibleCards));

            for (int i = 0; i < _cardBtns.Count; i++)
            {
                var btn = _cardBtns[i];
                if (btn == null || btn.GameObject == null) continue;

                if (i >= _shown.Count) { btn.SetVisible(false); continue; }

                var meta = _shown[i];
                btn.SetVisible(true);
                btn.SetText(MakeCardLabel(meta));
            }

            RefreshSelection();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PresetWindow.RefreshCards]", ex);
        }
    }

    /// <summary>卡片上那行字：名字 + （不兼容时）一个红色标记。</summary>
    private static string MakeCardLabel(PresetMeta m)
    {
        var name = string.IsNullOrWhiteSpace(m.Name) ? m.FileName : m.Name;
        // ⚠️ 截断长度按"一行两个"之后的卡片宽（约 ColW/2）估的 —— 卡片再变窄就要跟着减。
        if (name.Length > 8) name = name[..8] + "…";
        return m.Compatible ? name : $"<color=#FF3B30>{name}</color>";
    }

    private static void OnCardClicked(int slot)
    {
        try
        {
            if (slot < 0 || slot >= _shown.Count) return;
            var path = _shown[slot].Path;

            // 用户："再次点击取消选中状态"；点另一张则"切换选中为那个"
            _selectedPath = (_selectedPath == path) ? null : path;

            RefreshSelection();
            RefreshDetail();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PresetWindow.OnCardClicked]", ex);
        }
    }

    /// <summary>
    /// 单选的真值来源就是 <see cref="_selectedPath"/> —— 这里一次性把所有卡片的
    /// 高光按它刷一遍。<b>不要再在别处单独 SetSelected</b>，否则会出现两张都亮。
    /// </summary>
    private static void RefreshSelection()
    {
        for (int i = 0; i < _cardBtns.Count; i++)
        {
            var btn = _cardBtns[i];
            if (btn == null || btn.GameObject == null) continue;

            bool sel = i < _shown.Count && _shown[i].Path == _selectedPath;
            try { btn.SetSelected(sel); } catch { }
        }

        // 右侧两个按钮：没选中就不可用（用户设计图："此时右侧按钮可以点击"）
        bool has = SelectedMeta() != null;
        SetButtonEnabled(_loadBtn, has);
        SetButtonEnabled(_delBtn, has);
    }

    /// <summary>把按钮调成"灰掉"的样子（HudUIButton 没有 enabled，只能靠颜色 + 挡掉点击）。</summary>
    private static void SetButtonEnabled(HudUIButton? btn, bool enabled)
    {
        if (btn == null || btn.GameObject == null) return;
        try
        {
            btn.SetColor(enabled ? new LightInDark.Color(1f, 1f, 1f, 1f)
                                 : new LightInDark.Color(0.45f, 0.45f, 0.45f, 0.75f));
        }
        catch { }
    }

    /// <summary>
    /// **开关本窗口所有按钮的可点性**。
    ///
    /// ⚠️ 2026-10-06 用户："隔着他能点到他下面的预设界面按钮" ——
    ///    原版 <c>PassiveButtonManager</c> 的**点击**判定对**每个碰撞盒重叠的按钮**都会派发
    ///    （只有**悬停**才按 z 取最靠前那个，见 HandleMouseOver 的 z 比较）——
    ///    所以上层窗口"盖住"下层**不等于**拦住了点击，必须显式把下层按钮关掉。
    ///
    ///    做法：直接 <c>Collider.enabled = false</c>。
    ///    <c>PassiveButtonManager</c> 循环里有 <c>col.isActiveAndEnabled</c> 判定，
    ///    关掉碰撞盒它就不会再收到任何事件 ✓（比 SetActive 温和 —— 视觉还在，只是点不到）
    /// </summary>
    internal static void SetInteractive(bool on)
    {
        foreach (var b in _cardBtns) SetButtonCollider(b, on);
        SetButtonCollider(_loadBtn, on);
        SetButtonCollider(_delBtn, on);
    }

    private static void SetButtonCollider(HudUIButton? btn, bool on)
    {
        try
        {
            if (btn == null || btn.GameObject == null) return;
            if (btn.Collider != null) btn.Collider.enabled = on;
        }
        catch { }
    }

    private static PresetMeta? SelectedMeta()
        => _selectedPath == null ? null : _shown.FirstOrDefault(m => m.Path == _selectedPath);

    /// <summary>预设的显示名（没写名字就退回文件名）。</summary>
    private static string DisplayName(PresetMeta m)
        => string.IsNullOrWhiteSpace(m.Name) ? m.FileName : m.Name;

    // ---------------------------------------------------------------------
    //  右侧按钮 + 详情
    // ---------------------------------------------------------------------

    private static void BuildRightButtons(Transform root)
    {
        try
        {
            // ★ 两个按钮**并排一行**（用户 2026-10-06："按钮改短，一排俩按钮"）
            float half = (BtnW + BtnGap) * 0.5f;

            _loadBtn = HudUIButton.Create(root, "加载此预设", new Vector2(BtnW, BtnH), OnLoadClicked);
            if (_loadBtn != null && _loadBtn.GameObject != null)
                _loadBtn.SetPosition(new Vector3(RightCx - half, BtnRowY, RowZ));

            _delBtn = HudUIButton.Create(root, "删除此预设", new Vector2(BtnW, BtnH), OnDeleteClicked);
            if (_delBtn != null && _delBtn.GameObject != null)
                _delBtn.SetPosition(new Vector3(RightCx + half, BtnRowY, RowZ));

            // 详情底板（纯装饰）—— 从按钮那行下面一直铺到窗口底部附近
            var plate = new GameObject("PresetDetailBg");
            plate.layer = LayerExpansion.GetUILayer();
            plate.transform.SetParent(root, false);
            plate.transform.localPosition = new Vector3(RightCx, DetailPlateTopY - DetailPlateH * 0.5f, RowZ + 0.2f);
            plate.transform.localScale = Vector3.one;
            var sr = plate.AddComponent<SpriteRenderer>();
            sr.sprite = HudUIAssets.ButtonNormal;
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = new Vector2(RightW, DetailPlateH);
            sr.color = new Color(1f, 1f, 1f, 0.55f);

            // 详情文字：走窗口自带的 AddText（它内部已经设好 enableWordWrapping =
            // alignment != Center，所以 TopLeft 天然就能自动换行 ✓ 用户明确要求）
            _detail = _window.AddText("", DetailFont, TextAlignmentOptions.TopLeft);
            if (_detail != null)
            {
                // ⚠️⚠️ **顺序很重要：先定尺寸和 pivot，最后才 localPosition**。
                //
                //   改 `rectTransform.pivot` 会让**已经画出来的内容整体位移**
                //   `(newPivot - oldPivot) × rectSize` —— 锚点不动、矩形根据新 pivot 重算。
                //   实测（2026-10-06 用户截图）：先设位置再改 pivot，
                //   文字被顶上去约 1.5 单位，跑到窗口顶部、"预设名"那行被裁掉了。
                //
                //   正确顺序：sizeDelta → pivot → localPosition。
                _detail.alignment = TextAlignmentOptions.TopLeft;
                _detail.enableWordWrapping = true;
                _detail.overflowMode = TextOverflowModes.Overflow;
                _detail.enableAutoSizing = false;          // §12.2
                _detail.fontSize = DetailFont;
                _detail.fontSizeMin = DetailFont;
                _detail.fontSizeMax = DetailFont;
                _detail.fontStyle = FontStyles.Bold;       // §12.1

                _detail.rectTransform.sizeDelta = new Vector2(RightW - 0.36f, 3.00f);
                _detail.rectTransform.pivot = new Vector2(0f, 1f);     // ← pivot 必须在位置之前
                PlaceInRoot(_detail.transform, root,
                            new Vector3(RightCx - RightW * 0.5f + 0.18f, DetailTopY, RowZ - 0.1f));
                _detail.text = "";
            }

            // 列表计数（左下角那点提示：5 / 12）
            _countText = _window.AddText("", 1.2f, TextAlignmentOptions.Left);
            if (_countText != null)
            {
                _countText.transform.localPosition = new Vector3(LeftCx - ColW * 0.5f, ListTopY - MaxVisibleCards * CardGap - 0.05f, RowZ - 0.1f);
                _countText.alignment = TextAlignmentOptions.Left;
                _countText.enableAutoSizing = false;
                _countText.fontSize = 1.2f;
                _countText.fontSizeMin = 1.2f;
                _countText.fontSizeMax = 1.2f;
                _countText.fontStyle = FontStyles.Bold;
                _countText.text = "";
                PlaceInRoot(_countText.transform, root,
                            // ⚠️ 用**行数**算，不是槽位数 —— 一行两个之后
                            //    `MaxVisibleCards * CardGap` 会让它掉到窗口外面去。
                            new Vector3(LeftCx - ColW * 0.5f,
                                        ListTopY - (MaxVisibleCards / CardsPerRow) * CardGap - 0.02f,
                                        RowZ - 0.1f));
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PresetWindow.BuildRightButtons]", ex);
        }
    }

    /// <summary>
    /// 把一个节点按**窗口根坐标系**摆放。
    ///
    /// ⚠️⚠️ 为什么不能直接写 <c>node.transform.localPosition = pos</c>（2026-10-06 实测踩坑）：
    ///
    ///  <c>HudUIWindow.AddText</c> / <c>AddButton</c> 都是**先建一个父物体、再把真正的
    ///  TMP/渲染器建成它的子物体**，而那个父物体被摆在布局游标 <c>_currentY</c> 处：
    /// <code>
    ///   obj.transform.localPosition = new Vector3(0f, _currentY, -1f);   // 父物体
    ///   var tmp = HudUITextHelper.Create(obj.transform);                 // TMP 是子物体
    ///   return tmp;                                                      // 返回的是子物体！
    /// </code>
    ///  于是对返回的 TMP 写"绝对 localPosition"，实际是在**父物体偏移之上再叠一层** ——
    ///  文字被顶到 <c>_currentY + y</c>，实测直接跑出窗口上沿。
    ///
    ///  正确做法：**沿父链走到 root 的直接子物体**，对它写坐标。
    ///  这样无论上层套了几层都稳。
    /// </summary>
    internal static void PlaceInRoot(Transform? node, Transform root, Vector3 pos)
    {
        try
        {
            if (node == null) return;

            var t = node;
            while (t.parent != null && t.parent != root) t = t.parent;
            t.localPosition = pos;

            // 诊断：把"最终落在哪个节点、坐标多少"打出来 ——
            // 这条能一眼看出是不是又被某层父物体叠了偏移（2026-10-06 踩过）。
            LightLogger.Log($"[PresetWindow] 摆放 '{t.name}' → local={t.localPosition}（root={root.name}）");
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[PresetWindow.PlaceInRoot] {ex.Message}");
        }
    }

    /// <summary>
    /// 放大窗口右上角那个关闭按钮（X）。
    ///
    /// MetaScreen 建出来的 X 是固定尺寸的小图标，用户 2026-10-06 要求"退出按钮大一点"。
    /// 做法和 RoleDetailWindow.RewireCloseButton 一样：**按名字找子物体再改 localScale** ——
    /// MetaScreen 没有公开这个按钮的引用，只能按名字找。
    /// </summary>
    private static void ScaleCloseButton()
    {
        try
        {
            var root = _window != null ? _window.GameObject : null;
            if (root == null) return;

            var close = FindDeep(root.transform, "CloseButton");
            if (close == null)
            {
                LightLogger.LogWarning("[PresetWindow] 没找到 CloseButton，X 保持原大小");
                return;
            }

            close.localScale = Vector3.one * CloseButtonScale;
            LightLogger.Log($"[PresetWindow] 关闭按钮已放大到 {CloseButtonScale}×（{close.name}）");
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[PresetWindow.ScaleCloseButton] {ex.Message}");
        }
    }

    /// <summary>按名字递归找子物体（MetaScreen 不公开这些子物体，只能按名字挖）。</summary>
    private static Transform? FindDeep(Transform? parent, string name)
    {
        if (parent == null) return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            var c = parent.GetChild(i);
            if (c == null) continue;
            if (c.name == name) return c;
            var r = FindDeep(c, name);
            if (r != null) return r;
        }
        return null;
    }

    private static void RefreshDetail()
    {
        try
        {
            var m = SelectedMeta();

            if (_detail != null)
            {
                if (m == null)
                {
                    _detail.text = "<color=#9A9A9A>选择一个预设查看详情</color>";
                }
                else
                {
                    var sb = new System.Text.StringBuilder(320);
                    sb.Append("预设名：").Append(string.IsNullOrWhiteSpace(m.Name) ? m.FileName : m.Name).Append('\n');
                    sb.Append("预设作者：").Append(string.IsNullOrWhiteSpace(m.Author) ? "(未填)" : m.Author).Append('\n');
                    sb.Append("创建时间：").Append(string.IsNullOrWhiteSpace(m.Created) ? "(未知)" : m.Created).Append('\n');
                    sb.Append("AU版本：").Append(string.IsNullOrWhiteSpace(m.GameVersion) ? "(未知)" : m.GameVersion).Append('\n');
                    sb.Append("MOD版本：").Append(string.IsNullOrWhiteSpace(m.ModVersion) ? "(未知)" : "v" + m.ModVersion).Append("\n\n");

                    // 匹配情况：可能多行 → 上面已经开了自动换行
                    sb.Append(m.MatchReason()).Append("\n\n");

                    // 状态：写死的红绿两态
                    sb.Append("状态：<color=").Append(m.AvailabilityColor).Append('>')
                      .Append(m.AvailabilityText).Append("</color>");

                    _detail.text = sb.ToString();
                }
                _detail.ForceMeshUpdate();
            }

            if (_countText != null)
            {
                int total = PresetLibrary.List().Count;
                _countText.text = total == 0
                    ? "<color=#9A9A9A>还没有保存过预设</color>"
                    : $"{Math.Min(_shown.Count, _cardBtns.Count)} / {total}" +
                      (total > MaxVisibleCards ? "（滚轮翻页）" : "");
                _countText.ForceMeshUpdate();
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PresetWindow.RefreshDetail]", ex);
        }
    }

    // ---------------------------------------------------------------------
    //  动作
    // ---------------------------------------------------------------------

    private static void OnLoadClicked()
    {
        try
        {
            var m = SelectedMeta();
            if (m == null) return;

            var (ok, msg) = PresetStore.LoadFrom(m.Path);
            PresetToast.Show(ok ? "已加载预设" : "加载失败", msg);

            // ★ 弹窗提示（用户要求："加载完成"要弹个窗口）
            PresetNotice.Show(ok
                ? $"加载完成\n{DisplayName(m)}"
                : $"加载失败\n{msg}");

            LightLogger.Log($"[PresetWindow] 加载 {m.FileName} → {(ok ? "成功" : "失败：" + msg)}");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PresetWindow.OnLoadClicked]", ex);
        }
    }

    private static void OnDeleteClicked()
    {
        try
        {
            var m = SelectedMeta();
            if (m == null) return;

            bool ok = PresetLibrary.Delete(m.Path);
            _selectedPath = null;

            RefreshCards();
            RefreshDetail();

            // ★ 弹窗提示（用户要求："删除完成"要弹个窗口）
            PresetNotice.Show(ok
                ? $"删除完成\n{DisplayName(m)}"
                : "删除失败\n看日志");

            LightLogger.Log($"[PresetWindow] 删除 {m.FileName} → {(ok ? "成功" : "失败")}");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PresetWindow.OnDeleteClicked]", ex);
        }
    }

    // ---------------------------------------------------------------------
    //  滚轮
    // ---------------------------------------------------------------------

    /// <summary>由 <see cref="PresetWindowDriver"/> 每帧调用。</summary>
    internal static void TickScroll()
    {
        try
        {
            if (!IsOpen) return;

            float d = Input.mouseScrollDelta.y;
            if (Mathf.Abs(d) < 0.01f) return;

            int before = _scroll;
            _scroll += d > 0 ? -1 : 1;      // 滚轮向上 = 看更靠前的
            RefreshCards();
            RefreshDetail();

            if (_scroll != before) LightLogger.LogDebug($"[PresetWindow] 滚动到 {_scroll}");
        }
        catch { }
    }
}

/// <summary>
/// **预设操作的结果提示小窗**（用户 2026-10-06："删除预设和加载预设你弹个窗口提示，
/// 删除完成或者加载完成"）。
///
/// ⚠️ **不能用 <c>HudUI.OpenMessageDialog</c> / <c>OpenConfirmDialog</c>** ——
///    它们内部走 <c>HudUI.Create</c>，而那里是
///    <c>parent ??= HudManager.Instance?.transform; if (parent == null) throw</c>，
///    **规则编辑界面没有 HudManager → 直接抛**（和 PresetWindow 当初同一个坑）。
///    所以这里也显式传 parent。
///
/// ⚠️ 它和预设窗口是**两个独立窗口**，关掉提示不会关掉预设窗口。
/// </summary>
internal static class PresetNotice
{
    private static HudUIWindow? _w;

    public static bool IsOpen => _w != null && _w.GameObject != null;

    /// <summary>弹一条提示（再弹会先关掉上一条，不会叠一堆）。</summary>
    public static void Show(string message)
    {
        try
        {
            Close();

            var parent = Light.UI.Config.PresetWindow.ResolveParentCore();
            if (parent == null) { LightLogger.Log($"[预设] {message}"); return; }

            _w = HudUIWindow.Create("", new Vector2(4.40f, 1.90f), parent);
            if (_w == null || _w.Screen == null) { _w = null; return; }

            _w.AddText(message, 1.80f, TextAlignmentOptions.Center);
            _w.AddMargin(0.18f);
            _w.AddButton("确定", Close, new Vector2(1.60f, 0.50f));

            try { _w.ApplyCjkFont(); } catch { }

            // ★ 关掉下层预设窗口的可点性 —— 否则"隔着提示窗口还能点到下面的按钮"（用户实测）
            Light.UI.Config.PresetWindow.SetInteractive(false);

            LightLogger.Log($"[预设] 提示：{message}");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PresetNotice.Show]", ex);
        }
    }

    public static void Close()
    {
        try { _w?.Close(); }
        catch (Exception ex) { LightLogger.LogWarning($"[PresetNotice.Close] {ex.Message}"); }
        finally
        {
            _w = null;
            // ★ 还原下层预设窗口的可点性
            try { Light.UI.Config.PresetWindow.SetInteractive(true); } catch { }
        }
    }
}

/// <summary>
/// 预设窗口的每帧驱动器（滚轮）。
/// ⚠️ 托管 MonoBehaviour 必须先 <c>ClassInjector.RegisterTypeInIl2Cpp&lt;T&gt;()</c>，
///    否则 <c>AddComponent&lt;T&gt;()</c> 抛 TypeInitializationException（AGENTS.md §11.2）。
///    写在静态构造函数里最稳。
/// </summary>
public sealed class PresetWindowDriver : MonoBehaviour
{
    static PresetWindowDriver()
    {
        try { Il2CppInterop.Runtime.Injection.ClassInjector.RegisterTypeInIl2Cpp<PresetWindowDriver>(); }
        catch { }
    }

    private void Update()
    {
        try { PresetWindow.TickScroll(); } catch { }
    }
}

/// <summary>预设操作的结果提示 —— 统一走原版左上角那块描述文字。</summary>
internal static class PresetToast
{
    public static void Show(string title, string detail)
    {
        try
        {
            var menu = GameSettingMenu.Instance;
            var tmp = menu != null ? menu.MenuDescriptionText : null;
            if (tmp == null) { LightLogger.Log($"[预设] {title}：{detail}"); return; }

            tmp.SetText(string.IsNullOrWhiteSpace(detail) ? title : $"{title}\n{detail}");
            tmp.fontStyle = FontStyles.Bold;
            tmp.ForceMeshUpdate();
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[PresetToast] {ex.Message}");
        }
    }
}

/// <summary>
/// **保存预设窗口** —— 两个输入框（预设名 / 作者）+ 保存 / 取消。
///
/// 用户 2026-10-06："保存需要俩输入框。这俩输入框怎么说呢，HudUI 那个不好使，
/// 你修一下 HudUI 的拿来用吧。"
/// → 输入框用 **<see cref="Light.UI.Window.GUITextField"/>**（pull 下来的那份，H 帮助菜单在用）。
///   它自己每帧读 <c>Input.inputString</c>，还处理中文输入法，**不依赖原版 TextBoxTMP 的聚焦体系** ——
///   我原来克隆原版 <c>EnterCodeField</c>（`HudUIInputField`）那套在 HUD 窗口里根本收不到键盘，已删。
///
/// ⚠️ 它和预设窗口、提示窗口是**三个独立窗口**：
///    开保存窗口时会关掉预设窗口（不然三个叠一起，点击关系很难理清）。
/// </summary>
internal static class PresetSaveWindow
{
    private const float WinW = 5.80f;
    private const float WinH = 3.30f;

    private const float FieldW = 3.50f;
    private const float FieldH = 0.54f;

    private const float RowNameY = 0.62f;
    private const float RowAuthorY = -0.02f;
    private const float RowBtnY = -0.98f;

    private const float LabelX = -2.35f;    // 标签左对齐
    private const float FieldCx = 0.42f;    // 输入框中心

    private static HudUIWindow? _w;
    private static Light.UI.Window.GUITextField? _nameField;
    private static Light.UI.Window.GUITextField? _authorField;

    public static bool IsOpen => _w != null && _w.GameObject != null;

    public static void Open()
    {
        try
        {
            Close();

            var parent = PresetWindow.ResolveParentCore();
            if (parent == null) { LightLogger.LogWarning("[PresetSaveWindow] 找不到父物体"); return; }

            _w = HudUIWindow.Create("保存预设", new Vector2(WinW, WinH), parent);
            if (_w == null || _w.Screen == null) { _w = null; return; }

            var root = _w.Screen.transform;

            // 标题
            var title = _w.AddText("保存预设", 2.00f, TextAlignmentOptions.Center);
            if (title != null) PresetWindow.PlaceInRoot(title.transform, root, new Vector3(0f, 1.22f, -2f));

            // ---- 预设名 ----
            AddLabel(root, "预设名", LabelX, RowNameY);
            // ⚠️ 用 **GUITextField**（远端 pull 来的那份，H 帮助菜单在用）——
            //    它自己每帧读 Input.inputString，还处理中文输入法（imeCompositionMode /
            //    compositionString / 候选框跟随），不依赖原版 TextBoxTMP 的聚焦体系。
            //    我原来克隆原版 EnterCodeField 那套在 HUD 窗口里根本收不到键盘。
            _nameField = Light.UI.Window.GUITextField.Create(root,
                new Vector2(FieldW, FieldH), "给这个预设起个名字");
            _nameField.SetPosition(new Vector3(FieldCx, RowNameY, -2f));

            // ---- 作者 ----
            AddLabel(root, "作者", LabelX, RowAuthorY);
            _authorField = Light.UI.Window.GUITextField.Create(root,
                new Vector2(FieldW, FieldH), "留空 = 用你的玩家名");
            _authorField.SetPosition(new Vector3(FieldCx, RowAuthorY, -2f));
            _authorField.SetText(PresetLibrary.LocalPlayerName());   // 默认填玩家名

            // ---- 保存 / 取消 ----
            var save = HudUIButton.Create(root, "保存", new Vector2(1.70f, 0.56f), OnSave);
            if (save != null && save.GameObject != null)
                save.SetPosition(new Vector3(-0.98f, RowBtnY, -2f));

            var cancel = HudUIButton.Create(root, "取消", new Vector2(1.70f, 0.56f), Close);
            if (cancel != null && cancel.GameObject != null)
                cancel.SetPosition(new Vector3(0.98f, RowBtnY, -2f));

            try { _w.ApplyCjkFont(); } catch { }

            LightLogger.Log("[PresetSaveWindow] 已打开");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PresetSaveWindow.Open]", ex);
        }
    }

    public static void Close()
    {
        try { _w?.Close(); }
        catch (Exception ex) { LightLogger.LogWarning($"[PresetSaveWindow.Close] {ex.Message}"); }
        finally { _w = null; _nameField = null; _authorField = null; }
    }

    /// <summary>
    /// 在窗口里放一个左对齐的标签。
    ///
    /// ⚠️⚠️ 2026-10-06 修（用户："预设名 作者偏了，图里能看见"）：
    ///   <c>HudUIWindow.AddText</c> 建出来的 TMP 是**居中 pivot + 接近整窗宽的 rect**，
    ///   而我们用 <c>TextAlignmentOptions.Left</c> —— 文字是从 <c>pos.x - rectWidth/2</c> 开始画的。
    ///   所以直接给"想让它出现的 x"，实际会**往左偏半个 rect 宽**。
    ///
    ///   实测：LabelX = -2.35 应该落在屏幕 x≈358，实际跑到了 x≈40 ——
    ///   差了 318px ≈ 2.65 单位，正好是半个 rect 宽 ✓ 对上了。
    ///
    ///   修法：把 pivot 改成 <c>(0, 0.5)</c>（左边缘为锚）+ 给一个固定的小宽度 → 此时 pos.x 就是文字左边缘。
    ///   ⚠️ 这是本工程**第三次**踩同一类坑（前两次：RoleInfoPanel.PlaceIntro、ConfigUIPanel.AddRoleNameLabel）。
    /// </summary>
    private static void AddLabel(Transform root, string text, float x, float y)
    {
        try
        {
            var tmp = _w?.AddText(text, 1.55f, TextAlignmentOptions.Left);
            if (tmp == null) return;
            tmp.fontStyle = FontStyles.Bold;      // §12.1
            tmp.enableAutoSizing = false;         // §12.2
            tmp.fontSize = 1.55f;
            tmp.fontSizeMin = 1.55f;
            tmp.fontSizeMax = 1.55f;

            // ★ 定宽 + 左边缘 pivot —— 这样下面的 x 才是"文字左边缘"
            tmp.rectTransform.sizeDelta = new Vector2(1.10f, 0.40f);
            tmp.rectTransform.pivot = new Vector2(0f, 0.5f);

            PresetWindow.PlaceInRoot(tmp.transform, root, new Vector3(x, y, -2f));
        }
        catch { }
    }

    private static void OnSave()
    {
        try
        {
            var name = _nameField?.Text ?? "";
            var author = _authorField?.Text ?? "";

            if (string.IsNullOrWhiteSpace(name))
            {
                PresetNotice.Show("保存失败\n预设名不能为空");
                return;
            }

            // 作者留空 → 用玩家名（PresetLibrary 内部会兜底，这里显式取一次是为了提示里显示对）
            var who = PresetLibrary.ResolveAuthor(author);
            var (ok, msg) = PresetLibrary.SaveAs(name, who);

            Close();

            PresetNotice.Show(ok
                ? $"保存完成\n{name.Trim()}"
                : $"保存失败\n{msg}");

            if (ok) PresetWindow.RequestRefresh();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PresetSaveWindow.OnSave]", ex);
        }
    }
}
