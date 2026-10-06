using LightInDark.Core;
using Light.UI.Help;
using System;

namespace Light.Patches;

/// <summary>H 键帮助菜单开关</summary>
[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
public static class HelpKeyPatch
{
    public static void Postfix()
    {
        try
        {
            // 模态遮罩：H 菜单打开期间每帧禁用不属于本窗口的原版控件，防止误触背景（未打开时内部自动跳过）
            Light.UI.UiModalGuard.Sweep();

            // ESC 关闭：优先关二级窗口（职业详情），没有二级窗口才关整个 H 菜单
            if (Input.GetKeyDown(KeyCode.Escape) && HelpScreen.OpenedAnyHelpScreen)
            {
                if (!HelpScreen.TryCloseTopWindow())
                    HelpScreen.TryCloseHelpScreen();
            }
            // H 打开
            if (Input.GetKeyDown(KeyCode.H) && CanOpenHelp())
                HelpScreen.TryOpenHelpScreen();
            // F1 快捷查看自己职业
            if (Input.GetKeyDown(KeyCode.F1))
            {
                if (HelpScreen.OpenedAnyHelpScreen) HelpScreen.TryCloseHelpScreen();
                else HelpScreen.TryOpenMyInfo();
            }
        }
        catch (System.Exception)
        {
        }
    }

    /// <summary>是否可以打开帮助（防重复 / 聊天输入 / 占用状态）</summary>
    private static bool CanOpenHelp()
    {
        try
        {
            if (HelpScreen.OpenedAnyHelpScreen) return false;
            // 聊天输入框聚焦时屏蔽
            var chat = HudManager.Instance?.Chat;
            if (chat != null && chat.freeChatField != null && chat.freeChatField.textArea != null && chat.freeChatField.textArea.hasFocus)
                return false;
            if (Minigame.Instance != null) return false;
            if (IntroCutscene.Instance != null) return false;
            if (ExileController.Instance != null) return false;
            return true;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HelpKeyPatch.CanOpenHelp]", ex); return default;
        }
    }
}

/// <summary>会议开始自动关闭帮助</summary>
[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Awake))]
public static class MeetingCloseHelpPatch
{
    public static void Postfix()
    {
        try
        {
            HelpScreen.TryCloseHelpScreen();
        }
        catch (System.Exception)
        {
        }
    }
}

/// <summary>放逐开始自动关闭帮助</summary>
[HarmonyPatch(typeof(ExileController), nameof(ExileController.Begin))]
public static class ExileCloseHelpPatch
{
    public static void Postfix()
    {
        try
        {
            HelpScreen.TryCloseHelpScreen();
        }
        catch (System.Exception)
        {
        }
    }
}
