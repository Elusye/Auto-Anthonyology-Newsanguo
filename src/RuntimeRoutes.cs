using System;
using System.Linq;
using System.Threading.Tasks;
using AutoAnthony;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using newsanguo.Scripts.Api;

namespace Newsanguo.AutoAnthony;

/// <summary>
/// 三国"执行词"运行时路由 —— 把 AA 生成卡上内建 executor 不认识的 opcode 接到 newsanguo 已实现的机制。
///
/// 自写目录（<see cref="SelfCatalog"/>）里带 CustomOpcode 的原子，数值/文案/分类沿用官方基线，
/// 但执行时内建 executor 不认识该 opcode，会落到 <see cref="ComponentRuntimeApi"/> 按 (Opcode, Variant) 分发。
///
/// **调用面收敛在 <see cref="NewsanguoPublicApi"/> 上。**
/// 除原版 <see cref="FrailPower"/> 外，这里不直接引用 newsanguo 的实现类型
/// —— 那些改名就会让我们**加载即崩**（硬引用 dll 的代价）。
/// 代价：清单里的 newsanguo 依赖必须 ≥ 0.2.39，否则会在加载时抛 MissingMethod。
///
/// **没有参数位的枚举一律拆成一个种类一个 opcode**（Token 种类、变化目标种类），
/// handler 在构造时绑定枚举值 —— 比伪造参数通道干净。
///
/// 目标解析约定：<c>context.Target ?? 自己</c> —— 原子的 <c>Target</c> 决定执行器传什么；
/// 自身效果拿不到目标时就落回自己（与 <c>gain_block</c> 等内建 op 的行为一致）。
/// </summary>
internal static class RuntimeRoutes
{
    private const string PackageId = "newsanguo:autoanthony:runtime";

    private static bool _registered;
    private static bool _shapeDumped;

    /// <summary>
    /// 一次性把 <c>context.Card</c> 的公开属性名打进日志。
    ///
    /// 用途：定位"神秘卡牌"。生成卡的**名字是随机拼的**，从"无限鲨"这类名字无法反查它出自哪张壳；
    /// 而只含内建 opcode 的卡牌（例如只有 <c>end_turn</c>）根本不会走到我们的路由，
    /// 日志里什么都没有。把卡对象的属性名列出来，就能知道该读哪个属性拿到它的原子列表，
    /// 之后任何"这牌行为不对"的反馈都能一眼定位到壳。
    /// </summary>
    internal static void DumpCardShapeOnce(ComponentRuntimeContext context)
    {
        if (_shapeDumped || context.Card is not { } card) return;
        _shapeDumped = true;
        try
        {
            var cardProps = string.Join(",", typeof(CardModel).GetProperties().Select(p => p.Name));
            Log.Info($"card shape: type={card.GetType().FullName} baseProps=[{cardProps}]");
        }
        catch (Exception ex)
        {
            Log.Error("card shape dump failed: " + ex.Message);
        }
    }

