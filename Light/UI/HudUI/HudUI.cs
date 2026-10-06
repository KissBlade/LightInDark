using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Injection;
using LightInDark;
using LightInDark.UI.Window;
using Light.UI.Window;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.UI;
using LightInDark.Core;
using Button = UnityEngine.UI.Button;
using Object = UnityEngine.Object;
using Color = LightInDark.Color;

namespace Light.UI.HudUI;

/// <summary>
/// 按钮尺寸预设
/// </summary>
public static class ButtonSize
{
    public static readonly Vector2 Square = new(0.8f, 0.8f);
    public static readonly Vector2 Rectangle = new(2.5f, 0.5f);
    public static readonly Vector2 SmallSquare = new(0.4f, 0.4f);
    public static readonly Vector2 LongRectangle = new(3.5f, 0.5f);
    public static readonly Vector2 Wide = new(4.0f, 0.6f);
}

/// <summary>
/// 窗口尺寸预设
/// </summary>
public static class WindowSize
{
    /// <summary>角色选择窗口</summary>
    public static readonly Vector2 RoleSelect = new(7.6f, 4.2f);

    /// <summary>标准窗口</summary>
    public static readonly Vector2 Standard = new(5f, 3f);

    /// <summary>小窗口</summary>
    public static readonly Vector2 Small = new(3.5f, 1.5f);

    /// <summary>确认对话框</summary>
    public static readonly Vector2 Confirm = new(3.9f, 1.14f);

    /// <summary>大窗口</summary>
    public static readonly Vector2 Large = new(8f, 5f);

    /// <summary>帮助窗口</summary>
    public static readonly Vector2 Help = new(9f, 5.5f);

    /// <summary>带 ScrollView 的窗口内容区域（尺寸 6.9×3.0）</summary>
    public static readonly Vector2 ScrollViewContent = new(6.9f, 3.0f);
}

/// <summary>
/// 背景样式枚举
/// </summary>
public enum BackgroundSetting
{
    Off,
    Old,
    Modern,
}

/// <summary>
/// 字体缓存，从 Among Us 原版 VersionShower 克隆。
/// Among Us 使用 Barlow 字体（Barlow-Black SDF / Barlow-BoldItalic SDF）。
/// VersionShower 有公开字段 text (TextMeshPro)，字体在其 prefab 中序列化。
/// </summary>
public static class HudUIFont
{
    private static TMP_FontAsset? _fontAsset;
    private static Material? _fontMaterial;
    private static bool _triedInit;

    public static TMP_FontAsset FontAsset
    {
        get {
            try
            {     EnsureLoaded(); return _fontAsset!;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[HudUI.get]", ex); return default;
            } }
    }

    public static Material FontMaterial
    {
        get {
            try
            {     EnsureLoaded(); return _fontMaterial!;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[HudUI.get]", ex); return default;
            } }
    }

    public static void EnsureLoaded()
    {
        try
        {
            if (_triedInit) return;
            _triedInit = true;

            // 方案1：从 VersionShower.text 获取（VersionShower 有公开字段 text）
            try
            {
                var versionShower = Object.FindObjectOfType<VersionShower>();
                if (versionShower != null && versionShower.text != null && versionShower.text.font != null)
                {
                    _fontAsset = versionShower.text.font;
                    _fontMaterial = versionShower.text.fontMaterial;
                    return;
                }
            }
            catch { }

            // 方案2：从 HudManager.Instance.Dialogue.target 获取
            try
            {
                if (HudManager.Instance != null && HudManager.Instance.Dialogue != null &&
                    HudManager.Instance.Dialogue.target != null && HudManager.Instance.Dialogue.target.font != null)
                {
                    _fontAsset = HudManager.Instance.Dialogue.target.font;
                    _fontMaterial = HudManager.Instance.Dialogue.target.fontMaterial;
                    return;
                }
            }
            catch { }

            // 方案3：从场景中任意 TextMeshPro 获取
            try
            {
                var anyTmp = Object.FindObjectOfType<TextMeshPro>();
                if (anyTmp != null && anyTmp.font != null)
                {
                    _fontAsset = anyTmp.font;
                    _fontMaterial = anyTmp.fontMaterial;
                    return;
                }
            }
            catch { }

            // 方案4：TMP_Settings 默认
            try
            {
                if (TMP_Settings.defaultFontAsset != null)
                {
                    _fontAsset = TMP_Settings.defaultFontAsset;
                    _fontMaterial = TMP_Settings.defaultFontAsset.material;
                }
            }
            catch { }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.EnsureLoaded]", ex);
        }
    }
}

/// <summary>
/// 创建 TextMeshPro，使用 Among Us 原版字体。
/// </summary>
internal static class HudUITextHelper
{
    /// <param name="overrideFont">
    /// 指定字体（可选）。传 null 就走原来的 <see cref="HudUIFont"/> 自动解析。
    /// 背景图替换那套界面会传"简中字体模板"的字体（见 MenuTextTemplate2），
    /// 让中文用游戏自带的 NotoSansSC 字形，而不是靠 fallback 硬撑。
    /// </param>
    public static TextMeshPro Create(Transform parent, TMP_FontAsset? overrideFont = null,
        Material? overrideMat = null)
    {
        try
        {
            var obj = new GameObject("Text");
            obj.layer = LayerMask.NameToLayer("UI");
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = Vector3.zero;

            var tmp = obj.AddComponent<TextMeshPro>();

            if (overrideFont != null)
            {
                // ⚠️ 换字体**必须同时换材质**：字形来自字体自己的图集材质，
                //    只写 font 会继续用旧材质 → 字看不见或乱码。
                tmp.font = overrideFont;
                if (overrideMat != null) tmp.fontSharedMaterial = overrideMat;
            }
            else
            {
                HudUIFont.EnsureLoaded();
                if (HudUIFont.FontAsset != null)
                {
                    tmp.font = HudUIFont.FontAsset;
                    tmp.fontSharedMaterial = HudUIFont.FontMaterial;
                }
            }

            tmp.enableAutoSizing = false;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;
            tmp.outlineWidth = 0.15f;
            tmp.outlineColor = UnityEngine.Color.black;

            return tmp;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.Create]", ex); return default;
        }
    }
}

// =====================================================================
// MetaScreen
// =====================================================================

