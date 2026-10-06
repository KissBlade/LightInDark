using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using LightInDark.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace LightInDark.Audio
{
    public static class SfxManager
    {
        private static readonly Dictionary<Assembly, string[]> _assemblyResources = new();
        private static bool _assembliesScanned;

        private static readonly Dictionary<string, byte[]> _resourceBytes = new();
        private static readonly Dictionary<string, AudioClip> _clipCache = new();
        private static readonly Dictionary<string, string> _tempFiles = new();

        private static readonly HashSet<string> _loading = new();

        private static readonly HashSet<string> _failed = new();

        private static readonly HashSet<string> _pendingPlay = new();

        private static SfxHost _host;

        /// <summary>
        /// 播放相对路径音效。
        /// 有bug，暂时不可用。
        /// </summary>
        /// <param name="relativePath">相对路径（如 "./Resources/SFX/Sth.mp3"），null/空/空白时静默忽略。</param>
        /// <param name="volume">音量（1 = 原音量）。</param>
        /// <param name="pitch">音调（1 = 原速）。</param>
        public static void Play(string relativePath, float volume = 1f, float pitch = 1f)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return;
            if (_failed.Contains(relativePath)) return; // 已确认缺失，静默忽略

            try
            {
                if (_clipCache.TryGetValue(relativePath, out var cached) && cached != null)
                {
                    PlayClip(cached, volume, pitch);
                    return;
                }

                if (_loading.Contains(relativePath))
                {
                    _pendingPlay.Add(relativePath);
                    return;
                }

                EnsureHost();
                _loading.Add(relativePath);
                _host.StartCoroutine(CoLoadAndPlay(relativePath, volume, pitch, true).WrapToIl2Cpp());
            }
            catch (Exception ex)
            {
                _loading.Remove(relativePath);
                LightLogger.LogWarning($"[SfxManager] 播放调度失败 {relativePath}: {ex.Message}");
            }
        }

        public static bool ResourceExists(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return false;
            try { return GetBytes(relativePath) != null; }
            catch (Exception) { return false; }
        }

        public static void Warmup(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return;
            if (_clipCache.ContainsKey(relativePath)) return;
            if (_loading.Contains(relativePath)) return;
            if (_failed.Contains(relativePath)) return; // 已确认缺失，静默忽略

            try
            {
                EnsureHost();
                _loading.Add(relativePath);
                _host.StartCoroutine(CoLoadAndPlay(relativePath, 1f, 1f, false).WrapToIl2Cpp());
            }
            catch (Exception ex)
            {
                _loading.Remove(relativePath);
                LightLogger.LogWarning($"[SfxManager] 预热失败 {relativePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// 手动注册包含嵌入音效资源的程序集（默认自动扫描已加载程序集，一般无需调用）。
        /// </summary>
        public static void RegisterAssembly(Assembly assembly)
        {
            if (assembly == null) return;
            try
            {
                _assemblyResources[assembly] = assembly.GetManifestResourceNames();
                _failed.Clear(); // 新程序集可能带来此前缺失的资源，允许重新尝试
            }
            catch (Exception) { _assemblyResources[assembly] = Array.Empty<string>(); }
        }

        private static void PlayClip(AudioClip clip, float volume, float pitch)
        {
            if (clip == null) return;
            if (SoundManager.Instance == null) return;
            SoundManager.Instance.PlaySoundImmediate(clip, false, volume, pitch);
        }

        private static IEnumerator CoLoadAndPlay(string relativePath, float volume, float pitch, bool playWhenReady)
        {
            byte[] bytes = null;
            try { bytes = GetBytes(relativePath); }
            catch (Exception ex) { LightLogger.LogWarning($"[SfxManager] 读取资源失败 {relativePath}: {ex.Message}"); }

            if (bytes == null || bytes.Length == 0)
            {
                LightLogger.LogWarning($"[SfxManager] 未找到音效资源: {relativePath}");
                _loading.Remove(relativePath);
                yield break;
            }

            string filePath = GetTempFile(relativePath, bytes);
            if (filePath == null)
            {
                _loading.Remove(relativePath);
                yield break;
            }

            var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(filePath).AbsoluteUri, AudioType.MPEG);
            yield return request.SendWebRequest();

            try
            {
                if (request.result == UnityWebRequest.Result.Success)
                {
                    var handler = request.downloadHandler as DownloadHandlerAudioClip;
                    var clip = handler != null ? handler.audioClip : null;
                    if (clip != null)
                    {
                        clip.name = Path.GetFileNameWithoutExtension(relativePath);
                        _clipCache[relativePath] = clip;

                        bool shouldPlay = playWhenReady || _pendingPlay.Remove(relativePath);
                        if (shouldPlay) PlayClip(clip, volume, pitch);
                    }
                    else
                    {
                        LightLogger.LogWarning($"[SfxManager] 解码结果为空: {relativePath}");
                    }
                }
                else
                {
                    LightLogger.LogWarning($"[SfxManager] 加载失败 {relativePath}: {request.error}");
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[SfxManager] 解码/播放异常 {relativePath}: {ex.Message}");
            }
            finally
            {
                _loading.Remove(relativePath);
                _pendingPlay.Remove(relativePath);
            }
        }

        private static string GetTempFile(string relativePath, byte[] bytes)
        {
            try
            {
                if (_tempFiles.TryGetValue(relativePath, out var cached) && File.Exists(cached)) return cached;

                var dir = Path.Combine(Path.GetTempPath(), "LightInDark", "SFX");
                Directory.CreateDirectory(dir);

                var hash = GetShortHash(relativePath);
                var ext = Path.GetExtension(relativePath);
                if (string.IsNullOrEmpty(ext)) ext = ".mp3";

                var path = Path.Combine(dir, hash + ext);
                if (!File.Exists(path)) File.WriteAllBytes(path, bytes);

                _tempFiles[relativePath] = path;
                return path;
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[SfxManager] 写入临时文件失败 {relativePath}: {ex.Message}");
                return null;
            }
        }

        private static string GetShortHash(string s)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in s) h = h * 31 + c;
                return (h & 0x7FFFFFFF).ToString("X8");
            }
        }

        private static byte[] GetBytes(string relativePath)
        {
            if (_resourceBytes.TryGetValue(relativePath, out var cached)) return cached;
            if (_failed.Contains(relativePath)) return null;

            var normalized = Normalize(relativePath);
            EnsureAssemblies();

            foreach (var kv in _assemblyResources)
            {
                foreach (var resName in kv.Value)
                {
                    if (!Matches(resName, normalized)) continue;

                    using var stream = kv.Key.GetManifestResourceStream(resName);
                    if (stream == null) continue;

                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    var bytes = ms.ToArray();
                    _resourceBytes[relativePath] = bytes;
                    return bytes;
                }
            }

            _failed.Add(relativePath);
            return null;
        }

        private static string Normalize(string relativePath)
        {
            var p = relativePath.Replace('\\', '/').TrimStart('.', '/');
            return p.Replace('/', '.');
        }

        private static bool Matches(string resourceName, string normalizedDotted)
        {
            // "Light.Resources.SFX.Sth.mp3" 匹配 "Resources.SFX.Sth.mp3"
            var tail = ".Resources." + normalizedDotted;
            return resourceName.EndsWith(tail, StringComparison.OrdinalIgnoreCase)
                || resourceName.EndsWith(normalizedDotted, StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureAssemblies()
        {
            if (_assembliesScanned) return;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (_assemblyResources.ContainsKey(asm)) continue;
                    try { _assemblyResources[asm] = asm.GetManifestResourceNames(); }
                    catch (Exception) { _assemblyResources[asm] = Array.Empty<string>(); }
                }
            }
            catch (Exception) { }
            _assembliesScanned = true;
        }

        private static void EnsureHost()
        {
            if (_host != null) return;
            var go = new GameObject("LightInDarkSfxHost");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _host = go.AddComponent<SfxHost>();
        }

        private sealed class SfxHost : MonoBehaviour { }
    }
}
