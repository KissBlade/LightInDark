using System;
using LightInDark;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Roles;

namespace Light.Config;

/// <summary>
/// 职业配置自动注册器：为每个可分配职业生成配置块，
/// 含通用"数量/概率"配置与职业专属配置（RoleConfiguration 轻量项，键自动加前缀）。
/// </summary>
internal static class RoleConfigRegistrar
{
    private static bool _registered;

    /// <summary>注册全部职业配置块（幂等，插件启动时调用一次）。</summary>
    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        try
        {
            foreach (var role in RoleRegistry.AllRoles)
            {
                if (!role.CanBeAssigned) continue;   // 兜底职业（普通船员/内鬼）不出配置

                var block = new ConfigBlock(
                    $"lid.role.{role.CodeName}", role.Name, ToCategory(role.RoleCategory))
                    .SetHeaderColor(role.Color.ToUnityColor());

                // 通用配置：出现数量 / 出现概率
                //
                // ⚠️ 标签里**不要**再拼职业名（2026-10-06 用户要求）。
                //    这些项永远出现在**该职业自己的配置块/详情页**里，块头/标题已经写了职业名，
                //    再拼一遍就是"召集者 数量"这种重复（用户原话："把那个召集者删掉"）。
                //
                // ★ 但**悬停说明**要拼职业名，而且要**用职业自己的颜色**
                //   （用户 2026-10-06："数量和概率显示的文字我要 '职业名最大出现的数量'
                //     '职业名可能出现的概率'，其中职业名对应该职业的颜色"）。
                //   说明文字走 TMP 富文本，所以直接塞 <color=#RRGGBB>。
                string nameHex = RoleNameHex(role);
                block.AddConfiguration(
                    $"role.{role.CodeName}.count", role.Allocation.MaxCount, 0, 15, 1,
                    "数量", $"<color=#{nameHex}>{role.Name}</color>最大出现的数量");
                block.AddConfiguration(
                    $"role.{role.CodeName}.chance", role.Allocation.Chance, 0, 100, 5,
                    "概率", $"<color=#{nameHex}>{role.Name}</color>可能出现的概率")
                    .WithSuffix(ConfigSuffix.Percent);

                // 职业专属配置（轻量项）
                foreach (var item in role.RoleConfiguration)
                    AddItem(block, role, item);
            }
            LightLogger.Log("[RoleConfigRegistrar] 职业配置注册完成");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[RoleConfigRegistrar.Register]", ex);
        }
    }

    /// <summary>把轻量配置项展开为 ConfigBlock 配置（键自动加 role.&lt;CodeName&gt;. 前缀）。</summary>
    private static void AddItem(ConfigBlock block, RoleTemplate role, RoleConfigItem item)
    {
        try
        {
            string key = item.FullKey(role);
            string label = item.ResolveLabel(role);
            ConfigItem added = item.Type switch
            {
                ConfigType.Bool => block.AddConfiguration(key, item.Default is bool b && b, label, item.Detail),
                ConfigType.Float => block.AddConfiguration(key, ToFloat(item.Default), item.Min, item.Max, item.Step, label, item.Detail),
                ConfigType.Value => block.AddConfiguration(key, item.Default as string[] ?? Array.Empty<string>(), label, item.Detail),
                _ => block.AddConfiguration(key, ToInt(item.Default), (int)item.Min, (int)item.Max, (int)item.Step, label, item.Detail),
            };
            if (item.Suffix != ConfigSuffix.None) added.WithSuffix(item.Suffix);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[RoleConfigRegistrar] 配置项 {role.CodeName}.{item.Key} 注册失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 职业名在富文本里用的颜色（<c>#RRGGBB</c> 大写十六进制）。
    /// 说明文字是 TMP 富文本，所以直接塞 <c>&lt;color=#RRGGBB&gt;</c> 就能给职业名单独上色。
    /// </summary>
    private static string RoleNameHex(RoleTemplate role)
    {
        try
        {
            var c = LightInDark.ColorHelper.ToUnityColor(role.Color);
            return UnityEngine.ColorUtility.ToHtmlStringRGB(c);
        }
        catch { return "FFFFFF"; }
    }
    private static int ToInt(object v) => v is int i ? i : Convert.ToInt32(v ?? 0);
    private static float ToFloat(object v) => v is float f ? f : Convert.ToSingle(v ?? 0f);

    /// <summary>阵营枚举映射到配置分类。</summary>
    private static ConfigCategory ToCategory(RoleCategory category) => category switch
    {
        RoleCategory.Crewmate => ConfigCategory.Crewmate,
        RoleCategory.Impostor => ConfigCategory.Impostor,
        _ => ConfigCategory.Neutral,
    };
}
