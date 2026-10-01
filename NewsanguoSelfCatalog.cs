// ============================================================================================
// NewsanguoSelfCatalog —— 三国"自写原子目录"（v1）
//
// 与已被本文件取代的"官方克隆目录"旧方案的区别：
//  - 官方克隆 = 整体换皮（SemanticId 换成 newsanguo/*，Template/Scope/Spec/数值/文案全部保留官方值）。
//  - 自写目录 = 数值基线仍取官方已验证 RuntimeSpec（官方数值基线，AA 经济模型要求原生模板/预算结构），
//    但每个原子都重新登记为 newsanguo/* 语义身份，并写入**自写的中英文文案**（经
//    OperationLocalizedText.TryCompile 编译为模板，本地化强校验 RenderChinese==ChineseText 由此自动满足）；
//    Recipe（壳）也按三国主题自行编排（壳的 Type/Target/原子数分布决定生成器的形态窗口）。
//
// 与离线 harness（aanrg，mode=self / pool self / sgprobe self）中的 SelfCatalogBuilder.cs 同步维护：
// 该文件先经 harness 离线验证（94 槽 5 种子全 OK、生成 ops 无泄漏）后才拷入本目录。
// 本文件除 ChaosCardGenerator/AutoAnthony 外不引用任何其它类型，故两端都能编译。
// ============================================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ChaosCardGenerator;

namespace newsanguo.Scripts.AutoAnthony;

internal static class NewsanguoSelfCatalog
{
    // 自写原子行：语义身份 + 数值基线来源（官方原子结构）+ 自写文案。
    // CustomOpcode/CustomVariant 非空时，把克隆自官方的 RuntimeSpec 的 Opcode/Variant 覆写为自定义值
    // （内建 executor 不认识 → 落到 ComponentRuntimeApi 自定义路由；文案/数值结构仍沿用官方基线）。
    private sealed record AtomDef(string Id, string BaseOfficialId, string Zh, string En,
        string? CustomOpcode = null, string? CustomVariant = null);