/// <summary>
/// GUI 屏幕，MonoBehaviour，通过 ClassInjector 注册。
/// </summary>
public class MetaScreen : MonoBehaviour
{
    static MetaScreen()
    {
        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<MetaScreen>();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.MetaScreen]", ex);
        }
    }

    public void Awake()
    {
    }

    private GameObject? _combinedObject;
    private Vector2 _border;

    public Vector2 Border
    {
        get => _border;
        private set => _border = value;
    }

    /// <summary>
    /// 设置 Widget（旧式 IMetaWidgetOld 接口暂不支持，仅支持 GUIWidget）
    /// </summary>
    public void SetWidget(GUIWidget? widget, out Size actualSize)
    {
        try
        {
            SetWidget(widget, new Vector2(0f, 1f), out actualSize);
        }
        catch (Exception ex)
        {
            actualSize = default; LightLogger.LogError("[HudUI.SetWidget]", ex);
        }
    }

    public void SetWidget(GUIWidget? widget, Vector2 anchor, out Size actualSize)
    {
        try
        {
            ClearWidget();

            if (widget == null)
            {
                actualSize = Size.Zero;
                return;
            }

            var anchorRecord = new Anchor(anchor,
                new Vector3(_border.x * (anchor.x - 0.5f), _border.y * (anchor.y - 0.5f), -0.1f));

            var obj = widget.Instantiate(anchorRecord, new Size(_border), out actualSize);
            if (obj != null)
            {
                obj.transform.SetParent(transform, false);
            }
        }
        catch (Exception ex)
        {
            actualSize = default; LightLogger.LogError("[HudUI.SetWidget]", ex);
        }
    }

    private void ClearWidget()
    {
        try
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name != "BorderLine")
                    Object.Destroy(child.gameObject);
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.ClearWidget]", ex);
        }
    }

    /// <summary>
    /// 关闭窗口
    /// </summary>
    public void CloseScreen()
    {
        try
        {
            var target = _combinedObject ?? gameObject;
            // 立即销毁避免与下次窗口重叠
            Object.DestroyImmediate(target);
        }
        catch { }
    }

    // ---- 静态资源 ----

    // 背景精灵图（九宫格）
    private static Sprite FrameSprite => HudUIAssets.FrameSprite;
    private static Sprite InnerSprite => HudUIAssets.InnerSprite;

    // 关闭按钮精灵图
    private static Sprite CloseButtonNormal => HudUIAssets.CloseNormal;
    private static Sprite CloseButtonHover => HudUIAssets.CloseHover;

    // 导航按钮精灵图
    private static Sprite NavLeftNormal => HudUIAssets.NavLeftNormal;
    private static Sprite NavLeftHover => HudUIAssets.NavLeftHover;
    private static Sprite NavRightNormal => HudUIAssets.NavRightNormal;
    private static Sprite NavRightHover => HudUIAssets.NavRightHover;

    // ---- 生成窗口 ----

    /// <summary>
    /// 生成屏幕
    /// </summary>
    public static MetaScreen GenerateScreen(Vector2 size, Transform? parent, Vector3 localPos,
        BackgroundSetting backgroundSetting, bool withBlackScreen, bool withClickGuard,
        int sortingGroupOrder = 100)
    {
        try
        {
            var window = CreateObject("MetaWindow", parent, localPos);
            var sortGroup = window.AddComponent<SortingGroup>();
            sortGroup.sortingOrder = sortingGroupOrder;

            if (backgroundSetting == BackgroundSetting.Old)
            {
                var renderer = CreateObject<SpriteRenderer>("Background", window.transform, new Vector3(0, 0, 0.1f));
                renderer.sprite = Light.UI.Window.VanillaAsset.PopUpBackSprite;
                renderer.drawMode = SpriteDrawMode.Sliced;
                renderer.tileMode = SpriteTileMode.Continuous;
                renderer.size = size + new Vector2(0.45f, 0.35f);
                renderer.gameObject.layer = LayerExpansion.GetUILayer();
            }
            else if (backgroundSetting == BackgroundSetting.Modern)
            {
                var inner = CreateObject<SpriteRenderer>("Inner", window.transform, new Vector3(0, 0, 0.1f));
                inner.sprite = InnerSprite;
                inner.drawMode = SpriteDrawMode.Sliced;
                inner.tileMode = SpriteTileMode.Continuous;
                inner.size = size + new Vector2(0.75f, 0.75f);
                inner.gameObject.layer = LayerExpansion.GetUILayer();
                inner.color = UnityEngine.Color.white.RGBMultiplied(0.55f);

                var frame = CreateObject<SpriteRenderer>("Frame", window.transform, new Vector3(0, 0, -0.01f));
                frame.sprite = FrameSprite;
                frame.drawMode = SpriteDrawMode.Sliced;
                frame.tileMode = SpriteTileMode.Continuous;
                frame.size = size + new Vector2(0.75f, 0.75f);
                frame.gameObject.layer = LayerExpansion.GetUILayer();
            }

            if (withBlackScreen)
            {
                var renderer = CreateObject<SpriteRenderer>("BlackScreen", window.transform, new Vector3(0, 0, 0.2f));
                renderer.sprite = Light.UI.Window.VanillaAsset.FullScreenSprite;
                renderer.drawMode = SpriteDrawMode.Sliced;
                renderer.size = new Vector2(30f, 30f);
                renderer.color = new UnityEngine.Color(0, 0, 0, 0.4226f);
                renderer.gameObject.layer = LayerExpansion.GetUILayer();
            }

            if (withClickGuard)
            {
                var collider = CreateObject<BoxCollider2D>("ClickGuard", window.transform, new Vector3(0, 0, 0.2f));
                collider.isTrigger = true;
                collider.gameObject.layer = LayerExpansion.GetUILayer();
                collider.size = new Vector2(100f, 100f);
                collider.gameObject.SetUpButton(false, playSound: false);
            }

            var screen = CreateObject<MetaScreen>("Screen", window.transform, Vector3.zero);
            screen.Border = size;
            screen._combinedObject = window;

            return screen;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.GenerateScreen]", ex); return default;
        }
    }

    /// <summary>
    /// 关闭按钮（左上角那个 X）的**默认缩放**。
    ///
    /// ⚠️ 2026-10-06 用户连着几轮说"关闭按钮太小"，而我一直在**外面**用
    ///    `FindDeep("CloseButton")` 改 `localScale` —— **那套从来就没生效过**，两个原因：
    ///      ① 原来这里是**写死的 0.57**，外面怎么改都会被这里覆盖；
    ///      ② `HudUIWindow.GameObject` 是 **Screen 那一层**，而 CloseButton 是它的**兄弟**
    ///         （都挂在 MetaWindow 下）→ 从 `GameObject` 往下找**根本找不到**。
    ///    → **尺寸就该在这里改**（用户原话："你去动 HudUI 吧"）。
    ///    所有走 GenerateWindow 的窗口（预设窗口、提示窗口、音乐窗口…）一起生效。
    /// </summary>
    public const float DefaultCloseButtonScale = 0.90f;   // 原来是硬编码的 0.57

    /// <summary>
    /// 生成窗口
    /// </summary>
    public static MetaScreen GenerateWindow(Vector2 size, Transform? parent, Vector3 localPos,
        bool withBlackScreen = true, bool closeOnClickOutside = false,
        BackgroundSetting background = BackgroundSetting.Modern, bool withCloseButton = true,
        int sortingGroupOrder = 100, float closeButtonScale = DefaultCloseButtonScale)
    {
        try
        {
            var screen = GenerateScreen(size, parent, localPos, background, withBlackScreen, true, sortingGroupOrder);
            var obj = screen.transform.parent.gameObject;

            if (withCloseButton)
            {
                // ⚠️ 位置**不跟着缩放走**（2026-10-06 修：我一度让它按 `closeButtonScale/0.57`
                //   外移，结果按钮被推到窗口外面很远 —— 用户："太远了"）。
                //   算一下就知道不用挪：0.57 缩放时按钮半边 = 0.85×0.57/2 = 0.242，
                //   内边缘在 `-0.3 + 0.242 = -0.058`（正好贴窗口左缘）；放大到 0.90 后半边 = 0.3825，
                //   用同一个 -0.3，内边缘会稍微压进窗口一点 —— **这正是变大后该有的样子**。
                if (background == BackgroundSetting.Modern)
                {
                    // Modern 风格关闭按钮 — 左上角外侧
                    var collider = CreateObject<BoxCollider2D>("CloseButton", obj.transform,
                        new Vector3(-size.x / 2f - 0.3f, size.y / 2f + 0.2f, 0f));
                    collider.transform.localScale = new Vector3(closeButtonScale, closeButtonScale, 1f);
                    collider.isTrigger = true;
                    collider.gameObject.layer = LayerExpansion.GetUILayer();
                    collider.size = new Vector2(0.85f, 0.85f);

                    var renderer = collider.gameObject.AddComponent<SpriteRenderer>();
                    renderer.sprite = CloseButtonNormal;

                    var button = collider.gameObject.SetUpButton(true, renderer, playSound: true);
                    button.OnClick.AddListener((UnityAction)(() => Object.Destroy(obj)));
                    button.OnMouseOver.AddListener((UnityAction)(() => renderer.sprite = CloseButtonHover));
                    button.OnMouseOut.AddListener((UnityAction)(() => renderer.sprite = CloseButtonNormal));
                }
                else
                {
                    // Old 风格关闭按钮
                    var collider = CreateObject<BoxCollider2D>("CloseButton", obj.transform,
                        new Vector3(-size.x / 2f - 0.3f, size.y / 2f + 0.2f, 0f));
                    collider.transform.localScale = new Vector3(closeButtonScale, closeButtonScale, 1f);
                    collider.isTrigger = true;
                    collider.gameObject.layer = LayerExpansion.GetUILayer();
                    collider.size = new Vector2(0.85f, 0.85f);

                    var renderer = collider.gameObject.AddComponent<SpriteRenderer>();
                    renderer.sprite = CloseButtonNormal;

                    var button = collider.gameObject.SetUpButton(true, renderer, playSound: true);
                    button.OnClick.AddListener((UnityAction)(() => Object.Destroy(obj)));
                    button.OnMouseOver.AddListener((UnityAction)(() => renderer.sprite = CloseButtonHover));
                    button.OnMouseOut.AddListener((UnityAction)(() => renderer.sprite = CloseButtonNormal));
                }
            }

            if (closeOnClickOutside)
            {
                var clickGuard = obj.transform.FindChild("ClickGuard");
                if (clickGuard != null)
                {
                    clickGuard.GetComponent<PassiveButton>().OnClick.AddListener((UnityAction)(() => Object.Destroy(obj)));
                }
            }

            return screen;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.GenerateWindow]", ex); return default;
        }
    }

    /// <summary>
    /// 生成带导航按钮的窗口
    /// </summary>
    public static MetaScreen GenerateWindow(Func<int, GUIWidget> widgetGenerator, int index, (int min, int max) range,
        Vector2 size, Transform? parent, Vector3 localPos,
        bool withBlackScreen = true, bool closeOnClickOutside = false, bool withCloseButton = true)
    {
        try
        {
            var window = GenerateWindow(size, parent, localPos, withBlackScreen, closeOnClickOutside,
                BackgroundSetting.Modern, withCloseButton);

            SetUpNavButton(window, increment =>
            {
                if (increment)
                {
                    index++;
                    if (index >= range.max) index = range.min;
                }
                else
                {
                    index--;
                    if (index < range.min) index = range.max - 1;
                }
                window.SetWidget(widgetGenerator.Invoke(index), out _);
            });

            window.SetWidget(widgetGenerator.Invoke(index), out _);
            return window;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.GenerateWindow]", ex); return default;
        }
    }

    /// <summary>
    /// 设置导航按钮
    /// </summary>
    public static void SetUpNavButton(MetaScreen screen, Action<bool> navFunc)
    {
        try
        {
            var obj = screen.transform.parent.gameObject;

            PassiveButton GenerateButton(float x, int img)
            {
                var collider = CreateObject<BoxCollider2D>("NavButton", obj.transform,
                    new Vector3(screen.Border.x / 2f + 0.3f - x, screen.Border.y / 2f + 0.25f, 0f));
                collider.transform.localScale = new Vector3(0.57f, 0.57f, 1f);   // NavButton 不是关闭按钮，保持原样
                collider.isTrigger = true;
                collider.gameObject.layer = LayerExpansion.GetUILayer();
                collider.size = new Vector2(0.65f, 0.65f);

                var renderer = collider.gameObject.AddComponent<SpriteRenderer>();
                renderer.sprite = img == 0 ? NavLeftNormal : NavRightNormal;

                var button = collider.gameObject.SetUpButton(true, renderer, playSound: true);
                button.OnMouseOver.AddListener((UnityAction)(() => renderer.sprite = img == 0 ? NavLeftHover : NavRightHover));
                button.OnMouseOut.AddListener((UnityAction)(() => renderer.sprite = img == 0 ? NavLeftNormal : NavRightNormal));
                return button;
            }

            // 左箭头（上一页）
            GenerateButton(0.7f, 0).OnClick.AddListener((UnityAction)(() => navFunc.Invoke(false)));
            // 右箭头（下一页）
            GenerateButton(0.3f, 2).OnClick.AddListener((UnityAction)(() => navFunc.Invoke(true)));
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.SetUpNavButton]", ex);
        }
    }

    // ---- 辅助方法 ----

    private static GameObject CreateObject(string name, Transform? parent, Vector3 localPos)
    {
        try
        {
            var obj = new GameObject(name);
            obj.layer = LayerExpansion.GetUILayer();
            if (parent != null) obj.transform.SetParent(parent, false);
            obj.transform.localPosition = localPos;
            obj.transform.localScale = Vector3.one;
            return obj;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.CreateObject]", ex); return default;
        }
    }

    private static T CreateObject<T>(string name, Transform? parent, Vector3 localPos) where T : Component
    {
        var obj = CreateObject(name, parent, localPos);
        return obj.AddComponent<T>();
    }
}

