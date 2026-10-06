using System;
using System.Collections.Generic;
using System.Linq;
using LightInDark;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Game;
using LightInDark.Language;
using LightInDark.Roles;
using LightInDark.UI.Window;
using Light.UI.HudUI;
using Light.UI.Window;
using UnityEngine;
using Color = LightInDark.Color;
using FontStyle = LightInDark.UI.Window.FontStyle;
using LightGameManager = LightInDark.Game.GameManager;
using MetaScreen = Light.UI.HudUI.MetaScreen;
using TextAlignment = LightInDark.UI.Window.TextAlignment;

namespace Light.UI.Help;

/// <summary>
/// H 键帮助菜单
/// 左侧页签栏 + 右侧内容，两栏布局
/// </summary>
public static class HelpScreen
{
    /// <summary>帮助页签（位掩码）</summary>
    [Flags]
    public enum HelpTab
    {
        Search = 1, MyInfo = 2, Roles = 4, Overview = 8, Options = 16,
        Achievements = 64, Stamps = 128,
    }

    /// <summary>窗口尺寸</summary>
    private static readonly Vector2 HelpSize = new(7.8f, 4.7f);

    private static MetaScreen? _lastScreen;
    private static GameObject? _windowRoot;      // 帮助窗口根节点（供模态遮罩登记/注销）
    private static MetaScreen? _detailWindow;    // 职业详情二级窗口（ESC 优先关闭）
    private static HelpTab _lastTab = HelpTab.Roles;
    private static string _searchKeyword = "";

    /// <summary>帮助菜单是否已打开（窗口销毁后视为未打开）</summary>
    public static bool OpenedAnyHelpScreen => _lastScreen;

    /// <summary>打开帮助</summary>
    public static void TryOpenHelpScreen()
    {
        try
        {
            if (_lastScreen) return;
            _lastScreen = OpenHelpScreen();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.TryOpenHelpScreen", ex);
        }
    }

    /// <summary>打开帮助并定位到“我的职业”页（F1）；未分配职业时不打开</summary>
    public static void TryOpenMyInfo()
    {
        try
        {
            if (LightGameManager.Instance?.LocalPlayer?.HasRole != true) return;
            _lastTab = HelpTab.MyInfo;
            _lastScreen = OpenHelpScreen();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.TryOpenMyInfo", ex);
        }
    }

