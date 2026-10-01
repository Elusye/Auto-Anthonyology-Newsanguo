#!/usr/bin/env python3
"""生成 src/SelfCatalog.Extra.cs —— A 档新增原子 + 对应壳。

设计要点：
  · 每个原子都克隆一个**同结构**的官方原子（保留官方 Flags / Scope / Explicit，保证估值与
    文案编译规则都成立），只覆写数值槽（BaseValue）与自定义 opcode。
  · 每个原子都必须被某张壳引用，否则 SelfCatalog.Build() 会抛错；因此脚本为每个原子生成一张壳，
    壳的 Cost/Type/Target/Rarity **取自该基线官方配方**（不凭空编造）。
  · 条件类效果按官方 GoForTheEyes 的惯例配对：条件原子 owner=-1，被它门控的效果 owner=条件原子下标。

用法：python tools/gen_extra_atoms.py <内嵌目录>
"""
import json
import os
import sys

# (我们的原子后缀, 中文文案, 英文文案, 基线官方原子, 数值覆写, 壳Id, 壳中文名, 壳英文名, 稀有度覆写)
# 注意：**只保留结构真正不同的原子**。
# AA 的组件身份由 RuntimeSpec 结构决定（数值只是槽位，由 IComponentValuePolicy 围绕"中心值"采样生成），
# 所以「造成3/5/7/8/10/14/15/16点伤害」这类只改数字的原子会被合并成与既有 slash(6) 同一个组件 ——
# 实测 31 个原子只新增了 12 个组件，且合并后哪份文案/中心值存活是不确定的。
# 因此这里删掉纯数值变体，只留新结构。
TABLE = [
    # ---- 伤害：单体多段（既有 slash 是单体单段、flurry 是随机多段）----
    ("dmg6x2",  "造成6点伤害2次。", "Deal 6 damage 2 times.",   "ironclad/fightme/0",       [("damage", 6), ("hits", 2)],  "NSDmg6x2",  "连环", "Double", None),
    ("dmg11x3", "造成11点伤害3次。", "Deal 11 damage 3 times.", "regent/celestialmight/0", [("damage", 11), ("hits", 3)], "NSDmg11x3", "三叠", "Triple", None),
    # ---- 我方状态 ----
    ("temp_dex2",   "在本回合内获得2点敏捷。", "Gain 2 Dexterity this turn.", "silent/anticipate/0", [("amount", 2)], "NSTempDex2",   "身法", "Footwork", None),
    ("intangible1", "获得1层无实体。",         "Gain 1 Intangible.",          "silent/wraithform/0", [("amount", 1)], "NSIntangible1", "无形", "Formless", "Rare"),
    # ---- 敌方状态（既有无 weak 全体、无"目标失去力量"）----
    ("weak_all1",        "给予所有敌人1层虚弱。", "Apply 1 Weak to ALL enemies.", "silent/haze/1",           [("amount", 1)], "NSWeakAll1",       "威压", "Pressure", None),
    ("target_str_loss2", "使目标失去2点力量。",   "Target loses 2 Strength.",    "necrobinder/sharedfate/1", [("amount", 2)], "NSTargetStrLoss2", "夺势", "Unnerve", None),
    # ---- 牌库操控 / 其它（既有都没有）----
    ("discard_all",  "丢弃所有手牌。",       "Discard your hand.",            "silent/shadowstep/0", None, "NSDiscardAll",  "弃局", "Fold", None),
    ("draw_to_full", "抽牌直到抽满手牌。",   "Draw until your hand is full.", "colorless/scrawl/0",  None, "NSDrawToFull",  "补牌", "Refill", None),
]

