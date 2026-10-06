using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;          // SortingGroup
using UnityEngine.UI;
using LightInDark.Core;
using Light.UI.HudUI;
using Object = UnityEngine.Object;

namespace Light.Patches;

/// <summary>
/// **聊天框富文本 / emoji 支持**（用户 2026-10-06 需求）。
///
/// ═══════════════════════════════════════════════════════════════════════
///  用户原话：
///   · "渲染了，但是发出去后被剥离了。能不能做一下这个，聊天框里不渲染，
///      发出去后会渲染，这样方便看"
///   · "如果包含了 Rich 标签，那么克隆个发送按钮到我画的地方，叫 预览，
///      点击后打开一个窗口，可以看到渲染后文字"
///   · "发出去是豆腐块，你也看见了"
///
///  【这里做了什么】
///   ① **输入框不渲染** —— 把 `textArea.outputText.richText` 关掉，
///      这样输入时看到的是**原始标签**（`&lt;color=red&gt;你好&lt;/color&gt;`），方便检查有没有写错。
///      （之前是渲染的，标签一多就看不清自己到底打了什么。）
///   ② **克隆一个「预览」按钮**放在发送按钮左边，点开一个小窗口，
///      用 `<c>richText = true</c>` 的 TMP **渲染**当前输入内容 —— 发之前先看效果。
///
///  ⚠️ **没做的 / 做不到的**（说清楚，别让用户以为全好了）：
///   · **"发出去后渲染"** —— 游戏在发送前会把标签剥掉，那一段在 `ChatController`
///     的发送链路里，本轮**没找到确切的剥离点**，所以没动。
///   · **豆腐块** —— emoji 字形问题，见 `RichTextInputPatch.SetupEmojiFallback`。
///     那个是"字体里没有字形"，跟本文件无关。
/// ═══════════════════════════════════════════════════════════════════════
public static class ChatRichTextPatch
{
    /// <summary>克隆出来的预览按钮，避免重复建。</summary>
    private static GameObject? _previewButton;

    // =====================================================================
    //  让**发出去的**消息真的渲染富文本
    //
    //  用户 2026-10-06："发送出去时 Rich 文本没出现，我说 TONE 我记得可以，你去看看。"
    //
    //  看 TONE 的结果（`ChatCommandPatch.cs:2805`）—— **关键就一行**：
    //      chatBubble.TextArea.overrideColorTags = false;
    //
    //  TMP 的 `overrideColorTags` 打开时会**忽略文本里的 <color=...> 标签，
    //  强制用物体自己的颜色** —— 标签被解析掉、但颜色不生效，
    //  看起来就是"发出去之后标签被剥离了"（用户的原话）✓
    //
    //  同时补 richText = true 双保险（TMP 默认开，但原版可能在预制体里关掉过）。
    // =====================================================================