// =====================================================================
// HudUIButton — 自定义按钮，使用精灵图材质
// =====================================================================

/// <summary>
/// 自定义按钮，使用精灵图材质。支持普通/悬停/选中三态。
/// </summary>
public class HudUIButton
{
    public GameObject GameObject { get; private set; }
    public SpriteRenderer Renderer { get; private set; }
    public TextMeshPro Text { get; private set; }
    public PassiveButton Button { get; private set; }
    public BoxCollider2D Collider { get; private set; }

    private Sprite _normalSprite;
    private Sprite _hoverSprite;
    private Sprite _selectedSprite;
    private Sprite _selectedHoverSprite;
    private bool _isSelected;
    private Vector2 _size;

    private HudUIButton(GameObject obj)
    {
        try
        {
            GameObject = obj;
            Renderer = obj.GetComponent<SpriteRenderer>();
            Text = obj.GetComponentInChildren<TextMeshPro>();
            Button = obj.GetComponent<PassiveButton>();
            Collider = obj.GetComponent<BoxCollider2D>();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.HudUIButton]", ex);
        }
    }

    /// <summary>
    /// 创建现代风格按钮（使用 GUI/Button.png 精灵图）。
    /// 当 size 为 null 时，按钮尺寸自动匹配文本。
    /// </summary>
    public static HudUIButton Create(Transform parent, string text = "",
        Vector2? size = null, Action? onClick = null)
    {
        try
        {
            var obj = new GameObject("HudUIButton");
            obj.layer = LayerExpansion.GetUILayer();
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = Vector3.zero;
            obj.transform.localScale = Vector3.one;

            var renderer = obj.AddComponent<SpriteRenderer>();
            renderer.sprite = HudUIAssets.ButtonNormal;
            renderer.drawMode = SpriteDrawMode.Sliced;
            renderer.tileMode = SpriteTileMode.Continuous;

            var collider = obj.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;

            obj.AddComponent<SortingGroup>();

            var tmp = HudUITextHelper.Create(obj.transform);
            tmp.text = text;
            tmp.fontSize = 2f;
            tmp.fontSizeMax = 2f;
            tmp.fontSizeMin = 1f;
            tmp.m_fontSizeBase = 3f;
            tmp.enableAutoSizing = true;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = UnityEngine.Color.white;
            tmp.raycastTarget = false;
            tmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            tmp.rectTransform.localPosition = new Vector3(0f, 0f, -0.1f);
            tmp.ForceMeshUpdate();

            var hudButton = new HudUIButton(obj);
            hudButton._normalSprite = HudUIAssets.ButtonNormal;
            hudButton._hoverSprite = HudUIAssets.ButtonHover;
            hudButton._selectedSprite = HudUIAssets.ButtonSelected;
            hudButton._selectedHoverSprite = HudUIAssets.ButtonSelectedHover;

            // 尺寸：指定了用指定的，没指定用文本 × 1.5
            var btnSize = size ?? CalcAutoSize(tmp);
            hudButton.ApplySize(btnSize);

            var button = obj.AddComponent<PassiveButton>();
            // ⚠️ 必须在这里补一次：HudUIButton 的构造函数是在 AddComponent<PassiveButton>()
            //    **之前**调用的，那时 obj 上还没有 PassiveButton → `Button` 属性恒为 null。
            //    外部代码（如 BackgroundPanel）想给按钮追加 OnMouseOver 监听时会 NRE。
            hudButton.Button = button;
            button.OnMouseOver = new UnityEvent();
            button.OnMouseOut = new UnityEvent();
            button.OnClick = new Button.ButtonClickedEvent();

            button.OnMouseOver.AddListener((UnityAction)(() =>
            {
                renderer.sprite = hudButton._isSelected ? hudButton._selectedHoverSprite : hudButton._hoverSprite;
                try { SoundManager.Instance.PlaySound(Light.UI.Window.VanillaAsset.FindSoundClip("UI_Hover"), false, 0.8f); } catch { }
            }));
            button.OnMouseOut.AddListener((UnityAction)(() =>
            {
                renderer.sprite = hudButton._isSelected ? hudButton._selectedSprite : hudButton._normalSprite;
            }));

            var localOnClick = onClick;
            button.OnClick.AddListener((UnityAction)(() =>
            {
                try { SoundManager.Instance.PlaySound(Light.UI.Window.VanillaAsset.FindSoundClip("UI_Select"), false, 0.8f); } catch { }
                localOnClick?.Invoke();
            }));

            return hudButton;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.Create]", ex); return default;
        }
    }

    /// <summary>
    /// 创建简单颜色按钮（使用 ColorButton.png / ColorButtonSelected.png）。
    /// 当 size 为 null 时，按钮尺寸自动匹配文本。
    /// </summary>
    public static HudUIButton CreateColorButton(Transform parent, string text = "",
        Vector2? size = null, Action? onClick = null, Color? color = null)
    {
        try
        {
            var btnColor = color?.ToUnityColor() ?? UnityEngine.Color.white;

            var obj = new GameObject("HudUIColorButton");
            obj.layer = LayerExpansion.GetUILayer();
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = Vector3.zero;
            obj.transform.localScale = Vector3.one;

            var renderer = obj.AddComponent<SpriteRenderer>();
            renderer.sprite = HudUIAssets.ColorButtonSprite;
            renderer.drawMode = SpriteDrawMode.Sliced;
            renderer.tileMode = SpriteTileMode.Continuous;
            renderer.color = btnColor;

            var collider = obj.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;

            obj.AddComponent<SortingGroup>();

            var tmp = HudUITextHelper.Create(obj.transform);
            tmp.text = text;
            tmp.fontSize = 2f;
            tmp.fontSizeMax = 2f;
            tmp.fontSizeMin = 1f;
            tmp.m_fontSizeBase = 3f;
            tmp.enableAutoSizing = true;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = UnityEngine.Color.white;
            tmp.raycastTarget = false;
            tmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            tmp.rectTransform.localPosition = new Vector3(0f, 0f, -0.1f);
            tmp.ForceMeshUpdate();

            var hudButton = new HudUIButton(obj);
            hudButton._normalSprite = HudUIAssets.ColorButtonSprite;
            hudButton._hoverSprite = HudUIAssets.ColorButtonSelectedSprite;
            hudButton._selectedSprite = HudUIAssets.ColorButtonSelectedSprite;
            hudButton._selectedHoverSprite = HudUIAssets.ColorButtonSelectedSprite;

            var btnSize = size ?? CalcAutoSize(tmp);
            hudButton.ApplySize(btnSize);

            var button = obj.AddComponent<PassiveButton>();
            hudButton.Button = button;      // 同上：构造函数拿不到还没添加的组件
            button.OnMouseOver = new UnityEvent();
            button.OnMouseOut = new UnityEvent();
            button.OnClick = new Button.ButtonClickedEvent();

            var normalColor = btnColor;
            var hoverColor = new UnityEngine.Color(
                Mathf.Min(btnColor.r * 1.3f, 1f),
                Mathf.Min(btnColor.g * 1.3f, 1f),
                Mathf.Min(btnColor.b * 1.3f, 1f),
                btnColor.a);

            button.OnMouseOver.AddListener((UnityAction)(() =>
            {
                renderer.sprite = hudButton._hoverSprite;
                renderer.color = hoverColor;
            }));
            button.OnMouseOut.AddListener((UnityAction)(() =>
            {
                renderer.sprite = hudButton._normalSprite;
                renderer.color = normalColor;
            }));

            var localOnClick = onClick;
            button.OnClick.AddListener((UnityAction)(() =>
            {
                try { SoundManager.Instance.PlaySound(Light.UI.Window.VanillaAsset.FindSoundClip("UI_Select"), false, 0.8f); } catch { }
                localOnClick?.Invoke();
            }));

            return hudButton;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.CreateColorButton]", ex); return default;
        }
    }

    private const float DefaultMargin = 0.26f;

    /// <summary>
    /// 计算文本自适应的按钮尺寸（文本尺寸 × 1.5）
    /// </summary>
    private static Vector2 CalcAutoSize(TextMeshPro tmp)
    {
        try
        {
            float textW = tmp.preferredWidth;
            float textH = tmp.preferredHeight;
            return new Vector2(textW * 1.5f, textH * 1.5f);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.CalcAutoSize]", ex); return default;
        }
    }

    /// <summary>
    /// 应用尺寸到所有组件
    /// </summary>
    private void ApplySize(Vector2 size)
    {
        try
        {
            _size = size;
            Renderer.size = size;
            Collider.size = size;
            if (Text != null)
            {
                Text.rectTransform.sizeDelta = size - new Vector2(DefaultMargin * 0.6f, DefaultMargin * 0.6f);
                Text.ForceMeshUpdate();
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.ApplySize]", ex);
        }
    }

    public void SetSelected(bool selected)
    {
        try
        {
            _isSelected = selected;
            Renderer.sprite = selected ? _selectedSprite : _normalSprite;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.SetSelected]", ex);
        }
    }

    /// <summary>
    /// **覆盖"选中态"用的贴图**。
    ///
    /// 用户 2026-10-06（预设窗口）："这个选中态也太难堪了，你就让他变成类似于
    /// 鼠标悬停时的样子行不" —— 默认的 <c>_selectedSprite</c>（ButtonSelected）
    /// 是一整块高亮，用在**卡片列表**里太重；这里允许调用方换成别的
    /// （预设窗口就换成 <c>ButtonHover</c>，和悬停同一个观感）。
    ///
    /// ⚠️ 只影响**这一个按钮实例** —— 不动 <c>HudUIAssets</c>，别的窗口照旧。
    /// </summary>
    public void SetSelectedSprite(Sprite? selected, Sprite? selectedHover = null)
    {
        try
        {
            if (selected != null) _selectedSprite = selected;
            if (selectedHover != null) _selectedHoverSprite = selectedHover;
            else if (selected != null) _selectedHoverSprite = selected;

            // 立刻就刷一次，否则要等下一次悬停进出才看得到变化
            if (_isSelected) Renderer.sprite = _selectedSprite;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.SetSelectedSprite]", ex);
        }
    }

    /// <summary>
    /// 设置按钮尺寸。传入 null 则恢复为文本自适应（文本 × 1.5）。
    /// </summary>
    public void SetSize(Vector2? size)
    {
        try
        {
            if (size.HasValue)
            {
                ApplySize(size.Value);
            }
            else if (Text != null)
            {
                Text.ForceMeshUpdate();
                ApplySize(CalcAutoSize(Text));
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.SetSize]", ex);
        }
    }

    public void SetText(string text)
    {
        try
        {
            if (Text != null) { Text.text = text; Text.ForceMeshUpdate(); }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.SetText]", ex);
        }
    }

    public void SetVisible(bool visible) => GameObject.SetActive(visible);
    public void SetPosition(Vector3 localPos) => GameObject.transform.localPosition = localPos;
    public void SetColor(Color color) => Renderer.color = color.ToUnityColor();

    public void SetOnClick(Action onClick)
    {
        try
        {
            Button.OnClick.RemoveAllListeners();
            var localOnClick = onClick;
            Button.OnClick.AddListener((UnityAction)(() =>
            {
                try { SoundManager.Instance.PlaySound(Light.UI.Window.VanillaAsset.FindSoundClip("UI_Select"), false, 0.8f); } catch { }
                localOnClick?.Invoke();
            }));
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.SetOnClick]", ex);
        }
    }

    public void Destroy() => Object.Destroy(GameObject);
}

