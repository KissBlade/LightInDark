using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using LightInDark.Core;

namespace Light.Patches;

/// <summary>
/// **让原版聊天框能输入富文本标签和 Unicode 表情 / 特殊字符。**
///
/// ═══════════════════════════════════════════════════════════════════════
///  用户 2026-10-06："我想让原版那个输入框能输入富文本和 Unicode 表情以及字符，
///  比如 😡 👍 这些" / "就是原版那个聊天按钮里面的输入框"。
///
///  【两个拦路虎】
///
///  ① **输入过滤**（<c>TextBoxTMP.IsCharAllowed</c>，19.0 反编译源码 L296-303）：
/// <code>
///   return i == ' ' || (A-Z) || (a-z) || (0-9) || (À-ÿ) || (Ѐ-џ)
///       || ('\u3040'..'㆟') || ('ⱡ'..'힣')            // 中日韩
///       || (AllowSymbols &amp;&amp; SymbolChars.Contains(i))   // 只有 ?!,.'():;/\%%^&amp;-=¿？#
///       || (AllowEmail &amp;&amp; EmailChars.Contains(i));
/// </code>
///     · emoji（U+1F600 等）**全在范围外** → 直接被丢掉；
///     · 富文本标签的 **`&lt;` `&gt;` 也不在允许表里** → 一样打不进去。
///     → 所以必须 patch 掉它。
///
///  ② **字体没有 emoji 字形** —— 就算文本进去了，TMP 也只能画出豆腐块。
///     → 运行时从**系统字体**建一个 <c>TMP_FontAsset</c>，注册成**全局 fallback**。
///
///  ⚠️ 已知风险（说清楚，别当成"做完了"）：
///     `Segoe UI Emoji` 是**彩色位图字体（CBDT）**，TMP 的 SDF 烘焙对它的支持有限 ——
///     可能烘出来是空白或单色剪影。所以候选表里放了 `Segoe UI Symbol`（**单色**，TMP 一定能烘）
///     作为兜底：它虽然没有彩色 emoji，但 ☺ ♥ ★ ✓ ✈ 这类符号是有的。
///     到底能出多少，**看日志 + 实机**，别猜。
/// ═══════════════════════════════════════════════════════════════════════
public static class RichTextInputPatch
{
    // =====================================================================
    //  ① 放开输入过滤
    // =====================================================================

