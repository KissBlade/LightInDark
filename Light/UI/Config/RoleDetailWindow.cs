using System;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.UI.Window;
using UnityEngine;

// ⚠️ 工程里有**两个** MetaScreen：
//      · Light.UI.Window.MetaScreen      —— 只有裸 GenerateWindow(size,parent,pos,...)
//      · Light.UI.HudUI.MetaScreen       —— 带 background / sortingGroupOrder 的那个，**我们要的**
//    必须用别名消歧（否则要么解析不到、要么拿到错的那个）。
//    这条是照抄 MusicPlayerWindow 的，别删。
using MetaScreen = Light.UI.HudUI.MetaScreen;

namespace Light.UI.Config;

/// <summary>
/// **职业详情独立窗口** —— 点职业块后盖在原界面之上的小窗（用户 2026-10-06 要求：
/// "你开独立窗口。独立窗口比原来那个小一点"）。
///
/// ---- 和旧做法（就地切换）的区别 ----
/// 旧实现是 <c>ConfigUIPanel.ShowRole(block, _modPage.transform, ...)</c> —— 把 MOD 页的内容
/// **原地换掉**，返回时再换回来。问题是它和原页共用同一个宿主 <c>GameOptionsMenu</c>，
/// 层级/滚动/遮挡全纠缠在一起。
/// 现在改成独立窗口：窗口有自己的 SortingGroup(320)、自己的黑幕、自己的内容容器；
/// 关窗就销毁，原页面**从头到尾没被动过**（只临时藏了一下页签行）。
///
/// ---- 复用 <see cref="ConfigUIPanel.ShowRole"/> 的关键一步 ----
/// <c>ConfigUIPanel.Build</c> 里 <c>ResolveRowContainer</c> **优先**把行铺进
/// <c>_hostMenu</c> 的 <c>Scroller.Inner</c>（那是原版菜单的滚动容器）。
/// 独立窗口没有那个容器，所以进窗之前必须
/// <see cref="ConfigUIPanel.SetHostMenu"/><c>(null)</c> 把它摘掉 ——
/// 摘掉之后 <c>ResolveRowContainer</c> 会退回我们传入的 <c>fallback</c>（= 窗口内容节点）。
/// 关窗时由 <c>GameSettingMenuPatch.OnRolePageBack</c> 重新登记回去。
///
/// ---- 抄来的几条硬规矩（别改，都是踩过坑的）----
/// · 每个 <c>new GameObject</c> 都走 <c>LayerExpansion.GetUILayer()</c>（= layer 5）。
///   layer 0 归 Main Camera(depth −1)，会被 UI Camera(depth 99) 整个盖住。
/// · <c>sortingGroupOrder</c> 要够高：MetaScreen 会给窗口根挂一个 SortingGroup，
///   它把整窗合成一个渲染器，order 低了会被别的窗口整个盖住（音乐窗口 300，这里取 320）。
/// · <c>closeOnClickOutside: false</c> —— 本工程不用 HudUI 那块整屏 ClickGuard，
///   它的点击判定**不看 z**，会把窗口内部的空白也吃掉（表现为"点窗口里任何非按钮处都关窗"）。
/// · 窗口底图按 <c>WindowSize</c> 建，缩 <c>MetaWindow.localScale</c> 会让边框/遮罩/内容
///   **等比缩小**，内容坐标一个字都不用改（「更换背景图」/ 音乐窗口同款做法）。
///
/// ⚠️ 窗口**内部的排版**还没按设计图调 —— 详见 <see cref="Open"/> 末尾的注释。
/// </summary>
internal static class RoleDetailWindow
{
    // =====================================================================
    //  尺寸
    //
    //  "比原来那个小一点" —— 参照物是它盖住的那个 MOD 设置页
    //  （就是克隆出来的原版职业设置 GameOptionsMenu，视野约 9~10 宽）。
    //  这里给 7.2 × 5.0；和音乐窗口(9.2×5.2)比也明显小一圈。
    //  ⚠️ 设计图到了之后这里大概率要改。
    // =====================================================================
    private const float WindowW = 7.2f;
    private const float WindowH = 5.0f;