// =====================================================================
// HudUIWindow — 便捷窗口，封装 MetaScreen
// =====================================================================

/// <summary>
/// 便捷窗口封装，基于 MetaScreen。
/// 提供简单的 AddText / AddButton API。
/// </summary>
public class HudUIWindow
{
    public MetaScreen Screen { get; private set; }
    public GameObject GameObject { get; private set; }
    private readonly List<GameObject> _contentObjects = new();
    private float _currentY;
    private Vector2 _windowSize;

    private HudUIWindow(MetaScreen screen, Vector2 windowSize)
    {
        try
        {
            Screen = screen;
            GameObject = screen.gameObject;
            _windowSize = windowSize;
            _currentY = windowSize.y * 0.5f - 0.5f;        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.HudUIWindow]", ex);
        }
    }

    /// <summary>
    /// 创建窗口
    /// </summary>
    /// <summary>
    /// 创建窗口。
    /// </summary>
    /// <param name="blockInputBehind">
    /// **是否屏蔽窗口背后的点击**，默认 true。
    ///
    /// ⚠️ 为什么必须有这个（2026-10-06 用户："我发现确认框能点到原版的东西，
    ///    这个也内置到 HudUI 里面作为可选形参吧，默认 true，要不然一直有问题"）：
    ///
    ///    原版 <c>PassiveButtonManager</c> 的**点击**判定对**每个碰撞盒重叠的按钮**都会派发 ——
    ///    它**只对悬停按 z 取最靠前那个**（见 HandleMouseOver 里的 z 比较），
    ///    点击那一段（Update 里 L58 那个循环）**完全没有 z 判定**。
    ///
    ///    → **"窗口盖在上面"根本不等于"拦住了下面的点击"**，
    ///      黑幕、底板、SortingGroup 全都拦不住。
    ///      必须**显式把不属于本窗口的原版控件禁用掉**（就是 <see cref="UiModalGuard"/> 干的事）。
    ///
    ///    这里把它内置：开窗时 Push + 挂每帧 Sweep 驱动器，关窗时 Pop 还原。
    /// </param>
    public static HudUIWindow Create(string title = "", Vector2? size = null, Transform? parent = null,
        bool blockInputBehind = true)
    {
        try
        {
            parent ??= HudManager.Instance?.transform;
            if (parent == null) throw new InvalidOperationException("HudManager 未就绪");

            var windowSize = size ?? new Vector2(5f, 3f);
            var screen = MetaScreen.GenerateWindow(windowSize, parent, new Vector3(0f, 0f, -50f),
                withBlackScreen: true, closeOnClickOutside: false,
                background: BackgroundSetting.Modern, withCloseButton: true);

            var window = new HudUIWindow(screen, windowSize);
            if (blockInputBehind) window.EnableInputBlock();
            return window;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.Create]", ex); return default;
        }
    }

    /// <summary>
    /// 关窗。⚠️ 走这里关会**顺带还原被屏蔽的点击**；
    ///    直接点右上角 X（MetaScreen 内部是 `Object.Destroy(obj)`）不会走这里 ——
    ///    但 <see cref="UiModalGuard"/> 每帧会检查根节点是否还活着，销毁后会自动清理并还原 ✓
    /// </summary>
    public void Close()
    {
        DisableInputBlock();
        Screen.CloseScreen();
    }

    // =====================================================================
    //  输入屏蔽（"能点到背后的原版控件"的通用解法）
    // =====================================================================

    /// <summary>已登记的屏蔽根（= MetaWindow，不是 Screen）。null = 没开屏蔽。</summary>
    private Transform? _blockRoot;

    /// <summary>是否已经开了输入屏蔽。</summary>
    public bool InputBlocked => _blockRoot != null;

    /// <summary>
    /// 开输入屏蔽：**把不属于本窗口的原版控件临时禁用**，并挂一个每帧 Sweep 驱动器。
    ///
    /// ⚠️ 屏蔽根取 <c>Screen.transform.parent</c>（MetaWindow）而不是 <c>Screen.transform</c> ——
    ///    因为**关闭按钮 X 是 MetaWindow 的子物体、Screen 的兄弟**：
    ///    用 Screen 当根的话，连我们自己的 X 都会被判成"不属于本窗口"而禁掉，窗口就关不掉了。
    /// </summary>
    public void EnableInputBlock()
    {
        try
        {
            if (_blockRoot != null) return;

            _blockRoot = Screen != null ? Screen.transform.parent : null;
            if (_blockRoot == null) { LightLogger.LogWarning("[HudUIWindow] 拿不到屏蔽根，输入屏蔽未生效"); return; }

            UiModalGuard.Push(_blockRoot);

            // 挂每帧驱动器 —— UiModalGuard.Sweep 必须每帧跑，否则只是算了一次
            if (GameObject != null) GameObject.AddComponent<HudUIInputGuard>();

            LightLogger.Log($"[HudUIWindow] 已开启输入屏蔽（根={_blockRoot.name}）");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUIWindow.EnableInputBlock]", ex);
        }
    }

    /// <summary>关掉输入屏蔽并还原被禁用的原版控件。</summary>
    public void DisableInputBlock()
    {
        try
        {
            if (_blockRoot == null) return;
            UiModalGuard.Pop(_blockRoot);
            _blockRoot = null;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[HudUIWindow.DisableInputBlock] {ex.Message}");
        }
    }

    /// <summary>
    /// 把这个窗口里**所有**文字换成本模组的"简中字体模板"字体。
    ///
    /// 用法：**等所有 AddText / AddButton 都加完之后**再调一次。
    /// （AddButton 的标签也是内部建的 TMP，所以必须放在最后统一刷。）
    /// </summary>
    public void ApplyCjkFont()
    {
        try
        {
            var font = Light.UI.Window.MenuTextTemplate2.Font;
            if (font == null) return;
            var mat = Light.UI.Window.MenuTextTemplate2.FontMaterial;

            int n = 0;
            foreach (var tmp in Screen.GetComponentsInChildren<TextMeshPro>(true))
            {
                if (tmp == null) continue;
                tmp.font = font;
                if (mat != null) tmp.fontSharedMaterial = mat;
                n++;
            }
            LightLogger.Log($"[HudUIWindow] 已把 {n} 段文字换成简中字体 {font.name}");
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[HudUIWindow.ApplyCjkFont] {ex.Message}");
        }
    }

    public void ClearContent()
    {
        try
        {
            foreach (var obj in _contentObjects)
            {
                if (obj != null) Object.Destroy(obj);
            }
            _contentObjects.Clear();
            _currentY = _windowSize.y * 0.5f - 0.5f;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.ClearContent]", ex);
        }
    }

    public TextMeshPro AddText(string text, float fontSize = 2f,
        TextAlignmentOptions alignment = TextAlignmentOptions.Center)
    {
        try
        {
            var obj = new GameObject("Text");
            obj.layer = LayerExpansion.GetUILayer();
            obj.transform.SetParent(Screen.transform, false);
            obj.transform.localPosition = new Vector3(0f, _currentY, -1f);

            var tmp = HudUITextHelper.Create(obj.transform);
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontSizeMax = fontSize;
            tmp.fontSizeMin = fontSize * 0.5f;
            tmp.m_fontSizeBase = 3f;
            tmp.alignment = alignment;
            tmp.color = UnityEngine.Color.white;
            tmp.enableWordWrapping = alignment != TextAlignmentOptions.Center;
            tmp.ForceMeshUpdate();

            float textWidth = _windowSize.x - 0.6f;
            float textHeight = tmp.preferredHeight + 0.1f;
            tmp.rectTransform.sizeDelta = new Vector2(textWidth, textHeight);
            tmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            tmp.ForceMeshUpdate();

            textHeight = tmp.preferredHeight + 0.1f;
            tmp.rectTransform.sizeDelta = new Vector2(textWidth, textHeight);
            tmp.ForceMeshUpdate();

            _currentY -= tmp.preferredHeight + 0.2f;
            _contentObjects.Add(obj);
            return tmp;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.AddText]", ex); return default;
        }
    }

    public HudUIButton AddButton(string text, Action onClick, Vector2? size = null, Color? color = null)
    {
        try
        {
            var btn = HudUIButton.Create(Screen.transform, text, size ?? ButtonSize.Rectangle,
                () => { onClick?.Invoke(); });
            btn.SetPosition(new Vector3(0f, _currentY, -1f));

            var btnHeight = (size ?? ButtonSize.Rectangle).y;
            _currentY -= btnHeight + 0.15f;

            if (color != null) btn.SetColor(color.Value);
            _contentObjects.Add(btn.GameObject);
            return btn;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.AddButton]", ex); return default;
        }
    }

    public HudUIButton AddColorButton(string text, Action onClick, Vector2? size = null, Color? color = null)
    {
        try
        {
            var btn = HudUIButton.CreateColorButton(Screen.transform, text, size ?? ButtonSize.Rectangle,
                () => { onClick?.Invoke(); }, color);
            btn.SetPosition(new Vector3(0f, _currentY, -1f));

            var btnHeight = (size ?? ButtonSize.Rectangle).y;
            _currentY -= btnHeight + 0.15f;

            _contentObjects.Add(btn.GameObject);
            return btn;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUI.AddColorButton]", ex); return default;
        }
    }

    public void AddMargin(float height) => _currentY -= height;

    /// <summary>
    /// <see cref="HudUIWindow.EnableInputBlock"/> 的每帧驱动器。
    ///
    /// ⚠️ `UiModalGuard` 只负责**算出该禁用哪些控件**，真正干活的是每帧调用的 `Sweep()`。
    ///    主菜单那边由 `MainMenuPatch`（**只在 MainMenu 场景**）驱动，
    ///    大厅/设置界面**没有别人驱动** → 不挂这个的话屏蔽等于没开。
    ///
    /// ⚠️ 托管 MonoBehaviour 必须先 <c>ClassInjector.RegisterTypeInIl2Cpp&lt;T&gt;()</c>，
    ///    否则 <c>AddComponent&lt;T&gt;()</c> 抛 TypeInitializationException（AGENTS.md §11.2）。
    /// </summary>
    public sealed class HudUIInputGuard : MonoBehaviour
    {
        static HudUIInputGuard()
        {
            try { Il2CppInterop.Runtime.Injection.ClassInjector.RegisterTypeInIl2Cpp<HudUIInputGuard>(); }
            catch { }
        }

        /// <summary>每 N 帧强制全扫一次 —— Sweep 内部有"Buttons 数量没变就短路"的优化，
        /// 而原版会自己把按钮 enabled 回来，所以要低频兜底。</summary>
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
}