    /// <summary>注册全部三国执行路由。须在路由表冻结（首次生成卡 op 执行）前调用；幂等。</summary>
    internal static void EnsureRegistered()
    {
        if (_registered) return;
        _registered = true;
        ComponentRuntimeApi.RegisterPackage(PackageId,
        [
            // ---- 资源 ----
            new ComponentRuntimeRoute("ns_gain_wine", "", new GainWineHandler()),
            new ComponentRuntimeRoute("ns_gain_heaven", "", new GainHeavenHandler()),
            new ComponentRuntimeRoute("ns_lose_heaven", "", new LoseHeavenHandler()),
            // ---- 能力 ----
            new ComponentRuntimeRoute("ns_frail", "", new FrailHandler()),
            new ComponentRuntimeRoute("ns_frail_self", "", new FrailSelfHandler()),
            new ComponentRuntimeRoute("ns_entangled", "", new EntangledHandler()),
            new ComponentRuntimeRoute("ns_flight", "", new FlightHandler()),
            new ComponentRuntimeRoute("ns_traitor_tyranny", "", new TraitorTyrannyHandler()),
            new ComponentRuntimeRoute("ns_stun", "", new StunHandler()),
            // ---- 牌库 ----
            new ComponentRuntimeRoute("ns_scry", "", new ScryHandler()),
            new ComponentRuntimeRoute("ns_exhume", "", new ExhumeHandler()),
            // 「自带判断的效果」：条件不成立时什么都不做。阈值放进 variant ——
            // 这样只需要 context.Amount 一个数字（授予量），
            // 且不同阈值天然成为**不同组件**，与 AA 的身份模型一致。
            new ComponentRuntimeRoute("ns_heaven_if_le", "2", new HeavenIfBelowHandler(2)),
            new ComponentRuntimeRoute("ns_stun_all_if_wine", "3", new StunAllIfWineHandler(3)),
            new ComponentRuntimeRoute("ns_draw_if_hand_eq", "2", new DrawIfHandEqHandler(2)),
            new ComponentRuntimeRoute("ns_energy_if_last_skill", "", new EnergyIfLastSkillHandler()),
            new ComponentRuntimeRoute("ns_rebound", "", new ReboundHandler()),
            new ComponentRuntimeRoute("ns_hand_to_draw_top", "", new HandToDrawTopHandler()),
            new ComponentRuntimeRoute("ns_copy_to_hand", "", new CopyToHandHandler()),
            new ComponentRuntimeRoute("ns_exhaust_na_heavens", "", new ExhaustNonAttackHeavensHandler()),
            // ---- 生成 Token：一个种类一个去向一个 opcode ----
            new ComponentRuntimeRoute("ns_add_cudgel", "", new AddTokenHandler(NewsanguoTokenKind.MilitaryCudgel, PileType.Hand)),
            new ComponentRuntimeRoute("ns_add_soldier", "", new AddTokenHandler(NewsanguoTokenKind.Soldier, PileType.Hand)),
            // 「不胜酒力」在 newsanguo 原卡里是进**弃牌堆**的，不是手牌。
            new ComponentRuntimeRoute("ns_add_lightweight", "", new AddTokenHandler(NewsanguoTokenKind.Lightweight, PileType.Discard)),
            // ---- 手牌变化为 Token：同样一个种类一个 opcode ----
            new ComponentRuntimeRoute("ns_transform_soldier", "", new TransformToTokenHandler(NewsanguoTokenKind.Soldier)),
            new ComponentRuntimeRoute("ns_transform_cudgel", "", new TransformToTokenHandler(NewsanguoTokenKind.MilitaryCudgel)),
            new ComponentRuntimeRoute("ns_transform_lightweight", "", new TransformToTokenHandler(NewsanguoTokenKind.Lightweight)),
            // ---- 音频副作用（固定降到 25%，无百分比版本，见 NewsanguoPublicApi 注释） ----
        ]);
        Log.Info("Runtime routes=" + ComponentRuntimeApi.RegisteredRoutes.Count + ": " +
                 string.Join(",", ComponentRuntimeApi.RegisteredRoutes));
    }

    /// <summary>取"该效果作用于谁"：有解析出的目标就用目标，否则落回自己。
    ///
    /// ⚠ 这个"有就用、没有就用自己"的约定**有歧义**：如果 AA 对**自身**效果也填了
    /// <c>context.Target</c>（例如填成敌人），自身效果就会打到别人身上 ——
    /// 实测症状是"打出给予自己帝王之征的牌，自己没拿到"。
    /// 因此**自身效果一律改用 <c>*_self</c> 后缀的 opcode**（永远作用于自己），
    /// 不再依赖这里的兜底。本方法保留给"确实需要目标"的效果。
    ///
    /// 解析失败会打日志 —— 之前是静默 return false，效果没生效却查不到原因（踩过）。</summary>
    internal static bool TryResolve(
        ComponentRuntimeContext context, out Player player, out Creature actor, out Creature target)
    {
        player = null!;
        actor = null!;
        target = null!;
        DumpCardShapeOnce(context);
        if (context.Card?.Owner is not { } owner)
        {
            Log.Error("runtime route: context.Card?.Owner 为空，效果被跳过");
            return false;
        }
        if (owner.Creature is not { } creature)
        {
            Log.Error("runtime route: owner.Creature 为空，效果被跳过");
            return false;
        }
        player = owner;
        actor = creature;
        target = context.Target ?? creature;
        return true;
    }