    /// <summary>
    /// 关闭最上层窗口：有职业详情二级窗口时优先关它，返回 true；否则返回 false（由调用方关整个 H 菜单）。
    /// </summary>
    public static bool TryCloseTopWindow()
    {
        try
        {
            if (_detailWindow)
            {
                UiModalGuard.Pop(_detailWindow!.transform.parent);
                _detailWindow.CloseScreen();
                _detailWindow = null;
                return true;
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.TryCloseTopWindow", ex);
        }
        return false;
    }

    /// <summary>关闭帮助</summary>
    public static void TryCloseHelpScreen()
    {
        try
        {
            // 二级窗口一并关闭，避免残留
            if (_detailWindow)
            {
                UiModalGuard.Pop(_detailWindow!.transform.parent);
                _detailWindow.CloseScreen();
            }
            _detailWindow = null;

            // 注销模态遮罩（还原被临时禁用的原版控件）
            if (_windowRoot != null)
            {
                UiModalGuard.Pop(_windowRoot.transform);
                _windowRoot = null;
            }
            if (_lastScreen)
            {
                _lastScreen!.CloseScreen();
                _lastScreen = null;
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.TryCloseHelpScreen", ex);
        }
    }

    // =====================================================================
    // 窗口与页签分发
    // =====================================================================

    /// <summary>当前有效页签（MyInfo 仅游戏中已分配职业时显示）</summary>
    private static HelpTab GetValidTabs()
    {
        try
        {
            var valid = HelpTab.Search | HelpTab.Roles | HelpTab.Overview | HelpTab.Options
                | HelpTab.Achievements | HelpTab.Stamps;
            if (LightGameManager.Instance?.LocalPlayer?.HasRole == true)
                valid |= HelpTab.MyInfo;
            return valid;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.GetValidTabs", ex); return default;
        }
    }

    /// <summary>帮助窗口父级：HUD → 主菜单 → 相机</summary>
    private static Transform? FindParent()
    {
        if (HudManager.Instance != null)
            return HudManager.Instance.transform;
        var mainMenu = GameObject.FindObjectOfType<MainMenuManager>();
        if (mainMenu != null)
            return mainMenu.transform;
        return Camera.main != null ? Camera.main.transform : null;
    }

    private static MetaScreen OpenHelpScreen()
    {
        try
        {
            var screen = MetaScreen.GenerateWindow(HelpSize, FindParent(), new Vector3(0, 0, -50f),
                withBlackScreen: true, closeOnClickOutside: false,
                background: BackgroundSetting.Modern, withCloseButton: true);

            // 登记模态遮罩：H 菜单打开期间禁用不属于本窗口的原版控件，避免误触背景按钮
            _windowRoot = screen.transform.parent.gameObject;
            UiModalGuard.Push(_windowRoot.transform);

            var validTabs = GetValidTabs();
            // MyInfo 仅在已分配职业时有效，否则回退默认页
            if (_lastTab == HelpTab.MyInfo && LightGameManager.Instance?.LocalPlayer?.HasRole != true)
                _lastTab = HelpTab.Roles;

            ShowScreen(screen, validTabs, _lastTab);
            return screen;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.OpenHelpScreen", ex); return default;
        }
    }

    private static void ShowScreen(MetaScreen screen, HelpTab validTabs, HelpTab tab)
    {
        try
        {
            _lastTab = tab;
            // 用居中锚点：根节点位置与内容高度无关，避免反复切页签时整栏上下浮动
            screen.SetWidget(BuildTabWidget(screen, validTabs, tab), new Vector2(0.5f, 0.5f), out _);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.ShowScreen", ex);
        }
    }

    /// <summary>页签显示顺序</summary>
    private static readonly HelpTab[] TabOrder =
    {
        HelpTab.Search, HelpTab.MyInfo, HelpTab.Roles, HelpTab.Overview, HelpTab.Options,
        HelpTab.Achievements, HelpTab.Stamps,
    };

    /// <summary>页签按钮属性</summary>
    private static TextAttribute TabButtonAttr => new(
        TextAlignment.Center, LIDGUI.Instance.GetFont(FontAsset.Gothic), FontStyle.Bold,
        new FontSize(1.6f, false), new Size(0.82f, 0.21f), Color.White, false);

    /// <summary>职业按钮属性</summary>
    private static TextAttribute RoleButtonAttr => new(
        TextAlignment.Center, LIDGUI.Instance.GetFont(FontAsset.GothicMasked), FontStyle.Bold,
        new FontSize(1.8f, false), new Size(1.2f, 0.29f), Color.White, false);

    private static string GetTabName(HelpTab tab) => tab switch
    {
        HelpTab.Search => Language.Translate("help.tabs.search", "搜索"),
        HelpTab.MyInfo => Language.Translate("help.tabs.myInfo", "我的职业"),
        HelpTab.Roles => Language.Translate("help.tabs.roles", "职业"),
        HelpTab.Overview => Language.Translate("help.tabs.overview", "概览"),
        HelpTab.Options => Language.Translate("help.tabs.options", "设置"),
        HelpTab.Achievements => Language.Translate("help.tabs.achievements", "成就"),
        HelpTab.Stamps => Language.Translate("help.tabs.stamps", "印章"),
        _ => "?",
    };

    private static GUIWidget BuildTabWidget(MetaScreen screen, HelpTab validTabs, HelpTab tab)
    {
        try
        {
            var gui = LIDGUI.Instance;
            return gui.HorizontalHolder(GUIAlignment.Center,
                BuildTabsWidget(screen, validTabs, tab),
                gui.HorizontalMargin(0.15f),
                gui.VerticalHolder(GUIAlignment.Center, BuildTabContent(tab)));
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.BuildTabWidget", ex); return BuildErrorWidget();
        }
    }

    /// <summary>左侧页签栏：当前白、其他灰，纵向堆叠</summary>
    private static GUIWidget BuildTabsWidget(MetaScreen screen, HelpTab validTabs, HelpTab current)
    {
        try
        {
            var gui = LIDGUI.Instance;
            var buttons = new List<GUIWidget?>();
            foreach (var tab in TabOrder)
            {
                if ((validTabs & tab) == 0) continue;
                var color = tab == current ? Color.White : Color.Gray;
                buttons.Add(gui.RawButton(GUIAlignment.Center, TabButtonAttr, GetTabName(tab),
                    _ => ShowScreen(screen, validTabs, tab), color: color, selectedColor: color));
            }
            return gui.VerticalHolder(GUIAlignment.Center, buttons.ToArray());
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.BuildTabsWidget", ex); return BuildErrorWidget();
        }
    }

    private static GUIWidget BuildTabContent(HelpTab tab) => tab switch
    {
        HelpTab.Search => ShowSearchScreen(),
        HelpTab.MyInfo => ShowMyRolesScreen(),
        HelpTab.Roles => ShowAssignableScreen(),
        HelpTab.Overview => ShowPreviewScreen(),
        HelpTab.Options => ShowOptionsScreen(),
        HelpTab.Achievements => ShowPlaceholderScreen("help.tabs.achievements", "成就"),
        HelpTab.Stamps => ShowPlaceholderScreen("help.tabs.stamps", "印章"),
        _ => LIDGUI.Instance.EmptyWidget,
    };

    /// <summary>占位页：功能尚未实装时的空滚动页</summary>
    private static GUIWidget ShowPlaceholderScreen(string key, string name)
    {
        try
        {
            var gui = LIDGUI.Instance;
            var text = gui.RawText(GUIAlignment.Center, WrappingAttr(AttributeAsset.DocumentStandard),
                Language.Translate(key + ".empty", name + "内容尚未实装"));
            return gui.ScrollView(GUIAlignment.Center, new Size(6.1f, 4.1f), null,
                gui.VerticalHolder(GUIAlignment.Center, text), out _);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.ShowPlaceholderScreen", ex); return BuildErrorWidget();
        }
    }

    // =====================================================================
    // Roles 页
    // =====================================================================

    private static GUIWidget ShowAssignableScreen()
    {
        try
        {
            var gui = LIDGUI.Instance;
            // 点击按钮时该列表已完整，索引对应点击时的列表位置
            var allRoles = new List<RoleTemplate>();
            var inner = new List<GUIWidget?>();

            void AddCategory(RoleCategory category, string title, Color titleColor)
            {
                var roles = SortedRoles().Where(r => r.RoleCategory == category).ToList();
                if (roles.Count == 0) return;

                if (inner.Count > 0) inner.Add(gui.VerticalMargin(0.2f));
                inner.Add(gui.RawText(GUIAlignment.Left, gui.GetAttribute(AttributeAsset.DocumentTitle),
                    gui.ColorTextComponent(titleColor, new RawTextComponent(title)).GetString()));
                inner.Add(gui.VerticalMargin(0.1f));

                var buttons = new List<GUIWidget?>();
                foreach (var role in roles)
                {
                    allRoles.Add(role);
                    int index = allRoles.Count - 1;
                    var name = gui.ColorTextComponent(role.Color, new RawTextComponent(role.Name)).GetString();
                    buttons.Add(gui.RawButton(GUIAlignment.Center, RoleButtonAttr, name,
                        _ => OpenAssignableHelp(allRoles, index)));
                }
                inner.Add(gui.Arrange(GUIAlignment.Center, buttons, 4));
            }

            AddCategory(RoleCategory.Impostor, Language.Translate("role.category.impostor", "内鬼"), Color.ImpostorColor);
            AddCategory(RoleCategory.Neutral, Language.Translate("role.category.neutral", "中立"), new Color(1f, 0.7f, 0f));
            AddCategory(RoleCategory.Crewmate, Language.Translate("role.category.crewmate", "船员"), Color.CrewmateColor);

            return gui.ScrollView(GUIAlignment.Center, new Size(6.1f, 4.1f), null,
                gui.VerticalHolder(GUIAlignment.Center, inner), out _);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.ShowAssignableScreen", ex); return BuildErrorWidget();
        }
    }

    // =====================================================================
    // MyInfo 页
    // =====================================================================

    private static GUIWidget ShowMyRolesScreen()
    {
        try
        {
            var gui = LIDGUI.Instance;
            var role = LightGameManager.Instance?.LocalPlayer?.Role;
            var inner = new List<GUIWidget?>();

            if (role != null)
            {
                var name = gui.ColorTextComponent(role.Color, new RawTextComponent(role.Name)).GetString();
                inner.Add(gui.RawButton(GUIAlignment.Center, RoleButtonAttr, name,
                    _ => OpenAssignableHelp(new List<RoleTemplate> { role.Role }, 0)));
            }
            else
            {
                inner.Add(gui.RawText(GUIAlignment.Center, WrappingAttr(AttributeAsset.DocumentStandard),
                    Language.Translate("help.myInfo.none", "当前未分配职业")));
            }

            return gui.ScrollView(GUIAlignment.Center, new Size(6.1f, 3.4f), null,
                gui.VerticalHolder(GUIAlignment.Center, inner), out _);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.ShowMyRolesScreen", ex); return BuildErrorWidget();
        }
    }

    // =====================================================================
    // Search 页
    // =====================================================================

    private static GUIWidget ShowSearchScreen()
    {
        try
        {
            var gui = LIDGUI.Instance;
            var scrollView = new GUIScrollView(GUIAlignment.Center, new Size(6.1f, 3.2f),
                () => BuildSearchResultWidget(gui, _searchKeyword));

            void ShowResult(string keyword)
            {
                _searchKeyword = keyword;
                var artifact = scrollView.Artifact;
                if (artifact != null)
                    artifact.SetWidget(BuildSearchResultWidget(gui, keyword), out _);
                else if (_lastScreen)
                    // 结果区尚未实例化时整页重建
                    ShowScreen(_lastScreen!, GetValidTabs(), HelpTab.Search);
            }

            var field = new TextFieldWidget(GUIAlignment.Center, new Vector2(4.5f, 0.38f),
                Language.Translate("help.search.inputHint", "输入关键词"),
                keyword => ShowResult(keyword));

            var searchButton = gui.RawButton(GUIAlignment.Center, gui.GetAttribute(AttributeAsset.CenteredBoldFixed),
                Language.Translate("help.search.search", "搜索"),
                _ => ShowResult(field.Field?.Text ?? ""));

            return gui.VerticalHolder(GUIAlignment.Center,
                gui.HorizontalHolder(GUIAlignment.Center, field, gui.HorizontalMargin(0.1f), searchButton),
                gui.VerticalMargin(0.1f),
                scrollView);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.ShowSearchScreen", ex); return BuildErrorWidget();
        }
    }

    /// <summary>搜索结果：匹配职业名或描述全文（忽略大小写）</summary>
    private static GUIWidget BuildSearchResultWidget(LIDGUI gui, string keyword)
    {
        try
        {
            var inner = new List<GUIWidget?>();
            keyword = keyword.Trim();

            if (keyword.Length == 0)
            {
                inner.Add(gui.RawText(GUIAlignment.Left, WrappingAttr(AttributeAsset.DocumentStandard),
                    Language.Translate("help.search.hint", "输入关键词搜索职业")));
                return gui.VerticalHolder(GUIAlignment.Left, inner);
            }

            var matched = SortedRoles()
                .Where(r => r.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                         || r.GetDocumentText().Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matched.Count == 0)
            {
                inner.Add(gui.RawText(GUIAlignment.Left, WrappingAttr(AttributeAsset.DocumentStandard),
                    Language.Translate("help.search.noResult", "未找到相关职业")));
            }
            else
            {
                for (int i = 0; i < matched.Count; i++)
                {
                    int index = i;
                    var name = gui.ColorTextComponent(matched[i].Color, new RawTextComponent(matched[i].Name)).GetString();
                    inner.Add(gui.RawButton(GUIAlignment.Left, RoleButtonAttr, name,
                        _ => OpenAssignableHelp(matched, index)));
                    inner.Add(gui.VerticalMargin(0.1f));
                }
            }

            return gui.VerticalHolder(GUIAlignment.Left, inner);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.BuildSearchResultWidget", ex); return BuildErrorWidget();
        }
    }

    // =====================================================================
    // Overview 页
    // =====================================================================

    private static GUIWidget ShowPreviewScreen()
    {
        try
        {
            var gui = LIDGUI.Instance;

            // Il2Cpp 集合不支持 System.Linq，复制到 List 再 Count
            int playerCount = 0;
            if (PlayerControl.AllPlayerControls != null)
            {
                var players = new List<PlayerControl>();
                foreach (var pc in PlayerControl.AllPlayerControls) players.Add(pc);
                playerCount = players.Count;
            }

            GUIWidget BuildColumn(RoleCategory category, string title)
            {
                var column = new List<GUIWidget?>
                {
                    gui.RawText(GUIAlignment.Center, gui.GetAttribute(AttributeAsset.DocumentTitle), title),
                    gui.VerticalMargin(0.15f),
                };
                foreach (var role in SortedRoles())
                {
                    if (role.RoleCategory != category) continue;
                    column.Add(gui.RawText(GUIAlignment.Left, WrappingAttr(AttributeAsset.DocumentStandard),
                        GetAllocationLine(role)));
                    column.Add(gui.VerticalMargin(0.08f));
                }
                return gui.VerticalHolder(GUIAlignment.Center, column);
            }

            // 三栏并排（内鬼 / 中立 / 船员）
            var columns = gui.HorizontalHolder(GUIAlignment.Center,
                BuildColumn(RoleCategory.Impostor, Language.Translate("help.category.impostor", "内鬼")),
                gui.HorizontalMargin(0.5f),
                BuildColumn(RoleCategory.Neutral, Language.Translate("help.category.neutral", "中立")),
                gui.HorizontalMargin(0.5f),
                BuildColumn(RoleCategory.Crewmate, Language.Translate("help.category.crewmate", "船员")));

            var header = gui.VerticalHolder(GUIAlignment.Center,
                gui.RawText(GUIAlignment.Center, gui.GetAttribute(AttributeAsset.DocumentTitle),
                    Language.Translate("help.overview.header", "分配计划")),
                gui.VerticalMargin(0.1f),
                gui.RawText(GUIAlignment.Center, WrappingAttr(AttributeAsset.DocumentStandard),
                    Language.Translate("help.overview.players", "当前玩家数") + ": " + playerCount),
                gui.VerticalMargin(0.1f),
                columns);

            return gui.ScrollView(GUIAlignment.Center, new Size(6.1f, 2.92f), null, header, out _);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.ShowPreviewScreen", ex); return BuildErrorWidget();
        }
    }

    // =====================================================================
    // Options 页
    // =====================================================================

    private static GUIWidget ShowOptionsScreen()
    {
        try
        {
            var gui = LIDGUI.Instance;
            var inner = new List<GUIWidget?>();

            try
            {
                // CurrentGameOptions 返回 IGameOptions 接口，仅 MapId/NumImpostors 可直接访问，
                // 其余字段运行时反射读取（字段名以 AmongUs.GameOptions 为准）
                var options = GameOptionsManager.Instance.CurrentGameOptions;
                if (options != null)
                {
                    void AddLine(string name, string value)
                    {
                        inner.Add(gui.RawText(GUIAlignment.Left, WrappingAttr(AttributeAsset.DocumentStandard),
                            $"{name}: {value}"));
                        inner.Add(gui.VerticalMargin(0.03f));
                    }

                    string GetValue(string fieldName)
                    {
                        try
                        {
                            var type = options.GetType();
                            var field = type.GetField(fieldName);
                            if (field != null) return field.GetValue(options)?.ToString() ?? "?";
                            var prop = type.GetProperty(fieldName);
                            if (prop != null) return prop.GetValue(options)?.ToString() ?? "?";
                        }
                        catch { }
                        return "?";
                    }

                    string GetBoolValue(string fieldName) => GetValue(fieldName) == "True" ? GetBoolText(true) : GetBoolText(false);

                    AddLine(Language.Translate("options.map", "地图"), GetMapName(options.MapId));
                    AddLine(Language.Translate("options.impostors", "内鬼数量"), options.NumImpostors.ToString());
                    AddLine(Language.Translate("options.killCooldown", "击杀冷却"),
                        GetValue("KillCooldown") + Language.Translate("options.sec", "秒"));
                    AddLine(Language.Translate("options.playerSpeed", "玩家速度"),
                        GetValue("PlayerSpeedMod") + "×");
                    AddLine(Language.Translate("options.crewLight", "船员视野"),
                        GetValue("CrewLightMod") + "×");
                    AddLine(Language.Translate("options.impostorLight", "内鬼视野"),
                        GetValue("ImpostorLightMod") + "×");
                    AddLine(Language.Translate("options.killDistance", "击杀距离"), GetKillDistance(GetValue("KillDistance")));
                    AddLine(Language.Translate("options.commonTasks", "普通任务"), GetValue("NumCommonTasks"));
                    AddLine(Language.Translate("options.longTasks", "长任务"), GetValue("NumLongTasks"));
                    AddLine(Language.Translate("options.shortTasks", "短任务"), GetValue("NumShortTasks"));
                    AddLine(Language.Translate("options.emergencyMeetings", "紧急会议"), GetValue("NumEmergencyMeetings"));
                    AddLine(Language.Translate("options.emergencyCooldown", "会议冷却"),
                        GetValue("EmergencyCooldown") + Language.Translate("options.sec", "秒"));
                    AddLine(Language.Translate("options.discussionTime", "讨论时间"),
                        GetValue("DiscussionTime") + Language.Translate("options.sec", "秒"));
                    AddLine(Language.Translate("options.votingTime", "投票时间"),
                        GetValue("VotingTime") + Language.Translate("options.sec", "秒"));
                    AddLine(Language.Translate("options.anonymousVotes", "匿名投票"), GetBoolValue("AnonymousVotes"));
                    AddLine(Language.Translate("options.confirmImpostor", "确认内鬼"), GetBoolValue("ConfirmImpostor"));
                    AddLine(Language.Translate("options.visualTasks", "视觉任务"), GetBoolValue("VisualTasks"));
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("HelpScreen.ShowOptionsScreen.Options", ex);
            }

            if (inner.Count == 0)
                inner.Add(gui.RawText(GUIAlignment.Center, WrappingAttr(AttributeAsset.DocumentStandard),
                    Language.Translate("help.options.unavailable", "无法读取游戏设置")));

            return gui.ScrollView(GUIAlignment.Center, new Size(6.1f, 3.6f), null,
                gui.VerticalHolder(GUIAlignment.Left, inner), out _);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.ShowOptionsScreen", ex); return BuildErrorWidget();
        }
    }

    // =====================================================================
    // 职业详情子窗口
    // =====================================================================

    /// <summary>打开职业详情：列表内左右循环切换</summary>
    private static void OpenAssignableHelp(List<RoleTemplate> roles, int index)
    {
        try
        {
            if (roles.Count == 0) return;
            if (index < 0 || index >= roles.Count) index = 0;

            int currentIndex = index;

            void ReopenWindow()
            {
                if (_detailWindow)
                {
                    UiModalGuard.Pop(_detailWindow!.transform.parent);
                    _detailWindow.CloseScreen();
                    _detailWindow = null;
                }

                // 详情窗口后创建，同排序下自然渲染在帮助菜单之上
                _detailWindow = MetaScreen.GenerateWindow(new Vector2(7f, 4.5f), FindParent(), new Vector3(0, 0, -100f),
                    withBlackScreen: true, closeOnClickOutside: true,
                    background: BackgroundSetting.Modern, sortingGroupOrder: 200);
                // 详情窗口也登记遮罩：其按钮同样要放行，否则会被 H 菜单的遮罩一并禁用
                UiModalGuard.Push(_detailWindow.transform.parent);
                // 居中锚点：切换职业（内容高度变化）时窗口内容不上下浮动
                _detailWindow.SetWidget(BuildRoleDetailWidget(roles[currentIndex]), new Vector2(0.5f, 0.5f), out _);

                var win = _detailWindow;
                MetaScreen.SetUpNavButton(win, increment =>
                {
                    currentIndex = (roles.Count + currentIndex + (increment ? 1 : -1)) % roles.Count;
                    ReopenWindow();
                });
            }

            ReopenWindow();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.OpenAssignableHelp", ex);
        }
    }

    private static GUIWidget BuildRoleDetailWidget(RoleTemplate role)
    {
        try
        {
            var gui = LIDGUI.Instance;

            // 立绘区：框 + 画像重叠，无立绘时不显示
            GUIWidget? portrait = null;
            if (role.IconImage != null)
            {
                portrait = new GUIOverlapHolder(GUIAlignment.Left,
                    gui.Image(GUIAlignment.Left, HudUIAssets.FrameSprite, new FuzzySize(0.55f, 0.55f)),
                    gui.Image(GUIAlignment.Left, role.IconImage, new FuzzySize(0.5f, 0.5f)));
            }

            // 右侧文字列：职业名 + 开场白（开场白为空时不显示）
            var texts = new List<GUIWidget?>
            {
                gui.RawText(GUIAlignment.Left, gui.GetAttribute(AttributeAsset.OverlayTitle),
                    gui.ColorTextComponent(role.Color, new RawTextComponent(role.Name)).GetString()),
                gui.VerticalMargin(0.03f),
            };
            if (!string.IsNullOrEmpty(role.IntroText))
            {
                texts.Add(gui.RawText(GUIAlignment.Left, WrappingAttr(AttributeAsset.OverlayContent),
                    gui.ColorTextComponent(role.Color, new RawTextComponent(role.IntroText)).GetString()));
            }
            var textColumn = gui.VerticalHolder(GUIAlignment.Left, texts.ToArray());

            var headerWidgets = new List<GUIWidget?>();
            if (portrait != null) headerWidgets.Add(portrait);
            headerWidgets.Add(textColumn);
            var header = gui.HorizontalHolder(GUIAlignment.Left, headerWidgets.ToArray());

            return gui.VerticalHolder(GUIAlignment.Left,
                header,
                gui.VerticalMargin(0.1f),
                gui.RawText(GUIAlignment.Left, WrappingAttr(AttributeAsset.DocumentStandard), role.GetDocumentText()),
                gui.VerticalMargin(0.1f),
                gui.RawText(GUIAlignment.Left, WrappingAttr(AttributeAsset.OverlayContent),
                    Language.Translate("help.role.category", "阵营") + ": " + GetCategoryName(role.RoleCategory)),
                gui.VerticalMargin(0.1f),
                gui.RawText(GUIAlignment.Left, WrappingAttr(AttributeAsset.OverlayContent), GetAllocationLine(role)));
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.BuildRoleDetailWidget", ex); return BuildErrorWidget();
        }
    }

    // =====================================================================
    // 公共小工具
    // =====================================================================

    /// <summary>构造可换行的说明文本属性（长文本按容器宽度自动折行）</summary>
    private static TextAttribute WrappingAttr(AttributeAsset asset)
        => new(LIDGUI.Instance.GetAttribute(asset)) { Wrapping = true };

    private static string GetCategoryName(RoleCategory category) => category switch
    {
        RoleCategory.Impostor => Language.Translate("role.category.impostor", "内鬼"),
        RoleCategory.Neutral => Language.Translate("role.category.neutral", "中立"),
        _ => Language.Translate("role.category.crewmate", "船员"),
    };

    /// <summary>分配信息行（MaxCount==0 显示不参与分配）</summary>
    private static string GetAllocationLine(RoleTemplate role)
    {
        try
        {
            int maxCount = role.Allocation.MaxCount;
            int chance = role.Allocation.Chance;
            int guaranteed = role.Allocation.GuaranteedCount;

            if (maxCount <= 0)
                return role.Name + ": " + Language.Translate("help.overview.noAssign", "不参与分配");

            string text = $"{role.Name} × {maxCount}";
            if (guaranteed > 0)
                text += $" ({Language.Translate("help.overview.guaranteed", "必出")} {guaranteed})";
            else if (chance < 100)
                text += $" ({Language.Translate("help.overview.chance", "概率")} {chance}%)";
            return text;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.GetAllocationLine", ex);
            return Language.Translate("help.page.error", "该页面加载失败");
        }
    }

    /// <summary>按注册序号（再按内部名）排序的职业列表，保证各页显示稳定</summary>
    private static List<RoleTemplate> SortedRoles() =>
        RoleRegistry.AllRoles.OrderBy(r => r.Id).ThenBy(r => r.CodeName).ToList();

    /// <summary>页面构建失败时的降级提示 widget</summary>
    private static GUIWidget BuildErrorWidget()
    {
        try
        {
            var gui = LIDGUI.Instance;
            return gui.RawText(GUIAlignment.Center, WrappingAttr(AttributeAsset.DocumentStandard),
                Language.Translate("help.page.error", "该页面加载失败"));
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.BuildErrorWidget", ex);
            return LIDGUI.Instance.EmptyWidget;
        }
    }

    private static string GetBoolText(bool value) =>
        value ? Language.Translate("options.on", "开") : Language.Translate("options.off", "关");

    /// <summary>地图编号转名称</summary>
    private static string GetMapName(int mapId) => mapId switch
    {
        0 => Language.Translate("map.skeld", "飞船"),
        1 => Language.Translate("map.mira", "米拉"),
        2 => Language.Translate("map.polus", "波卢斯"),
        4 => Language.Translate("map.airship", "飞艇"),
        5 => Language.Translate("map.fungle", "真菌"),
        _ => mapId.ToString(),
    };

    /// <summary>击杀距离数值转文本</summary>
    private static string GetKillDistance(string value) => value switch
    {
        "0" => Language.Translate("killDistance.short", "短"),
        "1" => Language.Translate("killDistance.medium", "中"),
        "2" => Language.Translate("killDistance.long", "长"),
        _ => value,
    };
}
