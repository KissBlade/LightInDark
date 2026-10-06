using System;
using System.Collections.Generic;
using AmongUs.Data;
using LightInDark.Core;
using TMPro;

namespace Light.UI.Window;

/// <summary>
/// 给游戏内所有 TMP 字体注入 CJK 回退字体（简中/日/韩），
/// 解决原版字体不含中文字形导致的 H 菜单缺字。
/// 做法参考 Nebula：把东亚字体塞进每个字体的 fallbackFontAssetTable。
/// </summary>
public static class CjkFont
{
    private const string ScName = "NotoSansSC-Regular SDF";
    private const string JpName = "NotoSansJP-Regular SDF";
    private const string KrName = "NotoSansKR-Regular SDF";

    // 上次注入时的语言，用于幂等：语言没变就不再重复注入
    private static string? _lastLanguage;

    /// <summary>强制下次重新注入（切换语言后调用）。</summary>
    public static void Invalidate() => _lastLanguage = null;

    /// <summary>把所有 TMP 字体的回退表指向对应的东亚字体。幂等、廉价。</summary>
    public static void EnsureFallback()
    {
        try
        {
            string language = DataManager.Settings.Language.CurrentLanguage.ToString();
            if (_lastLanguage == language) return; // 语言未变且已注入过

            // 从游戏资源里捞出三个东亚字体（名字对不上则按包含名兜底）
            TMP_FontAsset? sc = null, jp = null, kr = null;
            foreach (var f in VanillaAsset.ListFontAssets())
            {
                if (f == null) continue;
                string n;
                try { n = f.name ?? ""; } catch { continue; }

                if (sc == null && (n == ScName || n.Contains("NotoSansSC"))) sc = f;
                else if (jp == null && (n == JpName || n.Contains("NotoSansJP"))) jp = f;
                else if (kr == null && (n == KrName || n.Contains("NotoSansKR"))) kr = f;
            }

            // 首选字体：日文→日，韩文→韩，中/其余→简中兜底（简中缺失再退日/韩）
            TMP_FontAsset? primary =
                language == "Japanese" ? jp :
                language == "Korean" ? kr : sc;
            primary ??= sc ?? jp ?? kr;

            if (primary == null)
            {
                LightLogger.LogWarning("[CjkFont] 未找到任何东亚字体，跳过 CJK 回退注入");
                return;
            }

            // 首选放最前，其余东亚字体依次跟随
            var order = new List<TMP_FontAsset> { primary! };
            if (sc != null && sc != primary) order.Add(sc);
            if (jp != null && jp != primary) order.Add(jp);
            if (kr != null && kr != primary) order.Add(kr);

            int count = 0;
            foreach (var font in VanillaAsset.ListFontAssets())
            {
                if (font == null) continue;
                try
                {
                    var table = font.fallbackFontAssetTable;
                    if (table == null) continue;

                    // ⚠️⚠️⚠️ **绝对不要 `table.Clear()`**（2026-10-06 修）。
                    //
                    //  这里原来是 `table.Clear()` 然后只塞三个 NotoSans ——
                    //  **等于把原版字体原有的整条回退链清空了**（日志："共处理 24 个字体"）。
                    //  原版靠那些回退覆盖的字符（符号、特殊字形…）一下子全失去回退，
                    //  画出来就是豆腐块。用户反馈："输入东西，再删除会出现莫名其妙的豆腐块" ——
                    //  删字会让 TMP 重排、重新查回退，**缺字形的地方就露出豆腐块**。
                    //  而 `CjkFont.cs` 是这次 pull 才进来的，**时间点完全吻合**。
                    //
                    //  正确做法：**插到最前面**（让东亚字体优先被查到），
                    //  **原有的回退项全部保留在后面**。这样只是"加了东西"，
                    //  不会让任何原本能显示的字符失去回退。
                    for (int k = order.Count - 1; k >= 0; k--)
                    {
                        var fb = order[k];
                        if (fb == null || font == fb) continue;   // 跳过自引用

                        try { table.Remove(fb); } catch { }        // 去重：先摘掉旧的
                        table.Insert(0, fb);                       // 再插到最前
                    }
                    count++;
                }
                catch { }
            }

            _lastLanguage = language;
            LightLogger.Log($"[CjkFont] 已注入 CJK 回退字体：首选「{primary!.name}」，共处理 {count} 个字体");
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[CjkFont] 注入 CJK 回退字体失败：{ex.Message}");
        }
    }
}