    private static readonly AtomDef[] AtomDefs =
    [
        // ---- 攻击（伤害结构按目标形态区分，保证 SchemaKey 各异）----
        new("newsanguo/ops/slash",          "ironclad/anger/0",          "造成6点伤害。",                  "Deal 6 damage."),
        new("newsanguo/ops/sweep_all",      "ironclad/breakthrough/1",   "对所有敌人造成9点伤害。",        "Deal 9 damage to ALL enemies."),
        new("newsanguo/ops/strike_wave",    "ironclad/conflagration/0",  "对所有敌人造成2点伤害4次。",     "Deal 2 damage to ALL enemies 4 times."),
        new("newsanguo/ops/flurry",         "ironclad/swordboomerang/0", "随机对敌人造成3点伤害3次。",     "Deal 3 damage to a random enemy 3 times."),
        new("newsanguo/ops/snipe",          "ironclad/juggernaut/1",     "随机对敌人造成6点伤害。",        "Deal 6 damage to a random enemy."),

        // ---- 防御 ----
        new("newsanguo/ops/guard",          "ironclad/armaments/0",      "获得5点格挡。",                  "Gain 5 Block."),

        // ---- 资源/费用 ----
        new("newsanguo/ops/draw",           "ironclad/battletrance/0",   "抽3张牌。",                      "Draw 3 cards."),
        new("newsanguo/ops/energy",         "ironclad/bloodletting/1",   "获得2点能量。",                  "Gain 2 Energy."),
        new("newsanguo/ops/hp_loss",        "ironclad/bloodwall/0",      "失去2点生命。",                  "Lose 2 HP."),
        new("newsanguo/ops/heal",           "ironclad/notyet/0",         "回复10点生命。",                 "Heal 10 HP."),
        new("newsanguo/ops/max_hp",         "ironclad/feed/2",           "永久获得3点最大生命。",          "Gain 3 Max HP permanently."),
        new("newsanguo/ops/temp_strength",  "ironclad/setupstrike/1",    "本回合获得2点力量。",            "Gain 2 Strength this turn."),

        // ---- 牌库操控 ----
        new("newsanguo/ops/exhaust_choose", "ironclad/brand/1",          "消耗手牌中的一张牌。",          "Exhaust a card from your hand."),
        new("newsanguo/ops/exhaust_random", "ironclad/cinder/1",         "随机消耗手牌中的一张牌。",      "Exhaust a random card from your hand."),
        new("newsanguo/ops/return_to_top",  "ironclad/headbutt/1",       "将弃牌堆中的一张牌放到抽牌堆顶部。", "Put a card from your discard pile on top of your draw pile."),
        new("newsanguo/ops/attack_back",    "ironclad/aggression/1",     "将弃牌堆中的一张随机攻击牌放入手牌。", "Put a random Attack from your discard pile into your hand."),
        new("newsanguo/ops/copy_to_discard","ironclad/anger/1",          "将此牌的一张复制加入弃牌堆。",  "Shuffle a copy of this card into your discard pile."),

        // ---- 敌方状态 ----
        new("newsanguo/ops/weak_1",         "ironclad/uppercut/1",       "给予1层虚弱。",                  "Apply 1 Weak."),
        new("newsanguo/ops/vuln_2",         "ironclad/bash/1",           "给予2层易伤。",                  "Apply 2 Vulnerable."),
        new("newsanguo/ops/vuln_double",    "ironclad/moltenfist/1",     "将该敌人身上的易伤层数翻倍。",  "Double the enemy's Vulnerable."),
        new("newsanguo/ops/vuln_all_1",     "ironclad/thunderclap/1",    "给予所有敌人1层易伤。",         "Apply 1 Vulnerable to ALL enemies."),
        new("newsanguo/ops/str_loss_turn",  "ironclad/mangle/1",         "使该敌人在本回合失去10点力量。","Lose 10 Strength this turn."),

        // ---- 自身增益 ----
        new("newsanguo/ops/strength",       "ironclad/brand/2",          "获得1点力量。",                  "Gain 1 Strength."),
        new("newsanguo/ops/plating",        "ironclad/stonearmor/0",     "获得4层覆甲。",                  "Gain 4 Plating."),
        new("newsanguo/ops/enemy_strength", "ironclad/fightme/3",        "使该敌人获得1点力量。",         "Give the enemy 1 Strength."),

        // ---- 三国专属资源（自定义执行路由：opcode 内建不认识 → 运行时按 (ns_gain_wine,"")/(ns_gain_heaven,"") 分发到
        //      mod 注册的 handler，handler 复用 drunken_might/heavens_force 施加酒力/天意之力；数值/文案结构沿用官方基线）----
        new("newsanguo/ops/wine",           "ironclad/brand/2",          "获得1点酒力。",                  "Gain 1 Drunken Might.", "ns_gain_wine", ""),
        new("newsanguo/ops/heaven",         "ironclad/brand/2",          "获得1点天意之力。",              "Gain 1 Heavens Force.", "ns_gain_heaven", ""),

        // ---- 能力根基（触发器/规则；作为 Power 卡首效果的持久基础）----
        new("newsanguo/ops/trigger_start",  "ironclad/aggression/0",     "在你的回合开始时。",            "At the start of your turn,"),
        new("newsanguo/ops/trigger_end",    "ironclad/stampede/0",       "在你的回合结束时。",            "At the end of your turn,"),
        new("newsanguo/ops/when_block",     "ironclad/juggernaut/0",     "每当你获得格挡时。",            "Whenever you gain Block,"),
        new("newsanguo/ops/when_exhaust",   "ironclad/darkembrace/0",    "每当有一张牌被消耗时。",        "Whenever a card is Exhausted,"),
        new("newsanguo/ops/when_vuln",      "ironclad/vicious/0",        "每当你施加易伤时。",            "Whenever you apply Vulnerable,"),
        new("newsanguo/ops/when_hp_loss",   "ironclad/inferno/2",        "每当你在回合内失去生命时。",    "Whenever you lose HP during your turn,"),
        new("newsanguo/ops/rule_vuln_amp",  "ironclad/cruelty/0",        "拥有易伤的敌人受到的伤害增加25%。", "Enemies with Vulnerable take 25% more damage."),

        // ---- 修饰（必须依附在某条基础效果上，由 AA 装配期自动配对）----
        new("newsanguo/ops/m_dmg_per_vuln", "ironclad/bully/1",          "该敌人每有一层易伤，这张牌额外造成2点伤害。", "This card deals 2 additional damage per stack of Vulnerable on the enemy."),
        new("newsanguo/ops/m_block_per_str","ironclad/expectafight/1",   "你每有1点力量，这张牌额外获得5点格挡。", "Per 1 Strength, this card gains 5 additional Block."),
        new("newsanguo/ops/m_extra_hit",    "ironclad/dismantle/2",      "这张牌额外造成1次伤害。",       "This card deals damage 1 additional time."),
        new("newsanguo/ops/m_per_exhaust",  "ironclad/ashenstrike/1",    "消耗牌堆每有一张牌，这张牌额外造成3点伤害。", "This card deals 3 additional damage for each card in your exhaust pile."),
        new("newsanguo/ops/m_body_slam",    "ironclad/bodyslam/1",       "这张牌造成等同于当前格挡的伤害。", "This card deals damage equal to your current Block.")
    ];

