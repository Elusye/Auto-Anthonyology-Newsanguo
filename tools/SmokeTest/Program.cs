// 适配器冒烟检查：把编译产物按游戏运行时的依赖关系加载起来，静态核对 mod 契约。
// 不启动游戏，也不执行 Adapter.Startup()（那需要完整 Godot/ModelDb 环境）。
using System.Reflection;
using System.Runtime.Loader;

const string GameDir = @"E:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64";
const string ModsDir = @"E:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods";
// 仓库根从**可执行文件位置**向上找（含 src/ 的那一层）—— 目录名改过、换机器都不用改代码。
// 之前这里硬编码了绝对路径，导致工作区目录一改名，冒烟检查就找不到 DLL。
static string FindRepoRoot()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "src"))) d = d.Parent;
    return d?.FullName ?? throw new InvalidOperationException("找不到仓库根：向上没有含 src/ 的目录");
}

var repoRoot = FindRepoRoot();
var AutoAnthonyRef = Path.Combine(repoRoot, "refs", "AutoAnthony.dll");

var target = args.Length > 0
    ? args[0]
    : Path.Combine(repoRoot, "bin", "Release", "autoanthony_newsanguo.dll");

var probeDirs = new[]
{
    Path.Combine(ModsDir, "STS2-RitsuLib", "compat", "0.111.0"),
    Path.Combine(ModsDir, "STS2-RitsuLib", "shared"),
    Path.Combine(ModsDir, "STS2-RitsuLib"),
    Path.Combine(ModsDir, "newsanguo"),
    GameDir,
    // 东尼算法：本机未安装该 mod，用 refs 下的编译参考充当运行时依赖
    Path.GetDirectoryName(Path.GetFullPath(AutoAnthonyRef))!,
};

// GetTypes 在个别依赖解析不了时会整体抛出；退化为"能加载多少算多少"。
static Type[] SafeTypes(Assembly a)
{
    try { return a.GetTypes(); }
    catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t is not null).ToArray()!; }
}

AssemblyLoadContext.Default.Resolving += (ctx, name) =>
{
    foreach (var dir in probeDirs)
    {
        var candidate = Path.Combine(dir, name.Name + ".dll");
        if (File.Exists(candidate)) return ctx.LoadFromAssemblyPath(candidate);
    }
    return null;
};

var failures = new List<string>();

// 诊断模式：导出**我们自写目录**里实际存在的原子（经 ImmutableComponentCatalog 去重后的结果）。
// 用来核对"源码声明的原子"与"目录里真正存在的组件"是否一致 —— 只改数字的原子会被合并。
if (args.Contains("--dump-self-atoms"))
{
    var modAsm = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(target));
    var sc = modAsm.GetType("Newsanguo.AutoAnthony.SelfCatalog")!;
    var build = sc.GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
    var cat = build.Invoke(null, null)!;
    var atoms = (System.Collections.IEnumerable)cat.GetType().GetProperty("Atoms")!.GetValue(cat)!;
    foreach (var a in atoms)
    {
        var t = a!.GetType();
        var sid = t.GetProperty("SemanticId")?.GetValue(a) as string;
        var spec = t.GetProperty("RuntimeSpec")?.GetValue(a);
        var op = spec?.GetType().GetProperty("Opcode")?.GetValue(spec) as string;
        var va = spec?.GetType().GetProperty("Variant")?.GetValue(spec) as string;
        var tg = spec?.GetType().GetProperty("Target")?.GetValue(spec) as string;
        var vals = spec?.GetType().GetProperty("Values")?.GetValue(spec) as System.Collections.IEnumerable;
        var vt = new List<string>();
        if (vals is not null)
            foreach (var v in vals)
            {
                var vv = v!.GetType();
                vt.Add($"{vv.GetProperty("Id")?.GetValue(v)}={vv.GetProperty("BaseValue")?.GetValue(v)}");
            }
        Console.WriteLine($"SELF\t{sid}\t{op}\t{va}\t{tg}\t{string.Join(",", vt)}");
    }
    return 0;
}

