using System;
using System.IO;
using System.Text;
using System.Threading;
using LightInDark.Core;

namespace LightInDark.Configuration;

/// <summary>
/// <c>.lidpreset</c> 的**落盘 / 读取**，以及"配置一变就异步写当前预设"。
///
/// ═══════════════════════════════════════════════════════════════════════
///  【目录布局】<c>&lt;persistentDataPath&gt;/LightInDark/Preset/</c>
/// <code>
///   LightInDark/
///     Preset/
///       Current.lidpreset     ← 当前配置，改任何配置项都会自动写这里
///       Save/                 ← 具名预设（本轮先不用）
/// </code>
///
///  【文件格式】照用户给的 D:\Coding\Current.lidpreset 样本：
/// <code>
///   # 不要任意更改以下内容，否则可能导致预设失效。
///   #
///   AU 19.0s Build 7489
///   Light In Dark v0.0.2
///   BY HVTXSVC
///   Preset File
///
///   --- BEGIN ---
///   v1|123|abcd|1,0,2,0.4,...
///   --- END ---
/// </code>
///  <c>--- BEGIN ---</c> / <c>--- END ---</c> **必须原样保留** —— 这是"用户可读的容器"，
///  以后导出成 TXT 给人分享时靠它对上；真正的机器可读部分只在两行之间。
///
///  【异步写入】
///  配置项改动很密集（拖数值条时一帧能变好几次），**不能每次都同步写盘**。
///  这里用"标脏 + 后台定时器去抖"：改动只置一个 volatile 标志，
///  由一个 <see cref="Timer"/> 在线程池上低频检查并写盘。
///  主线程**永远不做 IO**（本工程是 Unity，主线程卡一下就是掉帧）。
/// ═══════════════════════════════════════════════════════════════════════
/// </summary>
public static class PresetStore
{
    /// <summary>本模组版本号（写进文件头）。发布时和插件版本一起改。</summary>
    public const string ModVersion = "0.0.2";

    /// <summary>文件头作者行。样本里是 HVTXSVC；换成别人预设时这一行会被改写。</summary>
    private const string AuthorLine = "BY HVTXSVC";

    /// <summary>去抖间隔（毫秒）。拖数值条时不会每帧写盘。</summary>
    private const int DebounceMs = 400;

    private static readonly object WriteLock = new();

    private static volatile bool _dirty;
    private static int _writing;          // Interlocked 守卫：同一时刻只有一个线程在写
    private static Timer? _timer;
    private static bool _initialized;

    // ---------------------------------------------------------------------
    //  路径
    // ---------------------------------------------------------------------

    /// <summary><c>&lt;persistentDataPath&gt;/LightInDark/Preset</c></summary>
    public static string PresetDir =>
        Path.Combine(UnityEngine.Application.persistentDataPath, "LightInDark", "Preset");

    /// <summary>具名预设目录（本轮先不用，但先建出来）。</summary>
    public static string SaveDir => Path.Combine(PresetDir, "Save");

    /// <summary>当前配置那个文件。</summary>
    public static string CurrentPath => Path.Combine(PresetDir, "Current.lidpreset");

    // ---------------------------------------------------------------------
    //  生命周期
    // ---------------------------------------------------------------------

    /// <summary>
    /// 建立目录、挂变更监听、启动去抖定时器。
    /// 幂等 —— 重复调用只做一次。
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            Directory.CreateDirectory(PresetDir);
            Directory.CreateDirectory(SaveDir);

            // 挂变更监听：任何配置项一变就标脏
            foreach (var item in ConfigRegistry.All) HookItem(item);

            // 文件不存在就先落一份（让用户第一次进游戏就能看到这个文件）
            if (!File.Exists(CurrentPath))
            {
                LightLogger.Log($"[PresetStore] 当前预设不存在，先写一份：{CurrentPath}");
                WriteNow(CurrentPath);
            }
            else
            {
                LightLogger.Log($"[PresetStore] 已就绪：{CurrentPath}");
            }

