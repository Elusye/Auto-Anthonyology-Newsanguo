// ============================================================================================
// SelfCatalog.Extra —— A 档新增原子与配套壳（由 tools/gen_extra_atoms.py 生成，请勿手改）
//
// 每个原子克隆一个同结构的官方原子（保留官方 Flags/Scope/Explicit 与文案编译规则），
// 只覆写数值槽；每张壳的 Cost/Type/Target/Rarity 取自该基线的官方配方，不凭空编造。
// 条件类按官方 GoForTheEyes 惯例：条件原子 owner=-1，被门控效果 owner=条件原子下标。
// ============================================================================================
using ChaosCardGenerator;

namespace Newsanguo.AutoAnthony;

internal static partial class SelfCatalog
{
    private static readonly AtomDef[] ExtraAtomDefs =
    [
        new("newsanguo/ops/dmg6x2", "ironclad/fightme/0", "造成6点伤害2次。", "Deal 6 damage 2 times.", Values: [("damage", 6), ("hits", 2)]),
        new("newsanguo/ops/dmg11x3", "regent/celestialmight/0", "造成11点伤害3次。", "Deal 11 damage 3 times.", Values: [("damage", 11), ("hits", 3)]),
        new("newsanguo/ops/temp_dex2", "silent/anticipate/0", "在本回合内获得2点敏捷。", "Gain 2 Dexterity this turn.", Values: [("amount", 2)]),
        new("newsanguo/ops/intangible1", "silent/wraithform/0", "获得1层无实体。", "Gain 1 Intangible.", Values: [("amount", 1)]),
        new("newsanguo/ops/weak_all1", "silent/haze/1", "给予所有敌人1层虚弱。", "Apply 1 Weak to ALL enemies.", Values: [("amount", 1)]),
        new("newsanguo/ops/target_str_loss2", "necrobinder/sharedfate/1", "使目标失去2点力量。", "Target loses 2 Strength.", Values: [("amount", 2)]),
        new("newsanguo/ops/discard_all", "silent/shadowstep/0", "丢弃所有手牌。", "Discard your hand.", Values: []),
        new("newsanguo/ops/draw_to_full", "colorless/scrawl/0", "抽牌直到抽满手牌。", "Draw until your hand is full.", Values: []),
        new("newsanguo/ops/cond_cards_below3", "defect/ftl/1", "如果你在这回合打出的牌数小于3张，", "If you have played fewer than 3 cards this turn,", Values: [("threshold", 3)]),
        new("newsanguo/ops/cond_fatal", "colorless/handofgreed/1", "斩杀时，", "If this kills the enemy,", Values: []),
        new("newsanguo/ops/cond_intends_attack", "defect/gofortheeyes/1", "如果敌人的意图是攻击，", "If the enemy intends to attack,", Values: []),
        new("newsanguo/ops/heaven_on_turn_start", "silent/infiniteblades/0", "在你的回合开始时，", "At the start of your turn,", Values: []),
        new("newsanguo/ops/heaven_on_exhaust", "ironclad/darkembrace/0", "每当一张牌被消耗时，", "Whenever a card is Exhausted,", Values: []),
        new("newsanguo/ops/trigger_turn_start", "silent/infiniteblades/0", "在你的回合开始时。", "At the start of your turn,", Values: []),
        new("newsanguo/ops/trigger_turn_end", "ironclad/stampede/0", "在你的回合结束时。", "At the end of your turn,", Values: []),
        new("newsanguo/ops/trigger_card_played", "silent/afterimage/0", "每当你打出一张牌时。", "Whenever you play a card,", Values: []),
        new("newsanguo/ops/trigger_card_exhausted", "ironclad/darkembrace/0", "每当有一张牌被消耗时。", "Whenever a card is Exhausted,", Values: []),
        new("newsanguo/ops/rebound", "ironclad/anger/1", "将你在本回合打出的下一张牌放置到你的抽牌堆顶部。", "Put the next card you play this turn on top of your draw pile.", CustomOpcode: "ns_rebound", CustomVariant: "", Values: []),
        new("newsanguo/ops/hand_to_draw_top", "ironclad/anger/1", "将手牌中的一张牌放到抽牌堆的顶部，并且在其被打出之前其耗能变为0。", "Put a card from your hand on top of your draw pile; its cost becomes 0 until played.", CustomOpcode: "ns_hand_to_draw_top", CustomVariant: "", Values: [("count", 0)]),
        new("newsanguo/ops/energy_if_last_skill", "ironclad/bloodletting/1", "若你打出的上一张牌是技能牌，获得2点能量。", "If the last card you played was a Skill, gain 2 Energy.", CustomOpcode: "ns_energy_if_last_skill", CustomVariant: "", Values: [("energy", 2)]),
        new("newsanguo/ops/draw_if_hand_eq2", "colorless/finesse/1", "若你打出这张牌后恰好剩余两张手牌，则再抽2张牌。", "If you have exactly 2 cards in hand after playing this, draw 2 cards.", CustomOpcode: "ns_draw_if_hand_eq", CustomVariant: "2", Values: [("draw", 2)]),
        new("newsanguo/ops/stun_all_if_wine3", "ironclad/anger/1", "若你的酒力不小于三点：击晕所有敌人。", "If you have 3 or more Drunken Might, stun all enemies.", CustomOpcode: "ns_stun_all_if_wine", CustomVariant: "3", Values: []),
        new("newsanguo/ops/heaven_if_le2", "colorless/finesse/1", "若你的天意之力不大于二点，获得5点天意之力。", "If you have 2 or fewer Heavens Force, gain 5 Heavens Force.", CustomOpcode: "ns_heaven_if_le", CustomVariant: "2", Values: [("draw", 5)]),
        new("newsanguo/ops/exhume", "ironclad/anger/1", "从消耗牌堆中选择任意张牌放入手牌。", "Put any number of cards from your exhaust pile into your hand.", CustomOpcode: "ns_exhume", CustomVariant: "", Values: []),
        new("newsanguo/ops/end_turn", "regent/voidform/0", "结束你的回合。", "End your turn.", Values: []),
        new("newsanguo/ops/add_cudgel", "colorless/finesse/1", "将1张军杖加入你的手牌。", "Add 1 Military Cudgel to your hand.", CustomOpcode: "ns_add_cudgel", CustomVariant: "", Values: [("draw", 1)]),
        new("newsanguo/ops/add_soldier", "colorless/finesse/1", "将1张士兵加入你的手牌。", "Add 1 Soldier to your hand.", CustomOpcode: "ns_add_soldier", CustomVariant: "", Values: [("draw", 1)]),
        new("newsanguo/ops/copy_to_hand", "ironclad/anger/1", "增加一张此牌的复制品到你的手牌。", "Add a copy of this card to your hand.", CustomOpcode: "ns_copy_to_hand", CustomVariant: "", Values: []),
        new("newsanguo/ops/flight", "colorless/finesse/1", "获得3层飞行。", "Gain 3 Flight.", CustomOpcode: "ns_flight", CustomVariant: "", Values: [("draw", 3)]),
        new("newsanguo/ops/traitor_tyranny", "ironclad/anger/1", "目标的易伤不会减少。", "Target's Vulnerable does not decay.", CustomOpcode: "ns_traitor_tyranny", CustomVariant: "", Values: []),
        new("newsanguo/ops/add_lightweight", "colorless/finesse/1", "将1张不胜酒力加入你的弃牌堆。", "Add 1 Lightweight to your discard pile.", CustomOpcode: "ns_add_lightweight", CustomVariant: "", Values: [("draw", 1)]),
        new("newsanguo/ops/stun", "ironclad/anger/1", "击晕该敌人。", "Stun the enemy.", CustomOpcode: "ns_stun", CustomVariant: "", Values: []),
        new("newsanguo/ops/exhaust_na_heavens", "colorless/finesse/1", "消耗手牌中的所有非攻击牌，每张获得2点天意之力。", "Exhaust all non-Attack cards in your hand. Gain 2 Heavens Force for each.", CustomOpcode: "ns_exhaust_na_heavens", CustomVariant: "", Values: [("draw", 2)]),
        new("newsanguo/ops/transform_soldier", "ironclad/anger/1", "将你手牌中的任意张变化为士兵。", "Transform any number of cards in your hand into Soldiers.", CustomOpcode: "ns_transform_soldier", CustomVariant: "", Values: []),
        new("newsanguo/ops/exhaust_non_attack", "ironclad/secondwind/0", "消耗手牌中的所有非攻击牌。", "Exhaust all non-Attack cards in your hand.", Values: []),
        new("newsanguo/ops/retain_hand", "colorless/equilibrium/1", "在本回合保留你的手牌。", "Retain your hand this turn.", Values: []),
        new("newsanguo/ops/lose_heaven2", "ironclad/bloodwall/0", "失去2点天意之力。", "Lose 2 Heavens Force.", CustomOpcode: "ns_lose_heaven", CustomVariant: "", Values: [("hp_loss", 2)]),
        new("newsanguo/ops/frail_self1", "necrobinder/friendship/2", "给予自身1层脆弱。", "Gain 1 Frail.", CustomOpcode: "ns_frail_self", CustomVariant: "", Values: [("amount", 1)]),
        new("newsanguo/ops/frail_target1", "ironclad/mangle/1", "给予目标1层脆弱。", "Apply 1 Frail.", CustomOpcode: "ns_frail", CustomVariant: "", Values: [("amount", 1)]),
        new("newsanguo/ops/entangled", "ironclad/anger/1", "本回合你不能打出攻击牌。", "You cannot play Attacks this turn.", CustomOpcode: "ns_entangled", CustomVariant: "", Values: []),
        new("newsanguo/ops/scry7", "colorless/finesse/1", "预见7。", "Scry 7.", CustomOpcode: "ns_scry", CustomVariant: "", Values: [("draw", 7)]),
    ];