// =====================================================================
// HudUIInputField — 通用输入框（2026-10-06 新增）
//
// 用户："保存需要俩输入框。这俩输入框怎么说呢，HudUI 那个不好使，
//       你修一下 HudUI 的拿来用吧。"
//
// ⚠️ 之前是在 PresetWindow 里临时克隆原版 EnterCodeField，实测四个坑全踩：
//    ① **占位符"输入代码"一直挂着** —— 原版 TextBoxTMP 自带 placeholderText 子物体；
//    ② **整个框看不见** —— 原版那个 Background 在克隆体上是 null / 尺寸为 0；
//    ③ **什么都输不进去** —— 原版只允许房间码字符（allowAllCharacters = false）；
//    ④ **回车会触发"加入房间"** —— 克隆体自带 OnEnter 回调（§4.5 同一类坑）。
//    现在统一在这里修好，外面直接用。
// =====================================================================

/// <summary>
/// HudUI 的通用输入框（包装原版 <see cref="TextBoxTMP"/>）。
/// 用 <see cref="Create"/> 建，别自己 new。
/// </summary>
public class HudUIInputField
{
    public GameObject GameObject { get; private set; }
    public TextBoxTMP? Box { get; private set; }

    private TextMeshPro? _placeholder;
    private SpriteRenderer? _bg;

