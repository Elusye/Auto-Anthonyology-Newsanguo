using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using AutoAnthony;
using ChaosCardGenerator;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib;
using STS2RitsuLib.Content;
using newsanguo.Scripts.Characters;
using newsanguo.Scripts.Patches;

namespace newsanguo.Scripts.AutoAnthony;

/// <summary>
/// AutoAnthony（东尼算法）可选集成 —— “整池接管 + 替换初始牌组”：
/// AutoAnthony 启用时，newsanguo 牌池里除 2 张先古外的全部手工卡（88 张 = 4 初始 Basic +
/// 20 普 + 38 罕 + 26 稀）从一切获取/图鉴入口隐藏，池内奖励/商店/事件/发现等“可获得内容”
/// 全部换成 AA 生成的卡；初始牌组复刻观者方案替换为 AA 生成的基础卡（10 个 Basic 槽，
/// 每槽 1 张 → 开局 10 张独一无二的 AA 卡）；仅 2 张先古 Ancient（the_truest_mask/divine_insight）
/// 手工保留（AA 无法生成该稀有度）。
///
/// 工作方式（区别于早期纯校验 PoC / 6 槽演示）：
/// - newsanguo 对 AutoAnthony 保持“可选依赖”：本程序集不写任何继承 AA 基类的编译期类，
///   因此 AA 未订阅时不会触发 TypeLoad/GetTypes 崩溃，所有相关逻辑自动跳过。
/// - 固定槽模型（SlotCount 个 ExternalChaosCardModel 子类，Slot = 0..SlotCount-1）在确认
///   AA 已加载后**运行时用 Reflection.Emit 生成**，并经 RitsuLib `ModContentRegistry.RegisterCard`
///   注册为 NewsanguoCardPool 成员。动态类型会在 ModelDb.Init 前缀由 RitsuLib 自动注入
///   `ModelDb._contentById`（`ModHelper.AddModelToPool` 同时建立池成员资格）。
/// - 定义（ChaosCardDefinition）生命周期：
///   1) 尽早（TryRegister 时，AA 已加载后立即）安装“库预览”默认定义；ModelDb.InitIds 之后
///      （NotifyModelDbReady）强制重装一次，保证主菜单/图鉴/Preload 读取池卡属性时槽卡
///      必然可解析（ForSlot 不会抛）且卡图已取到全量手工卡；
///   2) 每局 RunStartedEvent 用局种子（RunState.Rng.StringSeed）重新生成并安装全量 SlotCount 张，
///      使本局战斗奖励/商店与开局牌组等“可获取池”全部是本局生成的卡；同一局重载（StringSeed
///      相同）会得到同一批卡，多人两端同种子也一致；
///   3) 局结束回主菜单恢复“库预览”默认集，保持图鉴/选人界面内容稳定。
/// - 生成：每批用**单个 RandomCardGenerator 实例**按稀有度从低到高连续 Generate（与 AA 原生整池
///   92 张/池同一模式，池内去重/预算/审计天然成立），合成后跑 ComponentPolicy.TryAuditPool；
///   失败整批换 attempt 偏移种子重试，保证可复现且极少失败。中文卡名用 NewsanguoGeneratedNameBank
///   词块按槽种子无放回抽取 SlotCount 个唯一组合覆盖（AA 官方词块只保证英文名唯一）。
/// - 前 10 个 Basic 槽承担“初始牌组”职责，复刻观者 mod 的基础卡加固：
///   IsUnacceptableBasic 重掷（排除“回合结束负面”低收益/单值过小的卡）+ EnsureStartingDeckCoverage
///   保证 10 张开局牌合计 伤害≥22 / 格挡≥20；初始牌组整体替换经本类安装的 Harmony Prefix
///   （StartingDeckPrefix）完成，绕过 RitsuLib 的手工注册初始卡。
/// - 每次 InstallDefinitions 后清理槽卡的 Canonical 缓存（_dynamicVars/_energyCost/_keywords/_tags，
///   同“东尼算法：观者”的 ResetCanonicalCardCaches）：AA 槽卡首次被读取时会把当时定义派生的
///   数值缓存进 CardModel 私有字段，不清缓存会导致换定义后卡面数字位仍显示 {damage0:diff} 一类
///   原始标记。
/// - 效果/外壳目录 = 三国"自写原子目录"（NewsanguoSelfCatalog.Build，官方数值基线 + 自写中英文
///   文案 + 自编壳）：数值槽沿用官方已验证结构保预算/升级/执行，身份/文案/壳自写归属本 mod；
///   卡名目录仍借用官方词块（AA 拆词表无外部入口）。
/// - “隐藏手工卡”在 NewsanguoCardPool.FilterThroughEpochs（获取链路）+ NCardLibraryGrid 图鉴补丁
///   （CardLibraryTakeoverPatch）两处实现；开局牌组由 StartingDeckPrefix 替换为 AA 基础槽。
/// - 卡图：AA 槽不固定复用 6 张图，而是收集本 mod 全部非衍生卡（NewsanguoCardPool 中属于本程序集、
///   非 Token 池的卡）的真实 PortraitPath，按槽序循环使用（ModelDb 就绪前回退原 6 张占位）。
/// - 所有 AA 调用都包 try/catch 且调用点守卫 IsAutoAnthonyLoaded()：AA 缺失/版本不符时
///   绝不打断 mod 本体（含 AA 类型引用的方法体必须只被“已确认加载”的调用点触发 JIT）。
/// </summary>
internal static class NewsanguoAutoAnthonyAdapter
{
    // ProfileId 是外部身份；Ironclad 是最接近的平衡原型（仅作卡名词块与生成器 archetype 来源）。
    internal const string ProfileId = "newsanguo:autoanthony";
    private const string PackageId = "newsanguo:autoanthony:components";