    private static readonly RecipeDef[] ExtraRecipeDefs =
    [
        new("NSDmg6x2", "连环", "Double", 2, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Uncommon, [-1], ["newsanguo/ops/dmg6x2"]),
        new("NSDmg11x3", "三叠", "Triple", 3, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Rare, [-1], ["newsanguo/ops/dmg11x3"]),
        new("NSTempDex2", "身法", "Footwork", 0, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Common, [-1], ["newsanguo/ops/temp_dex2"]),
        new("NSIntangible1", "无形", "Formless", 3, GeneratedCardType.Power, TargetMode.Other, GeneratedRarity.Rare, [-1], ["newsanguo/ops/intangible1"]),
        new("NSWeakAll1", "威压", "Pressure", 2, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Uncommon, [-1], ["newsanguo/ops/weak_all1"]),
        new("NSTargetStrLoss2", "夺势", "Unnerve", 0, GeneratedCardType.Skill, TargetMode.SingleEnemy, GeneratedRarity.Rare, [-1], ["newsanguo/ops/target_str_loss2"]),
        new("NSDiscardAll", "弃局", "Fold", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Rare, [-1], ["newsanguo/ops/discard_all"]),
        new("NSDrawToFull", "补牌", "Refill", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Rare, [-1], ["newsanguo/ops/draw_to_full"]),
        new("NSCondCards3", "蓄势", "Wind-up", 0, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Uncommon, [-1, -1, 1], ["newsanguo/ops/cond_cards_below3", "newsanguo/ops/slash", "newsanguo/ops/slash"]),
        new("NSCondFatal", "斩决", "Execute", 2, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Rare, [-1, -1, 1], ["newsanguo/ops/cond_fatal", "newsanguo/ops/slash", "newsanguo/ops/slash"]),
        new("NSCondIntendsAtk", "料敌", "Read", 0, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Common, [-1, -1, 1], ["newsanguo/ops/cond_intends_attack", "newsanguo/ops/slash", "newsanguo/ops/slash"]),
        new("NSHeavenOnTurnStart", "聚气", "Gather", 2, GeneratedCardType.Power, TargetMode.Other, GeneratedRarity.Rare, [-1, 0], ["newsanguo/ops/heaven_on_turn_start", "newsanguo/ops/heaven"]),
        new("NSHeavenOnExhaust", "焚天", "BurntOffer", 2, GeneratedCardType.Power, TargetMode.Other, GeneratedRarity.Rare, [-1, 0], ["newsanguo/ops/heaven_on_exhaust", "newsanguo/ops/heaven"]),
        new("NSTrigTurnStart", "固守", "Bulwark", 2, GeneratedCardType.Power, TargetMode.Other, GeneratedRarity.Rare, [-1, 0], ["newsanguo/ops/trigger_turn_start", "newsanguo/ops/guard"]),
        new("NSTrigTurnEnd", "收势", "WindDown", 1, GeneratedCardType.Power, TargetMode.Other, GeneratedRarity.Uncommon, [-1, 0], ["newsanguo/ops/trigger_turn_end", "newsanguo/ops/guard"]),
        new("NSTrigCardPlayed", "感应", "Attunement", 2, GeneratedCardType.Power, TargetMode.Other, GeneratedRarity.Rare, [-1, 0], ["newsanguo/ops/trigger_card_played", "newsanguo/ops/heaven"]),
        new("NSTrigCardExhausted", "余烬", "Embers", 2, GeneratedCardType.Power, TargetMode.Other, GeneratedRarity.Rare, [-1, 0], ["newsanguo/ops/trigger_card_exhausted", "newsanguo/ops/guard"]),
        new("NSRebound", "回旋", "Rebound", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Uncommon, [-1, -1], ["newsanguo/ops/rebound", "newsanguo/ops/guard"]),
        new("NSHandToDrawTop", "倒卷", "Rewind", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Uncommon, [-1, -1], ["newsanguo/ops/hand_to_draw_top", "newsanguo/ops/guard"]),
        new("NSEnergyIfLastSkill", "乘势", "Momentum", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Uncommon, [-1, -1], ["newsanguo/ops/energy_if_last_skill", "newsanguo/ops/wine"]),
        new("NSDrawIfHandEq2", "机变", "Contingency", 1, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Uncommon, [-1, -1], ["newsanguo/ops/draw_if_hand_eq2", "newsanguo/ops/slash"]),
        new("NSStunAllIfWine3", "醉打", "DrunkenSweep", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Rare, [-1, -1], ["newsanguo/ops/stun_all_if_wine3", "newsanguo/ops/guard"]),
        new("NSHeavenIfLe2", "否极", "Turning", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Uncommon, [-1, -1], ["newsanguo/ops/heaven_if_le2", "newsanguo/ops/guard"]),
        new("NSExhume", "招魂", "Reanimate", 2, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Rare, [-1, -1], ["newsanguo/ops/exhume", "newsanguo/ops/guard"]),
        new("NSEndTurn", "收兵", "StandDown", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Uncommon, [-1, -1], ["newsanguo/ops/end_turn", "newsanguo/ops/sweep_all"]),
        new("NSAddCudgel", "授杖", "GrantCudgel", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Uncommon, [-1, -1], ["newsanguo/ops/add_cudgel", "newsanguo/ops/guard"]),
        new("NSAddSoldier", "征兵", "Conscript", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Uncommon, [-1, -1], ["newsanguo/ops/add_soldier", "newsanguo/ops/guard"]),
        new("NSCopyToHand", "影从", "ShadowFollow", 1, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Uncommon, [-1, -1], ["newsanguo/ops/copy_to_hand", "newsanguo/ops/slash"]),
        new("NSFlight", "振翅", "TakeWing", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Uncommon, [-1, -1], ["newsanguo/ops/flight", "newsanguo/ops/guard"]),
        new("NSTraitorTyranny", "国贼", "Traitor", 1, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Rare, [-1, -1], ["newsanguo/ops/traitor_tyranny", "newsanguo/ops/slash"]),
        new("NSAddLightweight", "贪杯", "Overindulge", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Uncommon, [-1, -1], ["newsanguo/ops/add_lightweight", "newsanguo/ops/draw"]),
        new("NSStun", "震慑", "Daze", 1, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Uncommon, [-1, -1], ["newsanguo/ops/stun", "newsanguo/ops/slash"]),
        new("NSExhaustNaHeavens", "焚稿", "BurnTheDrafts", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Rare, [-1, -1], ["newsanguo/ops/exhaust_na_heavens", "newsanguo/ops/guard"]),
        new("NSTransformSoldier", "炼兵", "ForgeSoldiers", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Uncommon, [-1, -1], ["newsanguo/ops/transform_soldier", "newsanguo/ops/guard"]),
        new("NSExhaustNonAttack", "刮骨", "Scour", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Uncommon, [-1, -1], ["newsanguo/ops/exhaust_non_attack", "newsanguo/ops/guard"]),
        new("NSRetainHand", "按兵", "HoldFast", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Uncommon, [-1, -1], ["newsanguo/ops/retain_hand", "newsanguo/ops/draw"]),
        new("NSLoseHeaven2", "舍天", "Forsake", 0, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Common, [-1, -1], ["newsanguo/ops/lose_heaven2", "newsanguo/ops/draw"]),
        new("NSFrailSelf1", "力竭", "Spent", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Common, [-1, -1], ["newsanguo/ops/frail_self1", "newsanguo/ops/guard"]),
        new("NSFrailTarget1", "挫锐", "Blunt", 1, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Common, [-1, -1], ["newsanguo/ops/frail_target1", "newsanguo/ops/slash"]),
        new("NSEntangled", "自缚", "Bind", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Uncommon, [-1, -1], ["newsanguo/ops/entangled", "newsanguo/ops/guard"]),
        new("NSScry7", "观星", "Stargaze", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Rare, [-1], ["newsanguo/ops/scry7"]),
    ];
}