# 条件类：按官方 GoForTheEyes 惯例 —— 先无条件收益，再条件原子(owner=-1)，被门控收益 owner=条件下标。
# B 档：词汇表里没有、必须走自定义 opcode 的效果。handler 见 src/RuntimeRoutes.cs。
# 基线选取原则：除了给结构（Target/Scope/数值槽），基线的 Template/Flags 还决定**估值语义**，
# 所以负面效果挑负面基线（strength_loss / hp_loss），纯限制类挑无数值基线（voidform 无 Flags）。
# 每个原子都配了收益原子，组成一张真正能进池的牌。
B_TABLE = [
    # ⚠ **触发种类只保留 newsanguo 自己用过的那些。**
    # AA 的官方词汇表有 35 种触发种类，其中大量是原版铁甲战士/静默猎手的机制
    # （"每当你获得格挡时""每当你施加易伤时""每当你打出技能牌时"…）。
    # **照抄它们会稀释本 mod 的身份** —— 随机池里冒出 newsanguo 根本没有的触发器，
    # 玩家会以为在玩原版角色。
    # 判定依据是 newsanguo 卡面原文**真实出现过**的触发句式，不是印象：
    #   在你的回合开始时(5) / 你每打出一张牌(3) / 在你的回合结束时(2) / 每消耗一张牌(1)
    # 他们还有"生物死亡 / 失去酒力 / 进入额外回合 / 每 N 回合"，但 AA 里这些
    # 只有 Modifier 形态（不可门控）或没有对应原子，所以做不了。
    # ---- 触发类引擎牌（第二批）：把触发种类与 newsanguo 资源（天意之力）配对 ----
    # 12 个触发原子原本各自只配了一种收益（格挡/伤害），这里补上"资源引擎"形态，
    # 同时加强 Power 的（稀有度 x 类型）分布 —— 实测过 Rare 只有 1 张能力牌的塌陷。
    # 注意：这些行会生成与已有触发原子**同 spec 的新原子**，AA 会按结构合并它们，
    # 因此"声明原子数"会涨而"实际组件数"不涨 —— 这是预期的，不是 bug。
    dict(suffix="heaven_on_turn_start", zh="在你的回合开始时，", en="At the start of your turn,",
         base="silent/infiniteblades/0", vals=[], op=None,
         shell="NSHeavenOnTurnStart", shell_zh="聚气", shell_en="Gather", cost=2, rarity="Rare",
         target="Other", payoff=["heaven"], owners=[-1, 0], type="Power"),
    dict(suffix="heaven_on_exhaust", zh="每当一张牌被消耗时，", en="Whenever a card is Exhausted,",
         base="ironclad/darkembrace/0", vals=[], op=None,
         shell="NSHeavenOnExhaust", shell_zh="焚天", shell_en="BurntOffer", cost=2, rarity="Rare",
         target="Other", payoff=["heaven"], owners=[-1, 0], type="Power"),
    # ---- 触发类：克隆官方 AbilityTrigger（触发种类编码在 spec 的 Trigger.Kind 里）----
    # 文案**直接用官方原文**，这样措辞与游戏一致、反推工具能自动匹配。
    # owners=[-1, 0]：触发器 owner=-1、被门控效果 owner=0（照抄 ironclad/juggling）。
    # 已实机验证这条路走得通（「感应」的天意之力会随每次出牌增长）。
    # 可用种类共 35 个，这里只放最常用的；其余按需克隆。
    dict(suffix="trigger_turn_start", zh="在你的回合开始时。", en="At the start of your turn,",
         base="silent/infiniteblades/0", vals=[], op=None,
         shell="NSTrigTurnStart", shell_zh="固守", shell_en="Bulwark", cost=2, rarity="Rare",
         target="Other", payoff=["guard"], owners=[-1, 0], type="Power"),
    dict(suffix="trigger_turn_end", zh="在你的回合结束时。", en="At the end of your turn,",
         base="ironclad/stampede/0", vals=[], op=None,
         shell="NSTrigTurnEnd", shell_zh="收势", shell_en="WindDown", cost=1, rarity="Uncommon",
         target="Other", payoff=["guard"], owners=[-1, 0], type="Power"),
    dict(suffix="trigger_card_played", zh="每当你打出一张牌时。", en="Whenever you play a card,",
         base="silent/afterimage/0", vals=[], op=None,
         shell="NSTrigCardPlayed", shell_zh="感应", shell_en="Attunement", cost=2, rarity="Rare",
         target="Other", payoff=["heaven"], owners=[-1, 0], type="Power"),
    dict(suffix="trigger_card_exhausted", zh="每当有一张牌被消耗时。", en="Whenever a card is Exhausted,",
         base="ironclad/darkembrace/0", vals=[], op=None,
         shell="NSTrigCardExhausted", shell_zh="余烬", shell_en="Embers", cost=2, rarity="Rare",
         target="Other", payoff=["guard"], owners=[-1, 0], type="Power"),
    # ---- 牌堆取放 ----
    # Rebound：直接用**原版** ReboundPower，不需要自己跟踪"下一张打出的牌"。
    dict(suffix="rebound", zh="将你在本回合打出的下一张牌放置到你的抽牌堆顶部。",
         en="Put the next card you play this turn on top of your draw pile.",
         base="ironclad/anger/1", vals=[], op="ns_rebound",
         shell="NSRebound", shell_zh="回旋", shell_en="Rebound", cost=1, rarity="Uncommon",
         target="Other", payoff=["guard"], type="Skill"),
    # 手牌一张放抽牌堆顶 + 该牌耗能变 0（照抄「开玩笑」JustKidding，可放弃选择）。
    dict(suffix="hand_to_draw_top", zh="将手牌中的一张牌放到抽牌堆的顶部，并且在其被打出之前其耗能变为0。",
         en="Put a card from your hand on top of your draw pile; its cost becomes 0 until played.",
         base="ironclad/anger/1", vals=[("count", 0)], op="ns_hand_to_draw_top",
         shell="NSHandToDrawTop", shell_zh="倒卷", shell_en="Rewind", cost=1, rarity="Uncommon",
         target="Other", payoff=["guard"], type="Skill"),
    # 条件用引擎的 CardPlaysFinished 历史读（照抄「陶醉」）；无阈值，variant 留空。
    dict(suffix="energy_if_last_skill", zh="若你打出的上一张牌是技能牌，获得2点能量。",
         en="If the last card you played was a Skill, gain 2 Energy.",
         base="ironclad/bloodletting/1", vals=[("energy", 2)], op="ns_energy_if_last_skill",
         shell="NSEnergyIfLastSkill", shell_zh="乘势", shell_en="Momentum", cost=1, rarity="Uncommon",
         target="Other", payoff=["wine"], type="Skill"),
    # 条件与效果都在引擎公开面上（手牌数 + 抽牌），阈值 2 走 variant。
    dict(suffix="draw_if_hand_eq2", zh="若你打出这张牌后恰好剩余两张手牌，则再抽2张牌。",
         en="If you have exactly 2 cards in hand after playing this, draw 2 cards.",
         base="colorless/finesse/1", vals=[("draw", 2)], op="ns_draw_if_hand_eq", variant="2",
         shell="NSDrawIfHandEq2", shell_zh="机变", shell_en="Contingency", cost=1, rarity="Uncommon",
         target="SingleEnemy", payoff=["slash"], type="Attack"),
    # ---- 自带判断的效果 opcode（模式已验证：阈值走 variant、条件不成立返回 true）----
    dict(suffix="stun_all_if_wine3", zh="若你的酒力不小于三点：击晕所有敌人。",
         en="If you have 3 or more Drunken Might, stun all enemies.",
         base="ironclad/anger/1", vals=[], op="ns_stun_all_if_wine", variant="3",
         shell="NSStunAllIfWine3", shell_zh="醉打", shell_en="DrunkenSweep", cost=1, rarity="Rare",
         target="Other", payoff=["guard"], type="Skill"),
    # ---- 自带判断的效果 opcode（AA 的 condition 扩不了，就把判断放进 handler）----
    # **阈值走 variant、授予量走数值槽**：handler 只能拿到单一 Amount，两个数字必须分开走。
    # 阈值进 variant 还有个好处：不同阈值天然是不同组件，符合 AA 的身份模型。
    dict(suffix="heaven_if_le2", zh="若你的天意之力不大于二点，获得5点天意之力。",
         en="If you have 2 or fewer Heavens Force, gain 5 Heavens Force.",
         base="colorless/finesse/1", vals=[("draw", 5)], op="ns_heaven_if_le", variant="2",
         shell="NSHeavenIfLe2", shell_zh="否极", shell_en="Turning", cost=1, rarity="Uncommon",
         target="Other", payoff=["guard"], type="Skill"),
    # 挖坟：引擎侧能力，不需要 newsanguo 出入口（handler 直接调 CardPileCmd / CardSelectCmd）。
    # 无数值槽 → 文案不能带数字，基线挑 ironclad/anger/1（1 个 hidden 槽）。
    dict(suffix="exhume", zh="从消耗牌堆中选择任意张牌放入手牌。",
         en="Put any number of cards from your exhaust pile into your hand.",
         base="ironclad/anger/1", vals=[], op="ns_exhume",
         shell="NSExhume", shell_zh="招魂", shell_en="Reanimate", cost=2, rarity="Rare",
         target="Other", payoff=["guard"], type="Skill"),
    # `end_turn` 从普通表移到这里：原来它的壳**只含这一个原子**，等于一张
    # "唯一效果就是结束自己回合"的牌（纯负面，在随机池里毫无配合）—— 实测被当成 bug 上报。
    # 现在配一个大收益，语义才立得住：倾尽全力、然后交出回合。
    # 注意 `end_turn` 这个**原子**不能删：反推出来的 NSDRetire（告老还乡 = 无实体 + 结束回合）
    # 还在引用它，而目录要求每个原子至少被一张壳引用、且每个被引用的原子都必须有定义。
    dict(suffix="end_turn", zh="结束你的回合。", en="End your turn.",
         base="regent/voidform/0", vals=[], op=None,
         shell="NSEndTurn", shell_zh="收兵", shell_en="StandDown", cost=1, rarity="Uncommon",
         target="Other", payoff=["sweep_all"], type="Skill"),
    # ---- newsanguo 0.2.39 新公开入口解锁的 ----
    # 注意：Token 种类没有参数位，所以一个种类一个 opcode（handler 构造时绑定 NewsanguoTokenKind）。
    dict(suffix="add_cudgel", zh="将1张军杖加入你的手牌。", en="Add 1 Military Cudgel to your hand.",
         base="colorless/finesse/1", vals=[("draw", 1)], op="ns_add_cudgel",
         shell="NSAddCudgel", shell_zh="授杖", shell_en="GrantCudgel", cost=1, rarity="Uncommon",
         target="Other", payoff=["guard"], type="Skill"),
    dict(suffix="add_soldier", zh="将1张士兵加入你的手牌。", en="Add 1 Soldier to your hand.",
         base="colorless/finesse/1", vals=[("draw", 1)], op="ns_add_soldier",
         shell="NSAddSoldier", shell_zh="征兵", shell_en="Conscript", cost=1, rarity="Uncommon",
         target="Other", payoff=["guard"], type="Skill"),
    # 基线用 ironclad/anger/1（create_copy / self_card）：目标语义干净（就是"此牌"）。
    # 它原本的去向是弃牌堆，我们覆写 opcode 后由 handler 决定进手牌 —— 组件的**身份**靠 opcode 区分，
    # 因此与既有的 copy_to_discard 不会合并。
    dict(suffix="copy_to_hand", zh="增加一张此牌的复制品到你的手牌。",
         en="Add a copy of this card to your hand.",
         base="ironclad/anger/1", vals=[], op="ns_copy_to_hand",
         shell="NSCopyToHand", shell_zh="影从", shell_en="ShadowFollow", cost=1, rarity="Uncommon",
         target="SingleEnemy", payoff=["slash"], type="Attack"),
    dict(suffix="flight", zh="获得3层飞行。", en="Gain 3 Flight.",
         base="colorless/finesse/1", vals=[("draw", 3)], op="ns_flight",
         shell="NSFlight", shell_zh="振翅", shell_en="TakeWing", cost=1, rarity="Uncommon",
         target="Other", payoff=["guard"], type="Skill"),
    # 无数值的纯规则类：挑无 Flags 的基线（voidform），避免估值语义被带偏。
    dict(suffix="traitor_tyranny", zh="目标的易伤不会减少。", en="Target's Vulnerable does not decay.",
         base="ironclad/anger/1", vals=[], op="ns_traitor_tyranny",
         shell="NSTraitorTyranny", shell_zh="国贼", shell_en="Traitor", cost=1, rarity="Rare",
         target="SingleEnemy", payoff=["slash"], type="Attack"),
    # ---- 请求 1~5 落地后解锁的 ----
    # 去向是 Discard：newsanguo 原卡「真的是要醉啦！」就是不进手牌、进弃牌堆的。
    dict(suffix="add_lightweight", zh="将1张不胜酒力加入你的弃牌堆。",
         en="Add 1 Lightweight to your discard pile.",
         base="colorless/finesse/1", vals=[("draw", 1)], op="ns_add_lightweight",
         shell="NSAddLightweight", shell_zh="贪杯", shell_en="Overindulge", cost=1, rarity="Uncommon",
         target="Other", payoff=["draw"], type="Skill"),
    # 原版 CreatureCmd.Stun 没有层数概念，所以这条无数值槽（挑无 Flags 的基线）。
    dict(suffix="stun", zh="击晕该敌人。", en="Stun the enemy.",
         base="ironclad/anger/1", vals=[], op="ns_stun",
         shell="NSStun", shell_zh="震慑", shell_en="Daze", cost=1, rarity="Uncommon",
         target="SingleEnemy", payoff=["slash"], type="Attack"),
    # 消耗与"消耗了几张"必须一次调用完成 —— 拆两步跨 mod 观察不到张数。
    dict(suffix="exhaust_na_heavens", zh="消耗手牌中的所有非攻击牌，每张获得2点天意之力。",
         en="Exhaust all non-Attack cards in your hand. Gain 2 Heavens Force for each.",
         base="colorless/finesse/1", vals=[("draw", 2)], op="ns_exhaust_na_heavens",
         shell="NSExhaustNaHeavens", shell_zh="焚稿", shell_en="BurnTheDrafts", cost=1, rarity="Rare",
         target="Other", payoff=["guard"], type="Skill"),
    dict(suffix="transform_soldier", zh="将你手牌中的任意张变化为士兵。",
         en="Transform any number of cards in your hand into Soldiers.",
         base="ironclad/anger/1", vals=[], op="ns_transform_soldier",
         shell="NSTransformSoldier", shell_zh="炼兵", shell_en="ForgeSoldiers", cost=1, rarity="Uncommon",
         target="Other", payoff=["guard"], type="Skill"),
    # ---- 反推待办里"能用现成 opcode 表达"的：不需要 handler，只是缺一个原子 ----
    # ironclad/secondwind/0 == `exhaust_card`/`all` + CardFilter=non_attack + src=hand，
    # 正好是「消耗手牌中的所有非攻击牌」（NeverHadThese 缺的那条）。
    dict(suffix="exhaust_non_attack", zh="消耗手牌中的所有非攻击牌。",
         en="Exhaust all non-Attack cards in your hand.",
         base="ironclad/secondwind/0", vals=[], op=None,
         shell="NSExhaustNonAttack", shell_zh="刮骨", shell_en="Scour", cost=1, rarity="Uncommon",
         target="Other", payoff=["guard"], type="Skill"),
    # colorless/equilibrium/1 就是 `apply_power`/`retain_hand_this_turn`，
    # 对应「在本回合保留你的手牌。」（反推待办里出现过）。AA 的 apply_power 词汇表里没有独立
    # "retain hand" 名字，但变体已存在，所以不需要 handler。
    dict(suffix="retain_hand", zh="在本回合保留你的手牌。", en="Retain your hand this turn.",
         base="colorless/equilibrium/1", vals=[], op=None,
         shell="NSRetainHand", shell_zh="按兵", shell_en="HoldFast", cost=1, rarity="Uncommon",
         target="Other", payoff=["draw"], type="Skill"),
    dict(suffix="lose_heaven2", zh="失去2点天意之力。", en="Lose 2 Heavens Force.",
         base="ironclad/bloodwall/0", vals=[("hp_loss", 2)], op="ns_lose_heaven",
         shell="NSLoseHeaven2", shell_zh="舍天", shell_en="Forsake", cost=0, rarity="Common",
         target="Other", payoff=["draw"]),
    dict(suffix="frail_self1", zh="给予自身1层脆弱。", en="Gain 1 Frail.",
         base="necrobinder/friendship/2", vals=[("amount", 1)], op="ns_frail_self",
         shell="NSFrailSelf1", shell_zh="力竭", shell_en="Spent", cost=1, rarity="Common",
         target="Other", payoff=["guard"], type="Skill"),
    dict(suffix="frail_target1", zh="给予目标1层脆弱。", en="Apply 1 Frail.",
         base="ironclad/mangle/1", vals=[("amount", 1)], op="ns_frail",
         shell="NSFrailTarget1", shell_zh="挫锐", shell_en="Blunt", cost=1, rarity="Common",
         target="SingleEnemy", payoff=["slash"]),
    # ⚠ 收益**不能是伤害原子**。实测 AA 的生成卡类型是从原子推出来的，不只看这里的
    # GeneratedCardType：壳是 [entangled, sweep_all] 时，日志里卡牌类型是 **Attack**。
    # 于是打出这张攻击牌 → 它自己施加"本回合不能打出攻击牌" → 引擎重检发现
    # **正在结算的这张牌已经非法** → 中止出牌并结束回合（实测症状）。
    # 换成非伤害收益（格挡）后牌不再是攻击牌，缠绕只影响**后续**的攻击牌，符合原意。
    dict(suffix="entangled", zh="本回合你不能打出攻击牌。", en="You cannot play Attacks this turn.",
         base="ironclad/anger/1", vals=[], op="ns_entangled",
         shell="NSEntangled", shell_zh="自缚", shell_en="Bind", cost=1, rarity="Uncommon",
         target="Other", payoff=["guard"], type="Skill"),
    dict(suffix="scry7", zh="预见7。", en="Scry 7.",
         base="colorless/finesse/1", vals=[("draw", 7)], op="ns_scry",
         shell="NSScry7", shell_zh="观星", shell_en="Stargaze", cost=1, rarity="Rare",
         target="Other", payoff=[]),
]