    [HarmonyPatch(typeof(ChatBubble), nameof(ChatBubble.SetName))]
    public static class ChatBubbleRichTextPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ChatBubble __instance)
        {
            try
            {
                if (__instance == null) return;
                var area = __instance.TextArea;
                if (area == null) return;

                area.richText = true;              // 解析标签
                area.overrideColorTags = false;    // ★ 核心：别忽略 <color=...>

                LightLogger.LogDebug("[ChatRichText] 聊天气泡已开启富文本渲染");
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ChatRichText.ChatBubbleRichTextPatch] {ex.Message}");
            }
        }
    }

    // =====================================================================
    //  挂到 AbstractChatInputField.Start
    //
    //  ⚠️⚠️ **2026-10-06 严重事故（务必看完再改）**：
    //    我原来写的是 `[HarmonyPatch(typeof(FreeChatInputField), nameof(FreeChatInputField.Start))]` ——
    //    **`nameof` 能编译**（Start 是从基类继承来的），但 **Harmony 的 `DeclaredMethod` 只找
    //    "该类自己声明的"方法**，于是解析失败：
    // <code>
    //   [Warning: HarmonyX] AccessTools.DeclaredMethod: Could not find method for type
    //                       FreeChatInputField and name Start and parameters
    //   [Error :LightInDark] [LightPlugin.Load] HarmonyException: Patching exception in method null
    // </code>
    //    **而 `PatchAll` 是按类循环的 —— 一个类抛异常就中断整个循环**，
    //    排在 `ChatRichTextPatch` 之后的所有补丁（聊天优化、颜色……）**全部没挂上**。
    //    用户看到的就是"我整个聊天优化都炸了，渲染按钮啥的全没有，我的一堆颜色也没了"。
    //
    //    → **结论：patch 继承来的虚方法时，必须写"声明它的那个类"。**
    //      这里 `Start` 是 `AbstractChatInputField` 声明的 protected virtual（19.0 源码 L50），
    //      而 `FreeChatInputField` 只重写了 Awake/SetVisible/Clear/Submit —— 它**没有** Start。
    // =====================================================================

    [HarmonyPatch(typeof(AbstractChatInputField), nameof(AbstractChatInputField.Start))]
    public static class ChatInputStartPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(AbstractChatInputField __instance)
        {
            try
            {
                if (__instance == null) return;

                // ⚠️ 不能用 `is` / `as`（IL2CPP 里对派生类型判断会静默为 false，AGENTS.md §4.2.0）
                var field = __instance.TryCast<FreeChatInputField>();
                if (field == null) return;      // 不是大厅那个聊天框（可能是别的输入框），不管

                // ① 输入框显示原始标签（不解析 <color=...>）
                // ⚠️ **只改 TMP_InputField 自己的 richText**（官方支持的做法，它会自己去同步文本组件的 flag）。
                //    之前直接写 `outputText.richText = false` 是和 TMP_InputField **抢文本组件的管理权**：
                //    它会按自己的 richText 再把文本组件覆盖回来 → 字段与组件状态不一致 →
                //    删字时索引算错（表现就是「冒出一个方块、之后输入全乱」）。
                var box = field.GetComponentInChildren<TextBoxTMP>(true);
                if (box != null)
                {
                    try
                    {
                        var inputField = box.GetComponentInChildren<TMP_InputField>(true);
                        if (inputField != null) inputField.richText = false;
                    }
                    catch { }
                    LightLogger.Log("[ChatRichText] 聊天输入框已关闭富文本解析（只设 TMP_InputField.richText，显示原始标签）");
                }

                // ② 建「预览」按钮
                BuildPreviewButton(field);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[ChatRichText.ChatInputStartPatch]", ex);
            }
        }
    }

    // =====================================================================
    //  预览按钮
    // =====================================================================

    private static void BuildPreviewButton(FreeChatInputField field)
    {
        try
        {
            if (_previewButton != null) return;      // 已经建过

            // 原版发送按钮：AbstractChatInputField.submitButton（private → interop 里是属性）
            var src = GetSubmitButton(field);
            if (src == null)
            {
                LightLogger.LogWarning("[ChatRichText] 找不到原版发送按钮，预览按钮跳过");
                return;
            }

            LogSortingOnce(src);      // ★ 一次性诊断：把真实排序层级打出来

            var srcTf = src.transform;
            var clone = Object.Instantiate(srcTf.gameObject, srcTf.parent);
            clone.name = "LightChatPreviewButton";
            clone.SetActive(true);

            // ★ 放在发送按钮**下面**（用户："你把预览按钮放发送下面吧"）
            float h = 0.42f;
            try
            {
                var col = src.GetComponent<BoxCollider2D>();
                if (col != null && col.size.y > 0.05f) h = col.size.y;
            }
            catch { }
            clone.transform.localPosition = srcTf.localPosition + new Vector3(0f, -(h + 0.10f), 0f);
            clone.transform.localScale = srcTf.localScale;

            // ⚠️⚠️ **干掉克隆体自带的原版逻辑**：
            //    ChatInputFieldButton.Awake 里订阅了 `button.OnClick → OnButtonClicked → OnPressed`，
            //    不清理的话**点预览会连带把消息发出去**（§4.5 同一类坑）。
            try
            {
                var old = clone.GetComponent<ChatInputFieldButton>();
                if (old != null) Object.Destroy(old);
            }
            catch { }

            // 改文字（要在 SetEnabled 改颜色之前）
            SetLabel(clone, "预览");

            // 换点击逻辑
            var pb = clone.GetComponentInChildren<PassiveButton>(true);
            if (pb == null) { LightLogger.LogWarning("[ChatRichText] 预览按钮上没有 PassiveButton"); return; }

            // ⚠️⚠️ "点不了"的三个嫌疑，全部强制打开（2026-10-06 用户反馈）：
            //   ① **克隆体继承了原版"未聚焦就禁用"的状态** —— 原版聊天框在没焦点时会把发送键灰掉，
            //      我们克隆的那一刻正好是灰的 → 组件 disabled → 点不动；
            //   ② `pb.enabled = false`；
            //   ③ 碰撞盒被关掉。
            //    克隆时机不可控，所以三个都显式打开，别指望它。
            pb.enabled = true;
            try
            {
                var cols = pb.Colliders;
                if (cols != null) foreach (var c in cols) if (c != null) c.enabled = true;
            }
            catch { }

            pb.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();   // ★ 整体替换，不是追加
            pb.OnClick.AddListener((UnityAction)(() => OpenPreview(field)));

            _previewButton = clone;
            _previewLabel = FindLabel(clone);
            _previewPb = pb;

            // ---- 输入变化 → 刷新"能不能渲染" ----
            // ⚠️ **不挂聊天框的 OnChange**：OnChange 是在输入处理链路里**同步**触发的，
            //    挂在那里做额外工作，有可能在输入法提交那一帧挤掉字符（小概率吞字）。
            //    改由自己的 Update 轮询（ChatPreviewDriver），完全不碰输入链路。
            try
            {
                var driver = clone.AddComponent<ChatPreviewDriver>();
                driver.Field = field;
            }
            catch (Exception ex) { LightLogger.LogWarning($"[ChatRichText] 挂轮询驱动失败：{ex.Message}"); }

            RefreshPreviewButton(field);   // 先按当前内容定一次

            LightLogger.Log($"[ChatRichText] 预览按钮已建（位置 {clone.transform.localPosition}）");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ChatRichText.BuildPreviewButton]", ex);
        }
    }

    private static TextMeshPro? _previewLabel;
    private static PassiveButton? _previewPb;

    /// <summary>取按钮里的标签 TMP（第一个）。</summary>
    private static TextMeshPro? FindLabel(GameObject go)
    {
        try
        {
            foreach (var t in go.GetComponentsInChildren<TextMeshPro>(true))
                if (t != null) return t;
        }
        catch { }
        return null;
    }

    // =====================================================================
    //  「能不能渲染」检测 → 亮 / 灭
    //
    //  用户 2026-10-06："每输入一个 < 或 >，预览按钮检测能不能渲染出来，
    //  如果能，那么就亮，如果不能，那就不亮。"
    //
    //  ⚠️ 检测手段：**让 TMP 自己解析一遍，再对比"解析后"和"原文"**：
    //    · `<color=red>你好</color>` → 解析后 `你好` → **不相等 → 标签被吃掉了 → 能渲染** ✓
    //    · `你好`                    → 解析后还是 `你好` → 相等 → 没标签，不用预览
    //    · `<abc>你好`               → TMP 不认这个标签，**原样保留** → 相等 → 不算能渲染 ✓
    //
    //  这比"自己写正则匹配标签"可靠得多 —— **"认不认"是 TMP 说了算，不是我们说了算**。
    // =====================================================================

    /// <summary>
    /// 原文里有没有"TMP 真的会解析"的富文本标签。
    ///
    /// ⚠️⚠️ 2026-10-06 改（原来的实现有副作用）：
    ///   原来我造了一个**真的 TextMeshPro 当探针**去 `ForceMeshUpdate` 再对比解析结果：
    /// <code>
    ///   var go = new GameObject("LightChatRichProbe");
    ///   _probe = go.AddComponent&lt;TextMeshPro&gt;();     // ← 会自己建材质 + 渲染器
    /// </code>
    ///   用户反馈："豆腐块删不掉，而且**只会出现一个**" ——
    ///   **"删不掉 + 只有一个 + 位置固定"是"那是一个独立物体"的典型特征，不是文本。**
    ///   而那个探针正是一个挂在场景根、永不销毁的 TMP 渲染器。
    ///
    ///   → 现在改成**纯字符串扫描**：不创建任何 Unity 对象，不可能留下任何痕迹。
    ///     代价是"哪些标签算数"由我们列白名单，而不是 TMP 说了算 ——
    ///     但白名单就是 TMP 支持的那套，够用且可控。
    /// </summary>
    private static bool HasRenderableRichText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (text.IndexOf('<') < 0) return false;

        int i = 0;
        while (true)
        {
            i = text.IndexOf('<', i);
            if (i < 0) break;

            int j = text.IndexOf('>', i + 1);
            if (j < 0) break;                       // 没闭合，后面也不会有了

            if (IsKnownTmpTag(text.Substring(i + 1, j - i - 1))) return true;
            i = j + 1;
        }
        return false;
    }

    /// <summary>是不是 TMP 认得的标签名（`&lt;color=red&gt;` / `&lt;/color&gt;` / `&lt;b&gt;` …）。</summary>
    private static bool IsKnownTmpTag(string inner)
    {
        if (string.IsNullOrEmpty(inner)) return false;

        if (inner[0] == '/') inner = inner.Substring(1);      // 闭合标签
        if (inner.Length == 0) return false;

        // 取开头的连续字母作为标签名（后面可能是 = 参数）
        int k = 0;
        while (k < inner.Length && char.IsLetter(inner[k])) k++;
        if (k == 0) return false;

        switch (inner.Substring(0, k).ToLowerInvariant())
        {
            case "color": case "b": case "i": case "u": case "s":
            case "size": case "align": case "alpha": case "cspace": case "mspace":
            case "indent": case "lineheight": case "line-height": case "link":
            case "mark": case "nobr": case "page": case "pos": case "space":
            case "sprite": case "style": case "sub": case "sup": case "voffset":
            case "width": case "font": case "gradient": case "br":
                return true;
            default:
                return false;
        }
    }
    // =====================================================================
    //  诊断：输入框里是否混进了"会画成方块 / 破坏光标"的字符
    // =====================================================================

    /// <summary>已报告过的可疑码位（每种只报一次，避免刷屏）。</summary>
    private static readonly HashSet<int> _reportedUnsafe = new();

    /// <summary>
    /// 检测输入框文本里的可疑字符并打日志（带完整码位明细）。
    /// 用途：定位「删字冒方块」到底是**字符**问题还是**渲染残留** ——
    /// 若日志从不触发，说明方块不在文本里，得往渲染层查。
    /// </summary>
    private static void DiagnoseChatText(FreeChatInputField field)
    {
        try
        {
            string text = "";
            try { text = field.Text ?? ""; } catch { }

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bool bad = char.IsSurrogate(c)
                        || char.IsControl(c)
                        || (c >= '\u200B' && c <= '\u200F')
                        || (c >= '\u2028' && c <= '\u202E')
                        || (c >= '\u2060' && c <= '\u206F')
                        || c == '\uFEFF'
                        || (c >= '\uE000' && c <= '\uF8FF');
                if (!bad) continue;
                if (!_reportedUnsafe.Add(c)) continue;      // 每种码位只报一次

                var sb = new System.Text.StringBuilder(text.Length * 7 + 64);
                sb.Append("[ChatRichText.诊断] 输入框出现可疑字符 U+").Append(((int)c).ToString("X4"))
                  .Append("（index=").Append(i).Append("，长度=").Append(text.Length).Append("）码位明细：");
                for (int k = 0; k < text.Length; k++)
                    sb.Append("U+").Append(((int)text[k]).ToString("X4")).Append(' ');
                LightLogger.LogWarning(sb.ToString());
                break;   // 一次只报第一个
            }
        }
        catch { }
    }

    /// <summary>每帧轮询入口（由 <see cref="ChatPreviewDriver"/> 调用；不参与输入链路）。</summary>
    internal static void Tick(FreeChatInputField field)
    {
        RefreshPreviewButton(field);
        DiagnoseChatText(field);
        RichTextInputPatch.LogInputChange(field);   // 输入序列诊断（定位"第几个字被吞"）
    }

    /// <summary>上次的"能不能渲染"与文本长度，状态没变就跳过（这个方法每敲一个字都会被调）。</summary>
    private static bool _lastCanRender;
    private static int _lastCanRenderLen = -1;

    /// <summary>按当前输入刷新预览按钮的亮 / 灭（状态不变时不做任何事）。</summary>
    private static void RefreshPreviewButton(FreeChatInputField field)
    {
        try
        {
            if (_previewButton == null) return;

            string text = "";
            try { text = field.Text ?? ""; } catch { }

            bool canRender = HasRenderableRichText(text);

            // ⚠️ 这里挂在 OnChange 上，**每敲一个字都会进来**：
            //    状态没变就立刻返回，不做 UI 写操作、不写日志 ——
            //    否则每键一次的额外开销可能在输入法提交那一帧挤掉字符（小概率吞字）。
            if (canRender == _lastCanRender && text.Length == _lastCanRenderLen) return;
            _lastCanRender = canRender;
            _lastCanRenderLen = text.Length;

            if (_previewLabel != null)
                _previewLabel.color = canRender
                    ? UnityEngine.Color.white
                    : new UnityEngine.Color(0.5f, 0.5f, 0.5f, 0.85f);

            // ⚠️ **只改颜色，不关碰撞盒** —— 关掉的话点了彻底没反应；
            //    而"灭"只是个视觉提示（点了也只会提示"没有可预览的标签"）。
            if (_previewPb != null) _previewPb.enabled = true;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ChatRichText.RefreshPreviewButton]", ex);
        }
    }

    /// <summary>
    /// 一次性诊断：把**聊天框那一带的真实排序层级**打出来。
    ///
    /// ⚠️ 为什么需要它：19.0/18.0 的反编译源码里**一个 sortingOrder 都没有** ——
    ///    全在预制体资源里，静态读不出来。所以"窗口盖不盖得住"只能靠实机数据判断，
    ///    而不是像原来那样 100 → 320 → 900 一个个猜。
    ///    这条日志把父链上每一层的 <c>SortingGroup.order</c> / <c>SpriteRenderer.sortingOrder</c>
    ///    和 renderer 数量全打出来，一眼就能看出该给多少。
    /// </summary>
    private static bool _sortingLogged;

    private static void LogSortingOnce(Component src)
    {
        if (_sortingLogged) return;
        _sortingLogged = true;

        try
        {
            var sb = new System.Text.StringBuilder(512);
            sb.Append("[ChatRichText.排序诊断] 从聊天框往上 6 层：");

            var cur = src.transform;
            for (int depth = 0; cur != null && depth < 6; depth++, cur = cur.parent)
            {
                int sgOrder = int.MinValue;
                try
                {
                    var sg = cur.GetComponent<SortingGroup>();
                    if (sg != null) sgOrder = sg.sortingOrder;
                }
                catch { }

                // 找该层自己带的渲染器排序（取第一个）
                string srInfo = "-";
                try
                {
                    var sr = cur.GetComponent<SpriteRenderer>();
                    if (sr != null)
                        srInfo = $"layer={sr.sortingLayerName}/order={sr.sortingOrder}";
                }
                catch { }

                sb.Append($"\n  [{depth}] {cur.name}  SortingGroup.order={(sgOrder == int.MinValue ? "无" : sgOrder.ToString())}" +
                          $"  SpriteRenderer={srInfo}  z={cur.localPosition.z:F1}");
            }

            LightLogger.Log(sb.ToString());
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[ChatRichText.LogSortingOnce] {ex.Message}");
        }
    }

    /// <summary>拿原版发送按钮。字段是 private，interop 里能读到；读不到就按名字找。</summary>
    private static Component? GetSubmitButton(FreeChatInputField field)
    {
        try
        {
            var prop = field.GetType().GetProperty("submitButton");
            if (prop != null)
            {
                var v = prop.GetValue(field);
                if (v is Component c) return c;
            }
        }
        catch { }

        // 兜底：场景里找 ChatInputFieldButton
        try
        {
            var all = Object.FindObjectsOfType<ChatInputFieldButton>();
            if (all != null && all.Length > 0) return all[0];
        }
        catch { }

        return null;
    }

    /// <summary>把按钮上的文字改成 <paramref name="text"/>（同时断开翻译器，否则它会把文字改回去）。</summary>
    private static void SetLabel(GameObject go, string text)
    {
        try
        {
            // 断开 TextTranslatorTMP —— 它按翻译键重写文字（§5.2 同类坑）
            foreach (var tr in go.GetComponentsInChildren<TextTranslatorTMP>(true))
                if (tr != null) tr.enabled = false;

            foreach (var tmp in go.GetComponentsInChildren<TextMeshPro>(true))
            {
                if (tmp == null) continue;
                tmp.text = text;
                tmp.fontStyle = FontStyles.Bold;       // §12.1
                tmp.ForceMeshUpdate();
                break;                                  // 只改第一个（就是标签）
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[ChatRichText.SetLabel] {ex.Message}");
        }
    }

    // =====================================================================
    //  预览窗口
    // =====================================================================

    private static HudUIWindow? _previewWindow;

    /// <summary>
    /// 预览窗口的 z。
    ///
    /// ⚠️⚠️ 这个值被夹在**两个约束**之间，不能随便写（2026-10-06 连着踩了两次）：
    ///
    ///   · **不能太浅**：聊天框在 `z = -430`（`ChatScreenRoot`，日志实测），
    ///     同层同 order 时 **z 越小越靠前** —— 原来给 -200，整个窗口被聊天框压住；
    ///   · **不能太深**：**Main Camera 在 z=-10、near=-1000** →
    ///     可见范围是 **z ≥ -1010**。我一度给 -1000，
    ///     窗口自己的元素落在 -1009.9（勉强在内），而**内容还要再往前 2 个单位到 -1012**
    ///     → **整个掉出近裁剪面被裁掉** → 表现就是"窗口框在、里面啥也没有"。
    ///
    ///  → 取 **-600**：稳稳在聊天框(-430)前面，又离裁剪面(-1010)有 400 单位余量。
    /// </summary>
    private const float ZOffset = -600f;

    /// <summary>
    /// 把预览窗口里**每个渲染器**的真实状态打出来。
    ///
    /// 为什么需要：用户截图显示"框在、黑幕在、内容不在" ——
    /// 这既可能是"内容根本没建"，也可能是"建了但 layer/z/active 不对"，
    /// 还可能是"被别的渲染器盖住"。**光看截图分不出来，必须打数据。**
    /// </summary>
    private static void DumpWindowDiagnostics(Transform root)
    {
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            sb.Append("[ChatRichText.窗口诊断] 根=").Append(root.name)
              .Append("  activeInHierarchy=").Append(root.gameObject.activeInHierarchy);

            // 根自己那条链上的 SortingGroup
            var sg = root.GetComponentInParent<SortingGroup>();
            if (sg != null)
            {
                string layerName = "?";
                try { layerName = SortingLayer.IDToName(sg.sortingLayerID); } catch { }
                sb.Append($"\n  SortingGroup: layer='{layerName}'(id={sg.sortingLayerID}) order={sg.sortingOrder}" +
                          $" enabled={sg.enabled} active={sg.gameObject.activeInHierarchy}");
            }
            else sb.Append("\n  SortingGroup: 无");

            // 窗口下所有渲染器
            int total = 0, shown = 0;
            var all = root.GetComponentsInParent<Transform>(true);   // 只是拿 MetaWindow 链
            var meta = root.parent != null ? root.parent : root;

            foreach (var r in meta.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                total++;
                if (shown >= 14) continue;      // 别刷爆日志
                shown++;

                string extra = "";
                if (r is SpriteRenderer sr)
                    extra = $" sprite={(sr.sprite != null ? sr.sprite.name : "null")} size={sr.size} color={sr.color}";

                // ⚠️ TextMeshPro 不是 Renderer（它挂在 GameObject 上，渲染器是它的 TMPRenderer）
                //    所以不能写 `r is TextMeshPro`，要单独从物体上取。
                var tmp = r.GetComponent<TextMeshPro>();
                if (tmp != null)
                    extra += $" text='{(tmp.text != null && tmp.text.Length > 14 ? tmp.text.Substring(0, 14) + "…" : tmp.text)}' fs={tmp.fontSize}";

                var wp = r.transform.position;
                var lp = r.transform.localPosition;
                sb.Append($"\n  [{shown}] {r.GetType().Name} '{r.name}' path={PathOf(r.transform, meta)}" +
                          $" layer={r.gameObject.layer} active={r.gameObject.activeInHierarchy} enabled={r.enabled}" +
                          // ★ 世界/本地坐标 + 缩放全打出来 ——
                          //   "内容看不见"只有三种可能：位置不对 / 缩放为 0 / 被盖住。
                          //   上一轮我只打了 z，看不出位置问题，白跑一轮。
                          $" world=({wp.x:F2},{wp.y:F2},{wp.z:F2}) local=({lp.x:F2},{lp.y:F2},{lp.z:F2})" +
                          $" lossyScale=({r.transform.lossyScale.x:F2},{r.transform.lossyScale.y:F2})" +
                          $" sortingLayer='{r.sortingLayerName}'/{r.sortingOrder}{extra}");
            }

            sb.Append($"\n  渲染器共 {total} 个（只列了前 {shown} 个）");
            LightLogger.Log(sb.ToString());
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[ChatRichText.DumpWindowDiagnostics] {ex.Message}");
        }
    }

    /// <summary>从 node 到 stop 的相对路径（日志用）。</summary>
    private static string PathOf(Transform node, Transform stop)
    {
        try
        {
            var parts = new System.Collections.Generic.List<string>();
            var cur = node;
            for (int i = 0; cur != null && cur != stop && i < 8; i++, cur = cur.parent)
                parts.Insert(0, cur.name);
            return string.Join("/", parts);
        }
        catch { return "?"; }
    }

    private static void OpenPreview(FreeChatInputField field)
    {
        try
        {
            var text = "";
            try { text = field.Text ?? ""; } catch { }

            if (string.IsNullOrWhiteSpace(text))
            {
                LightLogger.Log("[ChatRichText] 输入框是空的，不弹预览");
                return;
            }

            ClosePreview();

            // ⚠️ 和 PresetWindow 一样：**必须显式传 parent** ——
            //    `HudUIWindow.Create` 默认拿 HudManager，而这里不一定有。
            var parent = Light.UI.Config.PresetWindow.ResolveParentCore();
            if (parent == null) { LightLogger.LogWarning("[ChatRichText] 找不到父物体"); return; }

            // ⚠️⚠️ **排序层级直接拉满**（用户："你看看原版层级，整大点"）。
            //
            //  查过 19.0 / 18.0 的反编译源码：**一个 `sortingOrder` 赋值都没有** ——
            //  说明原版这些值全写在**预制体资源**里，静态读不到，只能靠运行时诊断。
            //
            //  既然读不到，就别猜中间值（100 → 320 → 900 一路试太慢），
            //  `SortingGroup.sortingOrder` 是 int，实际有效范围到 32767 —— 直接给 30000，
            //  正常 UI 不可能排在这之上。
            //  ⚠️ 如果 30000 还被压住，那问题就**不是排序层级**（可能是 renderer 被禁用、
            //     或者不在同一台相机的 cullingMask 里），得换方向查。
            _previewWindow = HudUIWindow.Create("预览", new Vector2(6.20f, 3.00f), parent,
                blockInputBehind: true, sortingGroupOrder: 30000, z: ZOffset, topSortingLayer: true);
            if (_previewWindow == null || _previewWindow.Screen == null) { _previewWindow = null; return; }

            var root = _previewWindow.Screen.transform;

            // 标题
            var title = _previewWindow.AddText("预览（渲染后）", 1.60f, TextAlignmentOptions.Center);
            if (title != null) Light.UI.Config.PresetWindow.PlaceInRoot(
                title.transform, root, new Vector3(0f, 1.05f, -2f));

            // ★ 正文：**richText = true** 才会解析 <color=...> 这类标签
            var body = _previewWindow.AddText(text, 1.70f, TextAlignmentOptions.TopLeft);
            if (body != null)
            {
                body.richText = true;                   // ★ 核心
                body.enableAutoSizing = false;          // §12.2
                body.fontSize = 1.70f;
                body.fontSizeMin = 1.70f;
                body.fontSizeMax = 1.70f;
                body.enableWordWrapping = true;         // 长文本要换行
                body.overflowMode = TextOverflowModes.Overflow;
                body.rectTransform.sizeDelta = new Vector2(5.40f, 1.60f);
                body.rectTransform.pivot = new Vector2(0f, 1f);   // ★ pivot 必须在位置之前
                Light.UI.Config.PresetWindow.PlaceInRoot(
                    body.transform, root, new Vector3(-2.70f, 0.72f, -2f));
                body.ForceMeshUpdate();
            }

            // 关闭
            var close = HudUIButton.Create(root, "关闭", new Vector2(1.70f, 0.52f), ClosePreview);
            if (close != null && close.GameObject != null)
                close.SetPosition(new Vector3(0f, -1.10f, -2f));

            try { _previewWindow.ApplyCjkFont(); } catch { }

            // ★★ **内容全部建完之后**才抬排序（2026-10-06 真根因）——
            //    原来在 Create 里抬，那时内容还没建，后建的内容 order 还是 0，
            //    被 order=30000 的**黑幕**整个盖住 → 看起来就是一块空的暗板。
            _previewWindow.AscendSorting(30000);

            // ★★ 完整诊断（2026-10-06，第 4 轮才加上 —— 前三轮都在猜参数）
            //
            //  重新看用户截图：**窗口的框和黑幕都在，缺的是内容（标题/正文/关闭按钮）** ——
            //  这说明"内容没画出来"，**不是**"被聊天框挡住"。
            //  两件事的排查方向完全相反，我前三轮一直按后者在调 sortingOrder / sortingLayer。
            //
            //  这条把窗口里每个渲染器的真实状态打出来，一次定位。
            DumpWindowDiagnostics(root);

            LightLogger.Log($"[ChatRichText] 已打开预览（{text.Length} 字）");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ChatRichText.OpenPreview]", ex);
        }
    }

    private static void ClosePreview()
    {
        try { _previewWindow?.Close(); }
        catch { }
        finally { _previewWindow = null; }
    }
}

