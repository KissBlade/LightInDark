using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using InnerNet;
using LightInDark.Core;

namespace Light.UI.MainMenu
{
    /// <summary>
    /// 一条自带服务器配置。
    ///
    /// 用法见下面 <see cref="LightServerList.BuiltInServers"/> 的注释 —— 一行一个，加几行就是几个服务器。
    /// </summary>
    public readonly struct LightServerEntry
    {
        public readonly string Name;

        public readonly string Ip;

        public readonly ushort Port;

        public readonly bool UseDtls;

        public LightServerEntry(string name, string ip, ushort port = 22023, bool useDtls = false)
        {
            Name = name;
            Ip = ip;
            Port = port;
            UseDtls = useDtls;
        }

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Ip);
    }

    public static class LightServerList
    {
        public static readonly LightServerEntry[] BuiltInServers =
        {
             new("<color=#ff7518>帆船服</color><color=#ffff00>[广州]</color>", "as-gz.play.fcaugame.cn", 443),
             new("<color=#FFFF00>hvt</color><color=#FF80FF>PIG</color><color=#00FF00>服</color>",  "hvtpigimpostorserver.torv.site", 443),
             new("","",443)

        };

        private static List<IRegionInfo>? _builtIn;

        private static bool _loggedList;
        private static bool _loggedFallback;

        /// <summary>有没有配置任何可用的自带服务器。</summary>
        public static bool HasBuiltIn =>
            BuiltInServers != null && BuiltInServers.Any(e => e.IsValid);

        private static string? RegionIp(IRegionInfo? region)
        {
            try
            {
                var servers = region?.Servers;
                if (servers == null || servers.Length == 0) return null;
                return servers[0]?.Ip;
            }
            catch { return null; }
        }

        /// <summary>判定一个区域是不是自定义服务器。和 Nebula 的 AmongUsUtil.IsCustomServer 同一套判据。</summary>
        public static bool IsCustomRegion(IRegionInfo? region)
        {
            try
            {
                if (region == null) return false;
                var tn = region.TranslateName;
                return tn == StringNames.NoTranslation || tn == null;
            }
            catch { return false; }
        }

        /// <summary>
        /// 构造自带区域对象（只构造一次，缓存在 <see cref="_builtIn"/>）。
        ///
        /// ⚠️ **完全不碰 <see cref="ServerManager"/>** —— 见类注释里那段踩坑说明。
        /// </summary>
        private static List<IRegionInfo> EnsureBuiltIn()
        {
            if (_builtIn != null) return _builtIn;

            _builtIn = new List<IRegionInfo>();
            if (!HasBuiltIn) return _builtIn;

            try
            {
                foreach (var e in BuiltInServers)
                {
                    if (!e.IsValid) continue;

                    var region = new StaticHttpRegionInfo(
                        e.Name,
                        StringNames.NoTranslation,
                        e.Ip,
                        new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<ServerInfo>(
                            new[] { new ServerInfo(e.Name, e.Ip, e.Port, e.UseDtls) })).Cast<IRegionInfo>();

                    _builtIn.Add(region);
                    LightLogger.Log($"[LightServerList] 已准备自带服务器：{e.Name} {e.Ip}:{e.Port}");
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[LightServerList.EnsureBuiltIn]", ex);
            }

            return _builtIn;
        }

        /// <summary>
        /// 拿"允许出现在下拉框里的区域" = **自带服务器（按声明顺序）+ 用户自己的私服**，官服被过滤掉。
        ///
        /// ⚠️ 只**读** <see cref="ServerManager.AvailableRegions"/>，绝不写。
        /// ⚠️ 过滤后为空 → 回退成原列表（否则在线功能全废）。
        /// </summary>
        public static List<IRegionInfo> AllowedRegions()
        {
            try
            {
                var sm = DestroyableSingleton<ServerManager>.Instance;
                if (sm == null) return new List<IRegionInfo>();

                // 用户自己的区域（regionInfo.json 读进来的那些）—— 只读，一个字段都不改
                var all = sm.AvailableRegions?.ToList() ?? new List<IRegionInfo>();
                var userCustom = all.Where(IsCustomRegion).ToList();

                // 自带服务器
                var builtIn = EnsureBuiltIn();

                // 用户列表里已经有同 IP 的，就不重复列（用 IP 比，不用显示名）
                var builtInIps = new HashSet<string>(
                    builtIn.Select(RegionIp).Where(ip => !string.IsNullOrEmpty(ip))!,
                    StringComparer.OrdinalIgnoreCase);

                var merged = new List<IRegionInfo>(builtIn);
                merged.AddRange(userCustom.Where(r =>
                {
                    var ip = RegionIp(r);
                    return ip == null || !builtInIps.Contains(ip);
                }));

                if (merged.Count == 0)
                {
                    if (!_loggedFallback)
                    {
                        _loggedFallback = true;
                    }
                    return all;
                }

                if (!_loggedList)
                {
                    _loggedList = true;
                    var names = string.Join(", ", merged.Take(14).Select(r => r?.Name ?? "?"));
                }

                return merged;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[LightServerList.AllowedRegions]", ex);
                try { return DestroyableSingleton<ServerManager>.Instance?.AvailableRegions?.ToList() ?? new List<IRegionInfo>(); }
                catch { return new List<IRegionInfo>(); }
            }
        }
    }
}