    private HudUIInputField(GameObject go, TextBoxTMP? box)
    {
        GameObject = go;
        Box = box;
    }

    /// <summary>当前文本。</summary>
    public string Text => Box != null ? (Box.text ?? "") : "";

    public void SetText(string text)
    {
        try
        {
            if (Box == null) return;
            Box.SetText(text ?? "");
            RefreshPlaceholder();
        }
        catch (Exception ex) { LightLogger.LogWarning($"[HudUIInputField.SetText] {ex.Message}"); }
    }

    public void SetPosition(Vector3 localPos)
    {
        try { if (GameObject != null) GameObject.transform.localPosition = localPos; } catch { }
    }

    public void SetActive(bool on)
    {
        try { if (GameObject != null) GameObject.SetActive(on); } catch { }
    }

    /// <summary>主动聚焦（弹出软键盘 —— PC 上没用，但手柄/触屏有用）。</summary>
    public void Focus()
    {
        try { Box?.GiveFocus(); } catch { }
    }

    /// <summary>按当前文本决定占位符显不显示。</summary>
    private void RefreshPlaceholder()
    {
        try
        {
            if (_placeholder == null) return;
            bool empty = string.IsNullOrEmpty(Text);
            _placeholder.gameObject.SetActive(empty);
        }
        catch { }
    }

