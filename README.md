# 新三国 × 东尼算法（newsanguo_autoanthony）

> **职责边界**：本文件只讲**怎么用**（构建、部署、工具、依赖）。
> 当前规模、扩展模式、决策与**踩过的坑**见 [docs/STATUS.md](docs/STATUS.md) ——
> 两边都写状态一定会漂移（已经漂移过一次）。

一个**独立 mod**，让《杀戮尖塔 2》的角色 mod [newsanguo（新三国）](https://steamcommunity.com/sharedfiles/filedetails/?id=3786611028)
接入随机卡生成 mod **东尼算法（Auto-Anthonyology，下称 AA）**。

功能与原「内置在 newsanguo 里」的实现一致：**整池接管 + 替换初始牌组**。

- 接管开启后，newsanguo 卡池里除 2 张先古（Ancient）外的全部手工卡从一切获取/图鉴入口隐藏；
  池内奖励 / 商店 / 事件 / 发现等"可获得内容"全部变成 AA 为本局生成的卡。
- 初始牌组替换为 **10 张独一无二的 AA 基础卡**（10 个 Basic 槽各 1 张），并做开局强度加固
  （合计伤害 ≥22 / 格挡 ≥20）。
- 每局用局种子确定性生成，同局读档与多人两端结果一致。

---

## 与旧内置实现的关键差异

旧代码（本目录根下的 5 个 `.cs`，从 newsanguo 剔除前是内置的）强依赖"AA 是可选依赖"这一前提，
因此塞进了大量规避手段。作为独立 mod，这些前提消失了，实现因此显著简化：

| 方面 | 旧内置实现 | 本独立 mod |
| --- | --- | --- |
| AA 依赖 | 可选。故不能有任何继承 AA 类型的类成员，运行时还要挂 `AssemblyLoad` 钩子 | **必需依赖**（清单已声明），直接引用 AA 类型 |
| 运行时路由 | `DispatchProxy` 生成透明代理，再用反射读上下文 | 直接实现 `IComponentRuntimeHandler`，上下文强类型 |
| 卡槽模型 | `Reflection.Emit` 运行时生成 94 个类型，再用 newsanguo 的 ModId 注入 | **96 个静态声明的具体类**，用本 mod 自己的 ModId 注册 |
| 隐藏手工卡 | ① newsanguo 的 `FilterThroughEpochs` 钩子 ② 修剪 `NCardLibraryGrid._allCards` 私有字段 | 在 sts2 核心 `CardPoolModel.GenerateAllCards` 的**唯一咽喉点**重建成员表 |
| 槽位数 | 94（按当时池成员：Uncommon 38） | **96**（按 newsanguo 0.2.38 实测：Uncommon 40） |

> 关于"隐藏手工卡"：`FilterThroughEpochs` 其实是 **sts2 核心** `CardPoolModel` 的方法，不是 newsanguo 的钩子；
> 而 newsanguo 0.2.38 已把卡池简化为 `TypeListCardPoolModel`，私有字段名也不再稳定。
> 因此改为在 `CardPoolModel.AllCards` 取值器上做 Postfix —— 所有获取入口最终都读 `AllCards`，一处拦截即全部生效。
> 用 Postfix 而非 Prefix 有两个好处：① 能读到原始列表，从而**按规则**（稀有度）保留先古卡，不必硬编码卡名；
> ② 不会自我递归。
>
> ⚠ **为什么是 `AllCards` 而不是看起来更"源头"的 `GenerateAllCards`**：`TypeListCardPoolModel`
> （newsanguo 卡池的基类，来自 RitsuLib）**复写了** `GenerateAllCards`。Harmony 改的是方法体本身、
> 不参与虚分派，所以补在基类 `CardPoolModel.GenerateAllCards` 上会被子类复写**静默绕过**（补丁装上、
> 日志正常、但完全不生效）。`AllCards` 则只在 `CardPoolModel` 上声明、无人复写，是真正能拦住所有
> 卡池的那个点（东尼算法自己也是补在这里）。`tools/SmokeTest` 里有专门的断言守住这条。

---

## 目录结构

```
NewsanguoAutoAnthonyAdapter.csproj   构建定义（只编译 src/**）
newsanguo_autoanthony.json            mod 清单（id 必须与程序集名一致）
refs/AutoAnthony.dll                  编译参考：AA 0.3.104（本地从源码构建）
src/
  ModEntry.cs            [ModInitializer] 入口 + 日志
  Adapter.cs             适配核心：注册、卡槽计划、生成、定义安装、立绘、缓存清理
  SlotCards.cs           96 个卡槽类 + SlotTable（由 tools/gen_slotcards.py 生成）
  SelfCatalog.cs         三国"自写原子目录"（仅依赖 AA 与 BCL，原样搬出）
  GeneratedNameBank.cs   中文卡名词块库（原样搬出）
  RuntimeRoutes.cs       ns_gain_wine / ns_gain_heaven 运行时路由
  TakeoverPatches.cs     卡池接管 + 初始牌组替换的 Harmony 补丁
tools/
  gen_slotcards.py       生成 src/SlotCards.cs（改槽位计划后重跑）
  SmokeTest/             冒烟检查：加载产物核对 mod 契约
_ref/src/                东尼算法 0.3.104 完整源码（查阅用，不参与编译）
dist/                    /p:PackageMod=true 产出的安装目录（可整目录拖进 mods\）
*.cs（根目录 5 个）        从 newsanguo 剔除时的**原始文件**，仅作对照，不参与编译
```

## 构建

需要 .NET SDK（本机为 10.0.302，产物目标框架是 `net9.0`，与 newsanguo / AA 一致）
以及《杀戮尖塔 2》与已安装的 `newsanguo`、`STS2-RitsuLib`。

```powershell
dotnet build NewsanguoAutoAnthonyAdapter.csproj -c Release
# 若要指定游戏目录：
dotnet build NewsanguoAutoAnthonyAdapter.csproj -c Release /p:Sts2Dir="E:\...\Slay the Spire 2"
```

产物：`bin\Release\newsanguo_autoanthony.dll`。

只需要一份目录产物（不碰游戏目录），用于检查安装布局或手动投放：

```powershell
dotnet build NewsanguoAutoAnthonyAdapter.csproj -c Release /p:PackageMod=true
# -> dist\newsanguo_autoanthony\{newsanguo_autoanthony.dll, newsanguo_autoanthony.json}
```

部署到游戏（默认关闭，因为目标在会话工作区之外）：

```powershell
dotnet build NewsanguoAutoAnthonyAdapter.csproj -c Release /p:DeployToMods=true
```

即复制到 `mods\newsanguo_autoanthony\`（`newsanguo_autoanthony.dll` + `newsanguo_autoanthony.json`）。
**注意**：游戏 mod 加载器按 `<清单 id>.dll` 约定发现程序集，所以程序集名必须保持 `newsanguo_autoanthony`。

冒烟检查：

```powershell
dotnet run --project tools\SmokeTest\SmokeTest.csproj -c Release -- bin\Release\newsanguo_autoanthony.dll
```

---

## 卡槽计划（必须与 `src/SlotCards.cs` 一致）

newsanguo 0.2.38 的 `NewsanguoCardPool` 实测成员：

| 稀有度 | 手工卡数 | 说明 |
| --- | ---: | --- |
| Basic | 4 | Strike/Defend 各带 `RegisterCharacterStarterCard(...,4)` → 开局 10 张 |
| Common | 20 | |
| Uncommon | 40 | |
| Rare | 26 | |
| Ancient | 2 | TheTruestMask / DivineInsight，AA 无法生成该稀有度，**手工保留** |

本 mod 槽位：**96 = Basic 10 + Common 20 + Uncommon 40 + Rare 26**。
Common/Uncommon/Rare 与手工卡数量对齐，接管后稀有度分布不变；Basic 取 10 是为了承担初始牌组
（每槽 1 张 → 10 张独一无二的开局牌）。

改槽位数要同时改 `Adapter` 的常量与 `tools/gen_slotcards.py` 的 `PLAN`，重跑脚本即可。

## 版本契约

启动时用反射核对（`ApiVersion` 是 `const`，直接比较会被常量折叠而失效，故必须反射读）：

| 接口 | 要求版本 |
| --- | ---: |
| `ComponentApi` | 3 |
| `ComponentPackageApi` | 3 |
| `ComponentRuntimeApi` | 1 |
| `ExternalComponentCharacterApi` | 4 |

任一不符则跳过 AA 集成，并且**不会接管卡池** —— 否则池里会是一批永远装不上定义、`ForSlot` 必抛的槽卡。

## 验证状态

### 已完成（可复现）

- **干净重建通过**，0 错误；且是**对着玩家实际安装的东尼算法 0.3.113 编译的**
  （`refs\AutoAnthony.dll` 取自创意工坊 `3786611028`；0.3.104 那份旧参考留作 `AutoAnthony-0.3.104.dll`）。
- **静态冒烟检查 46/46 通过**（`tools/SmokeTest`）。它把产物按真实依赖关系加载，除结构核对
  （程序集命名、`[ModInitializer]`、ProfileId、96 个槽卡契约、两处补丁签名、**补丁点未被复写**、
  API 版本门、已安装 newsanguo 二进制的卡池成员数）之外，还**离线跑通了两条真正的运行路径**：
  - `TryRegisterAutoAnthony()`：版本门 → 自写目录构建 → `ComponentPackageApi.Register` 的强校验
    → 外部角色注册 → 运行期宿主槽类型校验 → 运行时路由注册；
  - `TryInstallDefinitions()`：**96 张卡生成 + 池级审计 + `InstallDefinitions` 全部成功（约 1.4 s）**。
- **AA API 版本门已对着 0.3.113 核对通过**：`ComponentApi`=3、`ComponentPackageApi`=3、
  `ComponentRuntimeApi`=1、`ExternalComponentCharacterApi`=4 —— 与 0.3.104 完全相同。
- **卡池成员数已对二进制核实**：已安装的 `newsanguo.dll` 里注册进 `NewsanguoCardPool` 的卡 = **92**
  （2 张先古 + **90 张可接管替换**），与源码扫描及 96 槽计划一致。
- **跨 mod 注册已实机确认可行**：首次实机运行日志里出现了
  `Registered 96 slot cards into NewsanguoCardPool (modId=newsanguo_autoanthony).`
- **安装布局已核实并已部署**：清单 `id` 与程序集名一致、依赖版本与实际安装版本匹配；
  已部署到 `mods\newsanguo_autoanthony\`，且部署的 DLL 与本地构建产物 SHA-256 一致。

### 实机卡死（已修复）

首次实机运行**在开局"先古之民"处卡死**。日志给出了完整因果链：

```
Registered 96 slot cards into NewsanguoCardPool ...
Startup failed: InvalidDataException: Self atom newsanguo/ops/temp_strength ... failed to compile localized text
```

**根因**：`SelfCatalog.Build()` 抛错 → `Startup` 在**槽卡已经注册进池之后**中断 →
池里留下 96 张**永远装不上定义**的槽卡 → 游戏任何一次 `Rarity`/`Generated` 读取都抛 → 卡死。
这是**我自己的顺序缺陷**，不是 newsanguo 或东尼算法的问题。

**四层修复**（前两层保证这类问题不再致命，后两层修掉具体不兼容）：

1. **调整注册顺序**：先完成 AA 侧全部注册，**再**把槽卡注册进卡池。AA 侧失败就直接返回，
   卡池保持原样 —— 半成品状态从结构上不可能出现。
2. **接管以"定义已安装"为前提**：`IsTakeoverActive = AA 已注册 && 槽卡已注册 && 定义已安装`。
   定义没装上时卡池重建返回原池内容，游戏照常可玩（只是没有接管）。
   注意这里踩了个循环依赖：`TryInstallDefinitions` 一度也用 `IsTakeoverActive` 做守卫，
   而该标志又由安装成功才置位 —— 会导致定义永远装不上、mod 彻底失效。已拆成
   `CanInstallDefinitions`（只需 AA 已注册）。
3. **自写文案编译失败不再抛错**，改为退回官方原子的本地化文案并记录（`SelfCatalog.LastFallbacks`），
   冒烟检查会断言降级数为 0。
4. **修掉两处 AA 0.3.113 的版本漂移**：官方语义 ID `ironclad/fightme/3` 已不存在（改用 `.../2`）；
   `temp_strength` 的文案写"2 点"但基线槽位值是 3（编译器按槽位字面量匹配），文案改为 3。
   这两处正是"靠语义 ID 克隆官方原子"这一设计随 AA 版本漂移的典型表现。

### 先古卡图串用（已修复）

实机反馈：**先古卡的卡图会套到非先古卡上**，两者尺寸不一样。

**根因**：`RefreshPortraitPool` 把接管前捕获的**全部**手工卡图收进同一个池子，其中包含 2 张先古
（`TheTruestMask` / `DivineInsight`），而 96 个槽位全是普通稀有度（Basic/Common/Uncommon/Rare），
按槽序取模循环时先古图就落到了普通卡上。AA 官方规则本来就是**普通牌排除先古图、先古牌只用本角色
自己的先古图**（`AutoAnthonyEditorApi` 的说明里也写明"普通牌排除尺寸不同的先古卡图"），旧内置代码
的注释里甚至写着"含 2 张先古 Ancient"，是同一个疏漏。

**修复**：立绘按稀有度分成 `_portraitPoolNormal` / `_portraitPoolAncient` 两组，由
`PortraitPoolFor(GeneratedRarity)` 按槽卡稀有度取用；当前槽位计划里没有先古槽，所以先古图不再参与
普通槽循环。`SlotRarities` 与 `PortraitPoolFor` 都有冒烟断言守住。

顺带修掉一处**兜底图路径全错**的问题：`FallbackSlotPortraits` 写的是 snake_case
（`wine_the_old_hero.png` 之类），但 newsanguo 的卡图文件名是**类名 PascalCase**
（`NewsanguoCardTemplate` 用 `$".../cards/{GetType().Name}.png"` 拼路径）—— 实测 6 张里 5 张不存在，
会让 ModelDb 就绪前的库预览显示破图。已改为真实存在的 PascalCase 路径。

### 立绘静默退回 6 张占位图（已修复）

实机反馈：**卡图数量明显减少**。

日志给出了确证：整个运行期**一次 `Collected portraits` 都没出现过**，
而 `Pool verification: AllCards total=100; AA slots present=96/96` 说明卡池接管本身是正常的。
因果链：

1. 立绘取自"**接管前**的原始池成员"（`_originalNewsanguoMembers`），而它只在
   `RebuildPoolContents` 里被捕获一次；
2. 但卡池成员表是**惰性**的 —— ModelDb 初始化阶段并没有枚举过它，
   于是 `OnModelDbReady` 调 `RefreshPortraitPool()` 时源仍是 `null`；
3. 旧代码在这个分支上**静默 return**，于是 `PortraitPoolFor` 一直返回
   `FallbackSlotPortraits`（6 张占位图）→ 96 个槽只有 6 种立绘。

**修复**：
- 新增 `EnsurePortraitSource()`：主动访问一次 `AllCards` 触发 Postfix 完成捕获；
  在 `OnModelDbReady` 与 **`OnRunStarted`**（局开始时卡池必然已枚举）各调一次，
  保证**第一局**就用上全量图池；
- **取消静默 return**：源不可用、或扫到 0 张可用图时都记 `ERROR`，
  并输出 `scanned` / `unusable` 计数，避免这类问题再次无从察觉；
- 冒烟检查新增 `EnsurePortraitSource` 存在性断言。

现在正常运行时日志应出现：
`Collected portraits: normal≈90, ancient=2, scanned=..., unusable=...`。
若出现 ERROR 或 `normal` 很小，说明图池没收集到，而不是"卡图变少了"。

### 从卡池反推目录（`tools/derive_catalog.py`）

不再手写文案：把**卡牌源码 + 本地化 + 数值变量**三份数据合并，反推出壳。

| 数据 | 来源 | 得到 |
| --- | --- | --- |
| 卡牌源码 `Scripts/Cards/*.cs` | 构造函数 / `CanonicalVars` | 费用、类型、目标、稀有度 |
| `localization/{zhs,eng}/cards.json` | 按键 `<MOD>_CARD_<SNAKE(类名)>` | 中英卡名与描述 |
| 变量声明 → 占位符名 | `DamageVar→Damage`、`PowerVar<T>→T`、`HeavensForceVar→HeavensForcePower` | 把 `{Damage:diff()}` 换成实际数字 |

**匹配手法**：把卡面描述与原子文案都做「数值无关化」（数字→`#`）再比对，于是
「造成13点伤害。」能匹配到既有原子「造成6点伤害。」。两种键并存：

- **字面键**（已代入数值）→ 对上原子文案里的字面量；
- **变量键**（保留 `{Energy}` 这样的变量名）→ 供别名表使用。卡面常把单位省成图标
  （`获得{Energy:energyIcons()}。` 没有"点能量"字样），字面键永远匹配不上，
  靠别名表 `ALIASES` 显式接上，**不新造原子**。

产物 [src/SelfCatalog.Derived.cs](src/SelfCatalog.Derived.cs)（`--emit` 生成）：

- **与既有壳形状去重**（费用/类型/目标/稀有度/原子序列全同则跳过）——
  `StrikeNewsanguo`/`DefendNewsanguo` 就因与 `NSBasicStrike`/`NSBasicDefend` 完全同形而被跳过；
- ⚠ **数值不随卡面固定**：AA 的组件身份只认结构，具体数字由数值策略围绕中心值采样生成。
  所以反推保留的是**形状**（费用/类型/目标/稀有度/效果序列），不是原卡的确切数字。

**实测效果**（92 张池成员，随待办推进持续更新）：子句匹配 **94/180（52%）**，完全可推导 **29 张** →
去重后生成 **25 张壳**（`StrikeNewsanguo`/`DefendNewsanguo` 与既有 Basic 壳同形而跳过）。
壳按稀有度已达 `Basic 5 / Common 21 / Uncommon 39 / Rare 20`（需求 10/20/40/26），
目录规模 85 壳 / 50 声明原子（49 个实际组件）。

**条件类的一个陷阱**：原卡常把条件与效果写在**同一句**（"如果敌人的意图是攻击，则获得3点能量。"），
而 AA 要求它们是两个原子。只按句号切会整句对不上裸条件原子，于是**已有条件原子的卡也被误判成"缺原子"**。
`match_compound()` 逐个逗号试切
**条件类为什么不能靠加产 `condition` 变体解决**：AA 的 `condition` 是封闭集合
（只有 `IComponentRuntimeHandler` 一个扩展点，且 AA 会校验 owner 必须指向真正的条件原子），
mod 扩不了。条件类待办只能走「**自带判断的效果 opcode**」：把判断放进 handler，
**阈值走 variant、授予量走数值槽**（handler 只能拿到单一 `Amount`），
条件不成立时返回 `true`（`false` 在 AA 眼里是"执行失败"）。
代价：AA 按无条件效果估值，且一个条件一个 opcode、不能组合。
已实机验证 `ns_heaven_if_le` 与 `ns_stun_all_if_wine`；详见 [docs/STATUS.md](docs/STATUS.md)。
，且只在"前半是 `cond_` 原子且后半也能匹配"时才认；
效果那半常带连接词「则」，查表时用 `strip_connective()` 去掉。生成的壳里
条件 owner 为 `-1`、被门控效果 owner 为**条件下标**（照抄官方 `GoForTheEyes`）。

**原子待办**：报告里的「差 1~2 条就成」清单就是待办。推进时**先查运行时目录**把每条分成两类：

- **能用现成 opcode 表达** → 只需加一个原子，零风险。已用此法补上
  `exhaust_non_attack`（`ironclad/secondwind/0` 就是 `exhaust_card/all` + `CardFilter=non_attack`）
  与 `retain_hand`（`apply_power/retain_hand_this_turn`）；
- **词汇表里没有** → 必须自定义 opcode + handler（B 档那套）。已确认属于这类的有：
  生成指定 Token 卡（军杖/士兵）、挖坟（**没有任何 `move_card` 的 `src=exhaust`**）、
  击晕/飞行（`apply_power` 的 18 个变体里都没有）、易伤不减少、以及
  「每当你抽到这张牌，增加一张其复制品到你的手牌」（`create_copy` 只有
  `this_card→弃牌堆` 与 `referenced_card→手牌`，**没有 `this_card→手牌`**）。

报告在 `refs/derive-report.txt`。

### B 档：自定义 opcode（首批 5 个 / 7 个原子）

词汇表里查不到的效果，走"自定义 opcode + 我们自己的 `IComponentRuntimeHandler`"（`ns_gain_wine` 的同一套路）。
handler 都在 [src/RuntimeRoutes.cs](src/RuntimeRoutes.cs)，路由在 `RuntimeRoutes.EnsureRegistered()` 注册。

| opcode | 效果 | 复用什么 |
| --- | --- | --- |
| `ns_lose_heaven` | 失去天意之力 | `NewsanguoPublicApi.AddHeavensForce`（负增量） |
| `ns_frail` | 施加脆弱（自身 / 目标两种原子） | 原版 `FrailPower` —— AA 的 `apply_power` 词汇表没收录 frail |
| `ns_entangled` | 本回合不能打出攻击牌 | newsanguo `EntangledPower` |
| `ns_dragon_omen` | 施加帝王之征（自身 / 目标两种原子） | newsanguo `DragonOmenPower` |
| `ns_scry` | 预见 N | newsanguo `ScryCmd.Scry` |

目标解析统一用 `context.Target ?? 自己` —— 原子的 `Target` 决定执行器传什么，
自身效果拿不到目标时落回自己（与内建 `gain_block` 等行为一致），因此一个 opcode 就能同时支持自身/目标两种原子。

**每个原子都配了收益原子组成一张完整可用的牌**（例如 `NSFrailTarget1` = 造成6点伤害 + 给予1层脆弱），
否则"只减益自己"的壳放进生成池没有意义。

⚠️ **两个踩过的坑，都已加防护**：

1. **基线必须在"运行时目录"里选，不能只查 `catalog_runtime_specs.json`。**
   后者有 **931** 条，是超集；六个角色的**运行时**生成目录合计只有 **467** 个原子
   （Ironclad 84 / Silent 72 / Defect 81 / Necrobinder 95 / Regent 79 / Colorless 56）。
   例如 `ironclad/bloodletting/0` 在 JSON 里有、但不在运行时目录里，只查 JSON 会让 `Build()` 抛
   `missing official baseline`。现在 `gen_extra_atoms.py` 接受第二个参数（`--dump-ironclad-atoms`
   导出的 `runtime-atoms.tsv`）做白名单校验，基线不在里面就直接报错，不再等到运行时才炸。
2. **基线是 Power 配方时不能照抄卡牌类型。** 例如 `necrobinder/friendship/2`（`NCR:LoseStrength`）
   属于一张 Power 卡，照抄会让"给予自身脆弱"变成 Power 牌（留在场上）。脚本里用 `type=` 显式覆盖。

**目录数据已持久化到 `refs/aa-catalog/`**（4 个 JSON + `runtime-atoms.tsv`），
所以重新生成是可复现的两步（原先放在临时目录，被清理后生成器就跑不动了）：

```powershell
dotnet run --project tools\SmokeTest -- bin\Release\newsanguo_autoanthony.dll --extract-embedded refs\aa-catalog
dotnet run --project tools\SmokeTest -- bin\Release\newsanguo_autoanthony.dll --dump-ironclad-atoms > refs\aa-catalog\runtime-atoms.tsv
python tools\gen_extra_atoms.py refs\aa-catalog refs\aa-catalog\runtime-atoms.tsv
```

### 剪掉既有原子（39 → 15）

按"**与新手写清单重复者保留、其余删除**"剪枝既有目录。脚本 [`tools/prune_legacy_atoms.py`](tools/prune_legacy_atoms.py)
（确定性、可重跑，`--apply` 才落盘），原文件备份在 `backup/SelfCatalog.cs.orig`。

保留的 15 个（都在新清单里有对应条目）：

| 保留 | 对应新清单条目 |
| --- | --- |
| `slash` / `sweep_all` / `strike_wave` | 造成6点伤害 / 对所有敌人造成8·12点伤害 / 对所有敌人造成4·5点伤害4次 |
| `guard` | 获得5/8点格挡 |
| `draw` / `energy` / `hp_loss` | 抽3/4张牌 / 获得2/3点能量 / 失去2点生命 |
| `exhaust_choose` / `copy_to_discard` | 消耗一张手牌 / 将一张此牌的复制品加入弃牌堆 |
| `weak_1` / `vuln_2` / `vuln_all_1` | 给予1层虚弱 / 给予目标1·2层易伤 / 给予所有敌人1层虚弱和易伤 |
| `wine` / `heaven` | 获得4/6点酒力 / 获得2/3点天意之力 |
| `m_extra_hit` | 则攻击两次 |

删除的 24 个：`flurry`、`snipe`、`heal`、`max_hp`、`temp_strength`、`exhaust_random`、`return_to_top`、
`attack_back`、`vuln_double`、`str_loss_turn`、`strength`、`plating`、`enemy_strength`、
`trigger_start`、`trigger_end`、`when_block`、`when_exhaust`、`when_vuln`、`when_hp_loss`、`rule_vuln_amp`、
`m_dmg_per_vuln`、`m_block_per_str`、`m_per_exhaust`、`m_body_slam`。

**连带删掉 27 张壳**（`SelfCatalog.Build()` 要求壳引用的原子必须存在，反之每个原子也必须被壳引用，
两边必须同时改）。注意 7 个触发器原子与 5 个修饰原子被删后，所有 Power 类壳
（`NSPyre`/`NSDemonForm`/`NSJuggernaut`/`NSDarkEmbrace`/`NSVicious`/`NSInferno`/`NSRage`/`NSExhaustPower`/
`NSBarricadeLike`/`NSCruelty`）也随之消失。

**当前规模**：声明 28 个原子（15 保留 + 13 A 档）/ **实际 27 个组件**（`dmg11x3` 与 `dmg6x2` 结构相同被合并）；
**38 张壳**。3 张 Basic 壳因 `slash`/`guard`/`vuln_2` 都被判定为重复而幸存，初始牌组不受影响。

**已验证**：`tools/SmokeTest` 48 项全过，其中 `TryInstallDefinitions` 用 **5 个不同种子**各生成
96 张卡并通过池级审计（壳变少时"唯一签名用尽"可能只在部分种子触发，所以单种子不算数）。

⚠️ **代价（需要留意）**：壳的供给已经明显低于槽位需求 ——

| 稀有度 | 壳数 | 槽位需求 |
| --- | ---: | ---: |
| Basic | 3 | 10 |
| Common | 14 | 20 |
| Uncommon | 11 | 40 |
| Rare | 10 | 26 |

也就是说大量生成卡会**只有数值不同、结构重复**（AA 靠数值策略拉开差异，所以仍能生成并通过审计），
而且比之前更接近 emergency fallback 的边界。要恢复形态多样性，得靠 B 档补齐新结构（尤其是触发器与修饰类）。

### A 档原子落地（13 个）

`src/SelfCatalog.Extra.cs`（由 `tools/gen_extra_atoms.py` 生成）+ `SelfCatalog` 的数值覆写能力。

新增的 13 个原子都是**结构上真正新的**（既有目录里没有的）：

| 原子 | 结构 | 基线 |
| --- | --- | --- |
| `dmg6x2` / `dmg11x3` | 单体多段（既有只有单体单段、随机多段、全体多段） | `ironclad/fightme/0` / `regent/celestialmight/0` |
| `temp_dex2` | 本回合敏捷（`template_self_action`/`n_tempdex`） | `silent/anticipate/0` |
| `intangible1` | 无实体（`n_intangible`） | `silent/wraithform/0` |
| `weak_all1` | 全体虚弱 | `silent/haze/1` |
| `target_str_loss2` | 令目标失去力量 | `necrobinder/sharedfate/1` |
| `discard_all` / `draw_to_full` / `upgrade_self` / `end_turn` | 丢全部手牌 / 抽满手牌 / 升级自身 / 结束回合 | `silent/shadowstep/0` 等 |
| `cond_cards_below3` / `cond_fatal` / `cond_intends_attack` | 条件判定（按官方 GoForTheEyes 惯例配对：条件 `owner=-1`，被门控效果 `owner=条件下标`） | `defect/ftl/1` 等 |

**关键发现：「只改数字」的原子是无效的，甚至有害。**
第一次我按你列的数值做了 31 个原子（含「造成3/5/7/8/10/14/15/16点伤害」等），结果**只新增了 12 个组件**
（`recipes=83` 而 `atoms=51`），而且包校验直接报
`component newsanguo/ops/dmg6x2 localization does not reproduce its Chinese projection`。原因是：

- **组件身份由 `RuntimeSpec` 结构决定，数值只是槽位**。所以 8 个「不同伤害数字」的原子会与既有的
  `slash`(6 点) 合并成同一个组件 —— 谁的文案/中心值存活是不确定的。
- **数值本来就由策略生成**：`ComponentApi.DefaultValuePolicy` 会围绕原子的值作**采样中心**
  （`SampleAroundCenter` / `ScaleRewardCenter` / `ClampSampledValue`），按稀有度与收益行数缩放。

所以想要「伤害在 3~16 之间变化」不需要 8 个原子，**一个组件 + 数值策略**就够；要调数字就调原子的值
（= 采样中心），要调分布则替换 `IComponentValuePolicy`。这也是为什么官方 931 条 spec 里伤害五花八门、
却只有少数几种 `deal_damage` 结构。

**影响**：壳从 52 张增至 65 张，生成池的可选形态变多。原子与壳的 `Cost`/`Rarity` 目前由
`tools/gen_extra_atoms.py` 里一张**暂定价目表**按量级推导（伤害按 `damage×hits`、格挡按 0.75 折算），
不合适就直接改那张表重跑脚本。

### 版本事实（都是踩过的坑）

- 实际订阅的东尼算法是 **0.3.113**，不是 Wiki 记的 0.3.104；好在 API 版本没变，兼容。
- 已安装的 newsanguo 是 **0.2.37**（创意工坊 `3784364146` 与 `mods\newsanguo` 的 DLL
  **字节完全相同**），而源码仓库在 0.2.38。两者卡池成员数一致（92），但**依赖版本号必须以安装版为准** ——
  清单原先写 `min_version 0.2.38` 会被依赖检查直接拒掉，已改为 `0.2.37`。
- 东尼算法**由游戏直接从创意工坊目录加载**（日志里可见
  `Found mod manifest file ...\workshop\content\2868840\...`），**不需要**手工复制进 `mods\`。
- 自写目录靠**官方语义 ID** 克隆数值基线，AA 改官方目录就会失配。`tools/SmokeTest` 的
  `--dump-ironclad-atoms` 会导出当前 AA 的官方 Ironclad 原子表（ID/模板/opcode/variant/目标/数值槽），
  便于核对。

### 游戏内实测状态

**已实机确认**（每轮修复后由实机反馈）：

- 卡池接管真的生效：生成卡能进入奖励与牌组；
- 开局"先古之民"处的**卡死已修复**；
- 立绘：先古图不再套到普通卡上、图数量恢复正常（含 `Collected portraits` 计数）；
- B 档执行路由（脆弱 / 帝王之征 / 缠身 / 预见 / 失去天意之力）**均正常**；
- newsanguo 0.2.39 第一批新入口（生成军杖·士兵、复制品入手、飞行、国贼）**均正常**；
- 0.2.39 第二批新入口（不胜酒力**入弃牌堆**、击晕、消耗非攻击牌按张给天意、
  手牌变化为士兵、音量降至 25%）**均正常**。

> 执行路由的完整清单以 [src/RuntimeRoutes.cs](src/RuntimeRoutes.cs) 为准（33 个 opcode）；
> 冒烟检查会打印组件按 opcode 的分布，可用来核对每个 opcode 是否真的进了目录。

**仍未经实机确认**：

- 图鉴（compendium）与奖励界面对 96 张槽卡的展示是否正确；
- **读档 / 多人同步**；
- Basic 壳只有 5 个（需求 10），开局 10 张牌组的稀有度分布是否总能达标。

启动游戏后，可在 `%APPDATA%\SlayTheSpire2\logs\godot.log` 里搜 `[AAadapter]`。