    /// <summary>整体缩放。和音乐窗口一致（0.82），视觉 5.90 × 4.10。</summary>
    private const float PanelScale = 0.82f;

    /// <summary>关闭按钮缩放（MetaScreen 默认那个偏小，用户反馈太小）。</summary>
    private const float CloseButtonScale = 1.15f;

    private static MetaScreen? _screen;
    private static GameObject? _windowObj;
    private static Action? _onBack;

    /// <summary>窗口是否开着。</summary>
    public static bool IsOpen => _windowObj != null;

    /// <summary>
    /// 打开职业详情窗口。
    /// </summary>
    /// <param name="block">该职业的配置块（键形如 <c>lid.role.&lt;CodeName&gt;</c>）。</param>
    /// <param name="parent">父级 —— 传 MOD 页的 transform，保证同场景同层。</param>
    /// <param name="onBack">点返回（或关窗）后回调，由调用方恢复页签行。</param>
    public static void Open(ConfigBlock block, Transform parent, Action onBack)
    {
        if (block == null) { LightLogger.LogWarning("[RoleDetailWindow] block 为 null，不打开"); return; }
        if (parent == null) { LightLogger.LogWarning("[RoleDetailWindow] parent 为 null，不打开"); return; }

        // ⚠️⚠️ **不要把窗口挂在传进来的 parent（_modPage）下面**（2026-10-06 修"窗口不居中"）。
        //
        //    _modPage 是克隆出来的 GameOptionsMenu 的子物体，它的原点**不在屏幕中心**
        //    → 挂它下面，无论怎么算 localPosition 都会偏（实测偏右上约 180px）。
        //
        //    抄 MusicPlayerWindow.ResolveParent 的做法：挂 **MainMenuManager / HudManager** ——
        //    它们的 transform 本身就是居中的，于是 (0,0,z) 就等于屏幕正中。
        //    这套在本工程已经实机验证过（音乐窗口、「更换背景图」都是这么摆的）。
        var centered = ResolveCenteredParent();
        var anchor = centered != null ? centered : parent;

        Close();    // 幂等：先关掉可能还开着的旧窗

        try
        {
            _screen = MetaScreen.GenerateWindow(
                new Vector2(WindowW, WindowH),
                anchor,                         // ★★★ 必须是 anchor，不是 parent！
                                                //    2026-10-06 踩过：上面算出了 anchor 却在这里传了 parent，
                                                //    结果"窗口不居中"查了三轮 —— 位置和之前像素级一致，
                                                //    因为父物体压根没换。
                                                //    anchor 是居中的物体 → (0,0) 就是屏幕正中。
                new Vector3(0f, 0f, -45f),
                withBlackScreen: true,          // 压暗背后的菜单，视觉上"进入二级页"
                closeOnClickOutside: false,     // 见类注释：不用整屏 ClickGuard
                background: Light.UI.HudUI.BackgroundSetting.Modern,
                withCloseButton: true,          // 右上角那个 X（下面 RewireCloseButton 会把它挪过去）
                sortingGroupOrder: 320);        // 压过音乐窗口(300)

            if (_screen == null)
            {
                LightLogger.LogWarning("[RoleDetailWindow] MetaScreen.GenerateWindow 返回 null，放弃");
                return;
            }

            var win = _screen.transform.parent;
            if (win == null)
            {
                LightLogger.LogWarning("[RoleDetailWindow] 窗口没有父物体，放弃");
                return;
            }

            _windowObj = win.gameObject;
            _windowObj.transform.localScale = Vector3.one * PanelScale;
            _onBack = onBack;

            // 把 MetaScreen 自带那个关闭按钮从左上角挪到右上角，并接到关窗上
            RewireCloseButton();

            // ⚠️⚠️ **必须自己驱动 UiModalGuard.Sweep**（2026-10-06 修"点击穿透"）。
            //
            //  MainMenuPatch.cs:1151 是 `if (sceneName != "MainMenu") return;`，
            //  而 `UiModalGuard.Sweep()` 在**那之后**（L1160）——
            //  规则编辑界面**不是 MainMenu 场景**，所以 Sweep 从来没跑过，
            //  原版控件从没被禁用 → 右上角的聊天/好友/设置按钮照样能点。
            //
            //  MusicPlayerWindow 也是自己驱动的（它注释里写了同样的原因）。
            //  我们这边没有 MonoBehaviour，所以在窗口物体上挂一个轻量驱动器。
            try
            {
                var drv = _windowObj.AddComponent<WindowGuardDriver>();
                LightLogger.Log("[RoleDetailWindow] 已挂 UiModalGuard 每帧驱动器（防点击穿透）");
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[RoleDetailWindow] 挂遮罩驱动器失败: {ex.Message}");
            }

            // 挡住窗口**以外**原版控件的点击（窗口开着时不该能点到背后的菜单）
            try { UiModalGuard.Push(win); } catch { }

            // ⚠️⚠️ **必须摘掉宿主**：否则行会铺进原版菜单的 Scroller.Inner，
            //      出现在窗口外面（甚至窗口后面）。详见类注释。
            // ⚠️⚠️ **两个都要**（2026-10-06 修"窗口是空的"）：
            //   · SetHostMenu(null)          —— 摘掉"MOD 设置页"那个 GameOptionsMenu
            //   · SetForceOwnContainer(true) —— **连 _templates 也跳过**
            //   只做前者不够：ResolveRowContainer 是 `_hostMenu ?? _templates`，
            //   _templates 仍是原版菜单，行会被铺回原版滚动容器 → 窗口里什么都没有。
            ConfigUIPanel.SetHostMenu(null);
            ConfigUIPanel.SetForceOwnContainer(true);
            ConfigUIPanel.SetHideBackButton(true);   // 不要「< 返回」，改用右上角的 X

            // ShowRole 内部会在顶部画「< 返回」并把它接到我们传进去的回调上 ✓
            ConfigUIPanel.ShowRole(block, _screen.transform, Back);

            LightLogger.Log($"[RoleDetailWindow] 已打开职业详情窗：{block.DisplayName}" +
                            $"（窗 {WindowW}×{WindowH} ×{PanelScale}，容器={_screen.transform.name}）");

            // ⚠️ TODO（等设计图）：窗口**内部**的排版还没调。
            //    现在沿用 ConfigUIPanel 为原版菜单定的坐标常量
            //    （StartY=0.713 / RowX=0.952 / HeaderX=-0.903 / SpacingY=0.45），
            //    它们是"左边有地图预览"那套布局的产物 —— 在干净的窗口里会偏右下。
            //    设计图到了之后应改成窗口自己的基准（行居中、从顶部往下排）。
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[RoleDetailWindow.Open]", ex);
        }
    }

