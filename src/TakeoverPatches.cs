using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using newsanguo.Scripts.Characters;

namespace Newsanguo.AutoAnthony;

/// <summary>
/// 整池接管的 Harmony 补丁集合。
///
/// 与旧内置实现的机制差异（重要）：
///  旧实现用两处补丁 —— ① newsanguo 自己的 <c>FilterThroughEpochs</c> 钩子过滤"获取链路"；
///  ② <c>NCardLibraryGrid._Ready</c> 后修剪私有 <c>_allCards</c> 字段隐藏图鉴。
///  但 <c>FilterThroughEpochs</c> 其实是 **sts2 核心** <see cref="CardPoolModel"/> 的方法（不是 newsanguo 的），
///  且 newsanguo 0.2.38 已把卡池简化为 <c>TypeListCardPoolModel</c>，私有字段名也不再保证稳定。
///
///  因此本实现改为在**唯一咽喉点** <see cref="CardPoolModel.AllCards"/> 上做 Postfix：
///  对 newsanguo 卡池直接重建整份成员表（AA 槽卡 + 手工保留的 2 张先古），手工卡自然从
///  奖励/商店/事件/图鉴/发现等一切入口消失 —— 因为这些入口最终都读 <c>AllCards</c>。
///
///  ⚠ 为什么是 AllCards 而不是 GenerateAllCards：
///  <c>TypeListCardPoolModel</c>（newsanguo 卡池的基类，来自 RitsuLib）**复写了** <c>GenerateAllCards</c>，
///  而 Harmony 补丁改的是方法体本身、不参与虚分派 —— 补在基类 <c>CardPoolModel.GenerateAllCards</c> 上
///  会被子类复写完全绕过（静默不生效）。<c>AllCards</c> 则只在 <c>CardPoolModel</c> 上声明、无人复写，
///  是真正能拦住所有卡池的那个点（东尼算法自己也是补在这里）。
///
///  用 Postfix（而非 Prefix）有两个好处：① 能读到原始列表，从而按<b>规则</b>（稀有度）保留先古卡，
///  不必硬编码卡名；② 不会自我递归。
/// </summary>
internal static class TakeoverPatches
{
    private static bool _installed;

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;

        var harmony = new Harmony(ModEntry.ModId);
        InstallPoolContentsPatch(harmony);
        InstallStartingDeckPatch(harmony);
    }

    /// <summary>在 CardPoolModel.AllCards 取值器上装 Postfix；只对 newsanguo 卡池生效。</summary>
    private static void InstallPoolContentsPatch(Harmony harmony)
    {
        try
        {
            // AllCards 只在 CardPoolModel 上声明、无人复写，因此它是唯一能拦住全部卡池的补丁点。
            var target = AccessTools.PropertyGetter(typeof(CardPoolModel), nameof(CardPoolModel.AllCards));
            if (target is null)
            {
                Log.Error("Cannot resolve CardPoolModel.AllCards getter; pool takeover inactive.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(
                AccessTools.Method(typeof(PoolContentsPatch), nameof(PoolContentsPatch.Postfix))));
            Log.Info("Pool takeover patch installed (CardPoolModel.AllCards getter postfix).");
        }
        catch (Exception e)
        {
            Log.Error("Install pool takeover patch FAILED: " + e);
        }
    }

    /// <summary>
    /// 接管激活后把 ModCharacterTemplate 的 StartingDeck getter 劫持为 AA 基础槽卡。
    /// 目标方法为 ModCharacterTemplate&lt;...&gt;.StartingDeck 的 sealed getter
    /// （NewsanguoCharacter 不重写）；RitsuLib 的起始内容后置会跳过模板 getter，
    /// 因此 Prefix 返回 false 后不会有任何手工初始卡被追加。
    /// </summary>
    private static void InstallStartingDeckPatch(Harmony harmony)
    {
        try
        {
            // NewsanguoCharacter : ModCharacterTemplate<NewsanguoCardPool, NewsanguoRelicPool, NewsanguoPotionPool>
            var baseType = typeof(NewsanguoCharacter).BaseType;
            var getter = baseType?.GetProperty(
                "StartingDeck", BindingFlags.Instance | BindingFlags.Public)?.GetGetMethod(nonPublic: true);
            if (getter is null)
            {
                Log.Error("Cannot resolve ModCharacterTemplate.StartingDeck getter; deck replacement inactive.");
                return;
            }

            harmony.Patch(getter, prefix: new HarmonyMethod(
                AccessTools.Method(typeof(StartingDeckPatch), nameof(StartingDeckPatch.Prefix))));
            Log.Info("Starting-deck replacement patch installed (basics=" + Adapter.BasicSlotCount +
                     ", deck=" + string.Join("/", Adapter.BasicSlotDeckCounts) + ").");
        }
        catch (Exception e)
        {
            Log.Error("Install starting-deck patch FAILED: " + e);
        }
    }
}

/// <summary>卡池成员表接管：newsanguo 卡池只保留「AA 槽卡 + 手工先古」，其余手工卡全部退出获取池。</summary>
internal static class PoolContentsPatch
{
    internal static void Postfix(CardPoolModel __instance, ref IEnumerable<CardModel> __result)
    {
        if (!Adapter.IsTakeoverActive) return;
        if (__instance is not NewsanguoCardPool) return;

        try
        {
            __result = Adapter.RebuildPoolContents(__result);
        }
        catch (Exception e)
        {
            // 重建失败时保持原池（宁可没接管，也不要空池导致开局卡死）
            Log.Error("Rebuild pool contents FAILED; keeping original pool: " + e.Message);
        }
    }
}

/// <summary>初始牌组替换：接管激活时把 NewsanguoCharacter 初始牌组换成 Basic 槽卡。</summary>
internal static class StartingDeckPatch
{
    internal static bool Prefix(object __instance, ref IEnumerable<CardModel> __result)
    {
        if (!Adapter.IsTakeoverActive) return true;
        if (__instance is not NewsanguoCharacter) return true;

        var deck = Adapter.TryBuildReplacementStartingDeck();
        if (deck is null) return true;

        __result = deck;
        return false;
    }
}