    /// <summary>
    /// 建一个输入框。
    /// </summary>
    /// <param name="parent">挂哪。</param>
    /// <param name="size">外框尺寸（宽 × 高，世界单位）。</param>
    /// <param name="placeholder">空的时候显示的灰字提示。</param>
    /// <param name="characterLimit">最大字符数（&lt;=0 = 不限）。</param>
    public static HudUIInputField Create(Transform parent, Vector2 size,
        string placeholder = "", int characterLimit = 24)
    {
        var go = new GameObject("HudUIInput");
        go.layer = LayerExpansion.GetUILayer();
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale = Vector3.one;

        var field = new HudUIInputField(go, null);

        try
        {
            // ① 底框：**自己画**（原版那个 Background 在克隆体上不可靠，见类注释 ②）
            var bg = go.AddComponent<SpriteRenderer>();
            bg.sprite = HudUIAssets.ButtonNormal;
            bg.drawMode = SpriteDrawMode.Sliced;
            bg.size = size;
            field._bg = bg;

            // ② 克隆一个原版 TextBoxTMP 当输入核心
            var tpl = FindTextBoxTemplate();
            if (tpl == null)
            {
                LightLogger.LogWarning("[HudUIInputField] 找不到可克隆的 TextBoxTMP，输入框不可用");
                return field;
            }

            var box = Object.Instantiate(tpl, go.transform);
            box.gameObject.name = "InputCore";
            box.gameObject.SetActive(true);
            box.transform.localPosition = new Vector3(0f, 0f, -0.1f);
            box.transform.localScale = Vector3.one;
            field.Box = box;

            // ③ 输入规则：**必须放开**，否则只能输房间码那几个字符（坑 ③）
            box.allowAllCharacters = true;
            box.AllowSymbols = true;
            box.AllowPaste = true;
            box.ForceUppercase = false;
            box.ClearOnFocus = false;
            box.ClearOnlyPlaceholderOnFocus = true;
            box.characterLimit = characterLimit;

            // ④ 清掉原版自带的行为与占位符（坑 ① ④）
            box.OnEnter = new UnityEngine.UI.Button.ButtonClickedEvent();   // 否则回车=加入房间
            box.OnChange ??= new UnityEngine.UI.Button.ButtonClickedEvent();
            box.OnChange.AddListener((UnityAction)(() => field.RefreshPlaceholder()));
            ClearVanillaPlaceholder(box);

            // ⑤ 尺寸/字体：原版那个是给 6 位房间码用的，小得很
            if (box.Background != null)
            {
                box.Background.drawMode = SpriteDrawMode.Sliced;
                box.Background.size = new Vector2(size.x - 0.12f, size.y - 0.12f);
            }

            if (box.outputText != null)
            {
                var tmp = box.outputText;
                tmp.enableAutoSizing = false;          // §12.2：不关掉字号完全不生效
                tmp.fontSize = 1.55f;
                tmp.fontSizeMin = 1.55f;
                tmp.fontSizeMax = 1.55f;
                tmp.fontStyle = FontStyles.Bold;       // §12.1
                tmp.alignment = TextAlignmentOptions.Left;
                tmp.enableWordWrapping = false;
                tmp.overflowMode = TextOverflowModes.Ellipsis;
                tmp.rectTransform.sizeDelta = new Vector2(size.x - 0.30f, size.y - 0.16f);
                tmp.rectTransform.localPosition = new Vector3(0f, 0f, -0.05f);
            }

            // ⑥ 我们自己的占位符（原版那个已经被清掉了）
            if (!string.IsNullOrEmpty(placeholder))
            {
                var ph = HudUITextHelper.Create(go.transform);
                if (ph != null)
                {
                    ph.text = placeholder;
                    ph.enableAutoSizing = false;
                    ph.fontSize = 1.45f;
                    ph.fontSizeMin = 1.45f;
                    ph.fontSizeMax = 1.45f;
                    ph.fontStyle = FontStyles.Bold;
                    ph.alignment = TextAlignmentOptions.Left;
                    ph.color = new UnityEngine.Color(1f, 1f, 1f, 0.45f);   // 全限定：本文件 Color 会撞 LightInDark.Color
                    ph.rectTransform.sizeDelta = new Vector2(size.x - 0.30f, size.y - 0.16f);
                    ph.rectTransform.localPosition = new Vector3(-0.02f, 0f, -0.15f);
                    field._placeholder = ph;
                }
            }

            field.SetText("");
            LightLogger.Log($"[HudUIInputField] 输入框已建（{size.x}×{size.y}，上限 {characterLimit} 字）");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUIInputField.Create]", ex);
        }

