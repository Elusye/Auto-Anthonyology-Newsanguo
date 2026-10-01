// ============================================================================================
// SelfCatalog.Derived —— 从 newsanguo 卡池**反推**出来的壳（由 tools/derive_catalog.py 生成，请勿手改）
//
// 数据来源：卡牌源码（费用/类型/目标/稀有度）+ localization/{zhs,eng}/cards.json（卡名）
//          + 匹配到既有原子的效果序列。原子全部复用，本文件不新增原子。
//
// ⚠ 数值不随卡面固定：AA 的组件身份只认结构，具体数字由数值策略围绕中心值采样生成，
//    因此这里保留的是「形状」（费用/类型/目标/稀有度/效果序列），不是原卡的确切数字。
// ============================================================================================
using ChaosCardGenerator;

namespace Newsanguo.AutoAnthony;

internal static partial class SelfCatalog
{
    private static readonly RecipeDef[] DerivedRecipeDefs =
    [
        new("NSDAGrandToast", "当浮一大白", "A Grand Toast", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Basic, [-1], ["newsanguo/ops/wine"]),
        new("NSDBladeOfVirtue", "仁之剑，义之剑", "Blade of Virtue", 1, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Basic, [-1, -1, -1, -1], ["newsanguo/ops/slash", "newsanguo/ops/weak_1", "newsanguo/ops/slash", "newsanguo/ops/vuln_2"]),
        new("NSDBonelessPalm", "化骨绵掌", "Boneless Palm", 1, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Uncommon, [-1, -1], ["newsanguo/ops/slash", "newsanguo/ops/target_str_loss2"]),
        new("NSDCommanderArrives", "大都督到！", "Commander Arrives", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Common, [-1, -1], ["newsanguo/ops/guard", "newsanguo/ops/add_cudgel"]),
        new("NSDCrossForCross", "他过江我也过江！", "Cross for Cross", 1, GeneratedCardType.Skill, TargetMode.SingleEnemy, GeneratedRarity.Uncommon, [-1, 0], ["newsanguo/ops/cond_intends_attack", "newsanguo/ops/energy"]),
        new("NSDDivination", "占卜", "Divination", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Common, [-1, -1], ["newsanguo/ops/heaven", "newsanguo/ops/draw"]),
        new("NSDDivineInsight", "参悟天意", "Divine Insight", 0, GeneratedCardType.Power, TargetMode.Other, GeneratedRarity.Ancient, [-1, -1, 1], ["newsanguo/ops/lose_heaven2", "newsanguo/ops/trigger_card_played", "newsanguo/ops/heaven"]),
        new("NSDDongZhuoTheTraitor", "国贼董卓嘛！", "Dong Zhuo, the Traitor!", 1, GeneratedCardType.Skill, TargetMode.SingleEnemy, GeneratedRarity.Uncommon, [-1, -1], ["newsanguo/ops/vuln_2", "newsanguo/ops/traitor_tyranny"]),
        new("NSDGetOut", "叉出去！", "Get Out!", 1, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Common, [-1, -1], ["newsanguo/ops/slash", "newsanguo/ops/exhaust_choose"]),
        new("NSDGreatEvil", "做下大恶", "Great Evil", 1, GeneratedCardType.Attack, TargetMode.Other, GeneratedRarity.Common, [-1, -1], ["newsanguo/ops/sweep_all", "newsanguo/ops/heaven_if_le2"]),
        new("NSDHeavenAndEarth", "天上人间", "Heaven and Earth", 2, GeneratedCardType.Power, TargetMode.Other, GeneratedRarity.Uncommon, [-1, -1], ["newsanguo/ops/lose_heaven2", "newsanguo/ops/flight"]),
        new("NSDHumanTransmutationSpell", "人体炼成术", "Human Transmutation Spell", 2, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Rare, [-1, -1], ["newsanguo/ops/lose_heaven2", "newsanguo/ops/transform_soldier"]),
        new("NSDImGettingDrunk", "真的是要醉啦！", "I'm Getting Drunk", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Uncommon, [-1, -1], ["newsanguo/ops/wine", "newsanguo/ops/add_lightweight"]),
        new("NSDInvokeHeaven", "召唤天意", "Invoke Heaven", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Uncommon, [-1], ["newsanguo/ops/heaven"]),
        new("NSDLightningStrike", "迅雷打击", "Lightning Strike", 0, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Common, [-1, -1, 1], ["newsanguo/ops/slash", "newsanguo/ops/cond_cards_below3", "newsanguo/ops/heaven"]),
        new("NSDMindControlSpell", "心灵控制术", "Mind Control Spell", 2, GeneratedCardType.Skill, TargetMode.SingleEnemy, GeneratedRarity.Rare, [-1, -1], ["newsanguo/ops/lose_heaven2", "newsanguo/ops/stun"]),
        new("NSDNeverHadThese", "从来就没有这些！", "Never Had These", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Uncommon, [-1, -1], ["newsanguo/ops/guard", "newsanguo/ops/exhaust_na_heavens"]),
        new("NSDNeverHappened", "万万没有此事啊！", "Never Happened", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Common, [-1, -1], ["newsanguo/ops/guard", "newsanguo/ops/rebound"]),
        new("NSDNewGamePlus", "二周目玩家", "New Game Plus", 0, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Rare, [-1, -1, -1, -1], ["newsanguo/ops/wine", "newsanguo/ops/heaven", "newsanguo/ops/scry7", "newsanguo/ops/draw"]),
        new("NSDOnset", "发病", "Onset", 0, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Uncommon, [-1, -1], ["newsanguo/ops/lose_heaven2", "newsanguo/ops/energy"]),
        new("NSDPartyOn", "接着奏乐接着舞", "Party On", 0, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Uncommon, [-1, -1, -1], ["newsanguo/ops/energy", "newsanguo/ops/draw", "newsanguo/ops/entangled"]),
        new("NSDPeekIntoHeaven", "窥探天意", "Peek Into Heaven", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Uncommon, [-1, -1, -1], ["newsanguo/ops/lose_heaven2", "newsanguo/ops/scry7", "newsanguo/ops/draw"]),
        new("NSDPlot", "密谋", "Plot", 0, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Common, [-1, -1], ["newsanguo/ops/slash", "newsanguo/ops/draw_if_hand_eq2"]),
        new("NSDQuadBlast", "马氏四连", "Quad Blast", 2, GeneratedCardType.Attack, TargetMode.Other, GeneratedRarity.Uncommon, [-1], ["newsanguo/ops/strike_wave"]),
        new("NSDReanimationSpell", "亡灵复活术", "Reanimation Spell", 2, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Rare, [-1, -1], ["newsanguo/ops/lose_heaven2", "newsanguo/ops/exhume"]),
        new("NSDRetire", "告老还乡", "Retire", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Uncommon, [-1, -1], ["newsanguo/ops/intangible1", "newsanguo/ops/end_turn"]),
        new("NSDSlamTheBowl", "盖饭", "Slam the Bowl", 0, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Common, [-1, -1, -1], ["newsanguo/ops/discard_all", "newsanguo/ops/slash", "newsanguo/ops/copy_to_discard"]),
        new("NSDTenThousandTransparentHoles", "一万个透明窟窿！", "Ten Thousand Transparent Holes!", 2, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Rare, [-1], ["newsanguo/ops/dmg6x2"]),
        new("NSDUnstoppable", "水火无敌", "Unstoppable", 1, GeneratedCardType.Skill, TargetMode.Other, GeneratedRarity.Rare, [-1, -1], ["newsanguo/ops/lose_heaven2", "newsanguo/ops/intangible1"]),
    ];
}