    /// <summary>只取"施放者自己"：自身效果专用，不看 <c>context.Target</c>。</summary>
    internal static bool TryActor(
        ComponentRuntimeContext context, out Player player, out Creature actor)
    {
        player = null!;
        actor = null!;
        DumpCardShapeOnce(context);
        if (context.Card?.Owner is not { } owner)
        {
            Log.Error("runtime route: context.Card?.Owner 为空，效果被跳过");
            return false;
        }
        if (owner.Creature is not { } creature)
        {
            Log.Error("runtime route: owner.Creature 为空，效果被跳过");
            return false;
        }
        player = owner;
        actor = creature;
        return true;
    }
}

/// <summary>ns_gain_wine → 复用「酒力」（DrunkenMightPower）。</summary>
internal sealed class GainWineHandler : IComponentRuntimeHandler
{
    public async Task<bool> ExecuteAsync(ComponentRuntimeContext context)
    {
        if (context.Card?.Owner is not { } player || player.Creature is not { } creature)
            return false;

        await NewsanguoPublicApi.ApplyDrunkenMight(
            context.ChoiceContext, creature, context.Amount, creature, context.Card, silent: false);
        return true;
    }
}

/// <summary>ns_gain_heaven → 复用「天意之力」次级资源。</summary>
internal sealed class GainHeavenHandler : IComponentRuntimeHandler
{
    public async Task<bool> ExecuteAsync(ComponentRuntimeContext context)
    {
        if (context.Card?.Owner is not { } player) return false;

        await NewsanguoPublicApi.AddHeavensForce(
            context.ChoiceContext, player, context.Amount, context.Card);
        return true;
    }
}

/// <summary>ns_lose_heaven → 失去天意之力（同一入口传负增量）。</summary>
internal sealed class LoseHeavenHandler : IComponentRuntimeHandler
{
    public async Task<bool> ExecuteAsync(ComponentRuntimeContext context)
    {
        if (context.Card?.Owner is not { } player) return false;

        await NewsanguoPublicApi.AddHeavensForce(
            context.ChoiceContext, player, -context.Amount, context.Card);
        return true;
    }
}

/// <summary>ns_frail → 施加原版「脆弱」。自身/目标由原子的 Target 决定。</summary>
internal sealed class FrailHandler : IComponentRuntimeHandler
{
    public async Task<bool> ExecuteAsync(ComponentRuntimeContext context)
    {
        if (!RuntimeRoutes.TryResolve(context, out _, out var actor, out var target)) return false;

        await PowerCmd.Apply<FrailPower>(
            context.ChoiceContext, target, context.Amount, actor, context.Card);
        return true;
    }
}

/// <summary>ns_entangled → 「缠身」：本回合不能打出攻击牌。
///
/// **永远是自身效果**（给敌人挂这个能力没有意义），所以用 <see cref="RuntimeRoutes.TryActor"/>，
/// 不看 <c>context.Target</c> —— 实测 AA 连自身效果也会填 Target（已证实），
/// 依赖它就等于把自身增益/减益打到别人身上。
///
/// ⚠ 布尔开关型能力（<c>PowerStackType.Single</c> + <c>AllowNegative =&gt; false</c>），
/// 必须传正整数 1；原版「接着奏乐接着舞」也是传字面量 1。</summary>
internal sealed class EntangledHandler : IComponentRuntimeHandler
{
    public async Task<bool> ExecuteAsync(ComponentRuntimeContext context)
    {
        if (!RuntimeRoutes.TryActor(context, out _, out var actor)) return false;

        Log.Info($"ns_entangled: cardType={context.Card?.Type} amount={context.Amount} " +
                 $"contextTargetNull={context.Target is null}");

        await NewsanguoPublicApi.ApplyEntangled(
            context.ChoiceContext, actor, 1, actor, context.Card);
        return true;
    }
}



