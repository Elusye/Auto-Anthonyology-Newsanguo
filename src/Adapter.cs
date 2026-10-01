using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using AutoAnthony;
using ChaosCardGenerator;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib;
using STS2RitsuLib.Content;
using newsanguo.Scripts.Characters;

namespace Newsanguo.AutoAnthony;

/// <summary>
/// 东尼算法（AutoAnthony）适配核心 —— "整池接管 + 替换初始牌组"。
///
/// 接管开启后：newsanguo 牌池里除 2 张先古（Ancient）外的全部手工卡从一切获取/图鉴入口隐藏，
/// 池内奖励/商店/事件/发现等"可获得内容"全部换成 AA 生成的卡；初始牌组替换为 10 张独一无二的
/// AA 基础卡（10 个 Basic 槽各 1 张）。
///
/// 与旧"内置在 newsanguo 里"的实现相比的关键差异：
///  · AA 由可选依赖变为**必需依赖**（清单 dependencies 已声明），因此不再需要
///    AssemblyLoad 钩子、DispatchProxy、以及"含 AA 类型的方法体不得被 JIT"的一整套规避；
///  · 卡槽模型由 Reflection.Emit 动态生成改为 **96 个静态声明的具体类**（<see cref="SlotTable"/>），
///    符合 AA 官方 examples/WatcherComponentAdapter 推荐的形态，ModelDb 可直接发现稳定 ModelId；
///  · 隐藏手工卡由"FilterThroughEpochs 钩子 + 图鉴私有字段修剪"两处补丁，改为在
///    <see cref="CardPoolModel.GenerateAllCards"/> 唯一咽喉点重建成员表（见 <see cref="TakeoverPatches"/>）；
///  · 卡槽注册进 newsanguo 卡池使用**本 mod 自己的 ModId**（RitsuLib 的注册按 mod 归属追踪）。
///
/// 保留的既有设计：卡槽按稀有度从低到高排布、单生成器连续刷完整池、池级审计与换种子重试、
/// 开局牌组加固（伤害≥22 / 格挡≥20）、中文卡名从词块库无放回抽取、每次安装后清理 Canonical 缓存。
/// </summary>
internal static class Adapter
{
    // ProfileId 是外部身份；Ironclad 是最接近的平衡原型（仅作卡名词块与生成器 archetype 来源）。
    internal const string ProfileId = "newsanguo:autoanthony";
    private const string ComponentPackageId = "newsanguo:autoanthony:components";

    // ---- 槽位计划（必须与 src/SlotCards.cs 的静态声明一致）----
    // 手工池实测（newsanguo 0.2.38）：Basic 4 / Common 20 / Uncommon 40 / Rare 26 / Ancient 2 = 92。
    // 除 2 张 Ancient 外全部由 AA 生成卡替换：Basic 10（承担初始牌组）+ Common 20 + Uncommon 40 + Rare 26。
    internal const int BasicSlotCount = 10;
    internal const int CommonSlotCount = 20;
    internal const int UncommonSlotCount = 40;
    internal const int RareSlotCount = 26;
    internal const int SlotCount = BasicSlotCount + CommonSlotCount + UncommonSlotCount + RareSlotCount; // 96