// 诊断模式：把 AA 程序集里**内嵌的官方目录数据**导出到磁盘（0.3.113 的权威 opcode/variant 词汇表
// 与官方原子表就藏在这些 JSON 里），用于评估"某个效果用现成 opcode 能不能表达"。
if (args.Contains("--extract-embedded"))
{
    var outDir = args.SkipWhile(x => x != "--extract-embedded").Skip(1).FirstOrDefault() ?? ".";
    Directory.CreateDirectory(outDir);
    var a = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(AutoAnthonyRef));
    foreach (var name in a.GetManifestResourceNames())
    {
        using var stream = a.GetManifestResourceStream(name)!;
        var file = Path.Combine(outDir, name.Replace('/', '_'));
        using var fs = File.Create(file);
        stream.CopyTo(fs);
        Console.WriteLine($"{name}  ->  {file}  ({new FileInfo(file).Length} bytes)");
    }
    return 0;
}

// 诊断模式：导出当前 AA 的官方 Ironclad 原子语义 ID 表，用于核对 SelfCatalog 引用的“数值基线”
// 是否在该 AA 版本里仍然存在（AA 改过官方目录 ID，靠语义 ID 克隆的目录会因此失配）。
if (args.Contains("--dump-ironclad-atoms"))
{
    var a = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(AutoAnthonyRef));
    var ccc = a.GetType("ChaosCardGenerator.CharacterComponentCatalogs")!;
    var gen = a.GetType("ChaosCardGenerator.GeneratedCharacter")!;
    // 有两个 Get(GeneratedCharacter) 重载，必须按参数类型精确取，否则 AmbiguousMatchException。
    var get = ccc.GetMethod("Get", BindingFlags.Public | BindingFlags.Static, null, new[] { gen }, null)!;
    // ⚠ 运行时目录是 catalog_runtime_specs.json（931 条）的**子集**：
    // Ironclad 只有 84 个原子。挑基线必须在运行时目录里找，不能只查 JSON。
    foreach (var charName in Enum.GetNames(gen))
    {
        var catalog = get.Invoke(null, new[] { Enum.Parse(gen, charName) })!;
    var atoms = (System.Collections.IEnumerable)catalog.GetType().GetProperty("Atoms")!.GetValue(catalog)!;
    foreach (var atom in atoms)
    {
        var t = atom.GetType();
        var sid = t.GetProperty("SemanticId")?.GetValue(atom) as string;
        var tpl = t.GetProperty("Template")?.GetValue(atom) as string;
        var spec = t.GetProperty("RuntimeSpec")?.GetValue(atom);
        var opcode = spec?.GetType().GetProperty("Opcode")?.GetValue(spec) as string;
        var variant = spec?.GetType().GetProperty("Variant")?.GetValue(spec) as string;
        var specTarget = spec?.GetType().GetProperty("Target")?.GetValue(spec) as string;
        var values = spec?.GetType().GetProperty("Values")?.GetValue(spec) as System.Collections.IEnumerable;
        var valueText = new List<string>();
        if (values is not null)
        {
            foreach (var v in values)
            {
                var vt = v!.GetType();
                var id = vt.GetProperty("Id")?.GetValue(v) as string;
                var bv = vt.GetProperty("BaseValue")?.GetValue(v);
                var off = vt.GetProperty("Offset")?.GetValue(v);
                var exp = vt.GetProperty("Explicit")?.GetValue(v);
                valueText.Add($"{id}={bv}+{off}{(exp is true ? "" : "(hidden)")}");
            }
        }
        Console.WriteLine($"SID\t{charName}\t{sid}\t{tpl}\t{opcode}\t{variant}\t{specTarget}\t{string.Join(",", valueText)}");
        }
    }
    return 0;
}
void Check(bool ok, string label, string? detail = null)
{
    Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label + (detail is null ? "" : "  -- " + detail));
    if (!ok) failures.Add(label);
}

Console.WriteLine($"target = {target}");
var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(target));
Console.WriteLine($"assembly = {asm.GetName().Name} {asm.GetName().Version}");
Console.WriteLine();

