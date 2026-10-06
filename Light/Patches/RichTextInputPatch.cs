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
    /// 让 <c>TextBoxTMP.IsCharAllowed</c> **一律返回 true**（除了 IP 模式）。
    ///
    /// ⚠️ 这是个 **Prefix 且返回 false** 的"完全替换"写法 —— 原方法体不再执行。
    /// ⚠️ **IpMode 保持原样**：那个输入框专门收 IP，放开会把 "." 之外的东西也吃进去，
    ///    而且它有自己的解析逻辑，别去动。
    /// </summary>
    [HarmonyPatch(typeof(TextBoxTMP), nameof(TextBoxTMP.IsCharAllowed))]
    public static class IsCharAllowedPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(TextBoxTMP __instance, char i, ref bool __result)
        {
            try
            {
                // IP 输入框不动（它必须是"只允许数字和点"）
                if (__instance != null && __instance.IpMode)
                    return true;      // 走原方法

                __result = IsAllowed(i);
                return false;         // 不走原方法
            }
            catch
            {
                return true;          // 出任何问题都退回原逻辑，别把输入框弄死
            }
        }
    }

    /// <summary>
    /// **放行哪些字符。**
    ///
    /// ⚠️ 2026-10-06 用户："输入东西，再删除会出现莫名其妙的豆腐块。"
    ///
    ///  根因：我把 `IsCharAllowed` 改成"一律放行"，**emoji 也能打进去了** ——
    ///  但字体里没有那些字形，TMP 画出来就是一串**豆腐块**；
    ///  删字时 TMP 重排，豆腐块跟着闪来闪去。
    ///
    ///  **"能输入"和"能显示"是两件事。** 字体那一半（`EnableEmojiFallback`）没做成，
    ///  那输入这一半就不该放开 —— 否则用户打进去的每个 emoji 都会变成豆腐块。
    ///
    ///  所以这里：**放开富文本需要的标点，挡掉画不出来的 emoji / 符号区**。
    ///  等以后有能用的 emoji Font Asset 了，把下面那几行注释掉即可。
    /// </summary>
    private static bool IsAllowed(char c)
    {
        // 控制字符（换行/制表等）会把 UI 排版搞乱，但退格要留
        if (c == '\b') return true;
        if (char.IsControl(c)) return false;

        // ---- 以下都是"字体里没有 → 必出豆腐块"的区段，先挡掉 ----

        // emoji 是 U+1Fxxx，在 C# 里表现为**代理对**（两个 char）
        if (char.IsSurrogate(c)) return false;

        // 杂项符号（☀☁☺♥…）与装饰符号（✂✈✉…）
        if (c >= '\u2600' && c <= '\u27BF') return false;

        // 补充箭头 / 更多符号
        if (c >= '\u2B00' && c <= '\u2BFF') return false;

        // 变体选择符（emoji 的"彩色/黑白"后缀，单独出现也是豆腐块）
        if (c >= '\uFE00' && c <= '\uFE0F') return false;

        // 其它平面（U+1F000 以上全是 emoji / 生僻字）
        if (c >= '\uFFF0') return false;

        return true;   // 其余全部放行（含富文本要用的 < > = " # 等）
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