    /// <summary>
    /// 让输入框能打进「原版会拒、但我们能显示」的字符（中文、全角标点、富文本标点），
    /// 同时**否决**会毁排版的字符（哪怕原版放行）。
    ///
    /// ⚠️ 为什么必须"既能补放行、也能否决"：
    ///   · 只补放行 → 原版放行的零宽字符 / **代理对的一半**会漏进来；
    ///     删字时留下孤立代理或零宽字符 → 冒出一个方块、之后光标与输入整体错乱（用户报的症状）。
    ///   · 前缀式"全盘接管" → 又会让中文被吞。
    ///   → 所以：先否决危险字符，再把原版结论交回，最后按白名单补放行。
    /// </summary>
    [HarmonyPatch(typeof(TextBoxTMP), nameof(TextBoxTMP.IsCharAllowed))]
    public static class IsCharAllowedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(TextBoxTMP __instance, char i, ref bool __result)
        {
            try
            {
                if (__instance != null && __instance.IpMode) return;   // IP 输入框不干预

                // ① 危险字符：直接否决
                if (IsUnsafe(i)) { ReportSwallowed(i, "危险字符"); __result = false; return; }

                // ② 原版放行的照旧放行
                if (__result) return;

                // ③ 原版拒绝的，按白名单补放行（中文 / 全角标点 / 富文本标点）
                if (IsPrintable(i)) { __result = true; return; }

                // ④ 原版拒绝、我们也不放行 —— 若是个"看得出用户想输入"的字符，记一条日志
                //    （用来判定"吞字"到底是不是我们造成的）
                ReportSwallowed(i, "原版拒绝且未补放行");
            }
            catch { }
        }
    }

    /// <summary>
    /// 会让排版 / 光标出问题的字符：无字形（豆腐块）、零宽、**代理对的一半**、私有使用区等。
    /// 其中代理对最要命 —— 删字只删掉一半就留下孤立代理，后续输入直接报废。
    /// </summary>
    private static bool IsUnsafe(char c)
    {
        // ⚠️⚠️ 退格（U+0008）**必须判定为"不可插入"**。
        //   日志实证：`[ChatRichText.诊断] 输入框出现可疑字符 U+0008 … U+0031 U+0032 U+0008`
        //   —— Among Us 的输入循环是「先问 IsCharAllowed，再处理退格」，
        //   原版对 '\b' 返回 false 才会落到删除分支；我们一旦放行，'\b' 就被当普通字符塞进文本，
        //   渲染成方块并污染 TMP 的字符/光标索引（＝用户报的「删字冒方块、之后输入全乱」）。
        //   （char.IsControl 已覆盖 '\b'，这里不再为它开任何例外。）
        if (char.IsSurrogate(c)) return true;                // emoji 代理对（半个 = 孤立代理）
        if (char.IsControl(c)) return true;                  // 控制字符（含退格、换行等）
        if (c == '\u00AD') return true;                      // 软连字符
        if (c >= '\u200B' && c <= '\u200F') return true;     // 零宽空格 / 方向标记
        if (c >= '\u2028' && c <= '\u202E') return true;     // 行分隔符 / 双向控制
        if (c >= '\u2060' && c <= '\u206F') return true;     // 词连接符 / 不可见运算符 / 双向隔离
        if (c >= '\u2600' && c <= '\u27BF') return true;     // 杂项 / 装饰符号（字体多无字形）
        if (c >= '\u2B00' && c <= '\u2BFF') return true;     // 补充箭头
        if (c >= '\uE000' && c <= '\uF8FF') return true;     // 私有使用区（发送保护的中转字符也在此）
        if (c == '\uFEFF') return true;                      // BOM / 零宽不换行空格
        if (c >= '\uFE00' && c <= '\uFE0F') return true;     // 变体选择符
        if (c >= '\uFFF0') return true;                      // 特殊区 / 其它平面
        return false;
    }

    /// <summary>能正常显示的字符（中文、全角标点、富文本标点等）。</summary>
    private static bool IsPrintable(char c) => !IsUnsafe(c);

    // ---- 输入序列诊断：每次文本变化打一条（有上限，避免刷屏）----
    private static string _lastSeenText = "";
    private static int _textChangeLogs;

    /// <summary>
    /// 记录聊天框文本的每一次变化（带码位）。用来定位「第几个字被吞」：
    /// 打几个字却少一个，码位序列里会直接空出来。
    /// </summary>
    public static void LogInputChange(FreeChatInputField field)
    {
        try
        {
            if (_textChangeLogs >= 300) return;

            string text = "";
            try { text = field.Text ?? ""; } catch { }
            if (text == _lastSeenText) return;
            _lastSeenText = text;

            _textChangeLogs++;
            var sb = new System.Text.StringBuilder(64 + text.Length * 7);
            sb.Append("[RichTextInput.输入诊断] 文本变化（").Append(text.Length).Append(" 字）：");
            for (int i = 0; i < text.Length; i++)
                sb.Append("U+").Append(((int)text[i]).ToString("X4")).Append(' ');
            LightLogger.LogDebug(sb.ToString());
        }
        catch { }
    }

    /// <summary>已报告过的"被吞"码位（每种只报一次）。</summary>
    private static readonly HashSet<int> _reportedSwallowed = new();

    /// <summary>
    /// 记录一个被判定为"不可输入"的字符。
    ///
    /// ⚠️ 只在它看起来是用户**想输入**的字符时记录（字母/数字/标点/符号）：
    ///    控制字符、代理对被吞是**预期行为**（它们画不出来，见 `IsUnsafe`）。
    ///    用途：判定「中文吞字」到底是不是本模组的过滤造成的 ——
    ///    若复现吞字时日志里出现这条，说明是我们拦截了它，按码位精准修；
    ///    若日志始终不出现，说明字符压根没进到过滤这一层，得往输入法/游戏侧查。
    /// </summary>
    private static void ReportSwallowed(char c, string why)
    {
        try
        {
            if (char.IsControl(c) || char.IsSurrogate(c)) return;   // 预期被吞，不报
            bool intended = char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsSymbol(c);
            if (!intended) return;
            if (!_reportedSwallowed.Add(c)) return;                 // 每种码位只报一次

            LightLogger.LogWarning($"[RichTextInput.吞字诊断] 字符被拦截：'{c}' U+{((int)c):X4}" +
                                   $"（原因：{why}；字母={char.IsLetter(c)} 数字={char.IsDigit(c)} 标点={char.IsPunctuation(c)}）");
        }
        catch { }
    }

    // =====================================================================
    //  ② emoji / 符号字体 fallback
    // =====================================================================

    /// <summary>候选字体（按顺序试，第一个烘成功就用）。</summary>
    private static readonly (string OsName, string Desc)[] FontCandidates =
    {
        ("Segoe UI Emoji",   "彩色 emoji（CBDT，TMP 支持有限）"),
        ("Segoe UI Symbol",  "单色符号（TMP 一定能烘，但没有彩色 emoji）"),
        ("Segoe UI Historic","古文字符号"),
    };

    private static readonly List<TMP_FontAsset> _created = new();
    private static bool _done;

    /// <summary>
    /// **要不要注册 emoji 字体 fallback。默认 false。**
    ///
    /// ⚠️ 2026-10-06 用户实测："聊天框里面输入东西，再删除会出现莫名其妙的豆腐块"。
    ///    原因：`Segoe UI Emoji` 是**彩色位图字体（CBDT）**，TMP 烘出来的图集里
    ///    那些码位是**空的/坏的字形**；输入时 TMP 走 fallback 链找到它，
    ///    画出来就是一串豆腐块（而且增删文字会让 TMP 反复重排 fallback，闪来闪去）。
    ///
    ///    → **没做成的东西就别挂着**。输入过滤（IsCharAllowed）保留（那个是好的），
    ///      字体 fallback 关掉，等有**离线烘好的 emoji Font Asset** 再开。
    /// </summary>
    private const bool EnableEmojiFallback = false;

    /// <summary>
    /// 建 emoji 字体资源并注册为**全局 fallback**。
    /// 由 <c>LightPlugin.Load</c> 在合适的时机调一次（幂等）。
    /// </summary>
    public static void SetupEmojiFallback()
    {
        if (_done) return;
        _done = true;

        if (!EnableEmojiFallback)
        {
            LightLogger.Log("[RichTextInput] emoji 字体 fallback 已按开关关闭（默认关，原因见常量注释）。" +
                            "字符输入过滤仍然生效。");
            return;
        }

        try
        {
            foreach (var (osName, desc) in FontCandidates)
            {
                var asset = TryBuildFontAsset(osName);
                if (asset == null) continue;

                RegisterAsFallback(asset);
                _created.Add(asset);
                LightLogger.Log($"[RichTextInput] emoji 字体已就绪：'{osName}'（{desc}），" +
                                $"字形数 {SafeGlyphCount(asset)}");
            }

            if (_created.Count == 0)
                LightLogger.LogWarning("[RichTextInput] 没能建成任何 emoji/符号字体 —— " +
                                       "emoji 能输进去但画不出来（会是豆腐块）");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[RichTextInput.SetupEmojiFallback]", ex);
        }
    }

    /// <summary>从系统字体烘一个 TMP 字体资源。烘不出来返回 null。</summary>
    private static TMP_FontAsset? TryBuildFontAsset(string osFontName)
    {
        try
        {
            // Unity 的"按名字取系统字体"
            var osFont = Font.CreateDynamicFontFromOSFont(osFontName, 48);
            if (osFont == null)
            {
                LightLogger.LogDebug($"[RichTextInput] 系统里没有字体 '{osFontName}'");
                return null;
            }

            // ⚠️ 图集开大一点：emoji 字形多，1024 容易爆（爆了会静默丢字形）
            var asset = TMP_FontAsset.CreateFontAsset(
                osFont,
                samplingPointSize: 48,
                atlasPadding: 4,
                renderMode: UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                atlasWidth: 2048,
                atlasHeight: 2048);

            if (asset == null) return null;

            // ⚠️ 打 DontUnloadUnusedAsset —— 否则切场景被回收 → 又变豆腐块（§4.6）
            asset.hideFlags = HideFlags.DontUnloadUnusedAsset;
            if (asset.atlasTexture != null)
                asset.atlasTexture.hideFlags = HideFlags.DontUnloadUnusedAsset;

            asset.name = "LID_Emoji_" + osFontName.Replace(" ", "");
            return asset;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[RichTextInput] 烘焙字体 '{osFontName}' 失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 注册成 fallback。
    /// ⚠️ **两处都要加**：
    ///   · <c>TMP_Settings.fallbackFontAssets</c> —— 全局兜底（新建的 TMP 也吃得到）
    ///   · 每个**已在用**的字体资源自己的 <c>fallbackFontAssetTable</c> —— 老 TMP 只认这个
    ///     （只加全局列表对已经建好的 TMP 不一定生效）
    /// </summary>
    private static void RegisterAsFallback(TMP_FontAsset asset)
    {
        try
        {
            var global = TMP_Settings.fallbackFontAssets;
            if (global != null && !global.Contains(asset)) global.Add(asset);

            // 给所有已加载的字体资源挂上
            int hooked = 0;
            try
            {
                var all = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<TMP_FontAsset>());
                if (all != null)
                {
                    foreach (var o in all)
                    {
                        var fa = o.TryCast<TMP_FontAsset>();
                        if (fa == null) continue;
                        var table = fa.fallbackFontAssetTable;
                        if (table == null) continue;
                        if (!table.Contains(asset)) { table.Add(asset); hooked++; }
                    }
                }
            }
            catch (Exception ex) { LightLogger.LogDebug($"[RichTextInput] 遍历字体资源失败：{ex.Message}"); }

            LightLogger.Log($"[RichTextInput] 字体已挂到 {hooked} 个字体资源的 fallback 表上");
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[RichTextInput.RegisterAsFallback] {ex.Message}");
        }
    }

    private static int SafeGlyphCount(TMP_FontAsset asset)
    {
        try { return asset.characterTable != null ? asset.characterTable.Count : -1; }
        catch { return -1; }
    }
}