/// <summary>ns_frail_self → 「脆弱」施加到<b>自己</b>（与 ns_frail 分开，原因同上）。</summary>
internal sealed class FrailSelfHandler : IComponentRuntimeHandler
{
    public async Task<bool> ExecuteAsync(ComponentRuntimeContext context)
    {
        if (!RuntimeRoutes.TryActor(context, out _, out var actor)) return false;

        Log.Info($"ns_frail_self: amount={context.Amount} contextTargetNull={context.Target is null}");

        await PowerCmd.Apply<FrailPower>(
            context.ChoiceContext, actor, context.Amount, actor, context.Card);
        return true;
    }
}

/// <summary>ns_flight → 「飞行」。</summary>
internal sealed class FlightHandler : IComponentRuntimeHandler
{
    public async Task<bool> ExecuteAsync(ComponentRuntimeContext context)
    {
        if (!RuntimeRoutes.TryResolve(context, out _, out var actor, out var target)) return false;

        await NewsanguoPublicApi.ApplyFlight(
            context.ChoiceContext, target, context.Amount, actor, context.Card);
        return true;
    }
}

/// <summary>ns_traitor_tyranny → 「国贼」：目标的易伤不会减少。
///
/// 与 ns_entangled 同因：<c>TraitorTyrannyPower</c> 也是
/// <c>PowerStackType.Single</c> + <c>AllowNegative =&gt; false</c> 的布尔开关，
/// 而本原子没有数值槽 → Amount 会是 0。固定传 1。</summary>
internal sealed class TraitorTyrannyHandler : IComponentRuntimeHandler
{
    public async Task<bool> ExecuteAsync(ComponentRuntimeContext context)
    {
        if (!RuntimeRoutes.TryResolve(context, out _, out var actor, out var target)) return false;

        if (context.Amount != 1)
        {
            Log.Info($"ns_traitor_tyranny: Amount={context.Amount}（布尔开关型能力，按 1 施加）");
        }

        await NewsanguoPublicApi.ApplyTraitorTyranny(
            context.ChoiceContext, target, 1, actor, context.Card);
        return true;
    }
}

/// <summary>ns_stun → 击晕目标。原版 <c>CreatureCmd.Stun</c> 无层数概念，所以 <c>Amount</c> 不参与。</summary>
internal sealed class StunHandler : IComponentRuntimeHandler
{
    public async Task<bool> ExecuteAsync(ComponentRuntimeContext context)
    {
        if (!RuntimeRoutes.TryResolve(context, out _, out _, out var target)) return false;

        await NewsanguoPublicApi.Stun(target);
        return true;
    }
}

/// <summary>ns_exhume → 「从消耗牌堆中选择任意张牌放入手牌」（挖坟）。
///
/// 这是**引擎侧**能力，不需要 newsanguo 出入口 —— 照抄 newsanguo「亡灵复活术」的写法：
/// <c>PileType.Exhaust.GetPile</c> + <c>CardSelectCmd.FromCombatPile</c> + <c>CardPileCmd.Add</c>。
///
/// ⚠ 选择界面的提示文案复用了 newsanguo 的本地化键 <c>NEWSANGUO_CARD_SELECT_ANY</c>
/// （「选择任意张」）。它是软依赖：只要 newsanguo 在，键就在。
///
/// <c>Amount</c> 是"至多几张"；原子没有数值槽时 Amount 为 0，此时按"消耗牌堆全量"作为上限。</summary>
internal sealed class ExhumeHandler : IComponentRuntimeHandler
{
    public async Task<bool> ExecuteAsync(ComponentRuntimeContext context)
    {
        if (context.Card?.Owner is not { } player) return false;

        var exhaust = PileType.Exhaust.GetPile(player);
        if (exhaust is null || exhaust.Cards.Count == 0) return true;   // 空牌堆：无事发生，不是失败

        var max = context.Amount > 0 ? Math.Min(context.Amount, exhaust.Cards.Count) : exhaust.Cards.Count;
        Log.Info($"ns_exhume: exhaust={exhaust.Cards.Count} max={max} amount={context.Amount}");

        var selected = await CardSelectCmd.FromCombatPile(
            context: context.ChoiceContext,
            pile: exhaust,
            player: player,
            prefs: new CardSelectorPrefs(
                new LocString("cards", "NEWSANGUO_CARD_SELECT_ANY"), 0, max),
            filter: _ => true);

        var moved = 0;
        foreach (var card in selected)
        {
            await CardPileCmd.Add(card, PileType.Hand);
            moved++;
        }
        Log.Info($"ns_exhume: moved {moved} card(s) to hand");
        return true;
    }
}