    // 替换初始牌组时每张 AA 基础槽的份数：10 个 Basic 槽各 1 张 → 开局 10 张独一无二的卡。
    internal static readonly int[] BasicSlotDeckCounts = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1];

    /// <summary>按槽序生成稀有度计划（Basic 0..9，其后 Common/Uncommon/Rare 从低到高）。</summary>
    private static readonly GeneratedRarity[] SlotRarities = BuildSlotRarityPlan();

    // ---- 基础卡加固阈值（移植自「东尼算法：观者」）----
    private static readonly HashSet<string> RunEndingNegativeVariants =
        new(StringComparer.Ordinal) { "w_dienextturn", "w_energydowneachturn", "w_playableonlyassoleattack" };
    private const int RunEndingNegativePayoffFloor = 20;
    private const int StartingDeckDamageFloor = 22;
    private const int StartingDeckBlockFloor = 20;
    private const int BasicRerollMaxAttempts = 24;
    private const int BasicCoverageMaxPasses = 64;
    private const int MaxInstallAttempts = 4;

    // ---- 槽卡立绘 ----
    // ModelDb 就绪后收集 newsanguo 手工卡（整池接管中被隐藏的那批）的真实 PortraitPath 循环使用；
    // ModelDb 未就绪时回退这几张占位图，稍后由 ModelIdsInitializedEvent 刷新。
    // 注意：newsanguo 的卡图文件名是**类名 PascalCase**（见 NewsanguoCardTemplate 的
    // $"res://newsanguo/images/cards/{GetType().Name}.png"），不是 snake_case ——
    // 早期这里写的是 snake_case 路径，5/6 根本不存在，会让库预览显示破图。
    private static readonly string[] FallbackSlotPortraits =
    [
        "res://newsanguo/images/cards/WineTheOldHero.png",
        "res://newsanguo/images/cards/CrossForCross.png",
        "res://newsanguo/images/cards/QuadBlast.png",
        "res://newsanguo/images/cards/MedicalMastery.png",
        "res://newsanguo/images/cards/Empower.png",
        "res://newsanguo/images/cards/WineCut.png"
    ];

    private const string HitFx = "vfx/vfx_attack_blunt";
    private const string PowerIconPath = "res://images/atlases/power_atlas.sprites/strength_power.tres";
    private const string PowerBigIconPath = "res://images/powers/strength_power.png";

    private const string LibrarySeed = "newsanguo:library";

    private static readonly object Sync = new();
    private static bool _registered;
    private static bool _aaReady;
    private static bool _definitionsInstalled;
    private static bool _libraryInstalled;
    private static bool _modelDbReady;
    private static bool _runActive;
    // 立绘按稀有度分两组：**先古卡图与普通卡图尺寸不同**，混用会出现"先古图套在普通卡上"
    // （AA 官方规则同样是：普通牌排除先古图，先古牌只用本角色自己的先古图）。
    private static string[]? _portraitPoolNormal;
    private static string[]? _portraitPoolAncient;
    private static bool _portraitsCollected;

    // 接管前 newsanguo 卡池的原始成员（在 AllCards 的 Postfix 里首次捕获）：
    // 用于 ① 保留 2 张先古；② 收集被隐藏手工卡的立绘。捕获后不再更新。
    private static CardModel[]? _originalNewsanguoMembers;

    // 重建后的卡池成员表（96 槽卡 + 先古），算一次缓存，供 AllCards 热路径复用。
    private static CardModel[]? _takeoverContents;

    /// <summary>
    /// 整池接管是否激活。必须**同时**满足：
    ///  ① AA 侧注册成功（否则槽卡永远装不上定义）；
    ///  ② 槽卡已注册进 newsanguo 卡池；
    ///  ③ **本局定义确实已安装成功**。
    ///
    /// 第 ③ 条是最后一道保险：只要定义没装上，卡池重建就返回原池内容，游戏照常可玩。
    /// 少了任何一条都会出现"池里有一批读一次就抛的槽卡"——也就是开局卡死。
    /// </summary>
    internal static bool IsTakeoverActive => _aaReady && _registered && _definitionsInstalled;

    /// <summary>
    /// 是否具备"生成并安装本局定义"的前提 —— 只需要 AA 侧注册成功。
    /// **不能**用 <see cref="IsTakeoverActive"/> 来守这一步：后者要求定义已安装，
    /// 而定义正是由这一步安装的（循环依赖 → 定义永远装不上 → 接管永远不激活）。
    /// </summary>
    private static bool CanInstallDefinitions => _aaReady;

    /// <summary>由 <see cref="ModEntry.Init"/> 调用一次。</summary>
    internal static void Startup()
    {
        try
        {
            // ⚠ 顺序至关重要：**先**把 AA 侧全部注册成功，**再**往 newsanguo 卡池里塞槽卡。
            //
            // 反过来做会留下致命的半成品状态：槽卡已经在池里、定义却永远装不上，于是任何一次
            // Rarity/Generated 读取都会抛 —— 实测表现为开局"先古之民"处直接卡死。
            // AA 侧失败时这里直接返回，卡池保持原样，游戏仍能正常游玩（只是没有接管）。
            if (!TryRegisterAutoAnthony())
            {
                Log.Error("AutoAnthony integration unavailable; takeover disabled, pool left untouched.");
                return;
            }

            if (!RegisterSlotsIntoPool())
            {
                Log.Error("Slot cards were not registered; takeover disabled, pool left untouched.");
                return;
            }

            TakeoverPatches.Install();
            SubscribeLifecycle();
            TryEnsureLibraryDefaults();
        }
        catch (Exception e)
        {
            Log.Error("Startup failed: " + e);
        }
    }

    // ------------------------------------------------------------------ 注册

    /// <summary>把 96 个静态槽卡类注册进 newsanguo 的卡池。使用本 mod 自己的 ModId（RitsuLib 按归属追踪）。</summary>
    private static bool RegisterSlotsIntoPool()
    {
        if (_registered) return true;
        lock (Sync)
        {
            if (_registered) return true;
            try
            {
                var registry = ModContentRegistry.For(ModEntry.ModId);
                for (var slot = 0; slot < SlotTable.Count; slot++)
                {
                    registry.RegisterCard(
                        typeof(NewsanguoCardPool),
                        SlotTable.TypeForSlot(slot),
                        ModelPublicEntryOptions.FromStem("autoanthony_slot" + slot));
                }
                Log.Info($"Registered {SlotTable.Count} slot cards into NewsanguoCardPool " +
                         $"(modId={ModEntry.ModId}).");
            }
            catch (Exception e)
            {
                Log.Error("Register slot cards FAILED (takeover stays inactive): " + e);
                return false;
            }

            _registered = true;
            return true;
        }
    }

    /// <summary>注册 AA 组件包、外部角色身份与运行期宿主。幂等；返回是否可用。</summary>
    private static bool TryRegisterAutoAnthony()
    {
        if (_aaReady) return true;
        lock (Sync)
        {
            if (_aaReady) return true;
            return RegisterAutoAnthonyCore();
        }
    }

    private static bool RegisterAutoAnthonyCore()
    {
        // 版本核对：ApiVersion 是编译期常量，直接比较会被常量折叠而失效，
        // 因此运行时反射读取，确保与当前加载的 AA 真实版本一致。
        var componentApi = ReadApiVersion(typeof(ComponentApi));
        var packageApi = ReadApiVersion(typeof(ComponentPackageApi));
        var runtimeApi = ReadApiVersion(typeof(ComponentRuntimeApi));
        var characterApi = ReadApiVersion(typeof(ExternalComponentCharacterApi));
        if (componentApi != 3 || packageApi != 3 || runtimeApi != 1 || characterApi != 4)
        {
            Log.Error($"API version mismatch (ComponentApi={Show(componentApi)}, PackageApi={Show(packageApi)}, " +
                      $"RuntimeApi={Show(runtimeApi)}, ExternalCharacterApi={Show(characterApi)}); " +
                      "skip AutoAnthony integration.");
            return false;
        }

        // 卡名目录借用 Ironclad 官方词块（AA 拆词表编译期烘焙，外部不可增删；中文名随后被
        // GeneratedNameBank 覆盖）；效果/外壳目录 = 三国"自写原子目录"。
        var nameCatalog = CharacterComponentCatalogs.Get(GeneratedCharacter.Ironclad);
        var effectCatalog = SelfCatalog.Build();
        Log.Info($"Self catalog: recipes={effectCatalog.Recipes.Count} atoms={effectCatalog.Atoms.Count}");
        if (SelfCatalog.LastFallbacks.Count > 0)
        {
            // 这些原子的卡面措辞退回官方 Ironclad 文案（机制/数值不受影响）。
            // 换 AA 版本后渲染规则变化时会出现，属预期内的降级而非错误。
            Log.Info($"Self catalog text fallbacks ({SelfCatalog.LastFallbacks.Count}): " +
                     string.Join(", ", SelfCatalog.LastFallbacks));
        }

        var request = new ComponentProfileRequest(ProfileId, GeneratedCharacter.Ironclad, false);
        var profile = new ComponentGenerationProfile(
            ProfileId + ":normal",
            GeneratedCharacter.Ironclad,
            false,
            effectCatalog,
            effectCatalog,
            nameCatalog,
            () => ComponentApi.CreateNativeOccurrencePolicy(effectCatalog, false),
            ComponentApi.DefaultValuePolicy,
            new ComponentKeywordPolicy(
                AllowedBaseKeywords: new HashSet<ChaosCardGenerator.CardTag>(),
                AllowedUpgradeAdditions: new HashSet<ChaosCardGenerator.CardTag>(),
                AllowedUpgradeRemovals: new HashSet<ChaosCardGenerator.CardTag>(),
                GlobalUpgradeAdditions: new HashSet<ChaosCardGenerator.CardTag>(),
                GlobalUpgradeRemovals: new HashSet<ChaosCardGenerator.CardTag>(),
                UseArchetypeUpgradeDefaults: false,
                AllowedCustomBaseKeywords: new HashSet<string>(),
                AllowedCustomUpgradeAdditions: new HashSet<string>(),
                AllowedCustomUpgradeRemovals: new HashSet<string>(),
                GlobalCustomUpgradeAdditions: new HashSet<string>(),
                GlobalCustomUpgradeRemovals: new HashSet<string>()));

        ComponentPackageApi.Register(new ComponentPackageRegistration(
            ComponentPackageId,
            request,
            profile,
            IncludeInUltimateChaos: false, // 不进究极混沌加权池
            Localizations: []));           // 自写原子自带 LocalizedText，无需额外本地化条目

        // 外部角色身份注册必须在第一次 InstallDefinitions 之前完成（注册表随后冻结）。
        ExternalComponentCharacterApi.Register(new ExternalComponentCharacterRegistration(
            ProfileId, GeneratedCharacter.Ironclad, "newsanguo"));

        // 运行期宿主：让持续能力/跨卡池事件能重建本角色具体槽卡模型。
        ExternalComponentCharacterApi.RegisterRuntime(new ExternalComponentCharacterRuntimeRegistration(
            ProfileId,
            SlotTable.Count,
            SlotTable.TypeForSlot,
            () => _runActive,
            () => ModelDb.CardPool<NewsanguoCardPool>()));

        // 三国"执行词"运行时路由（须在路由表冻结前）。
        RuntimeRoutes.EnsureRegistered();

        _aaReady = true;
        Log.Info($"AutoAnthony registered (profile={ProfileId}, archetype=Ironclad, slots={SlotTable.Count}).");
        return true;
    }

    private static void SubscribeLifecycle()
    {
        RitsuLibFramework.SubscribeLifecycle<ModelIdsInitializedEvent>(_ => OnModelDbReady(), false);
        RitsuLibFramework.SubscribeLifecycle<RunStartedEvent>(evt =>
        {
            _runActive = true;
            OnRunStarted(evt.RunState);
        }, false);
        RitsuLibFramework.SubscribeLifecycle<RunLoadedEvent>(evt =>
        {
            _runActive = true;
            OnRunStarted(evt.RunState);
        }, false);
        RitsuLibFramework.SubscribeLifecycle<RunEndedEvent>(_ =>
        {
            _runActive = false;
            RestoreLibraryDefaultsAfterRun();
        }, false);
    }

    // ------------------------------------------------------------------ 卡池重建（整池接管）

    /// <summary>
    /// 在 <see cref="CardPoolModel.AllCards"/> 取值器的 Postfix 里重建 newsanguo 卡池成员表：
    /// 保留手工先古（AA 无法生成该稀有度）+ 全部 96 个 AA 槽卡，其余手工卡退出。
    /// 首次调用时捕获原始成员供立绘收集使用。
    /// </summary>
    internal static IEnumerable<CardModel> RebuildPoolContents(IEnumerable<CardModel> original)
    {
        // 接管前的原始成员：先古保留与立绘收集都依赖它（接管生效后池里就取不到了）。
        _originalNewsanguoMembers ??= original.ToArray();

        // 成员表本身是静态的（96 个槽类型 + 固定先古），算一次缓存即可 ——
        // AllCards 是热路径，不能每次访问都重新分配。
        return _takeoverContents ??= BuildTakeoverContents();
    }

    private static CardModel[] BuildTakeoverContents()
    {
        // 规则而非硬编码：只按稀有度保留先古，newsanguo 以后增删卡都不会漏隐藏/误隐藏。
        var keptAncients = (_originalNewsanguoMembers ?? [])
            .Where(card => card is not null && card.Rarity == CardRarity.Ancient);
        var slots = SlotTable.Types.Select(type => ModelDb.GetById<CardModel>(ModelDb.GetId(type)));
        return slots.Concat(keptAncients).ToArray();
    }

    // ------------------------------------------------------------------ 定义安装

    private static void OnModelDbReady()
    {
        if (!CanInstallDefinitions) return;
        try
        {
            _modelDbReady = true;
            // 主动触发一次卡池枚举，确保"接管前成员"已捕获，再收集全量手工卡立绘
            // （此前安装的库预览若在 ModelDb 就绪前，用的是 6 张回退图）。
            EnsurePortraitSource();
            RefreshPortraitPool();
            _libraryInstalled = false;
            TryEnsureLibraryDefaults();
        }
        catch (Exception e)
        {
            Log.Error("OnModelDbReady FAILED: " + e);
        }
    }

    private static void TryEnsureLibraryDefaults()
    {
        if (_libraryInstalled || !CanInstallDefinitions) return;
        if (TryInstallDefinitions(LibrarySeed))
            _libraryInstalled = true;
    }

    /// <summary>局结束回主菜单：把覆盖安装的局内定义换回"库预览"默认，让图鉴/选人界面内容稳定。</summary>
    private static void RestoreLibraryDefaultsAfterRun()
    {
        if (!CanInstallDefinitions) return;
        try
        {
            RefreshPortraitPool();
            _libraryInstalled = false;
            TryEnsureLibraryDefaults();
        }
        catch
        {
            // 恢复失败保持原定义亦可解析，不阻断
        }
    }

    /// <summary>每局开始/读档：用局种子生成全量卡并安装定义，让本局可获取池真正全是生成卡。</summary>
    private static void OnRunStarted(RunState? runState)
    {
        if (!CanInstallDefinitions) return;
        try
        {
            // 局开始时卡池必然已被枚举；此刻再补一次，保证**第一局**就用上全量图池而不是回退图。
            EnsurePortraitSource();
            RefreshPortraitPool();

            // StringSeed 同局（含存档重载/多人两端）一致 → 生成卡确定且可复现。
            var seed = runState?.Rng?.StringSeed ?? Guid.NewGuid().ToString("N");
            if (TryInstallDefinitions("run|" + seed))
            {
                _libraryInstalled = false;
                // 起始牌在 RunState 建立前就已被克隆（携带克隆时的旧缓存），必须连同本局牌组实例一起清缓存。
                ResetCanonicalCardCaches(runState);
                VerifySlotsInPool();
            }
        }
        catch (Exception e)
        {
            Log.Error("Run-start install FAILED: " + e);
        }
    }

    /// <summary>
    /// 用"种子键"确定性生成 SlotCount 张卡并组装成完整 ChaosCardDefinition 列表，整体覆盖安装。
    /// 每批用**单个 RandomCardGenerator 实例**按槽序（稀有度从低到高）连续生成 —— 与 AA 原生整池同一模式，
    /// 卡名/效果签名去重是生成器实例内部状态，只有连续刷完整池才得到池级唯一。
    /// </summary>
    private static bool TryInstallDefinitions(string seedKey)
    {
        if (!CanInstallDefinitions) return false;
        try
        {
            var names = PickUniqueCardNames(seedKey);
            var request = new ComponentProfileRequest(ProfileId, GeneratedCharacter.Ironclad, false);
            var lastFailure = "unknown";

            for (var attempt = 0; attempt < MaxInstallAttempts; attempt++)
            {
                var definitions = new List<ChaosCardDefinition>(SlotCount);
                var auditCards = new List<GeneratedCard>(SlotCount);
                var failed = false;
                var generator = new RandomCardGenerator(
                    request, StableSeed(seedKey + "|gen" + attempt), balancedValues: true);

                // 第一阶段：按槽序生成候选卡。Basic 槽承担初始牌组，做观者式加固。
                var candidate = new GeneratedCard[SlotCount];
                var basicCards = new GeneratedCard[BasicSlotCount];
                for (var slot = 0; slot < SlotCount; slot++)
                {
                    var rarity = SlotRarities[slot];
                    GeneratedCard card;
                    try
                    {
                        card = generator.Generate(rarity);
                    }
                    catch (Exception e)
                    {
                        failed = true;
                        lastFailure = $"slot {slot} ({rarity}): {e.Message}";
                        break;
                    }

                    if (slot < BasicSlotCount)
                    {
                        // 加固本身也消费生成器唯一签名，空间不足时 AA 会走 emergency fallback 并可能抛异常，
                        // 因此加固整体也纳入 attempt 重试。
                        try
                        {
                            card = RerollUnacceptableBasic(generator, card);
                            basicCards[slot] = card;
                            if (slot == BasicSlotCount - 1)
                                EnsureStartingDeckCoverage(basicCards, generator);
                            card = basicCards[slot];
                        }
                        catch (Exception e)
                        {
                            failed = true;
                            lastFailure = $"basic hardening slot {slot}: {e.Message}";
                            break;
                        }
                    }
                    candidate[slot] = card;
                }

                if (failed)
                {
                    Log.Info($"Generate attempt {attempt} failed for '{seedKey}': {lastFailure}");
                    continue;
                }

                // 第二阶段：中文卡名覆盖 + 组装定义。
                for (var slot = 0; slot < SlotCount; slot++)
                {
                    var card = candidate[slot] with
                    {
                        Name = new GeneratedCardName(
                            names[slot],
                            candidate[slot].Name?.English ?? "Chaos",
                            candidate[slot].Name?.SourceCardIds ?? [])
                    };
                    auditCards.Add(card);
                    definitions.Add(BuildDefinition(slot, card));
                }

                if (!ComponentPolicy.TryAuditPool(auditCards, out var auditFailure))
                {
                    Log.Info($"Audit attempt {attempt} failed for '{seedKey}': {auditFailure}");
                    continue;
                }

                ExternalComponentCharacterApi.InstallDefinitions(ProfileId, definitions);
                _definitionsInstalled = true;
                ResetCanonicalCardCaches();
                Log.Info($"Installed {definitions.Count} definitions seed='{seedKey}' attempt={attempt}.");
                return true;
            }

            Log.Error($"Install definitions FAILED ('{seedKey}'): all {MaxInstallAttempts} attempts exhausted ({lastFailure})");
            return false;
        }
        catch (Exception e)
        {
            Log.Error($"Install definitions FAILED ('{seedKey}'): {e}");
            return false;
        }
    }

    /// <summary>把一张 GeneratedCard 组装成 ChaosCardDefinition（与 AA 内部 BuildDefinitions 一致）。</summary>
    private static ChaosCardDefinition BuildDefinition(int slot, GeneratedCard card)
    {
        var attack = card.Type == GeneratedCardType.Attack;
        var portraits = PortraitPoolFor(card.Rarity);
        return new ChaosCardDefinition(
            slot,
            card,
            portraits[slot % portraits.Length], // 全量手工卡图按槽序循环（每张都参与，同槽跨局稳定）
            HitFx,
            attack ? "Attack" : "Cast",
            PowerIconPath,
            PowerBigIconPath,
            card.Operations.Select(OperationRuntimeSpecCompiler.RequireStructured).ToArray(),
            card.Upgrade?.Effects.Select(effect => effect.ValueSlotId).ToArray() ?? [],
            PortraitSourceId: null,
            PortraitVariantId: null,
            PortraitVariantPath: null);
    }

    // ------------------------------------------------------------------ 立绘与缓存

    /// <summary>
    /// 按槽卡的稀有度取立绘池。先古牌与普通牌**卡图尺寸不同**，必须分开取：
    /// 普通槽只用普通卡图；先古槽只用先古卡图（本 mod 当前槽位计划里没有先古槽）。
    /// 各自都留了资源尚未收集时的兜底。
    /// </summary>
    private static string[] PortraitPoolFor(GeneratedRarity rarity)
    {
        if (rarity == GeneratedRarity.Ancient)
            return _portraitPoolAncient is { Length: > 0 } ? _portraitPoolAncient : FallbackSlotPortraits;
        return _portraitPoolNormal is { Length: > 0 } ? _portraitPoolNormal : FallbackSlotPortraits;
    }

    /// <summary>
    /// 确保"接管前的原始池成员"已被捕获（立绘就是从这批被隐藏的手工卡上取的）。
    ///
    /// 它只在 <see cref="RebuildPoolContents"/> 里被捕获一次，而卡池成员表是**惰性**的：
    /// ModelDb 初始化阶段未必枚举过它，于是 OnModelDbReady 时源仍是空的，立绘就退回 6 张占位图 ——
    /// 表现为"96 个槽只有 6 种卡面立绘"。这里主动访问一次 AllCards，触发 Postfix 完成捕获。
    /// </summary>
    private static void EnsurePortraitSource()
    {
        if (_originalNewsanguoMembers is { Length: > 0 }) return;
        try
        {
            _ = ModelDb.CardPool<NewsanguoCardPool>().AllCards;
        }
        catch (Exception e)
        {
            Log.Error("Force pool enumeration FAILED: " + e.Message);
        }
    }

    /// <summary>
    /// 收集被接管隐藏的 newsanguo 手工卡立绘（真实存在的 PortraitPath，去重后按序稳定），
    /// 并**按稀有度分成"普通"与"先古"两组**供 <see cref="PortraitPoolFor"/> 分别取用。
    /// 数据源是接管前捕获的原始池成员 —— 接管生效后池里只剩槽卡与先古，不能再从池里取。
    /// </summary>
    private static void RefreshPortraitPool()
    {
        if (_portraitsCollected) return;
        var original = _originalNewsanguoMembers;
        if (original is null or { Length: 0 })
        {
            // 不再静默：静默会让"只剩 6 张占位图"这种问题完全无从察觉。
            Log.Error("Portrait source unavailable (pool not enumerated yet); keeping fallback portraits.");
            return;
        }
        try
        {
            var newsanguoAssembly = typeof(NewsanguoCardPool).Assembly;
            var seenNormal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenAncient = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var normal = new List<string>();
            var ancient = new List<string>();
            var scanned = 0;
            var missing = 0;
            foreach (var card in original)
            {
                if (card is null) continue;
                if (card.GetType().Assembly != newsanguoAssembly) continue; // 只取手工卡，跳过 AA 槽卡
                scanned++;
                var path = card.PortraitPath;
                if (string.IsNullOrWhiteSpace(path)) { missing++; continue; }
                if (!Godot.ResourceLoader.Exists(path, "")) { missing++; continue; }
                if (card.Rarity == CardRarity.Ancient)
                {
                    if (seenAncient.Add(path)) ancient.Add(path);
                }
                else if (seenNormal.Add(path))
                {
                    normal.Add(path);
                }
            }
            if (normal.Count == 0 && ancient.Count == 0)
            {
                Log.Error($"Portrait collection found nothing usable (scanned={scanned}, unusable={missing}); " +
                          "keeping fallback portraits.");
                return; // 资源还没就绪，留待下次再收
            }
            normal.Sort(StringComparer.Ordinal);
            ancient.Sort(StringComparer.Ordinal);
            if (normal.Count > 0) _portraitPoolNormal = normal.ToArray();
            if (ancient.Count > 0) _portraitPoolAncient = ancient.ToArray();
            _portraitsCollected = true;
            Log.Info($"Collected portraits: normal={normal.Count}, ancient={ancient.Count}, " +
                     $"scanned={scanned}, unusable={missing} " +
                     "(先古图尺寸不同，单独存放，不参与普通槽循环).");
        }
        catch (Exception e)
        {
            Log.Error("Collect card portraits FAILED: " + e.Message);
        }
    }

    /// <summary>
    /// 清理槽卡 CardModel 的 Canonical 缓存（AA 槽卡首次被读取时会把**当时定义**派生的数值缓存进
    /// CardModel 私有字段；换定义不清缓存会让卡面数字位显示 {damage0:diff} 一类原始标记）。
    /// 除 ModelDb 里的根模型，还必须清理本局已克隆进牌组的实例（开局克隆早于本次定义安装）。
    /// </summary>
    private static void ResetCanonicalCardCaches(RunState? runState = null)
    {
        if (!_modelDbReady) return;
        try
        {            FieldInfo?[] cacheFields =
            [
                AccessTools.Field(typeof(CardModel), "_dynamicVars"),
                AccessTools.Field(typeof(CardModel), "_energyCost"),
                AccessTools.Field(typeof(CardModel), "_keywords"),
                AccessTools.Field(typeof(CardModel), "_tags"),
                AccessTools.Field(typeof(CardModel), "_baseStarCost"),
                AccessTools.Field(typeof(CardModel), "_starCostSet")
            ];

            var cleared = 0;
            var preserved = 0;
            var slotTypeSet = new HashSet<Type>(SlotTable.Types);
            foreach (var slotType in SlotTable.Types)
            {
                try
                {
                    var model = ModelDb.GetById<CardModel>(ModelDb.GetId(slotType));
                    if (model is null) continue;
                    // 已升级的牌不能清缓存：清掉后卡牌会按**未升级的定义**重新推导，
                    // 升级效果（减费改 _energyCost、加伤改 _dynamicVars）就全没了。
                    if (IsUpgradedCard(model)) { preserved++; continue; }
                    foreach (var field in cacheFields)
                    {
                        if (field is null) continue;
                        field.SetValue(model, null);
                        cleared++;
                    }
                }
                catch
                {
                    // 个别槽解析失败不影响其余；留待下次安装再清
                }
            }

            if (runState?.Players is { } players)
            {
                foreach (var player in players)
                {
                    try
                    {
                        foreach (var card in player.Deck.Cards)
                        {
                            if (card is null || !slotTypeSet.Contains(card.GetType())) continue;
                            if (IsUpgradedCard(card)) { preserved++; continue; }
                            foreach (var field in cacheFields)
                            {
                                if (field is null) continue;
                                field.SetValue(card, null);
                                cleared++;
                            }
                        }
                    }
                    catch
                    {
                        // 单玩家解析失败不影响其余
                    }
                }
            }

            Log.Info($"Reset canonical caches ({cleared} field writes over {SlotTable.Count} slot models; " +
                     $"{preserved} 张已升级卡被跳过).");
        }
        catch (Exception e)
        {
            Log.Error("Reset canonical caches FAILED: " + e.Message);
        }
    }

    private static bool _upgradeProbeLogged;

    /// <summary>
    /// 判断一张牌是否已升级。
    ///
    /// **为什么必须判断**：本方法要清的字段（<c>_energyCost</c>、<c>_dynamicVars</c>…）正是升级会改的东西。
    /// 清掉后卡牌会按"**未升级的定义**"重新推导，于是升级效果消失 ——
    /// 实测症状：升级效果是减费的牌，SL 一次后减费没了（<c>RunLoadedEvent</c> 也会走这条路径）。
    /// 同一个原因还会吞掉加伤/加格挡类升级，只是不如减费显眼。
    ///
    /// 读不到升级状态时**返回 true**（保守跳过清理）：宁可少清一次显示缓存（代价只是卡面可能出现
    /// <c>{damage0:diff}</c> 这类原始标记），也绝不能吞掉玩家花资源做的升级。
    /// </summary>
    private static bool IsUpgradedCard(CardModel card)
    {
        try
        {
            var type = card.GetType();
            var prop = AccessTools.Property(type, "IsUpgraded")
                       ?? AccessTools.Property(typeof(CardModel), "IsUpgraded");
            if (prop?.GetValue(card) is bool byProp) return byProp;

            foreach (var name in new[] { "_isUpgraded", "_upgraded", "isUpgraded" })
            {
                var field = AccessTools.Field(type, name) ?? AccessTools.Field(typeof(CardModel), name);
                if (field?.GetValue(card) is bool byField) return byField;
            }
        }
        catch
        {
            // 落到下面的保守分支
        }

        if (!_upgradeProbeLogged)
        {
            _upgradeProbeLogged = true;
            Log.Error("IsUpgraded 读取失败：已按'已升级'保守处理（槽卡缓存不再清理）。" +
                      "若卡面出现 {damage0:diff} 一类原始标记，说明需要改用正确的升级判定字段。");
        }
        return true;
    }

    /// <summary>自检：确认全部槽卡已出现在 NewsanguoCardPool.AllCards（真实"可获取"来源）里。</summary>
    private static void VerifySlotsInPool()
    {
        try
        {
            var pool = ModelDb.CardPool<NewsanguoCardPool>();
            var cards = pool.AllCards?.ToArray() ?? [];
            var present = cards.Count(c => Array.IndexOf(SlotTable.Types, c.GetType()) >= 0);
            Log.Info($"Pool verification: AllCards total={cards.Length}; AA slots present={present}/{SlotTable.Count}.");
        }
        catch (Exception e)
        {
            Log.Error("Pool verification FAILED: " + e.Message);
        }
    }

    // ------------------------------------------------------------------ 初始牌组

    /// <summary>
    /// 构造替换牌组：Basic 槽 0..9 各按 BasicSlotDeckCounts 份数重复（合计 10 张）。
    /// 构造失败（如 ModelDb 未就绪）返回 null 让调用方回退原逻辑。
    /// </summary>
    internal static IEnumerable<CardModel>? TryBuildReplacementStartingDeck()
    {
        if (!IsTakeoverActive) return null;
        if (BasicSlotDeckCounts.Length != BasicSlotCount) return null;
        try
        {
            var deck = new List<CardModel>(BasicSlotCount);
            for (var slot = 0; slot < BasicSlotCount; slot++)
            {
                var type = SlotTable.Types[slot];
                var card = ModelDb.GetById<CardModel>(ModelDb.GetId(type));
                var count = BasicSlotDeckCounts[slot];
                for (var c = 0; c < count; c++)
                    deck.Add(card);
            }
            return deck;
        }
        catch (Exception e)
        {
            Log.Error("Build replacement starting deck FAILED: " + e.Message);
            return null;
        }
    }

    // ------------------------------------------------------------------ 生成辅助

    private static GeneratedRarity[] BuildSlotRarityPlan()
    {
        var values = new List<GeneratedRarity>(SlotCount);
        for (var i = 0; i < BasicSlotCount; i++) values.Add(GeneratedRarity.Basic);
        for (var i = 0; i < CommonSlotCount; i++) values.Add(GeneratedRarity.Common);
        for (var i = 0; i < UncommonSlotCount; i++) values.Add(GeneratedRarity.Uncommon);
        for (var i = 0; i < RareSlotCount; i++) values.Add(GeneratedRarity.Rare);
        return values.ToArray();
    }

    /// <summary>从词块组合表无放回抽取 SlotCount 个唯一中文卡名（跨槽不重名，同 seedKey 可复现）。</summary>
    private static string[] PickUniqueCardNames(string seedKey)
    {
        var prefixes = GeneratedNameBank.Prefixes;
        var suffixes = GeneratedNameBank.Suffixes;
        var total = prefixes.Length * suffixes.Length;
        if (total < SlotCount)
            throw new InvalidOperationException($"Name bank too small ({total}) for {SlotCount} slots.");

        var rng = new Random(StableSeed(seedKey + "|names"));
        var used = new HashSet<int>();
        var names = new string[SlotCount];
        for (var i = 0; i < SlotCount; i++)
        {
            var idx = -1;
            for (var t = 0; t < 300 && idx < 0; t++)
            {
                var candidate = rng.Next(total);
                if (used.Add(candidate)) idx = candidate;
            }
            if (idx < 0)
            {
                for (var j = 0; j < total && idx < 0; j++)
                    if (used.Add(j)) idx = j;
            }
            if (idx < 0)
                throw new InvalidOperationException("Name bank exhausted while sampling unique names.");
            names[i] = prefixes[idx / suffixes.Length] + suffixes[idx % suffixes.Length];
        }
        return names;
    }

    // ---- 基础卡（初始牌组）加固 ----

    private static GeneratedCard RerollUnacceptableBasic(RandomCardGenerator generator, GeneratedCard card)
    {
        for (var attempt = 0; attempt < BasicRerollMaxAttempts && IsUnacceptableBasic(card); attempt++)
        {
            GeneratedCard next;
            try
            {
                next = generator.GenerateWithoutSpecialX(GeneratedRarity.Basic);
            }
            catch (Exception e)
            {
                Log.Info($"Basic reroll attempt {attempt} failed; keep current card: {e.Message}");
                break;
            }
            card = next;
        }
        return card;
    }

    /// <summary>判定"不可作为初始牌"的基础卡：① 带回合结束负面变体且打印收益过低；② 直接伤害/格挡单值 1~2。</summary>
    private static bool IsUnacceptableBasic(GeneratedCard card)
    {
        if (card.Operations.Any(op => op.RuntimeSpec?.Variant is not null &&
                RunEndingNegativeVariants.Contains(op.RuntimeSpec.Variant)) &&
            PrintedPayoff(card) < RunEndingNegativePayoffFloor)
        {
            return true;
        }

        foreach (var operation in card.Operations)
        {
            var slot = operation.RuntimeSpec?.Opcode switch
            {
                "deal_damage" => "damage",
                "gain_block" => "block",
                _ => null
            };
            if (slot is null) continue;
            var value = OperationRuntimeSpecCompiler.StaticLiteralValue(operation, slot, 0);
            if (value > 0 && value < 3) return true;
        }
        return false;
    }

    /// <summary>打印收益粗估：伤害全额、格挡 1.2 倍、抽牌 4.6 倍、能量 6.5 倍（观者 mod 同款权重）。</summary>
    private static int PrintedPayoff(GeneratedCard card)
    {
        var total = 0;
        foreach (var operation in card.Operations)
        {
            switch (operation.RuntimeSpec?.Opcode)
            {
                case "deal_damage":
                {
                    var hits = Math.Max(1, OperationRuntimeSpecCompiler.StaticLiteralValue(operation, "hits", 0));
                    total += OperationRuntimeSpecCompiler.StaticLiteralValue(operation, "damage", 0) * hits;
                    break;
                }
                case "gain_block":
                    total += OperationRuntimeSpecCompiler.StaticLiteralValue(operation, "block", 0) * 12 / 10;
                    break;
                case "draw_cards":
                    total += OperationRuntimeSpecCompiler.StaticLiteralValue(operation, "draw", 0) * 46 / 10;
                    break;
                case "gain_energy":
                    total += OperationRuntimeSpecCompiler.StaticLiteralValue(operation, "energy", 0) * 65 / 10;
                    break;
            }
        }
        return total;
    }

    private static int SumOpcode(GeneratedCard card, string opcode, string slotId) =>
        card.Operations.Where(op => op.RuntimeSpec?.Opcode == opcode)
            .Sum(op => Math.Max(0, OperationRuntimeSpecCompiler.StaticLiteralValue(op, slotId, 0)));

    private static int WeightedBasicStat(GeneratedCard[] basicCards, string opcode, string slotId)
    {
        var total = 0;
        for (var s = 0; s < basicCards.Length; s++)
            total += BasicSlotDeckCounts[s] * SumOpcode(basicCards[s], opcode, slotId);
        return total;
    }

    /// <summary>保证 10 张叠出的开局牌组合计 伤害≥22 / 格挡≥20；不达标时重掷最弱基础槽。</summary>
    private static void EnsureStartingDeckCoverage(GeneratedCard[] basicCards, RandomCardGenerator generator)
    {
        var rerollFailures = 0;
        for (var pass = 0; pass < BasicCoverageMaxPasses; pass++)
        {
            var damage = WeightedBasicStat(basicCards, "deal_damage", "damage");
            var block = WeightedBasicStat(basicCards, "gain_block", "block");
            if (damage >= StartingDeckDamageFloor && block >= StartingDeckBlockFloor)
            {
                Log.Info($"Starting-deck coverage OK: damage={damage} block={block} (pass {pass}).");
                return;
            }

            var needDamage = damage < StartingDeckDamageFloor;
            var weakest = FindWeakestBasicSlot(basicCards, needDamage);
            try
            {
                basicCards[weakest] = RerollUnacceptableBasic(
                    generator, generator.GenerateWithoutSpecialX(GeneratedRarity.Basic));
            }
            catch (Exception e)
            {
                rerollFailures++;
                Log.Info($"Starting-deck coverage reroll (pass {pass}) failed: {e.Message}");
                if (rerollFailures >= 8)
                {
                    Log.Info("Starting-deck coverage: too many reroll failures; accept unmet coverage.");
                    break;
                }
            }
        }

        Log.Info($"Starting-deck coverage settled: damage={WeightedBasicStat(basicCards, "deal_damage", "damage")} " +
                 $"block={WeightedBasicStat(basicCards, "gain_block", "block")} " +
                 $"(targets >={StartingDeckDamageFloor}/{StartingDeckBlockFloor}).");
    }

    private static int FindWeakestBasicSlot(GeneratedCard[] basicCards, bool needDamage)
    {
        var result = 0;
        var minValue = int.MaxValue;
        for (var i = 0; i < basicCards.Length; i++)
        {
            var value = BasicSlotDeckCounts[i] *
                (needDamage ? SumOpcode(basicCards[i], "deal_damage", "damage")
                            : SumOpcode(basicCards[i], "gain_block", "block"));
            if (value < minValue)
            {
                minValue = value;
                result = i;
            }
        }
        return result;
    }

    // ------------------------------------------------------------------ 杂项

    /// <summary>运行时反射读取 ApiVersion 常量（编译期直接比较会被常量折叠而失效）。</summary>
    private static int? ReadApiVersion(Type apiType)
    {
        try
        {
            return apiType.GetField("ApiVersion", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is int v
                ? v
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Show(int? value) => value?.ToString() ?? "?";

    /// <summary>AA 文档要求：种子必须用 SHA-256 等稳定算法，禁止 GetHashCode。</summary>
    private static int StableSeed(string seed) =>
        BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(ProfileId + "|" + seed)), 0);
}
