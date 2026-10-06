using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LightInDark.Core;

namespace LightInDark.Configuration;

/// <summary>一个预设的元信息（都是从头文件里读出来的）。</summary>
public sealed class PresetMeta
{
    public string Path { get; init; } = "";
    public string Name { get; init; } = "";
    public string Author { get; init; } = "";
    public string Created { get; init; } = "";
    public string GameVersion { get; init; } = "";
    public string ModVersion { get; init; } = "";

    /// <summary>文件里的 key 指纹。</summary>
    public string KeyHash { get; init; } = "";

    /// <summary>指纹和当前注册表一致 → 这个预设能用。</summary>
    public bool Compatible => KeyHash == PresetCodec.RegistryKeyHash();

    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>文件里记的配置项**条数**（用来把"不匹配"的原因说清楚）。-1 = 没读到。</summary>
    public int ItemCount { get; init; } = -1;

    /// <summary>
    /// **可用性**：写死的红/绿两态（用户 2026-10-06："不可用和可用那块就写死，红字绿字，
    /// 那只是状态提示"）。
    /// 判据只有一个：配置项集合是否完全一致 —— 版本不同**不算**不可用（只提示）。
    /// </summary>
    public string AvailabilityText => Compatible ? "可用" : "不可用";

    public string AvailabilityColor => Compatible ? "#00FF2A" : "#FF3B30";

    /// <summary>
    /// **匹配情况**：把不匹配的**原因写清楚**，可能多行。
    /// ⚠️ UI 那边这块**必须开自动换行**（用户明确要求）——
    ///    文本比较长（例如"配置项数量变了：文件 12 项，当前 15 项"）。
    /// </summary>
    public string MatchReason()
    {
        var sb = new StringBuilder(160);

        // ① 主判据：key 指纹
        if (Compatible)
        {
            sb.Append("<color=#00FF2A>配置项均匹配</color>");
        }
        else
        {
            int now = PresetCodec.RegistryItemCount();
            sb.Append("<color=#FF3B30>配置项不匹配</color>");

            if (ItemCount < 0)
                sb.Append("\n原因：文件里没记条数（旧版预设？）");
            else if (ItemCount != now)
                sb.Append($"\n原因：配置项数量变了 —— 文件 {ItemCount} 项，当前 {now} 项");
            else
                sb.Append($"\n原因：数量相同（{now} 项）但键变了 —— 有项被改名或替换");
        }

        // ② 版本差异：只提示，不判"不可用"
        var curGame = SafeGameVersion();
        if (!string.IsNullOrWhiteSpace(GameVersion) && GameVersion != curGame
            && GameVersion != "unknown" && curGame != "unknown")
        {
            sb.Append($"\n<color=#FFD24A>游戏版本不同</color>：{GameVersion} → {curGame}");
        }

        if (!string.IsNullOrWhiteSpace(ModVersion) && ModVersion != PresetStore.ModVersion)
        {
            sb.Append($"\n<color=#FFD24A>模组版本不同</color>：v{ModVersion} → v{PresetStore.ModVersion}");
        }

        return sb.ToString();
    }

    private static string SafeGameVersion()
    {
        try
        {
            var v = UnityEngine.Application.version;
            return string.IsNullOrEmpty(v) ? "unknown" : v;
        }
        catch { return "unknown"; }
    }
}

/// <summary>
/// 预设**库**：列出 / 另存 / 删除 / 导出成人看的 TXT。
///
/// 目录：<c>&lt;persistentDataPath&gt;/LightInDark/Preset/Save/</c>（每个预设一个 .lidpreset）
/// 外加外面那个 <c>Current.lidpreset</c>（当前配置，由 <see cref="PresetStore"/> 自动写）。
/// </summary>
public static class PresetLibrary
{
    // ---------------------------------------------------------------------
    //  列出
    // ---------------------------------------------------------------------

    /// <summary>列出 Save/ 里所有预设（读头文件，不解析载荷 —— 列表要快）。</summary>
    public static List<PresetMeta> List()
    {
        var result = new List<PresetMeta>();
        try
        {
            var dir = PresetStore.SaveDir;
            if (!Directory.Exists(dir)) return result;

            foreach (var file in Directory.GetFiles(dir, "*.lidpreset"))
            {
                var meta = ReadMeta(file);
                if (meta != null) result.Add(meta);
            }

            // 新的排前面（创建时间是 yyyy/MM/dd HH:mm:ss 这种可排序的格式）
            result.Sort((a, b) => string.CompareOrdinal(b.Created, a.Created));
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[PresetLibrary.List] {ex.Message}");
        }
        return result;
    }

