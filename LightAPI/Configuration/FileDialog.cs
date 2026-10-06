using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using LightInDark.Core;

namespace LightInDark.Configuration;

/// <summary>
/// Windows 自带的文件选择框（导出/导入预设用）。
///
/// 走 <c>comdlg32.dll</c> 的 <c>GetOpenFileNameW</c> / <c>GetSaveFileNameW</c> ——
/// 就是用户说的"WINDOWS 自带那个玩意"。
///
/// ⚠️ 三个必须注意的点：
///
/// ① **纯 W 版（Unicode）**。ANSI 版在中文路径上会乱码；这里所有字符串都用
///    <c>CharSet.Unicode</c> + <c>LPWStr</c>。
///
/// ② **`lpstrFile` 缓冲区要足够大且可写**。老代码常犯的错是传一个 260 字节的缓冲
///    却用它装多选结果 —— 这里单选用 1024，够长路径。
///
/// ③ **它是模态对话框，会阻塞调用线程直到用户关掉**。
///    本工程只在按钮回调里调（主线程），阻塞期间游戏画面不动是正常的
///    —— 但**绝对不要在每帧的地方调**。
/// </summary>
public static class FileDialog
{
    // ---- comdlg32 ----

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public string lpstrFile;
        public int nMaxFile;
        public string lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int flagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileNameW(ref OpenFileName ofn);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetSaveFileNameW(ref OpenFileName ofn);

    // OFN_* 标志
    private const int OFN_OVERWRITEPROMPT = 0x00000002;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_NOCHANGEDIR = 0x00000008;   // ★ 别让对话框改掉进程的当前目录

    /// <summary>
    /// 弹"打开文件"框。返回 null = 用户取消。
    /// </summary>
    public static string? Open(string title, string filter, string? initialDir = null)
    {
        try
        {
            var ofn = new OpenFileName
            {
                lStructSize = Marshal.SizeOf<OpenFileName>(),
                lpstrFilter = filter,
                lpstrFile = new string('\0', 1024),
                nMaxFile = 1024,
                lpstrFileTitle = new string('\0', 512),
                nMaxFileTitle = 512,
                lpstrInitialDir = initialDir,
                lpstrTitle = title,
                Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR,
            };

            if (!GetOpenFileNameW(ref ofn)) return null;      // false = 取消（或出错，都当取消）
            return Clean(ofn.lpstrFile);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[FileDialog.Open]", ex);
            return null;
        }
    }

    /// <summary>
    /// 弹"保存文件"框。返回 null = 用户取消。
    /// 有覆盖确认；默认扩展名由 <paramref name="defaultExt"/> 给。
    /// </summary>
    public static string? Save(string title, string filter, string defaultExt, string? defaultName = null,
        string? initialDir = null)
    {
        try
        {
            var ofn = new OpenFileName
            {
                lStructSize = Marshal.SizeOf<OpenFileName>(),
                lpstrFilter = filter,
                // 预填文件名：用户打开对话框就能直接改名保存
                lpstrFile = (defaultName ?? "") + new string('\0', Math.Max(1, 1024 - (defaultName?.Length ?? 0))),
                nMaxFile = 1024,
                lpstrFileTitle = new string('\0', 512),
                nMaxFileTitle = 512,
                lpstrInitialDir = initialDir,
                lpstrTitle = title,
                lpstrDefExt = defaultExt,
                Flags = OFN_OVERWRITEPROMPT | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR,
            };

            if (!GetSaveFileNameW(ref ofn)) return null;
            return Clean(ofn.lpstrFile);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[FileDialog.Save]", ex);
            return null;
        }
    }

    /// <summary>对话框回填的是 NUL 结尾的一整块缓冲 → 取第一个 NUL 之前的部分。</summary>
    private static string? Clean(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        int nul = raw.IndexOf('\0');
        var s = (nul >= 0 ? raw[..nul] : raw).Trim();
        return s.Length == 0 ? null : s;
    }

    /// <summary>
    /// comdlg32 的 filter 格式：<c>"说明\0*.ext\0说明2\0*.ext2\0\0"</c>
    /// （每段都以 NUL 结尾，**最后还要多一个 NUL** 表示结束）。
    /// 手写这个字符串很容易少一个 \0 → 对话框弹不出来，所以统一走这个helper。
    /// </summary>
    public static string BuildFilter(params (string Desc, string Pattern)[] entries)
    {
        var sb = new StringBuilder();
        foreach (var (desc, pattern) in entries)
        {
            sb.Append(desc).Append('\0').Append(pattern).Append('\0');
        }
        sb.Append('\0');
        return sb.ToString();
    }

    /// <summary>常用：预设文件过滤器。</summary>
    public static string PresetFilter => BuildFilter(
        ("Light In Dark 预设 (*.lidpreset)", "*.lidpreset"),
        ("所有文件 (*.*)", "*.*"));

    /// <summary>常用：文本文件过滤器。</summary>
    public static string TxtFilter => BuildFilter(
        ("文本文件 (*.txt)", "*.txt"),
        ("所有文件 (*.*)", "*.*"));

    /// <summary>桌面上放东西的默认目录（初始目录用）。</summary>
    public static string DefaultDir()
    {
        try
        {
            var d = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) return d;
        }
        catch { }
        return PresetStore.SaveDir;
    }
}