/// <summary>
/// 预览按钮 / 诊断的**每帧轮询驱动**。
///
/// ⚠️ 为什么不用聊天框的 <c>OnChange</c>：那个事件是在**输入处理链路里同步触发**的
///    （每敲一个字都会进来）。我们挂在那里读文本、改颜色、扫可疑字符，
///    就有可能在输入法提交那一帧挤掉字符 —— 表现为「小概率吞字」。
///    改由独立 MonoBehaviour 的 Update 轮询后，我们的代码**完全不碰输入链路**。
/// </summary>
public class ChatPreviewDriver : MonoBehaviour
{
    static ChatPreviewDriver()
    {
        try { ClassInjector.RegisterTypeInIl2Cpp<ChatPreviewDriver>(); }
        catch { }
    }

    /// <summary>要盯着的聊天输入框。</summary>
    public FreeChatInputField? Field;

    public void Update()
    {
        try
        {
            if (Field == null) return;
            ChatRichTextPatch.Tick(Field);
        }
        catch { }
    }
}

/// <summary>
/// **发送链路诊断** —— 定位"富文本标签是在哪一步被剥离的"。
///
/// 用户 2026-10-06："渲染不了…不过能发出去"。
/// `overrideColorTags = false` 已经确认生效，所以问题**不在渲染侧**，在**发送链路**上。
///
/// 这条链是：`ChatController.SendChat(text)` → `PlayerControl.RpcSendChat` → 接收端 `AddChat`。
/// **不猜在哪丢，三段都打一次**，一次就能定位。
///
/// ⚠️ 用 `object[] __args` 而不是具名参数 —— 原版这几个方法的签名跨版本会变，
///    用 __args 最不容易因为参数名单不同而被打包失败（PatchAll 一失败就带走后面所有补丁）。
/// </summary>
public static class ChatSendChainDiag
{
    private static void Log(string stage, object[]? args)
    {
        try
        {
            string text = "(无参数)";
            if (args != null && args.Length > 0)
            {
                foreach (var a in args)
                {
                    if (a is string s) { text = s; break; }
                }
            }
            LightLogger.Log($"[ChatSendChain] {stage} ← '{text}'（{text.Length} 字）");
        }
        catch { }
    }

