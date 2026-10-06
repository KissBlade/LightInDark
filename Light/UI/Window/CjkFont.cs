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

                    table.Clear();
                    foreach (var fb in order)
                    {
                        if (font == fb) continue; // 跳过自引用
                        table.Add(fb);
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