    private sealed record RecipeDef(
        string Id, string ZhTitle, string EnTitle, int Cost, GeneratedCardType Type,
        TargetMode Target, GeneratedRarity Rarity, int[] Owners, string[] AtomIds);

    // 壳（recipe）自写编排：Type/Target/原子数组合决定生成器的形态窗口与可选取壳。
    private static readonly RecipeDef[] RecipeDefs =
    [
        // ================= Basic =================
        // 官方 Ironclad 目录含 3 个 Basic 壳（cost2 攻+易伤 / cost1 纯格挡 / cost1 纯攻击）。
        // 缺失会令 AA 的 Basic 稀有度在唯一签名用尽后频繁走 emergency fallback（升级合法性异常 → 定义
        // 安装失败/开局卡死），且开局牌组难以达标。故自写目录保留对位的 3 个 Basic 壳。
        new("NSBasicBash",    "重斩", "Bash",   2, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Basic, [-1, -1],          ["newsanguo/ops/slash", "newsanguo/ops/vuln_2"]),
        new("NSBasicDefend",  "招架", "Defend", 1, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Basic, [-1],              ["newsanguo/ops/guard"]),
        new("NSBasicStrike",  "斩击", "Strike", 1, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Basic, [-1],              ["newsanguo/ops/slash"]),
        // ================= Common =================
        new("NSSlash",        "斩",   "Slash",   1, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Common, [-1],              ["newsanguo/ops/slash"]),
        new("NSSlashDraw",    "追击", "Pursuit", 1, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Common, [-1, -1],          ["newsanguo/ops/slash", "newsanguo/ops/draw"]),
        new("NSSlashWeak",    "压阵", "Press",   1, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Common, [-1, -1],          ["newsanguo/ops/slash", "newsanguo/ops/weak_1"]),
        new("NSGuard",        "御守", "Guard",   1, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Common, [-1],              ["newsanguo/ops/guard"]),
        new("NSGuardDraw",    "稳守", "Hold",    1, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Common, [-1, -1],          ["newsanguo/ops/guard", "newsanguo/ops/draw"]),
        new("NSGuardWeak",    "虚张", "Bluff",   1, GeneratedCardType.Skill,  TargetMode.SingleEnemy, GeneratedRarity.Common, [-1, -1],          ["newsanguo/ops/guard", "newsanguo/ops/weak_1"]),
        new("NSFlurry",       "飞刃", "Blades",  1, GeneratedCardType.Attack, TargetMode.Other,        GeneratedRarity.Common, [-1],              ["newsanguo/ops/flurry"]),
        new("NSWave",         "火雨", "Ember",   2, GeneratedCardType.Attack, TargetMode.Other,        GeneratedRarity.Common, [-1],              ["newsanguo/ops/strike_wave"]),
        new("NSSweep",        "横扫", "Sweep",   1, GeneratedCardType.Attack, TargetMode.Other,        GeneratedRarity.Common, [-1],              ["newsanguo/ops/sweep_all"]),
        new("NSSnipe",        "暗箭", "Arrow",   1, GeneratedCardType.Attack, TargetMode.Other,        GeneratedRarity.Common, [-1],              ["newsanguo/ops/snipe"]),
        new("NSDraw",         "休整", "Rally",   1, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Common, [-1],              ["newsanguo/ops/draw"]),
        new("NSBurn",         "燃血", "Burn",    0, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Common, [-1, -1],          ["newsanguo/ops/hp_loss", "newsanguo/ops/energy"]),
        new("NSStrengthen",   "磨刀", "Whet",    1, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Common, [-1],              ["newsanguo/ops/temp_strength"]),
        new("NSVuln",         "破甲", "Sunder",  1, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Common, [-1, -1],          ["newsanguo/ops/slash", "newsanguo/ops/vuln_2"]),
        new("NSExhaustRandom","焚仓", "Cinder",  1, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Common, [-1],              ["newsanguo/ops/exhaust_random"]),
        new("NSBash",         "突刺", "Lunge",   1, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Common, [-1, -1],          ["newsanguo/ops/slash", "newsanguo/ops/str_loss_turn"]),

        // ================= Uncommon =================
        new("NSUppercut",     "连环威压", "Cuff",       2, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Uncommon, [-1, -1, -1], ["newsanguo/ops/slash", "newsanguo/ops/weak_1", "newsanguo/ops/vuln_2"]),
        new("NSHemo",         "血刃",     "Blood Blade",2, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Uncommon, [-1, -1],    ["newsanguo/ops/hp_loss", "newsanguo/ops/slash"]),
        new("NSDouble",       "裂帛",     "Rend",       2, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Uncommon, [-1, -1],    ["newsanguo/ops/slash", "newsanguo/ops/vuln_double"]),
        new("NSWhirl",        "旋风斩",   "Whirl",      1, GeneratedCardType.Attack, TargetMode.Other,        GeneratedRarity.Uncommon, [-1],       ["newsanguo/ops/flurry"]),
        new("NSEchoSlash",    "连环斩",   "Combo",      1, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Uncommon, [-1, 0],     ["newsanguo/ops/slash", "newsanguo/ops/m_extra_hit"]),
        new("NSBully",        "恃强",     "Bully",      2, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Uncommon, [-1, 0],     ["newsanguo/ops/slash", "newsanguo/ops/m_dmg_per_vuln"]),
        new("NSBodySlam",     "金钟",     "Bell",       1, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Uncommon, [-1, 0],     ["newsanguo/ops/slash", "newsanguo/ops/m_body_slam"]),
        new("NSBlockStr",     "以力御守", "Fortify",    2, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Uncommon, [-1, 0],    ["newsanguo/ops/guard", "newsanguo/ops/m_block_per_str"]),
        new("NSHeal",         "疗伤",     "Heal",       1, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Uncommon, [-1],       ["newsanguo/ops/heal"]),
        new("NSMaxHp",        "养精",     "Vigor",      1, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Uncommon, [-1],       ["newsanguo/ops/max_hp"]),
        new("NSDrawEnergy",   "犒军",     "Provisions", 0, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Uncommon, [-1, -1],    ["newsanguo/ops/draw", "newsanguo/ops/energy"]),
        new("NSExhaustDraw",  "烧尽",     "Incin",      1, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Uncommon, [-1, -1],    ["newsanguo/ops/exhaust_choose", "newsanguo/ops/draw"]),
        new("NSReturnTop",    "回马",     "Return",     1, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Uncommon, [-1],       ["newsanguo/ops/return_to_top"]),
        new("NSAttackBack",   "招还",     "Recall",     1, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Uncommon, [-1],       ["newsanguo/ops/attack_back"]),
        new("NSStrSelf",      "磨砺",     "Hone",       1, GeneratedCardType.Power,  TargetMode.Other,        GeneratedRarity.Uncommon, [-1],       ["newsanguo/ops/strength"]),
        new("NSPlating",      "披甲",     "Plate",      1, GeneratedCardType.Power,  TargetMode.Other,        GeneratedRarity.Uncommon, [-1],       ["newsanguo/ops/plating"]),
        new("NSDrink",        "小酌",     "Sip",        1, GeneratedCardType.Power,  TargetMode.Other,        GeneratedRarity.Uncommon, [-1],       ["newsanguo/ops/wine"]),
        new("NSHeaven",       "敬天",     "Venerate",   1, GeneratedCardType.Power,  TargetMode.Other,        GeneratedRarity.Uncommon, [-1],       ["newsanguo/ops/heaven"]),
        new("NSVulnAll",      "慑敌",     "Awe",        2, GeneratedCardType.Attack, TargetMode.Other,        GeneratedRarity.Uncommon, [-1, -1],    ["newsanguo/ops/sweep_all", "newsanguo/ops/vuln_all_1"]),

        // ================= Rare =================
        new("NSImpervious",   "至坚",     "Bulwark",    2, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Rare, [-1],          ["newsanguo/ops/guard"]),
        new("NSOffering",     "献祭",     "Offering",   0, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Rare, [-1, -1, -1],  ["newsanguo/ops/hp_loss", "newsanguo/ops/energy", "newsanguo/ops/draw"]),
        new("NSPyre",         "薪火",     "Pyre",       2, GeneratedCardType.Power,  TargetMode.Other,        GeneratedRarity.Rare, [-1, 0],       ["newsanguo/ops/trigger_start", "newsanguo/ops/energy"]),
        new("NSDemonForm",    "神威",     "Might",      3, GeneratedCardType.Power,  TargetMode.Other,        GeneratedRarity.Rare, [-1, 0],       ["newsanguo/ops/trigger_start", "newsanguo/ops/strength"]),
        new("NSJuggernaut",   "势如破竹", "Juggernaut", 2, GeneratedCardType.Power,  TargetMode.Other,        GeneratedRarity.Rare, [-1, 0],       ["newsanguo/ops/when_block", "newsanguo/ops/snipe"]),
        new("NSBarricadeLike","铜墙",     "Rampart",    3, GeneratedCardType.Power,  TargetMode.Other,        GeneratedRarity.Rare, [-1, 0],       ["newsanguo/ops/trigger_end", "newsanguo/ops/guard"]),
        new("NSDarkEmbrace",  "焚书",     "Ashes",      1, GeneratedCardType.Power,  TargetMode.Other,        GeneratedRarity.Rare, [-1, 0],       ["newsanguo/ops/when_exhaust", "newsanguo/ops/draw"]),
        new("NSVicious",      "引火",     "Pyro",       1, GeneratedCardType.Power,  TargetMode.Other,        GeneratedRarity.Rare, [-1, 0],       ["newsanguo/ops/when_vuln", "newsanguo/ops/slash"]),
        new("NSInferno",      "涅槃",     "Rekindle",   2, GeneratedCardType.Power,  TargetMode.Other,        GeneratedRarity.Rare, [-1, 0],       ["newsanguo/ops/when_hp_loss", "newsanguo/ops/strength"]),
        new("NSRage",         "回击",     "Retort",     1, GeneratedCardType.Power,  TargetMode.Other,        GeneratedRarity.Rare, [-1, 0],       ["newsanguo/ops/trigger_start", "newsanguo/ops/vuln_all_1"]),
        new("NSCruelty",      "残暴",     "Cruel",      1, GeneratedCardType.Power,  TargetMode.Other,        GeneratedRarity.Rare, [-1],          ["newsanguo/ops/rule_vuln_amp"]),
        new("NSEnemyStr",     "资敌",     "Sabotage",   1, GeneratedCardType.Skill,  TargetMode.SingleEnemy, GeneratedRarity.Rare, [-1],          ["newsanguo/ops/enemy_strength"]),
        new("NSDuplicate",    "连环",     "Echo",       2, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Rare, [-1, -1],       ["newsanguo/ops/slash", "newsanguo/ops/copy_to_discard"]),
        new("NSExhaustPower", "纵火",     "Arson",      2, GeneratedCardType.Power,  TargetMode.Other,        GeneratedRarity.Rare, [-1, 0],       ["newsanguo/ops/when_exhaust", "newsanguo/ops/m_per_exhaust"])
    ];