    [HarmonyPatch(typeof(ChatController), nameof(ChatController.SendChat))]
    public static class SendChatPatch
    {
        [HarmonyPrefix]
        public static void Prefix(object[] __args) => Log("① SendChat", __args);
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSendChat))]
    public static class RpcSendChatPatch
    {
        [HarmonyPrefix]
        public static void Prefix(object[] __args) => Log("② RpcSendChat", __args);
    }

    [HarmonyPatch(typeof(ChatController), nameof(ChatController.AddChat))]
    public static class AddChatPatch
    {
        [HarmonyPrefix]
        public static void Prefix(object[] __args) => Log("③ AddChat", __args);
    }
}

/// <summary>
/// **关掉原版脏话过滤器,保住富文本标签。**
///
/// 用户 2026-10-06："还是没渲染"。三段诊断给出了确定答案：
/// <code>
///   ① SendChat    ← '(无参数)'
///   ② RpcSendChat ← '&lt;color=red&gt;1&lt;/color&gt;'（20 字）   ← 标签完好
///   ③ AddChat     ← '1'（1 字）                          ← 标签没了
/// </code>
///
/// → 标签是在 `AddChat` 里被剥掉的。看 19.0 源码 `ChatController.cs:428`：
/// <code>
///   public void AddChat(PlayerControl sourcePlayer, string chatText, bool censor = true)
///   {
///       ...
///       if (censor &amp;&amp; DataManager.Settings.Multiplayer.CensorChat)
///           chatText = BlockedWords.CensorWords(chatText, false);   // ★ 就是这里
///   }
/// </code>
///   **原版脏话过滤器把 `&lt;color=red&gt;` 这种整段当成违规内容处理掉了。**
///
/// ⚠️ **取舍**：这一刀把脏话过滤**整个关掉**（不只是富文本）。
///    影响范围只有"聊天里不再自动打码骂人词"，对本模组的使用场景可以接受。
///    如果你只想保标签、还要留脏话过滤，那就得去 patch `BlockedWords.CensorWords`
///    让它跳过 `&lt;...&gt;` 段 —— 那个更精细但也更容易出错。
/// </summary>
public static class ChatNoCensorPatch
{
    [HarmonyPatch(typeof(ChatController), nameof(ChatController.AddChat))]
    public static class AddChatNoCensor
    {
        [HarmonyPrefix]
        public static void Prefix(ref bool censor)
        {
            censor = false;
        }
    }
}