    /// <summary>只读文件头，不解析载荷。</summary>
    public static PresetMeta? ReadMeta(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            string name = "", author = "", created = "", game = "", mod = "", hash = "";
            int itemCount = -1;
            foreach (var raw in File.ReadLines(path, Encoding.UTF8).Take(24))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("AU ", StringComparison.Ordinal)) game = line[3..].Trim();
                else if (line.StartsWith("Light In Dark v", StringComparison.Ordinal)) mod = line[15..].Trim();
                else if (line.StartsWith("BY ", StringComparison.Ordinal)) author = line[3..].Trim();
                else if (line.StartsWith("# 预设名：", StringComparison.Ordinal)) name = line[6..].Trim();
                else if (line.StartsWith("# 创建时间：", StringComparison.Ordinal)) created = line[7..].Trim();
                else if (line.StartsWith("v", StringComparison.Ordinal) && line.Contains('|'))
                {
                    // 载荷：v1|条数|指纹|值表
                    var seg = line.Split('|');
                    if (seg.Length >= 3) hash = seg[2];
                    if (seg.Length >= 2 && int.TryParse(seg[1], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out int c)) itemCount = c;
                }
            }

            // 名字没写在头里就退回文件名（老文件 / 手改过的文件）
            if (string.IsNullOrWhiteSpace(name))
                name = System.IO.Path.GetFileNameWithoutExtension(path);

            return new PresetMeta
            {
                Path = path,
                Name = name,
                Author = author,
                Created = created,
                GameVersion = game,
                ModVersion = mod,
                KeyHash = hash,
                ItemCount = itemCount,
            };
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[PresetLibrary.ReadMeta] {path}: {ex.Message}");
            return null;
        }
    }

    // ---------------------------------------------------------------------
    //  另存 / 删除
    // ---------------------------------------------------------------------

    /// <summary>把当前配置另存成一个具名预设。返回 (成功, 路径或错误)。</summary>
    public static (bool Ok, string Message) SaveAs(string name, string author)
    {
        try
        {
            name = Sanitize(name);
            if (string.IsNullOrWhiteSpace(name)) return (false, "预设名不能为空");

            Directory.CreateDirectory(PresetStore.SaveDir);
            var path = System.IO.Path.Combine(PresetStore.SaveDir, name + ".lidpreset");

            bool ok = PresetStore.WriteNow(path, name, author);
            return ok ? (true, path) : (false, "写入失败，看日志");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PresetLibrary.SaveAs]", ex);
            return (false, ex.Message);
        }
    }

    public static bool Delete(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[PresetLibrary.Delete] {ex.Message}");
            return false;
        }
    }

    /// <summary>去掉文件名里不能用的字符（用户可能输入 `a/b:c`）。</summary>
    private static string Sanitize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var bad = System.IO.Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(s.Length);
        foreach (char c in s.Trim())
            sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
        // Windows 不允许文件名以点或空格结尾
        return sb.ToString().TrimEnd('.', ' ');
    }

    // ---------------------------------------------------------------------
    //  作者：默认用玩家自己的名字
    // ---------------------------------------------------------------------

    /// <summary>
    /// 本地玩家名（作者栏的默认值）。
    ///
    /// 用户 2026-10-06："作者未填就用玩家名，PlayerControl 我记得有给本地玩家名字。"
    /// → <c>PlayerControl.LocalPlayer</c>（**静态字段**，interop 里是属性）
    ///    → <c>Data.PlayerName</c>。
    ///
    /// ⚠️ 主菜单里还没进大厅时 <c>LocalPlayer</c> 是 null —— 那时返回空串，
    ///    上层会显示"(未填)"而不是崩掉。
    /// </summary>
    public static string LocalPlayerName()
    {
        try
        {
            var pc = PlayerControl.LocalPlayer;
            if (pc != null)
            {
                var data = pc.Data;
                if (data != null && !string.IsNullOrWhiteSpace(data.PlayerName))
                    return data.PlayerName.Trim();
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogDebug($"[PresetLibrary.LocalPlayerName] {ex.Message}");
        }
        return "";
    }

    /// <summary>作者栏的最终取值：用户填了就用用户的，没填就退回玩家名。</summary>
    public static string ResolveAuthor(string? userInput)
        => string.IsNullOrWhiteSpace(userInput) ? LocalPlayerName() : userInput.Trim();

    // ---------------------------------------------------------------------
    //  导出成人看的 TXT
    // ---------------------------------------------------------------------

    /// <summary>
    /// 生成"给人看"的 TXT 内容（用户给的格式）：
    /// <code>
    /// Light In Dark版本：0.0.1
    /// Among Us版本：19.0s Build XXX
    /// 作者：hvtXsvc
    /// 模组设置：
    ///     启用调试模式：开
    ///       - 生成假人数量：1
    /// 船员：
    ///     召集者 - 1 * 100%
    ///       - 技能最大使用次数：1
    /// </code>
    /// 即：**分类 → 块（职业）→ 项**，缩进两级。
    /// </summary>
    public static string BuildTxt(string author)
    {
        var sb = new StringBuilder(4096);

        sb.Append("Light In Dark版本：").Append(PresetStore.ModVersion).Append('\n');
        sb.Append("Among Us版本：").Append(SafeGameVersion()).Append('\n');
        var who = ResolveAuthor(author);
        sb.Append("作者：").Append(string.IsNullOrWhiteSpace(who) ? "(未填)" : who).Append('\n');
        sb.Append("导出时间：").Append(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture)).Append('\n');

        // 按分类分组；同一分类内先打"非职业块"，再打职业块
        foreach (ConfigCategory cat in Enum.GetValues(typeof(ConfigCategory)))
        {
            var blocks = ConfigRegistry.Blocks
                .Where(b => b.Category == cat && b.Items.Any(i => i.IsVisible))
                .OrderBy(b => b.DisplayName, StringComparer.CurrentCulture)
                .ToList();
            if (blocks.Count == 0) continue;

            sb.Append('\n').Append(CategoryName(cat)).Append("：\n");

            foreach (var block in blocks)
            {
                // 职业块把"数量 × 概率"压缩到块标题那一行（用户样例里的 `召集者 - 1 * 100%`）
                var items = block.Items.Where(i => i.IsVisible).ToList();
                var count = items.FirstOrDefault(i => i.Key.EndsWith(".count", StringComparison.Ordinal));
                var chance = items.FirstOrDefault(i => i.Key.EndsWith(".chance", StringComparison.Ordinal));

                sb.Append("    ").Append(block.DisplayName);
                if (count != null || chance != null)
                {
                    sb.Append(" - ")
                      .Append(count != null ? count.GetInt().ToString(CultureInfo.InvariantCulture) : "?")
                      .Append(" * ")
                      .Append(chance != null ? chance.GetValueText() : "?");
                }
                sb.Append('\n');

                foreach (var item in items)
                {
                    // 数量/概率已经写在标题行了，不重复列
                    if (ReferenceEquals(item, count) || ReferenceEquals(item, chance)) continue;
                    if (PresetCodec_IsCountOrChance(item.Key)) continue;

                    sb.Append("      - ").Append(item.DisplayName)
                      .Append("：").Append(item.GetValueText()).Append('\n');
                }
            }
        }

        return sb.ToString();
    }

    private static bool PresetCodec_IsCountOrChance(string key)
        => key.EndsWith(".count", StringComparison.Ordinal) || key.EndsWith(".chance", StringComparison.Ordinal);

    private static string CategoryName(ConfigCategory c) => c switch
    {
        ConfigCategory.Crewmate => "船员",
        ConfigCategory.Impostor => "内鬼",
        ConfigCategory.Neutral => "中立",
        ConfigCategory.Modifier => "附加",
        ConfigCategory.Debug => "调试",
        _ => c.ToString(),
    };

    /// <summary>把 TXT 写到指定路径（导出按钮用）。</summary>
    public static (bool Ok, string Message) ExportTxt(string path, string author)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(path, BuildTxt(author), new UTF8Encoding(true));   // TXT 带 BOM，记事本才不会乱码
            return (true, path);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PresetLibrary.ExportTxt]", ex);
            return (false, ex.Message);
        }
    }

    private static string SafeGameVersion()
    {
        try
        {
            var v = UnityEngine.Application.version;
            return string.IsNullOrEmpty(v) ? "unknown" : v;
        }
        catch { return "unknown"; }
    }
}