    // 槽总数与稀有度计划：整池接管 = 手工可随机获得卡全部被 AA 卡替换。
    // 统计自 [RegisterCard(typeof(NewsanguoCardPool))] 真实池成员（去重 90 张，不含 Token）：
    // Basic 10 / 普 20 / 罕 38 / 稀 26 = 94 张由 AA 生成；仅剩 2 张先古 Ancient 手工保留。
    // 奖励层(Common/Uncommon/Rare)数量与手工卡统计一致（20/38/26）；Basic 用 10 张是为了
    // 复刻观者方案：每局初始牌 = 10 张独一无二的 AA 基础卡（每 Basic 槽 1 张）。
    // 槽按稀有度从低到高排布（0..9 Basic、10..29 普、30..67 罕、68..93 稀），与
    // “单生成器从低到高连续生成”对齐。
    // 注意：不能把 GeneratedRarity[] 存成静态字段——该枚举是 AA 类型，静态构造在 AutoAnthony
    // 尚未加载时就会因解析该类型而 FileNotFound（本类 .cctor 炸掉）。这里存底层 int，
    // 用前在已确认 AA 加载的代码路径里再强转（GeneratedRarity.Basic/Common/Uncommon/Rare/Ancient = 0..4）。
    private const int BasicSlotCount = 10;
    private const int CommonSlotCount = 20;
    private const int UncommonSlotCount = 38;
    private const int RareSlotCount = 26;
    private const int SlotCount = BasicSlotCount + CommonSlotCount + UncommonSlotCount + RareSlotCount; // 94
    private const int RarityBasicValue = 0;
    private const int RarityCommonValue = 1;
    private const int RarityUncommonValue = 2;
    private const int RarityRareValue = 3;
    private static readonly int[] SlotRarityValues =
        BuildSlotRarityPlan(BasicSlotCount, CommonSlotCount, UncommonSlotCount, RareSlotCount);

