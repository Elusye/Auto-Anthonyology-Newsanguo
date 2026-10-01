// ============================================================================================
// SelfCatalog —— 三国"自写原子目录"（v1）
//
// 独立 mod 版：由 newsanguo 内置适配器中的 NewsanguoSelfCatalog 原样搬出，
// 仅改命名空间/类名；本文件只依赖 ChaosCardGenerator(AutoAnthony) 与 BCL。
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

namespace Newsanguo.AutoAnthony;

internal static partial class SelfCatalog
{
    // 自写原子行：语义身份 + 数值基线来源（官方原子结构）+ 自写文案。
    // CustomOpcode/CustomVariant 非空时，把克隆自官方的 RuntimeSpec 的 Opcode/Variant 覆写为自定义值
    // （内建 executor 不认识 → 落到 ComponentRuntimeApi 自定义路由；文案/数值结构仍沿用官方基线）。
    // Values 非空时按槽位覆写数值 —— 官方基线只提供**结构**，数值可以自己定（A 档新增原子靠它）。
    private sealed record AtomDef(string Id, string BaseOfficialId, string Zh, string En,
        string? CustomOpcode = null, string? CustomVariant = null,
        (string Slot, int Value)[]? Values = null);

    private static readonly AtomDef[] AtomDefs =
    [
        new("newsanguo/ops/slash",          "ironclad/anger/0",          "造成6点伤害。",                  "Deal 6 damage."),
        new("newsanguo/ops/sweep_all",      "ironclad/breakthrough/1",   "对所有敌人造成9点伤害。",        "Deal 9 damage to ALL enemies."),
        new("newsanguo/ops/strike_wave",    "ironclad/conflagration/0",  "对所有敌人造成2点伤害4次。",     "Deal 2 damage to ALL enemies 4 times."),
        new("newsanguo/ops/guard",          "ironclad/armaments/0",      "获得5点格挡。",                  "Gain 5 Block."),
        new("newsanguo/ops/draw",           "ironclad/battletrance/0",   "抽3张牌。",                      "Draw 3 cards."),
        new("newsanguo/ops/energy",         "ironclad/bloodletting/1",   "获得2点能量。",                  "Gain 2 Energy."),
        new("newsanguo/ops/hp_loss",        "ironclad/bloodwall/0",      "失去2点生命。",                  "Lose 2 HP."),
        new("newsanguo/ops/exhaust_choose", "ironclad/brand/1",          "消耗手牌中的一张牌。",          "Exhaust a card from your hand."),
        new("newsanguo/ops/copy_to_discard","ironclad/anger/1",          "将此牌的一张复制加入弃牌堆。",  "Shuffle a copy of this card into your discard pile."),
        new("newsanguo/ops/weak_1",         "ironclad/uppercut/1",       "给予1层虚弱。",                  "Apply 1 Weak."),
        new("newsanguo/ops/vuln_2",         "ironclad/bash/1",           "给予2层易伤。",                  "Apply 2 Vulnerable."),
        new("newsanguo/ops/vuln_all_1",     "ironclad/thunderclap/1",    "给予所有敌人1层易伤。",         "Apply 1 Vulnerable to ALL enemies."),
        new("newsanguo/ops/wine",           "ironclad/brand/2",          "获得1点酒力。",                  "Gain 1 Drunken Might.", "ns_gain_wine", ""),
        new("newsanguo/ops/heaven",         "ironclad/brand/2",          "获得1点天意之力。",              "Gain 1 Heavens Force.", "ns_gain_heaven", ""),
        new("newsanguo/ops/m_extra_hit",    "ironclad/dismantle/2",      "这张牌额外造成1次伤害。",       "This card deals damage 1 additional time."),
    ];

    private sealed record RecipeDef(
        string Id, string ZhTitle, string EnTitle, int Cost, GeneratedCardType Type,
        TargetMode Target, GeneratedRarity Rarity, int[] Owners, string[] AtomIds);