Console.WriteLine("[1] 程序集命名须与清单 id 一致（加载器按 <id>.dll 发现）");
Check(asm.GetName().Name == "autoanthony_newsanguo", "assembly name == autoanthony_newsanguo", asm.GetName().Name);

Console.WriteLine("[2] [ModInitializer] 入口");
var initAttr = SafeTypes(asm).SelectMany(t => t.GetCustomAttributes())
    .FirstOrDefault(a => a.GetType().Name == "ModInitializerAttribute");
Check(initAttr is not null, "ModInitializerAttribute present");

Console.WriteLine("[3] Adapter.ProfileId");
var adapter = asm.GetType("Newsanguo.AutoAnthony.Adapter");
Check(adapter is not null, "Adapter type exists");
var profileId = adapter?.GetField("ProfileId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
    ?.GetValue(null) as string;
Check(profileId == "newsanguo:autoanthony", "ProfileId == newsanguo:autoanthony", profileId);

Console.WriteLine("[4] AA 基类与槽卡表契约");
var aa = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(AutoAnthonyRef));
var externalBase = aa.GetType("AutoAnthony.ExternalChaosCardModel");
Check(externalBase is not null, "AutoAnthony.ExternalChaosCardModel resolvable",
    externalBase?.Assembly.GetName().Name);