    private static IComponentCatalog? _cached;
    private static readonly object Sync = new();

    /// <summary>构建并缓存自写原子目录。同一进程只注册一次本地化/英文回退表，缓存供多 Profile 复用。</summary>
    internal static IComponentCatalog Build()
    {
        lock (Sync)
        {
            if (_cached is not null)
                return _cached;
            _cached = BuildCore();
            return _cached;
        }
    }

    private static IComponentCatalog BuildCore()
    {
        var official = CharacterComponentCatalogs.Get(GeneratedCharacter.Ironclad);
        var officialById = official.Atoms
            .Where(a => a.SemanticId is not null)
            .ToDictionary(a => a.SemanticId!, StringComparer.Ordinal);

        var registeredText = new HashSet<(string Template, string Zh)>();
        var selfAtoms = new Dictionary<string, ComponentAtom>(StringComparer.Ordinal);

        foreach (var def in AtomDefs)
        {
            if (!officialById.TryGetValue(def.BaseOfficialId, out var baseAtom))
                throw new InvalidDataException($"Self atom {def.Id}: missing official baseline {def.BaseOfficialId}.");
            var spec = baseAtom.RuntimeSpec
                ?? throw new InvalidDataException($"Self atom {def.Id}: baseline {def.BaseOfficialId} has no RuntimeSpec.");
            spec.Validate();
            // 自定义执行路由原子：仅覆写 Opcode/Variant（数值/目标/文案结构沿用官方基线；分类仍走官方 Template）。
            if (def.CustomOpcode is not null)
                spec = spec with { Opcode = def.CustomOpcode, Variant = def.CustomVariant ?? "" };

            if (!OperationLocalizedText.TryCompile(def.Zh, def.En, spec, out var compiled) || compiled is null)
                throw new InvalidDataException($"Self atom {def.Id} ({def.BaseOfficialId}) failed to compile localized text: zh='{def.Zh}' en='{def.En}'");

            // 注册英文回退文案（同一 Template+中文 只注册一次；与 WatcherCatalog.Load 相同策略）。
            if (registeredText.Add((baseAtom.Template, def.Zh)))
                ExternalOperationTextRegistry.Register(baseAtom.Template, def.Zh, def.En);

            // 原子 = 数值基线（RuntimeSpec 用 spec；普通原子即官方原样，自定义路由原子为覆写后 spec）+ 自写语义身份与文案。
            selfAtoms[def.Id] = baseAtom with
            {
                SemanticId = def.Id,
                ChineseText = def.Zh,
                LocalizedText = compiled,
                RuntimeSpec = spec
            };
        }

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        var recipes = new List<IroncladCardRecipe>(RecipeDefs.Length);
        foreach (var def in RecipeDefs)
        {
            var atoms = new ComponentAtom[def.AtomIds.Length];
            for (var i = 0; i < def.AtomIds.Length; i++)
            {
                if (!selfAtoms.TryGetValue(def.AtomIds[i], out var atom))
                    throw new InvalidDataException($"Recipe {def.Id} references unknown self atom {def.AtomIds[i]}.");
                atoms[i] = atom;
                referenced.Add(def.AtomIds[i]);
            }

            recipes.Add(new IroncladCardRecipe(
                Id: def.Id,
                ChineseTitle: def.ZhTitle,
                Cost: def.Cost,
                Type: def.Type,
                Target: def.Target,
                OriginalRarity: def.Rarity,
                Tags: [],
                Atoms: atoms,
                TriggerOwners: def.Owners,
                StarCost: -1,
                HasStarCostX: false,
                EnglishTitle: def.EnTitle,
                CustomKeywords: null));
        }

        var unreferenced = selfAtoms.Keys.Where(id => !referenced.Contains(id)).ToArray();
        if (unreferenced.Length > 0)
            throw new InvalidDataException("Self atoms with no referencing recipe: " + string.Join(", ", unreferenced));

        return new ImmutableComponentCatalog(GeneratedCharacter.Ironclad, recipes);
    }
}