    // 壳（recipe）自写编排：Type/Target/原子数组合决定生成器的形态窗口与可选取壳。
    private static readonly RecipeDef[] RecipeDefs =
    [
        new("NSBasicBash",    "重斩", "Bash",   2, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Basic, [-1, -1],          ["newsanguo/ops/slash", "newsanguo/ops/vuln_2"]),
        new("NSBasicDefend",  "招架", "Defend", 1, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Basic, [-1],              ["newsanguo/ops/guard"]),
        new("NSBasicStrike",  "斩击", "Strike", 1, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Basic, [-1],              ["newsanguo/ops/slash"]),
        new("NSSlash",        "斩",   "Slash",   1, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Common, [-1],              ["newsanguo/ops/slash"]),
        new("NSSlashDraw",    "追击", "Pursuit", 1, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Common, [-1, -1],          ["newsanguo/ops/slash", "newsanguo/ops/draw"]),
        new("NSSlashWeak",    "压阵", "Press",   1, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Common, [-1, -1],          ["newsanguo/ops/slash", "newsanguo/ops/weak_1"]),
        new("NSGuard",        "御守", "Guard",   1, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Common, [-1],              ["newsanguo/ops/guard"]),
        new("NSGuardDraw",    "稳守", "Hold",    1, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Common, [-1, -1],          ["newsanguo/ops/guard", "newsanguo/ops/draw"]),
        new("NSGuardWeak",    "虚张", "Bluff",   1, GeneratedCardType.Skill,  TargetMode.SingleEnemy, GeneratedRarity.Common, [-1, -1],          ["newsanguo/ops/guard", "newsanguo/ops/weak_1"]),
        new("NSWave",         "火雨", "Ember",   2, GeneratedCardType.Attack, TargetMode.Other,        GeneratedRarity.Common, [-1],              ["newsanguo/ops/strike_wave"]),
        new("NSSweep",        "横扫", "Sweep",   1, GeneratedCardType.Attack, TargetMode.Other,        GeneratedRarity.Common, [-1],              ["newsanguo/ops/sweep_all"]),
        new("NSDraw",         "休整", "Rally",   1, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Common, [-1],              ["newsanguo/ops/draw"]),
        new("NSBurn",         "燃血", "Burn",    0, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Common, [-1, -1],          ["newsanguo/ops/hp_loss", "newsanguo/ops/energy"]),
        new("NSVuln",         "破甲", "Sunder",  1, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Common, [-1, -1],          ["newsanguo/ops/slash", "newsanguo/ops/vuln_2"]),
        new("NSUppercut",     "连环威压", "Cuff",       2, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Uncommon, [-1, -1, -1], ["newsanguo/ops/slash", "newsanguo/ops/weak_1", "newsanguo/ops/vuln_2"]),
        new("NSHemo",         "血刃",     "Blood Blade",2, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Uncommon, [-1, -1],    ["newsanguo/ops/hp_loss", "newsanguo/ops/slash"]),
        new("NSEchoSlash",    "连环斩",   "Combo",      1, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Uncommon, [-1, 0],     ["newsanguo/ops/slash", "newsanguo/ops/m_extra_hit"]),
        new("NSDrawEnergy",   "犒军",     "Provisions", 0, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Uncommon, [-1, -1],    ["newsanguo/ops/draw", "newsanguo/ops/energy"]),
        new("NSExhaustDraw",  "烧尽",     "Incin",      1, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Uncommon, [-1, -1],    ["newsanguo/ops/exhaust_choose", "newsanguo/ops/draw"]),
        new("NSDrink",        "小酌",     "Sip",        1, GeneratedCardType.Power,  TargetMode.Other,        GeneratedRarity.Uncommon, [-1],       ["newsanguo/ops/wine"]),
        new("NSHeaven",       "敬天",     "Venerate",   1, GeneratedCardType.Power,  TargetMode.Other,        GeneratedRarity.Uncommon, [-1],       ["newsanguo/ops/heaven"]),
        new("NSVulnAll",      "慑敌",     "Awe",        2, GeneratedCardType.Attack, TargetMode.Other,        GeneratedRarity.Uncommon, [-1, -1],    ["newsanguo/ops/sweep_all", "newsanguo/ops/vuln_all_1"]),
        new("NSImpervious",   "至坚",     "Bulwark",    2, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Rare, [-1],          ["newsanguo/ops/guard"]),
        new("NSOffering",     "献祭",     "Offering",   0, GeneratedCardType.Skill,  TargetMode.Other,        GeneratedRarity.Rare, [-1, -1, -1],  ["newsanguo/ops/hp_loss", "newsanguo/ops/energy", "newsanguo/ops/draw"]),
        new("NSDuplicate",    "连环",     "Echo",       2, GeneratedCardType.Attack, TargetMode.SingleEnemy, GeneratedRarity.Rare, [-1, -1],       ["newsanguo/ops/slash", "newsanguo/ops/copy_to_discard"]),
    ];

    private static IComponentCatalog? _cached;
    private static readonly object Sync = new();
    private static readonly List<string> _fallbacks = [];

    /// <summary>
    /// 上一次构建中因自写文案编译不过、退回官方文案的原子（形如 <c>id (baseline ...)</c>）。
    /// 这些原子的卡面措辞仍是官方 Ironclad 的，机制与数值不受影响；需要时按此清单修正文案。
    /// </summary>
    internal static IReadOnlyList<string> LastFallbacks => _fallbacks;

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
        _fallbacks.Clear();