var slotTable = asm.GetType("Newsanguo.AutoAnthony.SlotTable");
Check(slotTable is not null, "SlotTable type exists");
var count = slotTable?.GetField("Count", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
    ?.GetValue(null);
Check(count is 96, "SlotTable.Count == 96", count?.ToString());
var types = slotTable?.GetField("Types", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
    ?.GetValue(null) as Type[];
Check(types is { Length: 96 }, "SlotTable.Types length == 96", (types?.Length ?? -1).ToString());

if (types is not null && externalBase is not null)
{
    var bad = new List<string>();
    for (var i = 0; i < types.Length; i++)
    {
        var t = types[i];
        if (t is null) { bad.Add($"[{i}] null"); continue; }
        if (!t.IsPublic) bad.Add($"{t.Name} not public");
        if (t.IsAbstract) bad.Add($"{t.Name} abstract");
        if (!externalBase.IsAssignableFrom(t)) bad.Add($"{t.Name} not ExternalChaosCardModel");
        if (t.GetConstructor(Type.EmptyTypes) is null) bad.Add($"{t.Name} no public parameterless ctor");
        if (t.GetProperty("Slot", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) is null)
            bad.Add($"{t.Name} no Slot");
    }
    Check(bad.Count == 0, "all 96 slot types concrete/public/subclass/ctor", string.Join("; ", bad.Take(6)));
    Check(types.Distinct().Count() == types.Length, "slot types distinct");
    Check(types[0].Name == "NewsanguoChaosCard000" && types[95].Name == "NewsanguoChaosCard095",
        "slot type naming NewsanguoChaosCard000..095", $"{types[0].Name}..{types[95].Name}");
}

Console.WriteLine("[5] 补丁方法契约");
var postfix = asm.GetType("Newsanguo.AutoAnthony.PoolContentsPatch")
    ?.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
Check(postfix is not null, "PoolContentsPatch.Postfix exists");
if (postfix is not null)
{
    var ps = postfix.GetParameters();
    var p0 = ps.Length > 0 ? ps[0].ParameterType.Name : "?";
    // 形参是 ref IEnumerable<CardModel>：ParameterType 形如 IEnumerable`1&，
    // 取一层得到 IEnumerable`1，再取其泛型实参才是 CardModel。
    var byRefElement = ps.Length > 1 && ps[1].ParameterType.IsByRef
        ? ps[1].ParameterType.GetElementType()
        : null;
    var genericArg = byRefElement is { IsGenericType: true }
        ? byRefElement.GetGenericArguments().FirstOrDefault()
        : null;
    Check(ps.Length == 2 && p0 == "CardPoolModel" && genericArg?.Name == "CardModel",
        "Postfix(CardPoolModel, ref IEnumerable<CardModel>)",
        $"{p0}, {ps.ElementAtOrDefault(1)?.ParameterType.Name}, arg={genericArg?.Name}");
}

var deckPrefix = asm.GetType("Newsanguo.AutoAnthony.StartingDeckPatch")
    ?.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
Check(deckPrefix is not null && deckPrefix.ReturnType == typeof(bool),
    "StartingDeckPatch.Prefix returns bool");

Console.WriteLine("[6] 补丁点必须是未被复写的那个（Harmony 改方法体，不参与虚分派）");// newsanguo 卡池基类 TypeListCardPoolModel 复写了 GenerateAllCards，但没复写 AllCards。
// 所以补在基类 GenerateAllCards 上会被子类复写静默绕过，必须补在 AllCards 上。
var sts2 = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(GameDir, "sts2.dll"));
var ritsuRuntime = AssemblyLoadContext.Default.LoadFromAssemblyPath(
    Path.Combine(probeDirs[0], "STS2-RitsuLib.Runtime.dll"));
var cardPoolModel = sts2.GetType("MegaCrit.Sts2.Core.Models.CardPoolModel");
var typeListPool = ritsuRuntime.GetType("STS2RitsuLib.Scaffolding.Content.TypeListCardPoolModel");
Check(cardPoolModel is not null, "CardPoolModel resolvable");
Check(typeListPool is not null, "TypeListCardPoolModel resolvable");
const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic
                            | BindingFlags.Instance | BindingFlags.DeclaredOnly;
if (cardPoolModel is not null && typeListPool is not null)
{
    Check(typeListPool.GetMethod("GenerateAllCards", Declared) is not null,
        "TypeListCardPoolModel 复写了 GenerateAllCards（故不可补在该方法上）");
    Check(typeListPool.GetProperty("AllCards", Declared) is null,
        "TypeListCardPoolModel 未复写 AllCards");
    Check(cardPoolModel.GetProperty("AllCards", Declared) is not null,
        "CardPoolModel 声明 AllCards（唯一咽喉点）");
}

// 同一类陷阱：若角色类复写了 StartingDeck，补在 ModCharacterTemplate 基类上同样会被绕过。
Console.WriteLine("[6b] 初始牌组补丁点：NewsanguoCharacter 不得复写 StartingDeck");
var newsanguoAsm = AssemblyLoadContext.Default.LoadFromAssemblyPath(
    Path.Combine(ModsDir, "newsanguo", "newsanguo.dll"));
var characterType = newsanguoAsm.GetType("newsanguo.Scripts.Characters.NewsanguoCharacter");
Check(characterType is not null, "NewsanguoCharacter resolvable");
if (characterType is not null)
{
    var baseType = characterType.BaseType;
    Check(baseType is not null && baseType.Name.StartsWith("ModCharacterTemplate"),
        "NewsanguoCharacter 直接继承 ModCharacterTemplate<,,>", baseType?.Name);
    Check(characterType.GetProperty("StartingDeck", Declared) is null,
        "NewsanguoCharacter 未复写 StartingDeck（补在基类才生效）");
}

Console.WriteLine("[7] 运行时路由类型");
Check(asm.GetType("Newsanguo.AutoAnthony.GainWineHandler") is not null, "GainWineHandler exists");
Check(asm.GetType("Newsanguo.AutoAnthony.GainHeavenHandler") is not null, "GainHeavenHandler exists");
var runtimeHandler = aa.GetType("AutoAnthony.IComponentRuntimeHandler");
foreach (var n in new[] { "Newsanguo.AutoAnthony.GainWineHandler", "Newsanguo.AutoAnthony.GainHeavenHandler" })
{
    var t = asm.GetType(n);
    Check(t is not null && runtimeHandler is not null && runtimeHandler.IsAssignableFrom(t),
        $"{n} implements IComponentRuntimeHandler");
}

Console.WriteLine("[8] AA API 版本门（必须与 Adapter 的运行时门一致，否则接管会静默跳过）");
foreach (var (typeName, expected) in new (string, int)[]
         {
             ("ChaosCardGenerator.ComponentApi", 3),
             ("ChaosCardGenerator.ComponentPackageApi", 3),
             ("AutoAnthony.ComponentRuntimeApi", 1),
             ("AutoAnthony.ExternalComponentCharacterApi", 4),
         })
{
    var t = aa.GetType(typeName);
    var v = t?.GetField("ApiVersion", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
    Check(t is not null && v is int iv && iv == expected,
        $"{typeName}.ApiVersion == {expected}", v?.ToString() ?? "missing");
}

// 顺带报出 AA 程序集的真实版本，便于确认我们是对着玩家实际安装的那份编译的。
var aaInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.GetFullPath(AutoAnthonyRef));
var aaAsmVersion = aa.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
Console.WriteLine($"       AA dll  = {AutoAnthonyRef}");
Console.WriteLine($"       file    = {aaInfo.FileVersion} / product {aaInfo.ProductVersion}");
Console.WriteLine($"       asmver  = {aa.GetName().Version} / info {aaAsmVersion}");

// 槽位计划是按 newsanguo 卡池成员数推出来的，而源码版本(0.2.38)可能与已安装二进制(0.2.37)不一致，
// 因此直接对**已安装的 DLL** 核对成员数，避免"照着一个没装的版本做适配"。
Console.WriteLine("[9] 已安装 newsanguo 二进制的卡池成员数（与源码扫描互相印证）");
var registered = new List<string>();
foreach (var t in SafeTypes(newsanguoAsm))
{
    foreach (var cad in t.GetCustomAttributesData())
    {
        if (cad.AttributeType.Name != "RegisterCardAttribute") continue;
        var arg = cad.ConstructorArguments.FirstOrDefault();
        if (arg.Value is Type poolType && poolType.Name == "NewsanguoCardPool")
            registered.Add(t.Name);
    }
}
Check(registered.Count == 92, "注册进 NewsanguoCardPool 的卡 = 92", registered.Count.ToString());
Check(registered.Contains("TheTruestMask") && registered.Contains("DivineInsight"),
    "2 张先古在其中（手工保留，不参与接管）");
Check(registered.Count - 2 == 90, "可被接管替换的手工卡 = 90", (registered.Count - 2).ToString());

// 自写目录只依赖 AA + BCL，可以完全离线构建 —— 这正是上次实机开局卡死的直接原因
// （Build() 抛错 → Startup 中断 → 槽卡已在池里但永远没有定义 → 一读 Rarity 就抛）。
Console.WriteLine("[10] 自写目录能否离线构建");
var selfCatalogType = asm.GetType("Newsanguo.AutoAnthony.SelfCatalog");
Check(selfCatalogType is not null, "SelfCatalog type exists");
if (selfCatalogType is not null)
{
    const BindingFlags Any = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    var buildMethod = selfCatalogType.GetMethod("Build", Any);
    object? catalog = null;
    string? buildError = null;
    try { catalog = buildMethod?.Invoke(null, null); }
    catch (TargetInvocationException tie) { buildError = tie.InnerException?.Message ?? tie.Message; }
    catch (Exception ex) { buildError = ex.Message; }
    Check(catalog is not null, "SelfCatalog.Build() 未抛异常", buildError);

    if (catalog is not null)
    {
        var recipes = catalog.GetType().GetProperty("Recipes")?.GetValue(catalog) as System.Collections.ICollection;
        var atoms = catalog.GetType().GetProperty("Atoms")?.GetValue(catalog) as System.Collections.ICollection;
        Console.WriteLine($"       recipes={recipes?.Count ?? -1}  atoms={atoms?.Count ?? -1}");
        Check(recipes is { Count: > 0 }, "recipes 非空");

        // 壳按稀有度的供给 vs 槽位需求 —— 壳太少会在"唯一签名用尽"后走 emergency fallback，
        // 那是历史上导致开局卡死的失败模式，所以必须能一眼看出来。
        if (recipes is not null)
        {
            var byRarity = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var r in recipes)
            {
                var rar = r!.GetType().GetProperty("OriginalRarity")?.GetValue(r)?.ToString() ?? "?";
                byRarity[rar] = byRarity.GetValueOrDefault(rar) + 1;
            }
            Console.WriteLine("       壳按稀有度: " + string.Join(", ",
                byRarity.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")) +
                "   （槽位需求 Basic=10, Common=20, Uncommon=40, Rare=26）");
        }

        // 组件身份由 RuntimeSpec **结构**决定（数值只是槽位），所以"只改数字"的原子会被合并成一个组件。
        // 把按 opcode 的组件数报出来，用来判断哪些原子是真正新增的结构。
        if (atoms is not null)
        {
            var byOp = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var a in atoms)
            {
                var spec = a!.GetType().GetProperty("RuntimeSpec")?.GetValue(a);
                var op = spec?.GetType().GetProperty("Opcode")?.GetValue(spec) as string ?? "(none)";
                byOp[op] = byOp.GetValueOrDefault(op) + 1;
            }
            Console.WriteLine("       组件按 opcode 分布: " +
                string.Join(", ", byOp.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}")));
        }

        var fallbacks = selfCatalogType.GetProperty("LastFallbacks", Any)?.GetValue(null)
            as System.Collections.IEnumerable;
        var fallbackList = fallbacks?.Cast<object>().Select(o => o?.ToString() ?? "?").ToList() ?? [];
        Console.WriteLine($"       文案降级原子数 = {fallbackList.Count}");
        foreach (var f in fallbackList) Console.WriteLine($"         - {f}");
        Check(fallbackList.Count == 0, "自写文案全部编译通过（0 降级）",
            fallbackList.Count == 0 ? null : string.Join("; ", fallbackList));
    }
}

// 真正的预检：TryRegisterAutoAnthony 这一整条路径**不碰 ModelDb/Godot**，可以离线跑完 ——
// 包含版本门、自写目录构建、ComponentPackageApi.Register 的强校验、外部角色注册、
// 运行期宿主的槽类型校验、以及运行时路由注册。上次实机卡死就发生在这条路径的中途。
Console.WriteLine("[11] 离线跑通 AA 注册路径（Adapter.TryRegisterAutoAnthony）");
if (adapter is not null)
{
    var tryRegister = adapter.GetMethod("TryRegisterAutoAnthony",
        BindingFlags.NonPublic | BindingFlags.Static);
    Check(tryRegister is not null, "TryRegisterAutoAnthony 存在");
    if (tryRegister is not null)
    {
        object? registeredOk = null;
        string? regError = null;
        try { registeredOk = tryRegister.Invoke(null, null); }
        catch (TargetInvocationException tie) { regError = tie.InnerException?.ToString(); }
        catch (Exception ex) { regError = ex.ToString(); }
        Check(registeredOk is true,
            "AA 注册路径全程无异常（组件包校验/外部角色/运行期宿主/运行时路由）", regError);

        // 注册成功的连带证据：外部角色确实登记了我们的 ProfileId。
        var externalApi = aa.GetType("AutoAnthony.ExternalComponentCharacterApi");
        var regs = externalApi?.GetProperty("RegisteredCharacters",
            BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as System.Collections.IEnumerable;
        var ids = regs?.Cast<object>()
            .Select(r => r.GetType().GetProperty("ProfileId")?.GetValue(r)?.ToString())
            .ToList() ?? [];
        Check(ids.Contains("newsanguo:autoanthony"),
            "ExternalComponentCharacterApi.RegisteredCharacters 含 newsanguo:autoanthony",
            string.Join(",", ids));
    }
}

// 生成与安装同样不碰 ModelDb（缓存清理那步被 _modelDbReady 挡掉），所以也能离线跑完：
// 这一步真正生成 96 张卡、跑池级审计、组装 ChaosCardDefinition 并 InstallDefinitions。
Console.WriteLine("[12] 离线跑通本局定义生成与安装（Adapter.TryInstallDefinitions）");
if (adapter is not null)
{
    var install = adapter.GetMethod("TryInstallDefinitions", BindingFlags.NonPublic | BindingFlags.Static);
    Check(install is not null, "TryInstallDefinitions 存在");
    if (install is not null)
    {
        // 多个不同种子都跑一遍：壳变少时"唯一签名用尽"可能只在部分种子上触发，
        // 单种子通过不足以说明稳。
        // 起始牌组覆盖有 64 次重掷兜底，但 **Basic 壳只有 5 个而槽位需求 10**。
        // 5 个种子不足以说明"总能达标"，所以加宽到 12 个。
        // 曾一次性扫过 **40 个种子：40/40 通过**（生成 + 池级审计），Basic 偏薄未构成实际风险。
        // 不常驻 40 是因为 40 次连跑约 15 分钟，且后续种子会明显变慢（状态累积）。
        var seeds = Enumerable.Range(0, 12)
            .Select(i => i == 0 ? "smoketest" : "sweep-" + i).ToArray();
        var okCount = 0;
        var worstMs = 0L;
        var errors = new List<string>();
        foreach (var s in seeds)
        {
            object? r = null;
            string? err = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { r = install.Invoke(null, new object[] { s }); }
            catch (TargetInvocationException tie) { err = tie.InnerException?.Message; }
            catch (Exception ex) { err = ex.Message; }
            sw.Stop();
            worstMs = Math.Max(worstMs, sw.ElapsedMilliseconds);
            if (r is true) okCount++;
            else errors.Add($"{s}: {err ?? "返回 false"}");
        }
        Check(okCount == seeds.Length,
            $"{seeds.Length} 个不同种子都能生成 96 张卡并过池级审计（成功 {okCount}）",
            errors.Count == 0 ? null : string.Join(" | ", errors));
        Check(worstMs < 60_000, "单次生成耗时在合理范围", $"{worstMs} ms");
    }
}

// 先古卡图与普通卡图尺寸不同：槽位计划里只要出现先古槽，就必须走单独的立绘池。
// 这里守住"当前没有先古槽"，也就守住了"先古图不会套到普通卡上"。
Console.WriteLine("[13] 立绘分组不变量（先古图尺寸不同于普通图）");
var slotRarities = adapter?.GetField("SlotRarities", BindingFlags.NonPublic | BindingFlags.Static)
    ?.GetValue(null) as Array;
Check(slotRarities is { Length: 96 }, "SlotRarities 长度 96", slotRarities?.Length.ToString());
if (slotRarities is not null)
{
    var ancientValue = Enum.Parse(aa.GetType("ChaosCardGenerator.GeneratedRarity")!, "Ancient");
    var ancientSlots = slotRarities.Cast<object>().Count(v => v.Equals(ancientValue));
    Check(ancientSlots == 0, "先古槽数 = 0（先古图不会进入普通槽循环）", ancientSlots.ToString());
}
Check(adapter?.GetMethod("PortraitPoolFor", BindingFlags.NonPublic | BindingFlags.Static) is not null,
    "PortraitPoolFor 按稀有度分组取图存在");
// 立绘源是惰性捕获的卡池成员表；若不在收集前主动触发一次枚举，OnModelDbReady 时源为空，
// 就会静默退回 6 张占位图（96 个槽只剩 6 种立绘）。这个方法就是那个"主动触发"。
Check(adapter?.GetMethod("EnsurePortraitSource", BindingFlags.NonPublic | BindingFlags.Static) is not null,
    "EnsurePortraitSource 存在（防止立绘静默退回占位图）");
var fallbackField = adapter?.GetField("FallbackSlotPortraits", BindingFlags.NonPublic | BindingFlags.Static);
var fallbackCount = (fallbackField?.GetValue(null) as Array)?.Length ?? -1;
Console.WriteLine($"       占位立绘数 = {fallbackCount}（正常运行时应该被全量图池替换掉）");
Check(fallbackCount > 0, "占位立绘表存在");

Console.WriteLine();
Console.WriteLine(failures.Count == 0 ? "==> 全部检查通过" : $"==> {failures.Count} 项失败");
return failures.Count == 0 ? 0 : 1;