    // =====================================================================
    //  窗口定位 / 关窗按钮
    // =====================================================================

    /// <summary>
    /// 求"把窗口放到屏幕正中"时该用的 localPosition（相对 parent）。
    ///
    /// ⚠️ **不能直接传 (0,0)** —— 那是 parent（`_modPage`）的原点，而它**不在屏幕中心**。
    ///    用户截图里窗口明显偏右上就是这个原因。
    ///    做法：把**屏幕中心**按 parent 所在平面的深度反投影回世界，再转到 parent 的本地坐标。
    /// </summary>
    private static Vector3 WinLocalPos(Transform parent)
    {
        var fallback = new Vector3(0f, 0f, -45f);
        try
        {
            if (parent == null) return fallback;

            var cam = FindUiCamera();
            if (cam == null) return fallback;

            // 用 parent 自己的位置求"深度"，保证换算出来和它在同一平面
            float depth = cam.WorldToScreenPoint(parent.position).z;
            if (depth <= 0f) return fallback;

            var world = cam.ScreenToWorldPoint(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, depth));
            var local = parent.InverseTransformPoint(world);
            LightLogger.Log($"[RoleDetailWindow] 居中计算：相机={cam.name}(ortho={cam.orthographic}, depth={cam.depth})，" +
                            $"深度={depth:F2}，屏幕={Screen.width}×{Screen.height}，算得 local=({local.x:F2}, {local.y:F2})" +
                            $"，parent={parent.name} parentWorld={parent.position}");
            return new Vector3(local.x, local.y, -45f);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[RoleDetailWindow.WinLocalPos] {ex.Message}");
            return fallback;
        }
    }

    /// <summary>找"画 UI 层"的那台相机（本工程不同场景相机配置不同，不能写死 Camera.main）。</summary>
    private static Camera? FindUiCamera()
    {
        try
        {
            int layer = LayerExpansion.GetUILayer();
            Camera? best = null;
            float bestDepth = float.MinValue;

            var cams = Camera.allCameras;
            if (cams != null)
            {
                foreach (var c in cams)
                {
                    if (c == null || !c.isActiveAndEnabled) continue;
                    if ((c.cullingMask & (1 << layer)) == 0) continue;   // 不画 UI 层
                    if (c.depth > bestDepth) { bestDepth = c.depth; best = c; }
                }
            }
            return best != null ? best : Camera.main;
        }
        catch { return Camera.main; }
    }

    /// <summary>
    /// 把 MetaScreen 自带的关闭按钮从**左上角外侧**挪到**右上角外侧**，并接到关窗上。
    ///
    /// ⚠️ `HudUI.GenerateWindow` 里 Modern 风格的关闭按钮固定建在
    ///    `(-size.x/2 - 0.3, size.y/2 + 0.2)` —— 那是**左上角**。
    ///    用户 2026-10-06 要右上角，所以自己挪（做法同 MusicPlayerWindow.RewireCloseButton）。
    /// </summary>
    private static void RewireCloseButton()
    {
        try
        {
            if (_windowObj == null) return;
            var t = _windowObj.transform;

            Transform? close = null;
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (c != null && c.name == "CloseButton") { close = c; break; }
            }
            if (close == null)
            {
                LightLogger.LogWarning("[RoleDetailWindow] 窗口里没找到 CloseButton，X 不会出现");
                return;
            }

            // 左上角 → 右上角（保持同一 z）
            // 关闭按钮放**左上角外侧**（用户 2026-10-06 改的主意 —— 之前要右上角）。
            // ⚠️ 这其实就是 MetaScreen 建它时的默认位置 `(-size.x/2 - 0.3, size.y/2 + 0.2)`，
            //    这里显式写出来是为了"以后想再挪"时一眼能找到，不用去翻 HudUI。
            close.localPosition = new Vector3(-WindowW * 0.5f - 0.3f, WindowH * 0.5f + 0.2f, close.localPosition.z);

            // ⚠️ 放大：MetaScreen 建出来的关闭按钮偏小（用户反馈"太小了"），
            //    做法同 MusicPlayerWindow.CloseButtonScale。
            close.localScale = Vector3.one * CloseButtonScale;

            var pb = close.GetComponent<PassiveButton>();
            if (pb == null) return;

            // ⚠️ 必须**替换**而不是 AddListener —— 见 AGENTS.md §4.5，
            //    克隆来的控件自带原版监听，只追加会同时触发别的行为。
            pb.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            pb.OnClick.AddListener((UnityEngine.Events.UnityAction)(() =>
            {
                try
                {
                    var clip = Light.UI.Window.VanillaAsset.FindSoundClip("UI_Select");
                    if (clip != null) SoundManager.Instance.PlaySound(clip, false, 0.8f);
                }
                catch { }
                Back();
            }));

            LightLogger.Log($"[RoleDetailWindow] 关闭按钮已放在左上角（{-(WindowW * 0.5f + 0.3f)}, {WindowH * 0.5f + 0.2f}）");
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[RoleDetailWindow.RewireCloseButton] {ex.Message}");
        }
    }
    /// <summary>点「&lt; 返回」：关窗，然后通知调用方恢复原界面。</summary>
    public static void Back()
    {
        var cb = _onBack;
        Close();
        try { cb?.Invoke(); }
        catch (Exception ex) { LightLogger.LogWarning($"[RoleDetailWindow.Back 回调] {ex.Message}"); }
    }

    /// <summary>关窗并销毁。幂等。</summary>
    public static void Close()
    {
        _onBack = null;

        try
        {
            ConfigUIPanel.Clear();
            // ⚠️ 必须关掉，否则原版规则编辑界面也会把行铺进自建容器（届时会整页空白）
            ConfigUIPanel.SetForceOwnContainer(false);
            ConfigUIPanel.SetHideBackButton(false);
        }
        catch { }

        try
        {
            if (_windowObj != null)
            {
                UiModalGuard.Pop(_windowObj.transform);
                UnityEngine.Object.Destroy(_windowObj);
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[RoleDetailWindow.Close] {ex.Message}");
        }

        _windowObj = null;
        _screen = null;
    }

    /// <summary>
    /// 找一个**原点在屏幕正中**的父物体给窗口用。
    ///
    /// 照抄 <c>MusicPlayerWindow.ResolveParent()</c> —— 那套在本工程已经实机验证过：
    ///   · MainMenu / MatchMaking 场景 → `MainMenuManager.Instance.transform`
    ///   · 其它场景 → `HudManager.Instance.transform`
    ///   · 再退 → `Camera.main.transform`
    /// 挂这些物体下面时，`localPosition = (0,0,z)` 就等于屏幕正中。
    /// </summary>
    private static Transform? ResolveCenteredParent()
    {
        try
        {
            string scene = "";
            try { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? ""; } catch { }

            if (scene == "MainMenu" || scene == "MatchMaking")
            {
                var menu = DestroyableSingleton<MainMenuManager>.Instance;
                if (menu != null) return menu.transform;
            }

            var hud = HudManager.Instance;
            if (hud != null) return hud.transform;

            var menu2 = DestroyableSingleton<MainMenuManager>.Instance;
            if (menu2 != null) return menu2.transform;

            var cam = Camera.main;
            return cam != null ? cam.transform : null;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[RoleDetailWindow.ResolveCenteredParent] {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// 职业详情窗口的**每帧遮罩驱动器**。
///
/// ⚠️ 为什么需要它（2026-10-06 用户报"窗口遮罩挡不住聊天/好友/设置按钮，还能点"）：
///  `UiModalGuard` 只负责**算出该禁用哪些原版控件**，真正干活的是每帧调用的 `Sweep()`。
///  而主菜单那边 `MainMenuPatch.cs:1151` 是 `if (sceneName != "MainMenu") return;`，
///  `Sweep()` 在那之后 —— **规则编辑界面不是 MainMenu 场景，Sweep 从来没跑过**。
///  所以窗口必须自己驱动（`MusicPlayerWindow` 也是这么做的）。
///
/// ⚠️ 托管 MonoBehaviour **必须**先 <c>ClassInjector.RegisterTypeInIl2Cpp&lt;T&gt;()</c>，
///    否则 <c>AddComponent&lt;T&gt;()</c> 抛 TypeInitializationException（AGENTS.md §11.2）。
///    写在静态构造函数里最稳。
/// </summary>
public sealed class WindowGuardDriver : MonoBehaviour
{
    static WindowGuardDriver()
    {
        try { Il2CppInterop.Runtime.Injection.ClassInjector.RegisterTypeInIl2Cpp<WindowGuardDriver>(); }
        catch { }
    }

    /// <summary>每 N 帧强制全扫一次 —— Sweep 内部有"Buttons 数量没变就短路"的优化，
    /// 而原版会自己把按钮 enabled 回来，所以要低频兜底（同 MusicPlayerWindow。ForceSweepEvery）。</summary>
    private const int ForceSweepEvery = 20;

    private int _tick;

    private void Update()
    {
        try { UiModalGuard.Sweep(); } catch { }

        if (++_tick >= ForceSweepEvery)
        {
            _tick = 0;
            try { UiModalGuard.ForceSweep(); } catch { }
        }
    }
}