    // 替换初始牌组时每张 AA 基础槽的份数：10 个 Basic 槽各 1 张 → 开局 10 张独一无二的卡
    // （与“东尼算法：观者” mod 一致：Basic 槽 = 初始牌，1 槽 1 牌）。
    // 此数组长度必须 == BasicSlotCount。
    internal static readonly int[] BasicSlotDeckCounts = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1];

    // 基础卡加固阈值（移植自“东尼算法：观者” mod）：按份数叠出的 10 张开局牌组合计下限。
    private static readonly HashSet<string> RunEndingNegativeVariants =
        new(StringComparer.Ordinal) { "w_dienextturn", "w_energydowneachturn", "w_playableonlyassoleattack" };
    private const int RunEndingNegativePayoffFloor = 20;
    private const int StartingDeckDamageFloor = 22;
    private const int StartingDeckBlockFloor = 20;
    private const int BasicRerollMaxAttempts = 24;
    private const int BasicCoverageMaxPasses = 64;

    // 槽卡立绘来源：
    // - ModelDb 就绪后（RefreshPortraitPool）收集 NewsanguoCardPool 里全部本程序集非衍生卡的真实
    //   PortraitPath（即整池接管中被隐藏的那批手工卡 ≈90 张，含 2 张先古 Ancient），按槽序取模循环
    //   → 94 个槽不再固定复用 6 张，凡非衍生卡图都参与，且同槽位跨局立绘稳定。
    // - ModelDb 未就绪（AA 极早加载、主菜单 Preload 前的库预览安装）时回退下面 6 张占位图，
    //   待 ModelIdsInitializedEvent（NotifyModelDbReady）刷新后重装一次库预览换用全量图池。
    private static readonly string[] FallbackSlotPortraits =
    [
        "res://newsanguo/images/cards/wine_the_old_hero.png",
        "res://newsanguo/images/cards/cross_for_cross.png",
        "res://newsanguo/images/cards/quad_blast.png",
        "res://newsanguo/images/cards/medical_mastery.png",
        "res://newsanguo/images/cards/empower.png",
        "res://newsanguo/images/cards/wine_cut.png"
    ];

    private static string[]? _portraitPool;

    private const string HitFx = "vfx/vfx_attack_blunt";
    private const string PowerIconPath = "res://images/atlases/power_atlas.sprites/strength_power.tres";
    private const string PowerBigIconPath = "res://images/powers/strength_power.png";

    private const string LibrarySeed = "newsanguo:library";
    private const int MaxInstallAttempts = 4;

    private static readonly object Sync = new();
    private static bool _registered;
    private static bool _assemblyHooked;
    private static bool _externalRegistered;
    private static Type[]? _slotTypes;
    private static bool _libraryInstalled;
    private static bool _startingDeckPatched;
    private static bool _modelDbReady;

    /// <summary>整池接管是否激活（AA 已加载、注册成功且槽模型已布入池）。用于池过滤/图鉴补丁的开关。</summary>
    internal static bool IsTakeoverActive => _registered && _slotTypes is { Length: SlotCount } && IsAutoAnthonyLoaded();

    /// <summary>由 Entry.Init 调用一次：尽量尽早完成注册并订阅必要的兜底时机。</summary>
    internal static void Startup()
    {
        try
        {
            if (!_assemblyHooked)
            {
                _assemblyHooked = true;
                AppDomain.CurrentDomain.AssemblyLoad += (_, e) =>
                {
                    try
                    {
                        if (string.Equals(e.LoadedAssembly.GetName().Name, "AutoAnthony", StringComparison.Ordinal))
                            TryRegister();
                    }
                    catch
                    {
                        // 仅注册兜底，失败可忽略
                    }
                };
                RitsuLibFramework.SubscribeLifecycle((IFrameworkLifecycleEvent evt) =>
                {
                    try
                    {
                        // ModelDb.InitIds 完成：动态槽模型此刻已在 ModelDb 可解析。
                        // 若启动早期“库预览”安装失败（AA 就绪偏晚等），此处兜底补装，保证主菜单/图鉴
                        // Preload 读取池卡属性时槽卡必然可解析（ForSlot 不抛）。
                        if (evt is ModelIdsInitializedEvent)
                        {
                            NotifyModelDbReady();
                        }
                        // 新局或存档续局：用局种子覆盖安装定义（同局两端/重载同种子 → 同一批卡）。
                        // 注意：必须先确认已加载再调用，含 AA 类型引用的方法体在 JIT 时会整体解析类型，
                        // 未加载就调用会在执行 IL 前抛 FileNotFoundException。
                        else if (evt is RunStartedEvent started && IsAutoAnthonyLoaded())
                        {
                            TryRegister();
                            OnRunStarted(started.RunState);
                        }
                        else if (evt is RunLoadedEvent loaded && IsAutoAnthonyLoaded())
                        {
                            TryRegister();
                            OnRunStarted(loaded.RunState);
                        }
                        // 局结束回主菜单：恢复“库预览”默认定义，保持图鉴/选人界面稳定。
                        else if (evt is RunEndedEvent)
                        {
                            RestoreLibraryDefaultsAfterRun();
                        }
                    }
                    catch
                    {
                        // 生命周期回调内禁止外抛
                    }
                }, replayCurrentState: false);
            }
            // 同样必须在确认已加载后才调用；未订阅 AutoAnthony 时保持静默跳过（由 AssemblyLoad 钩子兜底）。
            if (IsAutoAnthonyLoaded())
                TryRegister();
        }
        catch (Exception e)
        {
            Log($"Startup failed: {e}");
        }
    }

    /// <summary>AutoAnthony 已加载且 API 版本匹配时注册组件包 + 外部角色 + 固定槽模型；幂等。</summary>
    private static void TryRegister()
    {
        if (_registered) return;
        if (!IsAutoAnthonyLoaded()) return;

        lock (Sync)
        {
            if (_registered) return;
            try
            {
                // ApiVersion 是编译期常量（const），直接比较会被常量折叠而失效；
                // 这里在运行时反射读取，确保与当前加载的 AutoAnthony 真实版本核对。
                var apiVersion = ReadApiVersion(typeof(ComponentApi));
                var packageApiVersion = ReadApiVersion(typeof(ComponentPackageApi));
                if (apiVersion != 3 || packageApiVersion != 3)
                {
                    Log($"API version mismatch (ComponentApi={apiVersion?.ToString() ?? "?"}, " +
                        $"PackageApi={packageApiVersion?.ToString() ?? "?"}); skip AutoAnthony integration.");
                    _registered = true; // 不重试
                    return;
                }

                // 卡名目录借用 Ironclad 官方词块（AA 拆词表编译期烘焙，外部不可增删；中文名随后被
                // NewsanguoGeneratedNameBank 覆盖）；效果/外壳目录 = 三国"自写原子目录"
                // （NewsanguoSelfCatalog：官方数值基线 + 自写中英文文案 + 自编壳，经离线 harness
                // 94 槽多种子验证），语义身份/文案/壳全部归属本 mod，仅数值槽沿用官方已验证结构。
                var nameCatalog = CharacterComponentCatalogs.Get(GeneratedCharacter.Ironclad);
                var effectCatalog = NewsanguoSelfCatalog.Build();
                Log($"Self catalog: recipes={effectCatalog.Recipes.Count} atoms={effectCatalog.Atoms.Count}");
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
                        AllowedBaseKeywords: new HashSet<CardTag>(),
                        AllowedUpgradeAdditions: new HashSet<CardTag>(),
                        AllowedUpgradeRemovals: new HashSet<CardTag>(),
                        GlobalUpgradeAdditions: new HashSet<CardTag>(),
                        GlobalUpgradeRemovals: new HashSet<CardTag>(),
                        UseArchetypeUpgradeDefaults: false,
                        AllowedCustomBaseKeywords: new HashSet<string>(),
                        AllowedCustomUpgradeAdditions: new HashSet<string>(),
                        AllowedCustomUpgradeRemovals: new HashSet<string>(),
                        GlobalCustomUpgradeAdditions: new HashSet<string>(),
                        GlobalCustomUpgradeRemovals: new HashSet<string>()));

                ComponentPackageApi.Register(new ComponentPackageRegistration(
                    PackageId,
                    request,
                    profile,
                    IncludeInUltimateChaos: false, // 不进究极混沌加权池
                    Localizations: [])); // 克隆原子自带 LocalizedText，无需自定义本地化条目

                // 外部角色身份注册必须在第一次 InstallDefinitions 之前完成（注册表随后冻结）。
                if (!_externalRegistered)
                {
                    ExternalComponentCharacterApi.Register(new ExternalComponentCharacterRegistration(
                        ProfileId, GeneratedCharacter.Ironclad, "newsanguo"));
                    _externalRegistered = true;
                    Log("External character registered (profile=" + ProfileId + ", archetype=Ironclad).");
                }

                // 固定槽模型：运行时 Emit + 注册进 NewsanguoCardPool（须在 ModelDb.Init 冻结前）。
                if (_slotTypes is null)
                    TryEmitAndRegisterPoolSlots();

                if (_slotTypes is { Length: SlotCount })
                {
                    _registered = true;
                    Log("Registered OK. RegisteredPackages=[" +
                        string.Join(",", ComponentPackageApi.RegisteredPackages) + "]");

                    // 三国专属"执行词"运行时路由：把生成卡上内建 executor 不认识的 opcode
                    // （如 ns_gain_wine）接到 mod 现有 PowerModel 实现。须在路由表冻结（首次
                    // 生成卡 op 执行）前注册完成，此处远早于任何战斗。
                    NewsanguoRuntimeRoutes.EnsureRegistered();
                    Log("Runtime routes=" + ComponentRuntimeApi.RegisteredRoutes.Count + ": " +
                        string.Join(",", ComponentRuntimeApi.RegisteredRoutes.Select(r => r.Opcode + "/" + r.Variant)));

                    // 尽早安装库预览默认定义（ModelDb.InitIds 后还会兜底一次）。
                    TryEnsureLibraryDefaults();

                    // 接管激活后整体替换初始牌组（复刻观者：前 BasicSlotCount 个 Basic 槽为开局卡）。
                    // 安装时机需在任何 StartingDeck 查询之前（AA 加载后立即）；幂等。
                    InstallStartingDeckReplacementPatch();
                }
                else
                {
                    Log("Register incomplete: pool slots were not emitted; takeover stays inactive.");
                }
            }
            catch (Exception e)
            {
                Log($"Register FAILED: {e}");
            }
        }
    }

    /// <summary>
    /// RitsuLib 的 ModelIdsInitializedEvent（ModelDb.InitIds Postfix 发布）到达时调用：
    /// 此时槽模型已在 ModelDb 可解析，安装“库预览”默认定义，避免 Preload/主菜单读取池卡属性时
    /// 因未装定义而 ForSlot 抛异常。无 AA 类型引用，安全。
    /// </summary>
    internal static void NotifyModelDbReady()
    {
        if (!_registered || !IsAutoAnthonyLoaded()) return;
        try
        {
            _modelDbReady = true;
            // 收集全量非衍生卡图（此前安装的库预览若发生在 ModelDb 就绪前，用的是 6 张回退图）。
            RefreshPortraitPool();
            // 换用全量图池重装一次库预览（顺带清理 Canonical 缓存），保证图鉴/主菜单槽卡立绘不再是占位图。
            _libraryInstalled = false;
            TryEnsureLibraryDefaults();
        }
        catch
        {
            // 不打断启动
        }
    }

    /// <summary>每局开始：用局种子生成全量卡并安装定义，让本局可获取池（奖励/商店）真正全是生成卡。</summary>
    private static void OnRunStarted(RunState runState)
    {
        if (!_registered || !IsAutoAnthonyLoaded()) return;
        if (_slotTypes is not { Length: SlotCount })
        {
            Log("Run started but slot models are not registered; skip definition install.");
            return;
        }

        try
        {
            // StringSeed 同局（含存档重载/多人两端）一致 → 生成卡确定且可复现。
            var seed = runState?.Rng?.StringSeed ?? Guid.NewGuid().ToString("N");
            if (TryInstallDefinitions("run|" + seed))
            {
                _libraryInstalled = false; // 主菜单默认集已被局内定义覆盖，无需再补
                // 起始牌在 RunState 建立前就已被克隆（携带克隆时的旧缓存），必须连同本局
                // 牌组实例一起清缓存，否则开局首战卡面仍按旧定义变量名渲染。
                ResetCanonicalCardCaches(runState);
                VerifySlotsInPool();
            }
        }
        catch (Exception e)
        {
            Log($"Run-start install FAILED: {e}");
        }
    }

    /// <summary>
    /// 自检：确认全部槽卡模型已出现在 NewsanguoCardPool.AllCards（真实“可获取”来源）里。
    /// 仅访问核心 ModelDb/CardPoolModel（AA 已加载才被调用），异常只记日志不阻断。
    /// </summary>
    private static void VerifySlotsInPool()
    {
        if (_slotTypes is null) return;
        try
        {
            var pool = ModelDb.CardPool<NewsanguoCardPool>();
            var cards = pool.AllCards?.ToArray() ?? [];
            var present = new List<string>();
            foreach (var c in cards)
            {
                if (Array.IndexOf(_slotTypes, c.GetType()) >= 0)
                    present.Add($"{c.GetType().Name}#{c.Id}");
            }
            Log($"Pool verification: NewsanguoCardPool.AllCards total={cards.Length}; AA slots in pool={present.Count}: {string.Join(", ", present)}");
        }
        catch (Exception e)
        {
            Log($"Pool verification FAILED: {e.Message}");
        }
    }

    /// <summary>
    /// 槽卡立绘池：优先用 ModelDb 就绪后收集的本 mod 非衍生卡图全量集合，未就绪时回退 6 张占位图。
    /// </summary>
    private static string[] PortraitPool =>
        _portraitPool is { Length: > 0 } ? _portraitPool : FallbackSlotPortraits;

    /// <summary>
    /// 收集卡图：遍历 NewsanguoCardPool.AllCards，取属于本程序集（手工卡，排除 AA 运行时 Emit 的
    /// 动态槽模型——它们在独立程序集里）且资源确实存在的 PortraitPath。本池手工卡无 Token（衍生卡
    /// 一律注册在 ColorlessCardPool 的 Token 项里，天然不在本池），故收集结果即“非衍生卡图全量”。
    /// 去重后按类名排序保证跨局稳定；已收集过则跳过（幂等）。仅核心 ModelDb/Godot API，无 AA 引用。
    /// </summary>
    private static void RefreshPortraitPool()
    {
        if (_portraitPool is { Length: > 0 }) return;
        try
        {
            var pool = ModelDb.CardPool<NewsanguoCardPool>();
            var cards = pool.AllCards ?? [];
            var assembly = typeof(NewsanguoCardPool).Assembly;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = new List<string>();
            foreach (var card in cards)
            {
                if (card is null) continue;
                if (card.GetType().Assembly != assembly) continue; // 跳过 AA 动态槽
                var path = card.PortraitPath;
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (!Godot.ResourceLoader.Exists(path, "")) continue; // 无真实资源的路径不进池
                if (seen.Add(path))
                    paths.Add(path);
            }
            if (paths.Count == 0) return;
            paths.Sort(StringComparer.Ordinal);
            _portraitPool = paths.ToArray();
            Log($"Collected {paths.Count} card portraits (handcrafted, non-derivative).");
        }
        catch (Exception e)
        {
            Log($"Collect card portraits FAILED: {e.Message}");
        }
    }

    /// <summary>
    /// 清理槽卡 CardModel 的 Canonical 缓存（_dynamicVars/_energyCost/_keywords/_tags），移植自
    /// “东尼算法：观者”mod 的 ResetCanonicalCardCaches。AA 槽卡首次被读取时会把**当时定义**派生的
    /// 数值/变量名缓存进 CardModel 私有字段；此后换定义不清缓存，卡面数字位仍会按旧变量名渲染，
    /// 表现为 {damage0:diff} 一类的原始标记原样显示。因此每次 InstallDefinitions 成功后都要调用。
    ///
    /// 除了 ModelDb 里的槽卡**根模型**，还必须清理本局**已克隆进牌组的实例**：开局时游戏在
    /// RunState/种子建立之前就经 ToMutable()（MemberwiseClone 浅拷贝全部字段，含上述缓存）克隆起始牌，
    /// 而根模型此时往往带着主菜单图鉴按“库预览定义”读出的旧缓存 → 开局第一战若不清理，牌面仍按
    /// 旧变量名渲染（SL 之所以正常，是因为读档时这些数值从存档 Props 还原）。传入 runState 即一并清
    /// 该局各玩家 Deck 里属于槽模型的卡。此刻在首场战斗渲染之前，清理后下次访问会按新定义重新派生。
    /// ModelDb 未就绪前模型尚未实例化（也无陈旧缓存），直接跳过。无 AA 类型引用，可安全 JIT。
    /// </summary>
    private static void ResetCanonicalCardCaches(RunState? runState = null)
    {
        if (_slotTypes is null) return;
        if (!_modelDbReady) return; // 槽模型尚未进入 ModelDb，无缓存可清
        try
        {
            FieldInfo?[] cacheFields =
            [
                AccessTools.Field(typeof(CardModel), "_dynamicVars"),
                AccessTools.Field(typeof(CardModel), "_energyCost"),
                AccessTools.Field(typeof(CardModel), "_keywords"),
                AccessTools.Field(typeof(CardModel), "_tags")
            ];
            var cleared = 0;
            var slotTypeSet = new HashSet<Type>(_slotTypes);
            foreach (var slotType in _slotTypes)
            {
                try
                {
                    var model = ModelDb.GetById<CardModel>(ModelDb.GetId(slotType));
                    if (model is null) continue;
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

            // 清理本局已克隆进牌组的槽卡实例（开局/读档时克隆早于本次定义安装）。
            if (runState?.Players is { } players)
            {
                foreach (var player in players)
                {
                    try
                    {
                        foreach (var card in player.Deck.Cards)
                        {
                            if (card is null) continue;
                            if (!slotTypeSet.Contains(card.GetType())) continue;
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
            Log($"Reset canonical caches ({cleared} field writes across {_slotTypes.Length} slot models).");
        }
        catch (Exception e)
        {
            Log($"Reset canonical caches FAILED: {e.Message}");
        }
    }

    private static void TryEnsureLibraryDefaults()
    {
        if (_libraryInstalled) return;
        if (!_registered || !IsAutoAnthonyLoaded()) return;
        if (_slotTypes is null) return;
        if (TryInstallDefinitions(LibrarySeed))
            _libraryInstalled = true;
    }

    /// <summary>局结束回到主菜单：把覆盖安装的局内定义换回“库预览”默认，让图鉴/选人界面内容稳定。</summary>
    private static void RestoreLibraryDefaultsAfterRun()
    {
        if (!_registered || !IsAutoAnthonyLoaded()) return;
        try
        {
            // 局内 ModelDb 早已就绪，此刻补一次收集（幂等），保证回主菜单的库预览用全量图池。
            RefreshPortraitPool();
            _libraryInstalled = false;
            TryEnsureLibraryDefaults();
        }
        catch
        {
            // 恢复失败保持原定义亦可解析，不阻断
        }
    }

    /// <summary>
    /// 用“种子键”确定性生成 SlotCount 张卡并组装成完整 ChaosCardDefinition 列表，然后
    /// InstallDefinitions 整体覆盖安装。失败返回 false 并保持原定义不动。
    ///
    /// 每批用**单个 RandomCardGenerator 实例**按槽序（稀有度从低到高）连续生成——与 AA 原生整池
    /// （一池 92 张）同一模式：AA 的卡名/效果签名去重是生成器实例内部状态，只有连续刷完整池才会
    /// 得到池级唯一；早期 6 槽“每槽独立实例”是因槽太少才会耗尽高稀有 emergency 空间，整池不受此限。
    /// 单槽失败/审计不过时整批换 attempt 偏移种子重试（仍可复现）。
    /// </summary>
    private static bool TryInstallDefinitions(string seedKey)
    {
        if (_slotTypes is not { Length: SlotCount }) return false;
        try
        {
            // 中文名：无放回抽取 SlotCount 个唯一词块组合（同 seedKey 确定可复现，跨槽不重名）。
            var names = PickUniqueCardNames(seedKey);
            var request = new ComponentProfileRequest(ProfileId, GeneratedCharacter.Ironclad, false);
            var lastFailure = "unknown";
            for (var attempt = 0; attempt < MaxInstallAttempts; attempt++)
            {
                var definitions = new List<ChaosCardDefinition>(SlotCount);
                var auditCards = new List<GeneratedCard>(SlotCount);
                var summary = new List<string>(SlotCount);
                var failed = false;
                var generator = new RandomCardGenerator(
                    request, StableSeed(seedKey + "|gen" + attempt), balancedValues: true);

                // 第一阶段：按槽序生成候选卡（连续刷完整池让卡名/效果去重成为池级唯一）。
                // Basic 槽（0..BasicSlotCount-1）承担初始牌组，做观者式加固：
                //   ① 单卡不可用重掷（IsUnacceptableBasic）；
                //   ② 前 BasicSlotCount 张刷完后跑 EnsureStartingDeckCoverage，保证按
                //      BasicSlotDeckCounts 叠出的开局牌组合计 伤害/格挡 达到下限。
                var candidate = new GeneratedCard[SlotCount];
                var basicCards = new GeneratedCard[BasicSlotCount];
                for (var slot = 0; slot < SlotCount; slot++)
                {
                    // 已确认 AA 加载后才走到这里，int→GeneratedRarity 强转安全
                    var rarity = (GeneratedRarity)SlotRarityValues[slot];
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
                        // 基础卡加固（单卡重掷 + 全组覆盖）本身也消费生成器唯一签名，空间不足时 AA
                        // 会走 emergency fallback 并可能抛异常（自写目录签名空间比官方克隆小，实测触发）。
                        // 因此加固整体也纳入 attempt 重试：单次抛错 → 放弃本 attempt，换种子重来。
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
                    Log($"Generate attempt {attempt} failed for '{seedKey}': {lastFailure}");
                    continue;
                }

                // 第二阶段：卡名覆盖 + 组装定义 + 汇总日志。
                // 卡名覆盖：AA 只能从官方词块拼名（拆词表烘焙无外部入口），这里把中文名换成
                // 本 mod 卡名词块预先抽好的唯一组合；英文沿用生成值以保非空。
                // 槽位种子独立于效果种子，效果一样时名字也可换，但同 seedKey/slot 必然可复现。
                for (var slot = 0; slot < SlotCount; slot++)
                {
                    var rarity = (GeneratedRarity)SlotRarityValues[slot];
                    var card = candidate[slot] with
                    {
                        Name = new GeneratedCardName(
                            names[slot],
                            candidate[slot].Name?.English ?? "Chaos",
                            candidate[slot].Name?.SourceCardIds ?? [])
                    };

                    auditCards.Add(card);
                    definitions.Add(BuildDefinition(slot, card));
                    var name = card.Name?.Chinese;
                    summary.Add($"slot{slot}:{rarity}({card.Type})「{(string.IsNullOrEmpty(name) ? "?" : name)}」" +
                                $"cost={card.Cost} ops={card.Operations.Count}");
                }

                if (!ComponentPolicy.TryAuditPool(auditCards, out var auditFailure))
                {
                    Log($"Audit attempt {attempt} failed for '{seedKey}': {auditFailure}");
                    continue;
                }

                ExternalComponentCharacterApi.InstallDefinitions(ProfileId, definitions);
                // 定义已整体替换：清掉槽卡此前按旧定义缓存的 Canonical 值，否则卡面数字位仍显示
                // {damage0:diff} 一类的原始变量标记（换定义后 DynamicVars 需按新定义重新派生）。
                ResetCanonicalCardCaches();
                Log($"Installed {definitions.Count} definitions seed='{seedKey}' attempt={attempt}: " +
                    string.Join(", ", summary));
                return true;
            }
            Log($"Install definitions FAILED ('{seedKey}'): all {MaxInstallAttempts} attempts exhausted ({lastFailure})");
            return false;
        }
        catch (Exception e)
        {
            Log($"Install definitions FAILED ('{seedKey}'): {e}");
            return false;
        }
    }

    /// <summary>把一张 GeneratedCard 组装成 ChaosCardDefinition（公式与 AA 内部 BuildDefinitions 一致）。</summary>
    private static ChaosCardDefinition BuildDefinition(int slot, GeneratedCard card)
    {
        var attack = card.Type == GeneratedCardType.Attack;
        var portraits = PortraitPool;
        return new ChaosCardDefinition(
            slot,
            card,
            portraits[slot % portraits.Length], // 全量非衍生卡图按槽序循环（每张都参与，同槽跨局稳定）
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

    /// <summary>从词块组合表无放回抽取 SlotCount 个唯一中文卡名（跨槽不重名，同 seedKey 可复现）。</summary>
    private static string[] PickUniqueCardNames(string seedKey)
    {
        var prefixes = NewsanguoGeneratedNameBank.Prefixes;
        var suffixes = NewsanguoGeneratedNameBank.Suffixes;
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
                if (used.Add(candidate))
                    idx = candidate;
            }
            if (idx < 0)
            {
                // 随机命中率极低时的线性兜底
                for (var j = 0; j < total && idx < 0; j++)
                {
                    if (used.Add(j))
                        idx = j;
                }
            }
            if (idx < 0)
                throw new InvalidOperationException("Name bank exhausted while sampling unique names.");
            names[i] = prefixes[idx / suffixes.Length] + suffixes[idx % suffixes.Length];
        }
        return names;
    }

    /// <summary>按槽序生成稀有度计划（Basic 0..basic-1，其后从低到高 Common/Uncommon/Rare）。</summary>
    private static int[] BuildSlotRarityPlan(int basic, int common, int uncommon, int rare)
    {
        var values = new List<int>(basic + common + uncommon + rare);
        for (var i = 0; i < basic; i++) values.Add(RarityBasicValue);
        for (var i = 0; i < common; i++) values.Add(RarityCommonValue);
        for (var i = 0; i < uncommon; i++) values.Add(RarityUncommonValue);
        for (var i = 0; i < rare; i++) values.Add(RarityRareValue);
        return values.ToArray();
    }

    // ---- 基础卡（初始牌组）加固：移植自“东尼算法：观者” mod 的生成期重掷逻辑 ----

    /// <summary>Basic 卡不适合当初始牌时用 GenerateWithoutSpecialX 重掷，最多 BasicRerollMaxAttempts 次。
    /// 单次重掷若触发 AA emergency 异常则保留当前卡（不向调用方抛错，让整批 attempt 有机会换种子重来）。</summary>
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
                Log($"Basic reroll attempt {attempt} failed; keep current card: {e.Message}");
                break;
            }
            card = next;
        }
        return card;
    }

    /// <summary>
    /// 观者 mod 判定“不可作为初始牌”的基础卡：
    /// ① 带“回合结束负面”型变体（如死亡/每回合掉能量/只能单独作为攻击打出）且打印收益过低；
    /// ② 直接伤害/格挡单值 1~2、几乎没有价值的生效。
    /// </summary>
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
            if (slot is null)
                continue;
            var value = OperationRuntimeSpecCompiler.StaticLiteralValue(operation, slot, 0);
            if (value > 0 && value < 3)
                return true;
        }
        return false;
    }

    /// <summary>打印收益粗估：伤害全额、格挡 1.2 倍、抽牌 4.6 倍、能量 6.5 倍（观者 mod 同款权重）。</summary>
    private static int PrintedPayoff(GeneratedCard card)
    {
        var total = 0;
        foreach (var operation in card.Operations)
        {
            var runtimeSpec = operation.RuntimeSpec;
            switch (runtimeSpec?.Opcode)
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

    /// <summary>按 BasicSlotDeckCounts 份数加权合计某属性的开局总能力（伤害/格挡），对应真实 10 张牌组。</summary>
    private static int WeightedBasicStat(GeneratedCard[] basicCards, string opcode, string slotId)
    {
        var total = 0;
        for (var s = 0; s < basicCards.Length; s++)
            total += BasicSlotDeckCounts[s] * SumOpcode(basicCards[s], opcode, slotId);
        return total;
    }

    /// <summary>
    /// 保证 10 个 Basic 槽各 1 张叠出的开局牌组（10 张独一无二的卡）合计 伤害≥22 / 格挡≥20
    /// （观者 mod 对 10 张起步牌的相同下限）；不达标时重掷最弱基础槽直至达标或耗尽 BasicCoverageMaxPasses 次。
    /// 单次重掷若触发 AA emergency 异常（自写目录签名空间小会偶发），只记日志并保留当前卡；
    /// 连续多次失败则提前接受未达标覆盖——绝不向调用方抛错，避免整批定义安装被打断。
    /// </summary>
    private static void EnsureStartingDeckCoverage(GeneratedCard[] basicCards, RandomCardGenerator generator)
    {
        var rerollFailures = 0;
        for (var pass = 0; pass < BasicCoverageMaxPasses; pass++)
        {
            var damage = WeightedBasicStat(basicCards, "deal_damage", "damage");
            var block = WeightedBasicStat(basicCards, "gain_block", "block");
            if (damage >= StartingDeckDamageFloor && block >= StartingDeckBlockFloor)
            {
                Log($"Starting-deck coverage OK: damage={damage} block={block} (pass {pass}).");
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
                Log($"Starting-deck coverage reroll (pass {pass}) failed; keep current cards: {e.Message}");
                if (rerollFailures >= 8)
                {
                    Log($"Starting-deck coverage: too many reroll failures; accept unmet coverage.");
                    break;
                }
            }
        }
        var finalDamage = WeightedBasicStat(basicCards, "deal_damage", "damage");
        var finalBlock = WeightedBasicStat(basicCards, "gain_block", "block");
        Log($"Starting-deck coverage settled: damage={finalDamage} block={finalBlock} " +
            $"(targets ≥{StartingDeckDamageFloor}/≥{StartingDeckBlockFloor}).");
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

    /// <summary>
    /// 用 Reflection.Emit 生成 SlotCount 个 ExternalChaosCardModel 具体子类（每槽一个），
    /// 并注册为 NewsanguoCardPool 池成员。只在 AA 已加载、且 ModelDb 注册未冻结前调用一次。
    /// </summary>
    private static void TryEmitAndRegisterPoolSlots()
    {
        try
        {
            var externalBase = typeof(ExternalChaosCardModel);
            var cardPoolModelType = typeof(CardPoolModel);
            var slotBaseMethod = GetPropertyGetter(externalBase, "Slot") ?? GetPropertyGetter(typeof(ChaosCardModel), "Slot");
            var profileBaseMethod = GetPropertyGetter(externalBase, "ComponentProfileId");
            var poolBaseMethod = GetPropertyGetter(externalBase, "Pool");
            if (slotBaseMethod is null || profileBaseMethod is null || poolBaseMethod is null)
            {
                Log("Cannot resolve ExternalChaosCardModel abstract members; skip pool slots.");
                return;
            }

            // 取 ModelDb.CardPool&lt;NewsanguoCardPool&gt;() 供 Pool 覆写调用（运行时属性被读时才执行）。
            var cardPoolOpen = typeof(ModelDb).GetMethod(
                "CardPool", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (cardPoolOpen is null)
            {
                Log("Cannot resolve ModelDb.CardPool<>; skip pool slots.");
                return;
            }
            var cardPoolForNewsanguo = cardPoolOpen.MakeGenericMethod(typeof(NewsanguoCardPool));

            var assembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("newsanguo.autoanthony.slots"), AssemblyBuilderAccess.Run);
            var module = assembly.DefineDynamicModule("main");

            var types = new Type[SlotCount];
            for (var slot = 0; slot < SlotCount; slot++)
                types[slot] = EmitSlotType(module, externalBase, cardPoolModelType,
                    slotBaseMethod!, profileBaseMethod!, poolBaseMethod!, cardPoolForNewsanguo, slot);
            _slotTypes = types;

            // 注册为 NewsanguoCardPool 成员（ModHelper.AddModelToPool 当刻建立成员资格，
            // 动态类型随后由 RitsuLib 在 ModelDb.Init 前缀注入 _contentById）。
            var registry = ModContentRegistry.For(Entry.ModId);
            for (var slot = 0; slot < types.Length; slot++)
            {
                registry.RegisterCard(typeof(NewsanguoCardPool), types[slot],
                    ModelPublicEntryOptions.FromStem("autoanthony_slot" + slot));
            }

            Log($"Emitted & registered {types.Length} pool slots into NewsanguoCardPool.");
        }
        catch (Exception e)
        {
            _slotTypes = null;
            Log($"Emit pool slots FAILED: {e}");
        }
    }

    private static Type EmitSlotType(ModuleBuilder module, Type externalBase, Type poolReturnType,
        MethodInfo slotBaseMethod, MethodInfo profileBaseMethod, MethodInfo poolBaseMethod,
        MethodInfo cardPoolForNewsanguo, int slot)
    {
        var typeBuilder = module.DefineType(
            "NewsanguoAAChaosSlot" + slot,
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            externalBase);

        // 无参公共构造（ModelDb 实例化模型走无参构造）：调用 AA 基类受保护的默认构造。
        var baseCtor = externalBase.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, Type.EmptyTypes, null);
        if (baseCtor is null)
            throw new InvalidOperationException("ExternalChaosCardModel has no parameterless constructor.");
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, baseCtor);
        ctorIl.Emit(OpCodes.Ret);

        // protected override string ComponentProfileId => NewsanguoAutoAnthonyAdapter.ProfileId;
        EmitGetterOverride(typeBuilder, profileBaseMethod, "ComponentProfileId", typeof(string),
            MethodAttributes.Family, il =>
            {
                il.Emit(OpCodes.Ldstr, ProfileId);
                il.Emit(OpCodes.Ret);
            });

        // protected override int Slot => N;
        EmitGetterOverride(typeBuilder, slotBaseMethod, "Slot", typeof(int),
            MethodAttributes.Family, il =>
            {
                il.Emit(OpCodes.Ldc_I4, slot);
                il.Emit(OpCodes.Ret);
            });

        // public override CardPoolModel Pool => ModelDb.CardPool<NewsanguoCardPool>();
        EmitGetterOverride(typeBuilder, poolBaseMethod, "Pool", poolReturnType,
            MethodAttributes.Public, il =>
            {
                il.Emit(OpCodes.Call, cardPoolForNewsanguo);
                il.Emit(OpCodes.Ret);
            });

        return typeBuilder.CreateType();
    }

    private static void EmitGetterOverride(TypeBuilder typeBuilder, MethodInfo baseMethod, string propertyName,
        Type returnType, MethodAttributes visibility, Action<ILGenerator> writeBody)
    {
        var getter = typeBuilder.DefineMethod(
            baseMethod.Name,
            visibility | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.HideBySig | MethodAttributes.SpecialName,
            CallingConventions.HasThis,
            returnType,
            Type.EmptyTypes);
        writeBody(getter.GetILGenerator());
        typeBuilder.DefineMethodOverride(getter, baseMethod);

        var property = typeBuilder.DefineProperty(propertyName, PropertyAttributes.None, returnType, Type.EmptyTypes);
        property.SetGetMethod(getter);
    }

    /// <summary>沿继承链查找属性（含 protected），返回其 getter 方法。</summary>
    private static MethodInfo? GetPropertyGetter(Type type, string propertyName)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            var property = current.GetProperty(
                propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property is not null)
                return property.GetGetMethod(nonPublic: true);
        }
        return null;
    }

    /// <summary>运行时反射读取 ApiVersion 常量（编译期直接访问会被常量折叠）。</summary>
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

    /// <summary>东尼算法文档要求：种子必须用 SHA-256 等稳定算法，禁止 GetHashCode。</summary>
    private static int StableSeed(string seed) =>
        BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(ProfileId + "|" + seed)), 0);

    private static bool IsAutoAnthonyLoaded() =>
        AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "AutoAnthony")
        || Type.GetType("AutoAnthony.ComponentRuntimeApi, AutoAnthony", throwOnError: false) is not null;

    // ---- 初始牌组替换（复刻“东尼算法：观者”：前 BasicSlotCount 个 Basic 槽即开局牌） ----

    /// <summary>
    /// 接管激活后把 ModCharacterTemplate 的 StartingDeck getter 整体劫持为 AA 基础槽卡。
    /// 目标方法为 ModCharacterTemplate&lt;...&gt;.StartingDeck 的 sealed getter（NewsanguoCharacter 不重写）；
    /// RitsuLib 的起始内容后置会跳过模板 getter，因此 Prefix 返回 false 后不会有任何手工初始卡被追加。
    /// 本方法与 Prefix 均不引用 AA 类型（只依赖 core 与本程序集自组装类型），可安全 JIT。
    /// </summary>
    private static void InstallStartingDeckReplacementPatch()
    {
        if (_startingDeckPatched) return;
        if (!IsAutoAnthonyLoaded()) return;
        try
        {
            // NewsanguoCharacter : ModCharacterTemplate<NewsanguoCardPool, NewsanguoRelicPool, NewsanguoPotionPool>
            var baseType = typeof(NewsanguoCharacter).BaseType;
            var getter = baseType?.GetProperty(
                "StartingDeck", BindingFlags.Instance | BindingFlags.Public)?.GetGetMethod(nonPublic: true);
            if (getter is null)
            {
                Log("Cannot resolve ModCharacterTemplate.StartingDeck getter; deck replacement inactive.");
                return;
            }

            var harmony = new Harmony("newsanguo.autoanthony.startingdeck");
            harmony.Patch(getter, prefix: new HarmonyMethod(
                typeof(NewsanguoAutoAnthonyAdapter).GetMethod(
                    nameof(StartingDeckPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
            _startingDeckPatched = true;
            Log("Starting-deck replacement patch installed (ReplaceStartingCards ON, basics=" +
                BasicSlotCount + ", deck copies=" + string.Join("/", BasicSlotDeckCounts) + ").");
        }
        catch (Exception e)
        {
            Log($"Install starting-deck patch FAILED: {e}");
        }
    }

    /// <summary>
    /// Harmony Prefix：整池接管激活时把 NewsanguoCharacter 初始牌组替换为 Basic 槽卡。
    /// 不可用时（未激活/非本角色/模型未就绪）返回 true 走原逻辑。方法体无 AA 类型，安全。
    /// </summary>
    private static bool StartingDeckPrefix(object __instance, ref IEnumerable<CardModel> __result)
    {
        if (!IsTakeoverActive)
            return true;
        if (__instance is not NewsanguoCharacter)
            return true;

        var deck = TryBuildReplacementStartingDeck();
        if (deck is null)
            return true;
        __result = deck;
        return false;
    }

    /// <summary>
    /// 构造替换牌组：Basic 槽 0..BasicSlotCount-1 各按 BasicSlotDeckCounts 份数重复（合计 10 张）。
    /// 构造失败（如 ModelDb 未就绪）返回 null 让调用方回退原逻辑。
    /// </summary>
    internal static IEnumerable<CardModel>? TryBuildReplacementStartingDeck()
    {
        if (_slotTypes is not { Length: SlotCount })
            return null;
        if (BasicSlotDeckCounts.Length != BasicSlotCount)
            return null;
        try
        {
            var deck = new List<CardModel>(10);
            for (var slot = 0; slot < BasicSlotCount; slot++)
            {
                var type = _slotTypes[slot];
                var card = ModelDb.GetById<CardModel>(ModelDb.GetId(type));
                var count = slot < BasicSlotDeckCounts.Length ? BasicSlotDeckCounts[slot] : 1;
                for (var c = 0; c < count; c++)
                    deck.Add(card);
            }
            return deck;
        }
        catch (Exception e)
        {
            Log($"Build replacement starting deck FAILED: {e.Message}");
            return null;
        }
    }

    private static void Log(string message)
    {
        var line = $"AA-POC {message}";
        try
        {
            Entry.Logger.Info(line);
            Diagnostics.Log(line);
        }
        catch
        {
            // 日志失败不阻断
        }
    }
}