        // 数值基线索引：跨**全部**角色目录取，而不是只看 Ironclad ——
        // 新增原子（A 档）会借用其它角色的同结构官方原子（例如「对所有敌人造成伤害」借 Breakthrough、
        // 「本回合敏捷」借 Anticipate），只索引 Ironclad 会找不到基线。
        // 注意：借用的是**结构与官方 Flags**；本目录的身份、文案与生成的平衡原型仍是 Ironclad。
        var officialById = new Dictionary<string, ComponentAtom>(StringComparer.Ordinal);
        foreach (var character in Enum.GetValues<GeneratedCharacter>())
        {
            IComponentCatalog catalog;
            try
            {
                catalog = CharacterComponentCatalogs.Get(character);
            }
            catch
            {
                continue; // 个别角色目录在该环境下不可用，跳过即可
            }
            foreach (var atom in catalog.Atoms)
            {
                if (atom.SemanticId is { Length: > 0 })
                    officialById.TryAdd(atom.SemanticId!, atom);
            }
        }

        var registeredText = new HashSet<(string Template, string Zh)>();
        var selfAtoms = new Dictionary<string, ComponentAtom>(StringComparer.Ordinal);

        foreach (var def in AtomDefs.Concat(ExtraAtomDefs))
        {
            if (!officialById.TryGetValue(def.BaseOfficialId, out var baseAtom))
                throw new InvalidDataException($"Self atom {def.Id}: missing official baseline {def.BaseOfficialId}.");
            var spec = baseAtom.RuntimeSpec
                ?? throw new InvalidDataException($"Self atom {def.Id}: baseline {def.BaseOfficialId} has no RuntimeSpec.");
            spec.Validate();
            // 自定义执行路由原子：仅覆写 Opcode/Variant（数值/目标/文案结构沿用官方基线；分类仍走官方 Template）。
            if (def.CustomOpcode is not null)
                spec = spec with { Opcode = def.CustomOpcode, Variant = def.CustomVariant ?? "" };

            // 数值覆写：官方基线只提供结构，数值可以自己定（官方 Flags/Scope/Explicit 全部保留）。
            if (def.Values is { Length: > 0 })
            {
                var slots = spec.Values.ToArray();
                foreach (var (slotId, value) in def.Values)
                {
                    var index = Array.FindIndex(slots, s => s.Id == slotId);
                    if (index < 0)
                        throw new InvalidDataException(
                            $"Self atom {def.Id}: baseline {def.BaseOfficialId} has no value slot '{slotId}'.");
                    slots[index] = slots[index] with { BaseValue = value, Offset = 0 };
                }
                spec = spec with { Values = slots };
            }

            // 自写文案必须能按该 RuntimeSpec 的“可打印槽位”编译成具名模板（AA 用槽位字面量回填，
            // 编译不过即说明文案与槽位结构不符）。
            //
            // **绝不能因此整包抛错**：抛错会中断 Startup，而槽卡此时可能已经注册进卡池，
            // 于是留下一批永远装不上定义、一读 Rarity/Generated 就抛的卡 —— 实测会导致
            // 开局先古之民处直接卡死。因此这里退化为沿用官方原子的本地化文案（官方文案必然是
            // 对同一 RuntimeSpec 校验过的），并记录该原子以便日后修正措辞。
            if (OperationLocalizedText.TryCompile(def.Zh, def.En, spec, out var compiled) && compiled is not null)
            {
                // 注册英文回退文案（同一 Template+中文 只注册一次；与 WatcherCatalog.Load 相同策略）。
                if (registeredText.Add((baseAtom.Template, def.Zh)))
                    ExternalOperationTextRegistry.Register(baseAtom.Template, def.Zh, def.En);

                // 原子 = 数值基线（RuntimeSpec 用 spec；普通原子即官方原样，自定义路由原子为覆写后 spec）
                //        + 自写语义身份与文案。
                selfAtoms[def.Id] = baseAtom with
                {
                    SemanticId = def.Id,
                    ChineseText = def.Zh,
                    LocalizedText = compiled,
                    RuntimeSpec = spec
                };
            }
            else
            {
                _fallbacks.Add($"{def.Id} (baseline {def.BaseOfficialId})");
                // 身份与（可能的）自定义 opcode 仍然生效，只有卡面文案退回官方。
                selfAtoms[def.Id] = baseAtom with
                {
                    SemanticId = def.Id,
                    RuntimeSpec = spec
                };
            }
        }

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        // 三部分壳：手写保留的 + A/B 档新增的 + 从 newsanguo 卡池反推出来的。
        var allRecipeDefs = RecipeDefs.Concat(ExtraRecipeDefs).Concat(DerivedRecipeDefs).ToArray();
        var recipes = new List<IroncladCardRecipe>(allRecipeDefs.Length);
        foreach (var def in allRecipeDefs)
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