/// <summary>ns_heaven_if_le → 「若你的天意之力不大于 N 点，获得 M 点天意之力」。
///
/// 这是**自带判断的效果 opcode**：AA 的 condition 是封闭集合、mod 扩不了
/// （只有 IComponentRuntimeHandler 一个扩展点，且 AA 会校验 owner 必须指向真正的条件原子），
/// 所以不拆成"条件原子 + 效果原子"，而是把判断放进 handler 内部。
///
/// 阈值来自 **variant**（构造时绑定），授予量来自 <c>context.Amount</c> ——
/// 因为 handler 只能拿到单一 Amount，两个数字必须一个走 variant、一个走数值槽。
///
/// ⚠ 代价：对 AA 而言这是个**无条件**效果，估值会按"条件总成立"算，生成卡略偏贵。
/// ⚠ 条件不成立时返回 <c>true</c>（什么都没做），**不能返回 false** ——
/// false 在 AA 眼里是"执行失败"，可能触发容错路径，而这里只是"条件没满足"。</summary>
internal sealed class HeavenIfBelowHandler : IComponentRuntimeHandler
{
    private readonly int _threshold;

    internal HeavenIfBelowHandler(int threshold) => _threshold = threshold;

    public async Task<bool> ExecuteAsync(ComponentRuntimeContext context)
    {
        if (context.Card?.Owner is not { } player) return false;

        var current = NewsanguoPublicApi.GetHeavensForce(player);
        var grant = context.Amount;
        Log.Info($"ns_heaven_if_le: current={current} threshold={_threshold} grant={grant}");

        if (current > _threshold) return true;   // 条件不成立：什么都不做，但**不是失败**

        await NewsanguoPublicApi.AddHeavensForce(
            context.ChoiceContext, player, grant, context.Card);
        return true;
    }
}


/// <summary>ns_stun_all_if_wine → 「若你的酒力不小于 N 点：击晕所有敌人」。
///
/// 与 ns_heaven_if_le 同一套模式（自带判断的效果 opcode）：
/// 阈值走 **variant**（构造时绑定，不同阈值天然是不同组件），
/// 条件不成立时返回 <c>true</c>（什么都不做，但不是失败）。
///
/// 酒力用 <c>NewsanguoPublicApi.GetDrunkenMight</c> 读（0.2.39 公开入口）；
/// 敌人列表用引擎的 <c>CombatState.HittableEnemies</c>；击晕用公开的 <c>Stun</c>。
/// </summary>
internal sealed class StunAllIfWineHandler : IComponentRuntimeHandler
{
    private readonly int _threshold;

    internal StunAllIfWineHandler(int threshold) => _threshold = threshold;

    public async Task<bool> ExecuteAsync(ComponentRuntimeContext context)
    {
        if (context.Card?.Owner is not { } player) return false;
        if (player.Creature?.CombatState is not { } combatState) return false;

        var wine = NewsanguoPublicApi.GetDrunkenMight(player);
        Log.Info($"ns_stun_all_if_wine: wine={wine} threshold={_threshold}");

        if (wine < _threshold) return true;   // 条件不成立：什么都不做，但**不是失败**

        var hit = 0;
        foreach (var enemy in combatState.HittableEnemies)
        {
            await NewsanguoPublicApi.Stun(enemy);
            hit++;
        }
        Log.Info($"ns_stun_all_if_wine: stunned {hit} enemy(ies)");
        return true;
    }
}