/// <summary>
/// **保住富文本标签 —— 绕开原版的正则剥离。**
///
/// 用户 2026-10-06，三段诊断定位到精确的一行（`PlayerControl.cs:2616`）：
/// <code>
///   public bool RpcSendChat(string chatText)
///   {
///       chatText = Regex.Replace(chatText, "&lt;.*?&gt;", string.Empty);   // ★ 把 &lt;...&gt; 全删了
///       ...
///   }
/// </code>
///  日志实证：② 的 Prefix 在这行**之前**跑 → 看到完整标签；
///  ③ `AddChat` 拿到的只剩 `M` → **两边完全吻合**。
///
///  【做法】用一个**正则碰不到的中转字符**（Unicode 私有使用区 U+E000/U+E001）：
///    · 进 `RpcSendChat` 前：`&lt;` → U+E000，`&gt;` → U+E001   → 文本里没有 `&lt;` 了，正则无事可做
///    · 进 `AddChat` 前：   U+E000 → `&lt;`，U+E001 → `&gt;`   → 还原成标签，TMP 正常渲染
///
///  **为什么不用 Transpiler 去掉那行**：IL2CPP 下改 IL 风险高得多，
///  而"编码/解码"只用两个 Prefix 就能达到同样效果，稳定且好回滚。
///
///  ⚠️ 私有使用区字符正常输入打不出来（我上一轮已经把那一带在 `IsAllowed` 里挡掉了），
///     所以不用担心用户手打 U+E000 造成误还原。
/// </summary>
public static class ChatRichTextProtect
{
    private const char Lt = '\uE000';   // 代表 '<'
    private const char Gt = '\uE001';   // 代表 '>'