        return field;
    }

    /// <summary>把原版那个"输入代码"占位符清掉（它是 TextBoxTMP 的子物体）。</summary>
    private static void ClearVanillaPlaceholder(TextBoxTMP box)
    {
        try
        {
            var outTmp = box.outputText;
            foreach (var t in box.GetComponentsInChildren<TextMeshPro>(true))
            {
                if (t == null) continue;
                if (outTmp != null && t.Pointer == outTmp.Pointer) continue;   // 真输出文本，留着
                t.text = "";
                t.gameObject.SetActive(false);
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[HudUIInputField.ClearVanillaPlaceholder] {ex.Message}");
        }
    }

    /// <summary>
    /// 找一个原版 TextBoxTMP 当模板。
    /// ⚠️ 用 <c>FindObjectOfType</c> 而不是 <c>DestroyableSingleton</c> ——
    ///    后者找不到实例时**会凭空造一个 MainMenuManager 出来**，Awake 里直接 NRE（踩过）。
    /// </summary>
    private static TextBoxTMP? FindTextBoxTemplate()
    {
        // ① 主界面那个"输房间码"的（entercodeField 是 PassiveButton，真输入框在它子物体上）
        try
        {
            var menu = Object.FindObjectOfType<MainMenuManager>();
            if (menu != null && menu.entercodeField != null)
            {
                var t = menu.entercodeField.GetComponentInChildren<TextBoxTMP>();
                if (t != null) return t;
            }
        }
        catch { }

        // ② 兜底：已加载资源里任意一个
        try
        {
            var all = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<TextBoxTMP>());
            if (all != null && all.Length > 0) return all[0].TryCast<TextBoxTMP>();
        }
        catch { }

        return null;
    }
}