/// <summary>ns_draw_if_hand_eq → 「若你打出这张牌后恰好剩余 N 张手牌，则再抽 M 张牌」。
///
/// 条件与效果**都在引擎公开面上**（照抄 newsanguo「密谋」Plot.OnPlay）：
///   手牌数 = <c>PileType.Hand.GetPile(player).Cards.Count</c>
///   —— 打出时这张牌**已离开手牌**，所以此刻的牌数正是"打出后"的牌数（原卡注释也这么说）；
///   抽牌   = <c>CardPileCmd.Draw(ctx, n, player)</c>。
/// 阈值 N 是卡面固定的，放进 variant。
/// </summary>
internal sealed class DrawIfHandEqHandler : IComponentRuntimeHandler
{
    private readonly int _exactly;

    internal DrawIfHandEqHandler(int exactly) => _exactly = exactly;

    public async Task<bool> ExecuteAsync(ComponentRuntimeContext context)
    {
        if (context.Card?.Owner is not { } player) return false;

        var hand = PileType.Hand.GetPile(player)?.Cards.Count ?? -1;
        Log.Info($"ns_draw_if_hand_eq: hand={hand} exactly={_exactly} draw={context.Amount}");

        if (hand != _exactly) return true;   // 条件不成立：什么都不做，但**不是失败**

        await CardPileCmd.Draw(context.ChoiceContext, context.Amount, player);
        return true;
    }
}

/// <summary>ns_energy_if_last_skill → 「若你打出的上一张牌是技能牌，获得 N 点能量」。
///
/// 条件照抄 newsanguo「陶醉」Intoxicated.OnPlay：
///   <c>CombatManager.Instance.History.CardPlaysFinished.LastOrDefault(e =&gt; e.CardPlay?.Card?.Owner == player)</c>
/// 原卡注释点明了一个关键语义：**此牌尚未结算完成，不会把自己算进去** ——
/// 所以这里读到的确实是"上一张"，而不是"当前这张"。
///
/// 条件不成立返回 <c>true</c>（什么都不做，但**不是失败**）。无阈值，故 variant 为空串。
/// </summary>
internal sealed class EnergyIfLastSkillHandler : IComponentRuntimeHandler
{
    public async Task<bool> ExecuteAsync(ComponentRuntimeContext context)
    {
        if (context.Card?.Owner is not { } player) return false;

        var lastPlay = CombatManager.Instance.History.CardPlaysFinished
            .LastOrDefault(entry => entry.CardPlay?.Card?.Owner == player);
        var lastWasSkill = lastPlay is not null && lastPlay.CardPlay.Card.Type == CardType.Skill;
        Log.Info($"ns_energy_if_last_skill: lastWasSkill={lastWasSkill} energy={context.Amount}");

        if (!lastWasSkill) return true;   // 条件不成立：什么都不做，但**不是失败**

        await PlayerCmd.GainEnergy(context.Amount, player);
        return true;
    }
}

/// <summary>ns_rebound → 「将你在本回合打出的下一张牌放置到你的抽牌堆顶部」。
///
/// 直接用**原版** <c>ReboundPower</c>（newsanguo 的「万万没有此事啊」也是这么写的），
/// 所以既不碰 newsanguo 私有面，也不需要自己跟踪"下一张打出的牌"。
/// </summary>
internal sealed class ReboundHandler : IComponentRuntimeHandler
{
    public async Task<bool> ExecuteAsync(ComponentRuntimeContext context)
    {
        if (!RuntimeRoutes.TryActor(context, out _, out var actor)) return false;

        Log.Info("ns_rebound: applying ReboundPower");
        await PowerCmd.Apply<ReboundPower>(context.ChoiceContext, actor, 1, actor, context.Card);
        return true;
    }
}