COND_TABLE = [
    ("cond_cards_below3", "如果你在这回合打出的牌数小于3张，", "If you have played fewer than 3 cards this turn,",
     "defect/ftl/1", [("threshold", 3)], "NSCondCards3", "蓄势", "Wind-up"),
    ("cond_fatal", "斩杀时，", "If this kills the enemy,", "colorless/handofgreed/1", None, "NSCondFatal", "斩决", "Execute"),
    ("cond_intends_attack", "如果敌人的意图是攻击，", "If the enemy intends to attack,",
     "defect/gofortheeyes/1", None, "NSCondIntendsAtk", "料敌", "Read"),
]


def main():
    d = sys.argv[1]
    # 可选第二个参数：--dump-ironclad-atoms 导出的**运行时**目录（六角色共 467 个原子）。
    # ⚠ 必须用它校验基线，不能只查 catalog_runtime_specs.json（931 条）——
    # 那是超集，含大量不在生成目录里的原子（例如 ironclad/bloodletting/0 就在 JSON 里但不在运行时目录里，
    # 只查 JSON 会让 Build() 在运行时抛 "missing official baseline"）。
    runtime_ids = None
    if len(sys.argv) > 2 and os.path.exists(sys.argv[2]):
        runtime_ids = set()
        for line in open(sys.argv[2], encoding="utf-8"):
            f = line.rstrip("\n").split("\t")
            if len(f) >= 3 and f[0] == "SID":
                runtime_ids.add(f[2])
        print(f"运行时基线白名单: {len(runtime_ids)} 个原子（来自 {sys.argv[2]}）")

    specs = {e["Id"]: e["Spec"] for e in json.load(
        open(os.path.join(d, "AutoAnthony.Data.catalog_runtime_specs.json"), encoding="utf-8"))}
    recs = json.load(open(os.path.join(d, "AutoAnthony.Data.catalog_recipes.json"), encoding="utf-8"))
    byatom = {}
    for r in recs:
        for a in r["Atoms"]:
            byatom.setdefault(a["SemanticId"], r)

    atoms_out, recipes_out, problems = [], [], []

    def emit(suffix, zh, en, base, overrides, shell_id, shell_zh, shell_en, rarity_override,
             extra_atoms=None, owners=None, custom_opcode=None, custom_variant="",
             cost_override=None, target_override=None, type_override=None):
        if base not in specs:
            problems.append(f"{suffix}: 基线 {base} 不存在于 catalog JSON")
            return
        if runtime_ids is not None and base not in runtime_ids:
            problems.append(f"{suffix}: 基线 {base} 不在**运行时**目录里（Build() 会抛错）")
            return
        r = byatom.get(base)
        if r is None:
            problems.append(f"{suffix}: 基线 {base} 不属于任何官方配方")
            return
        ov = ", ".join(f'("{s}", {v})' for s, v in (overrides or []))
        extra_args = []
        if custom_opcode:
            extra_args.append(f'CustomOpcode: "{custom_opcode}"')
            extra_args.append(f'CustomVariant: "{custom_variant}"')
        extra_args.append(f"Values: [{ov}]")
        atoms_out.append(
            f'        new("newsanguo/ops/{suffix}", "{base}", "{zh}", "{en}", '
            + ", ".join(extra_args) + "),")
        cost, ctype, target = r["Cost"], r["Type"], r["Target"]
        rarity = rarity_override or r["OriginalRarity"]
        # 数值型原子若照抄官方配方的费用/稀有度会出现"0 费 16 伤"这类荒谬组合，
        # 因此按**量级**重新定价（伤害按 damage×hits 计，格挡按 0.75 折算）。
        # 这套价目表是暂定的，改这里即可整体重定价。
        vals = dict(overrides or [])
        if "damage" in vals:
            total = vals["damage"] * vals.get("hits", 1)
            cost = 0 if total <= 4 else 1 if total <= 9 else 2 if total <= 15 else 3
            rarity = "Common" if total <= 9 else "Uncommon" if total <= 15 else "Rare"
        elif "block" in vals:
            block = vals["block"]
            cost = 0 if block <= 4 else 1 if block <= 8 else 2
            rarity = "Common" if block <= 8 else "Uncommon"
        if cost_override is not None:
            cost = cost_override
        if target_override is not None:
            target = target_override
        # 基线是 Power 配方时不能照抄类型：即时效应当 Skill/Attack，Power 卡会留在场上。
        if type_override is not None:
            ctype = type_override
        atom_ids = [f"newsanguo/ops/{suffix}"] + [f"newsanguo/ops/{a}" for a in (extra_atoms or [])]
        atom_refs = ", ".join(f'"{a}"' for a in atom_ids)
        own = owners if owners is not None else [-1] * len(atom_ids)
        recipes_out.append(
            f'        new("{shell_id}", "{shell_zh}", "{shell_en}", {cost}, '
            f'GeneratedCardType.{ctype}, TargetMode.{target}, GeneratedRarity.{rarity}, '
            f'[{", ".join(str(o) for o in own)}], [{atom_refs}]),')
        print(f"  {suffix:18s} <- {base:34s} recipe={r['Id']:18s} cost={cost} "
              f"type={ctype:6s} target={target:14s} rarity={rarity}"
              + (f"  op={custom_opcode}" if custom_opcode else ""))

    print("=== 普通原子 ===")
    for row in TABLE:
        emit(*row)

    print("=== 条件类（配对：无条件伤害 + 条件 + 被门控伤害）===")
    for suffix, zh, en, base, ov, shell_id, shell_zh, shell_en in COND_TABLE:
        # 前后两段收益复用既有的 slash（单体 6 伤）—— 同一组件可以被多张壳引用。
        # owners=[-1,-1,1]：无条件伤害 / 条件原子 / 被条件下标 1 门控的伤害。
        emit(suffix, zh, en, base, ov, shell_id, shell_zh, shell_en, None,
             extra_atoms=["slash", "slash"], owners=[-1, -1, 1])

    # ---- B 档：需要自定义 opcode 的效果（handler 在 src/RuntimeRoutes.cs）----
    # 每个都配一张**完整可用**的牌（原子 + 收益），否则"只减益自己"的壳放进生成池没有意义。
    print("=== B 档（自定义 opcode + 收益，配成完整牌）===")
    for row in B_TABLE:
        emit(row["suffix"], row["zh"], row["en"], row["base"], row["vals"], row["shell"],
             row["shell_zh"], row["shell_en"], row["rarity"],
             extra_atoms=row["payoff"], custom_opcode=row["op"],
             custom_variant=row.get("variant", ""), owners=row.get("owners"),
             cost_override=row["cost"], target_override=row["target"],
             type_override=row.get("type"))

    if problems:
        print("\n!! 问题：")
        for p in problems:
            print("   ", p)
        return 1

    out = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "src", "SelfCatalog.Extra.cs")
    with open(out, "w", encoding="utf-8") as f:
        f.write("// ============================================================================================\n")
        f.write("// SelfCatalog.Extra —— A 档新增原子与配套壳（由 tools/gen_extra_atoms.py 生成，请勿手改）\n")
        f.write("//\n")
        f.write("// 每个原子克隆一个同结构的官方原子（保留官方 Flags/Scope/Explicit 与文案编译规则），\n")
        f.write("// 只覆写数值槽；每张壳的 Cost/Type/Target/Rarity 取自该基线的官方配方，不凭空编造。\n")
        f.write("// 条件类按官方 GoForTheEyes 惯例：条件原子 owner=-1，被门控效果 owner=条件原子下标。\n")
        f.write("// ============================================================================================\n")
        f.write("using ChaosCardGenerator;\n\n")
        f.write("namespace Newsanguo.AutoAnthony;\n\n")
        f.write("internal static partial class SelfCatalog\n{\n")
        f.write("    private static readonly AtomDef[] ExtraAtomDefs =\n    [\n")
        f.write("\n".join(atoms_out))
        f.write("\n    ];\n\n")
        f.write("    private static readonly RecipeDef[] ExtraRecipeDefs =\n    [\n")
        f.write("\n".join(recipes_out))
        f.write("\n    ];\n}\n")
    print(f"\n原子 {len(atoms_out)} 个，壳 {len(recipes_out)} 张 -> {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())