            _timer = new Timer(_ => FlushIfDirty(), null, DebounceMs, DebounceMs);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PresetStore.Initialize]", ex);
        }
    }

    /// <summary>把一个配置项挂上"变了就标脏"。注册表新增项时也要调它。</summary>
    public static void HookItem(ConfigItem? item)
    {
        if (item == null) return;
        try
        {
            // ⚠️ 用 += 是安全的：ConfigItem.Raise 是事件，重复挂同一个委托会重复触发，
            //    但 MarkDirty 是幂等的（只置一个标志），所以最坏情况只是多置一次。
            item.OnChanged += _ => MarkDirty();
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[PresetStore.HookItem] {ex.Message}");
        }
    }

    // ---------------------------------------------------------------------
    //  写
    // ---------------------------------------------------------------------

    /// <summary>标脏。**从任何线程调用都安全**，不做 IO。</summary>
    public static void MarkDirty() => _dirty = true;

    /// <summary>立刻同步写一次（关游戏、切场景等需要"保证落盘"的时机调）。</summary>
    public static void FlushNow()
    {
        _dirty = false;
        WriteNow(CurrentPath);
    }

    private static void FlushIfDirty()
    {
        if (!_dirty) return;

        // 同一时刻只允许一个线程写；写不了就下次再说（_dirty 保持 true）
        if (Interlocked.CompareExchange(ref _writing, 1, 0) != 0) return;
        try
        {
            _dirty = false;
            WriteNow(CurrentPath);
        }
        catch (Exception ex)
        {
            // 后台线程抛异常会静默杀掉定时器回调 → 必须自己兜住并留痕
            LightLogger.LogWarning($"[PresetStore] 后台写入失败：{ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _writing, 0);
        }
    }

    /// <summary>把当前配置写到指定路径（同步）。</summary>
    public static bool WriteNow(string path) => WriteNow(path, null, null);

    /// <summary>
    /// 把当前配置写到指定路径，并写入预设名 / 作者（具名预设用）。
    /// ⚠️ 名字和作者写成 <c>#</c> 注释行 —— 解析器跳过注释即可，
    ///    不会因为头里多了两行就让旧解析器失效。
    /// </summary>
    public static bool WriteNow(string path, string? presetName, string? author)
    {
        try
        {
            var payload = PresetCodec.Encode();

            var sb = new StringBuilder(payload.Length + 384);
            sb.Append("# 不要任意更改以下内容，否则可能导致预设失效。\n");
            sb.Append("# \n");
            sb.Append("AU ").Append(GameVersionText()).Append('\n');
            sb.Append("Light In Dark v").Append(ModVersion).Append('\n');
            sb.Append("BY ").Append(string.IsNullOrWhiteSpace(author) ? "" : author.Trim()).Append('\n');
            sb.Append("Preset File\n");
            if (!string.IsNullOrWhiteSpace(presetName))
                sb.Append("# 预设名：").Append(presetName.Trim()).Append('\n');
            sb.Append("# 创建时间：")
              .Append(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture))
              .Append('\n');
            sb.Append('\n');
            sb.Append("--- BEGIN ---\n");
            sb.Append(payload).Append('\n');
            sb.Append("--- END ---\n");

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // ⚠️ 先写临时文件再替换：写一半被杀进程也不会留下半截文件
            //    （用户下次进游戏会拿它当有效预设 → 值全错）。
            var tmp = path + ".tmp";
            lock (WriteLock)
            {
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));   // 不带 BOM
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            return true;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[PresetStore.WriteNow] {path}: {ex.Message}");
            return false;
        }
    }

    // ---------------------------------------------------------------------
    //  读
    // ---------------------------------------------------------------------

    /// <summary>读并**应用**一个 .lidpreset。返回 (成功, 说明)。</summary>
    public static (bool Ok, string Message) LoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return (false, $"文件不存在：{path}");

            var text = File.ReadAllText(path, Encoding.UTF8);
            var payload = ExtractPayload(text);
            if (payload == null)
                return (false, "没找到 --- BEGIN --- / --- END --- 之间的内容（文件被改坏了？）");

            var (ok, msg) = PresetCodec.DecodeAndApply(payload);
            if (ok)
            {
                // 加载完立刻把当前文件同步成新状态（否则下一次改动才写，中间窗口里两者不一致）
                FlushNow();
                LightLogger.Log($"[PresetStore] 已加载预设 {Path.GetFileName(path)}：{msg}");
            }
            else
            {
                LightLogger.LogWarning($"[PresetStore] 加载预设失败（{Path.GetFileName(path)}）：{msg}");
            }
            return (ok, msg);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PresetStore.LoadFrom]", ex);
            return (false, $"读取异常：{ex.Message}");
        }
    }

    /// <summary>从文件文本里抠出 BEGIN/END 之间的载荷；找不到返回 null。</summary>
    private static string? ExtractPayload(string text)
    {
        const string begin = "--- BEGIN ---";
        const string end = "--- END ---";

        int b = text.IndexOf(begin, StringComparison.Ordinal);
        if (b < 0) return null;
        b += begin.Length;

        int e = text.IndexOf(end, b, StringComparison.Ordinal);
        if (e < 0) return null;

        return text[b..e].Trim();
    }

    /// <summary>给文件头用的游戏版本串（拿不到就写 unknown，不影响解析）。</summary>
    private static string GameVersionText()
    {
        try
        {
            var v = UnityEngine.Application.version;
            return string.IsNullOrEmpty(v) ? "unknown" : v;
        }
        catch { return "unknown"; }
    }
}