    /// <summary>发送前：把尖括号换成中转字符，躲过 `Regex.Replace(chatText, "&lt;.*?&gt;", "")`。</summary>
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSendChat))]
    public static class EncodeBeforeSend
    {
        [HarmonyPrefix]
        public static void Prefix(ref string chatText)
        {
            try
            {
                if (string.IsNullOrEmpty(chatText)) return;
                if (chatText.IndexOf('<') < 0 && chatText.IndexOf('>') < 0) return;

                chatText = chatText.Replace('<', Lt).Replace('>', Gt);
                LightLogger.Log($"[ChatRichText] 发送前保护标签：'{chatText}'");
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ChatRichText.EncodeBeforeSend] {ex.Message}");
            }
        }
    }

    /// <summary>显示前：把中转字符还原成尖括号，让 TMP 渲染富文本。</summary>
    [HarmonyPatch(typeof(ChatController), nameof(ChatController.AddChat))]
    public static class DecodeBeforeDisplay
    {
        [HarmonyPrefix]
        public static void Prefix(ref string chatText)
        {
            try
            {
                if (string.IsNullOrEmpty(chatText)) return;
                if (chatText.IndexOf(Lt) < 0 && chatText.IndexOf(Gt) < 0) return;

                chatText = chatText.Replace(Lt, '<').Replace(Gt, '>');
                LightLogger.Log($"[ChatRichText] 显示前还原标签：'{chatText}'");
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ChatRichText.DecodeBeforeDisplay] {ex.Message}");
            }
        }
    }
}