/// <summary>ns_hand_to_draw_top → 「将手牌中的一张牌放到抽牌堆的顶部，并且在其被打出之前其耗能变为 0」。
///
/// 照抄 newsanguo「开玩笑」JustKidding.OnPlay：
///   <c>CardSelectCmd.FromHand</c> 选一张（**可放弃选择**）→
///   <c>CardPileCmd.Add(card, PileType.Draw, CardPilePosition.Top)</c> →
///   <c>card.EnergyCost.SetUntilPlayed(0)</c>。
/// 放弃选择时返回 <c>true</c>（什么都没做，但不是失败）。
/// </summary>
internal sealed class HandToDrawTopHandler : IComponentRuntimeHandler
{
    public async Task<bool> ExecuteAsync(ComponentRuntimeContext context)
    {
        if (!RuntimeRoutes.TryActor(context, out var player, out _)) return false;

        var picked = (await CardSelectCmd.FromHand(
            prefs: new CardSelectorPrefs(new LocString("cards", "NEWSANGUO_CARD_SELECT_ANY"), 1),
            context: context.ChoiceContext,
            player: player,
            filter: null,
            source: context.Card)).FirstOrDefault();

        if (picked is null) return true;   // 可放弃选择

        await CardPileCmd.Add(picked, PileType.Draw, CardPilePosition.Top);
        picked.EnergyCost.SetUntilPlayed(0);
        Log.Info("ns_hand_to_draw_top: moved " + picked.GetType().Name + " to draw pile top, cost 0 until played");
        return true;
    }
}

/// <summary>ns_scry → 「预见」：查看抽牌堆顶部 N 张，可选择丢弃任意张。</summary>
internal sealed class ScryHandler : IComponentRuntimeHandler
{
    public async Task<bool> ExecuteAsync(ComponentRuntimeContext context)
    {
        if (context.Card?.Owner is not { } player) return false;

        await NewsanguoPublicApi.Scry(context.ChoiceContext, player, context.Amount);
        return true;
    }
}

/// <summary>ns_copy_to_hand → 生成"此牌"的复制品加入手牌（乌角鲨 / 笑面虎）。</summary>
internal sealed class CopyToHandHandler : IComponentRuntimeHandler
{
    public async Task<bool> ExecuteAsync(ComponentRuntimeContext context)
    {
        if (context.Card?.Owner is not { } player || context.Card is not { } card) return false;

        await NewsanguoPublicApi.CopyCardToHand(player, card, context.Amount);
        return true;
    }
}

/// <summary>ns_exhaust_na_heavens → 消耗所有非攻击牌，每张给 N 点天意之力。
/// 必须一次调用完成：消耗动作与"消耗了几张"要在同一时刻观察，拆成两步跨 mod 做不到。</summary>
internal sealed class ExhaustNonAttackHeavensHandler : IComponentRuntimeHandler
{
    public async Task<bool> ExecuteAsync(ComponentRuntimeContext context)
    {
        if (context.Card?.Owner is not { } player) return false;

        await NewsanguoPublicApi.ExhaustNonAttackCardsForHeavens(
            context.ChoiceContext, player, context.Amount, context.Card);
        return true;
    }
}

/// <summary>ns_add_* → 生成指定种类的 Token 到指定牌堆。</summary>
internal sealed class AddTokenHandler : IComponentRuntimeHandler
{
    private readonly NewsanguoTokenKind _kind;
    private readonly PileType _destination;

    internal AddTokenHandler(NewsanguoTokenKind kind, PileType destination)
    {
        _kind = kind;
        _destination = destination;
    }

    public async Task<bool> ExecuteAsync(ComponentRuntimeContext context)
    {
        if (context.Card?.Owner is not { } player) return false;

        await NewsanguoPublicApi.AddTokenToHand(player, _kind, context.Amount, _destination);
        return true;
    }
}

/// <summary>ns_transform_* → 让玩家选择任意张手牌变化为指定 Token。</summary>
internal sealed class TransformToTokenHandler : IComponentRuntimeHandler
{
    private readonly NewsanguoTokenKind _kind;

    internal TransformToTokenHandler(NewsanguoTokenKind kind) => _kind = kind;

    public async Task<bool> ExecuteAsync(ComponentRuntimeContext context)
    {
        if (context.Card?.Owner is not { } player) return false;

        await NewsanguoPublicApi.TransformSelectedHandCardsToToken(
            context.ChoiceContext, player, _kind);
        return true;
    }
}

