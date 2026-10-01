# 项目状态与交接说明

> 收口快照。**状态、决策、教训写在本文件**；`README.md` 只讲"怎么用"（命令、配置、依赖）。
> 两份文件避免重复，否则一定会漂移（已经漂移过一次）。

## 一句话

**Auto-Anthonyology Newsanguo**（mod id `autoanthony_newsanguo`）：把**东尼算法（AutoAnthony）**的随机卡池
接到 **newsanguo** 角色上，完全复刻旧实现的行为（整池接管 + 替换初始牌组）。

> **改名历史**：原名 `newsanguo_autoanthony`，现名 **Auto-Anthonyology Newsanguo**。
> 显示名与技术标识**都已同步改**为 `autoanthony_newsanguo`（mod id / 程序集名 / 部署目录 /
> 清单文件名统一）。代价：对游戏而言这是**另一个 mod** —— 旧的 `mods\newsanguo_autoanthony\`
> 必须删掉，否则两个 mod 会同时注册内容、互相冲突；旧存档里该 mod 的归属也可能认不出来。

## 当前规模

| 项 | 数量 | 说明 |
| --- | ---: | --- |
| 壳 | **101** | Basic 5 / Common 24 / Uncommon 46 / Rare 25 / **Ancient 1**（槽位需求 10/20/40/26/0） |
| 原子（声明 / 实际） | **62 / 61** | 1 个因结构相同被 AA 合并；其中 12 个是触发原子 |
| 自定义 opcode | **23** | 全部经 `NewsanguoPublicApi`，无对 newsanguo 实现类型的硬引用 |
| 卡槽 | 96 | Basic 10 / Common 20 / Uncommon 40 / Rare 26 / Ancient 0 |
| 反推覆盖 | **98/180 子句（54%），31/92 张卡完整可反推** | 去重后生成 29 张壳 |

依赖：`STS2-RitsuLib >= 0.6.2`、`newsanguo >= 0.2.39`、`AutoAnthony >= 0.3.104`（实装 0.3.113）。

**已知死壳**：那 1 个 Ancient 壳永远不会被用到（槽位计划里先古槽是 0）。它来自某个远古卡的完整反推。
留着无害（`PortraitPoolFor` 已按稀有度分组，先古图不会套到普通卡上），但它是 101 里唯一空转的壳。

## 三种扩展模式（核心）

AA 只给了一个运行期扩展点：`IComponentRuntimeHandler`（**效果**执行器）。

### 模式 A：效果 opcode

调用面**收敛在 `NewsanguoPublicApi`**。唯一保留的直接引用是原版 `FrailPower`（游戏本体，稳定）。
需要读 newsanguo 状态而没有公开入口时，**按类型名匹配**而不是硬引用类型：

```csharp
enemy.Powers?.FirstOrDefault(p => p.GetType().Name == "DragonOmenPower")
```
改名时最坏只是该效果失效，而硬引用类型会**加载即崩**。

### 模式 B：自带判断的效果 opcode（条件类）

AA 的 `condition` 是**封闭集合**，mod 扩不了。所以条件不拆成两个原子，
而是**把判断放进 handler**：**阈值走 variant、授予量走数值槽**（handler 只能拿到单一 `Amount`），
**条件不成立返回 `true`**（`false` 在 AA 眼里是"执行失败"）。

代价：AA 按**无条件**效果估值；一个条件一个 opcode、不能组合。

**适用判据**：只有"**条件和效果都以普通效果形式存在**"时才能走这条路。
凡是"条件要改变这张牌自身的结构"（攻击次数/费用/目标）都做不了 ——
例如「如果敌人的意图不是攻击，则**攻击两次**」是闭环死结。

### 模式 C：触发类（**能做**，此前结论是错的，已更正）

AA 里有 **35 种触发种类**，编码在 spec 的 `Trigger` 字段里 —— **不在 Variant 里**
（70 个 `trigger` 原子的 Variant 全叫 `event`）：

```
Trigger: {'Kind': 'card_played', 'Lifetime': 'combat', 'ThresholdSlot': None, 'DurationSlot': None}
```

用法：**克隆官方触发原子 + 把我们的效果挂在 owner=0**
（触发器 owner=-1、被门控效果 owner=0，照抄官方 `ironclad/juggling`）。

**已实机验证**：`ns_gain_heaven`（自定义 opcode）挂在官方 `card_played` 触发器下，
天意之力确实随每次出牌增长。**官方门控触发器 + 自定义 opcode 效果是 AA 认可且可运行的结构。**

两条配套要求：

1. **触发种类不能自造**，必须克隆一个官方原子来借用它的 `Kind`；
2. **反推工具的门控逻辑必须认 `trigger_`**，否则触发卡会被推成两个并列效果
   —— 触发器变摆设、效果无条件生效。这是最容易被静默丢掉的正确性。

已进目录 12 种：`turn_start` / `turn_end` / `card_played` / `skill_played` / `power_played` /
`attack_played` / `card_exhausted` / `owner_hp_lost_during_turn` / `vulnerable_applied` /
`block_gained` / `strike_card_drawn` / `draw_pile_shuffled`。

### 仍然做不了的（真正的例外）

只有**非玩家动作驱动**的事件，AA 用的是 `Modifier` 形态（owner=-1、不门控任何东西）：

| 待办 | 死在哪 |
| --- | --- |
| 每当你**抽到这张牌** → 加复制品 | `r_wheneverdrawn` 是 `Modifier`，不是门控触发器 |
| 每当有**生物死亡** → 获得力量 | `ncr_whenevercreaturedies` 同样是 `Modifier` |
| 敌人意图不是攻击 → **攻击两次** | 条件读得到，但 handler 没有修改**这张牌自身**攻击次数的通道 |

## 反推工具的匹配规则（踩出来的）

1. **两种键各取所需**：字面键用**已代入数值**的文本（对上原子文案里的字面量）；
   变量键用**保留占位符**的原文（供别名表表达"这是 Energy 这一类效果"）。
2. **未解析的占位符 = 值通配。** 声明在卡文件之外的变量（如 `{DivineInsightPower}`、`{Summon}`）
   静态解析不到；把它们留在键里会让整条子句永远匹配不上。
   字面键里把 `{Name}` 也归一化成 `#` 即可。**只加字面键**，变量键要保留 `{Name}`。
   （这一条把覆盖率从 94 推到 98。）
3. **汉字数字只在"汉字数字 + 点"这一种组合上归一化。** 全局替换会把
   「上一张牌」「一名敌人」「攻击两次」这类**量词**也换掉、污染键空间（试过，已撤回）。
   而「三点」是**阈值**的写法，必须与「3点」等价 —— 否则我们自己按显示规则改成汉字的原子
   就再也匹配不上卡池原文（实测覆盖率因此掉过 2 条）。
4. **条件句与触发句都是门控**，可与后半段效果写在**同一句**里。
   `match_compound()` 逐个逗号试切，前半必须是门控原子（`cond_` / `trigger_`），
   后半也匹配才认。效果那半常带连接词「则」，查表时用 `strip_connective()` 去掉。

## 文案数字的规则（重要，踩过两次）

> **属于槽位值的数字必须写阿拉伯数字**（否则 AA 找不到锚点）；
> **不属于槽位值的数字必须写汉字**（否则会被按位置绑定、再用槽位值覆盖）。

AA 渲染卡面描述时把文案里的**阿拉伯数字按位置**绑定到数值槽，再用**槽位值**渲染回去 ——
**`hidden` 槽也不例外**。实测两次：文案写「耗能变为**0**」而基线槽位是 1 → 卡面显示「变为 **1**」；
阈值写「不小于 **3** 点」而基线槽位是 1 → 卡面显示「不小于 **1** 点」。

## 门禁：一条命令多个维度

`powershell -File tools\verify.ps1`（加 `-Deploy` 顺带部署到 mods）。

| 维度 | 挡什么 | 当前 |
| --- | --- | --- |
| 构建 | 编译错误 | 0 错误 |
| 冒烟 48 项 + 12 种子 | 目录构建、注册、生成/审计、立绘分组 | 48/48 |
| 覆盖率 `--min-rate` | **覆盖率悄悄下降** | 98/180（54%），下限 54 |
| 审计：文案数字 | 卡面显示与定义不符 | **0** |
| 审计：缺基线 | 基线不在运行时目录 → `Build()` 抛错 | **0** |
| 审计：形态分布 | **某一格（稀有度×类型）空缺或塌陷** | **0 违规** |

两条原则：

- **覆盖率下限只能升不能降**，升了要一起提高 `verify.ps1` 里的 `MinRate`；
- **"门禁通过"不算证据，"门禁能拦住违规"才算。** 所以 `--min-rate` 与形态下限都做过
  反向测试（注入违规、确认报错并返回非 0）。

**形态分布是"分布问题"，不是"正确性问题"** —— 池子能生成、卡都合法、覆盖率也不掉，
但某一格空了只有人眼看卡才发现（实测：Rare 只有 1 张能力牌，因为 12 个触发能力牌
全被设成了 Uncommon）。所以它单独成一项。

## 已验证 / 未验证

**已实机确认**：卡池接管、开局不卡死、立绘分组与数量、B 档全部执行路由、
newsanguo 0.2.39 两批新公开入口、挖坟（弹消耗牌堆选择界面）、六个自带判断的条件 opcode、
牌堆取放（回旋 / 倒卷）、**触发类**（`card_played` 一种）。

**未实机确认**：

1. **读档 / 多人同步** —— 唯一完全没覆盖的维度。条件类效果依赖回合内状态
   （`CombatManager.Instance.History`、手牌数），读档场景最容易出问题；
2. 图鉴与奖励界面对 96 张槽卡的展示；
3. Basic 壳只有 5 个（需求 10）—— 已量化：扫 40 个种子 **40/40 通过**，
   且有 `BasicCoverageMaxPasses = 64` 次重掷兜底。注意重掷耗尽时代码是"**接受未达标**"并打日志，
   不是崩溃 —— 真正的退化形式是"开局牌组伤害/格挡偏低"；
4. 「倒卷」的槽位覆写为 0 是否被 AA 钳回 1（若被钳，改用汉字写法规避）；
5. **其余 11 张触发卡**是否按条件反复触发（只验了 `card_played`）。

## 踩过的坑（都会再犯，务必先读）

1. **无自定义 opcode 的原子，基线 opcode 就是它的行为。**
   挑基线不能只看"有没有数值槽和 Flags"，必须看 opcode 干什么。踩过三次：
   `regent/voidform/0`（opcode = `end_turn`）用在 4 个原子上 → 打出直接结束回合；
   `ironclad/armaments/1`（`i_upgrade` = 升级手牌一张）用在"升级自身"上 → 描述与效果不符；
   基线是 Power 配方时照抄类型 → 即时效应当了 Power 牌。
   有脚本兜底：`tools\audit_baselines.py` 的「需人工确认」栏，每项都要能说出"基线语义与文案是同一件事"。
2. **删/改原子后必须重跑 `derive_catalog.py --emit`**，否则反推壳持有悬空引用
   （`Recipe NSDDeafenMe references unknown self atom`）。好在 `Build()` 会点名报错。
3. **自身效果不能依赖 `context.Target`。** AA 连自身效果也会填 Target，
   于是"给予自己帝王之征"打到了别人身上。自身效果一律用 `*_self` opcode + `TryActor`。
4. **AA 的生成卡类型从原子推断**，不只看配方的 `GeneratedCardType`。
   壳含伤害原子 → 算作 Attack → 若该壳又施加"本回合不能打出攻击牌"，打出即非法、回合结束。
5. **给已有方法加可选参数 = 改签名**，已编译调用方抛 `MissingMethodException`，
   **不是**"用默认值继续"。要加参数就保留旧签名转发 + 新增无默认值的重载。
6. **别名表是精确字符串匹配**，键写错不报错、只静默失效（曾因此误判"别名收益小"）。
7. **升级效果落在 `CardModel` 的缓存字段里**（`_energyCost`/`_dynamicVars`…）。
   "清缓存重推导"的判据必须是"**已升级的牌跳过**"，否则吞掉玩家花资源做的升级。
8. **同一症状可以有两个独立原因。** 改动后症状未消失，只能说明"这个原因不是唯一原因"，
   **不能**说明"这个原因不存在"。
9. **不要用一个反例推翻一整类。** 犯过两次：把"改了基线 bug 还在"当成"基线假设被证伪"；
   看到 `r_wheneverdrawn` 是 `Modifier` 就断言"整个触发类做不了"（实际有 35 种可用）。
   正确说法是"**我验证过的这一种不行**"。
10. **临时脚本会与真工具漂移。** 另写的 A/B 复刻与真工具差了 1 条子句；
    另写的探针报"0 张卡有残留占位符"而真报告里明明有。
    **检查逻辑要基于 `scan()`，不要另写复刻。**
11. **归因前先做单变量 A/B，别凭"最近改了什么"猜。** 覆盖率掉 2 条时先怪触发原子
    （`ab: regressed=0 improved=0`，无辜），又怪复合句门控（改了无变化），
    真凶是**自己**为修显示把阈值文案改成汉字、打断了字面匹配。
12. **别拿过期的数字做对比。** 用自己的形态表和**上一次**冒烟输出的数字比，
    得出"多算了 3 条 Common"，据此加了个没必要的修正 —— 实际两边一致
    （那次冒烟是 97 壳，当前是 101 壳）。
13. **循环变量在循环外是"最后一行"，不是"匹配行"。** 据此打印过完全错误的基线信息。
14. **写含中文的 `.ps1` 必须带 UTF-8 BOM。** PowerShell 5.1 对无 BOM 的 `.ps1` 按 ANSI 解码，
    中文被破坏后连字符串引号都会撑破，脚本整个跑不起来。
15. **部署 / 写 mods 目录的命令必须带 `danger-full-access`**，漏传时报
    `MSB3021 Access denied` —— 那是沙箱拒绝，不是代码问题。
16. **Python 报告必须自己写 UTF-8 文件。** 直接 print 会被 PowerShell 管道按 GBK 解码，中文落盘即乱码。

## 怎么继续

```powershell
# 一条命令跑完所有门禁（推荐；加 -Deploy 顺带部署）
powershell -File tools\verify.ps1
powershell -File tools\verify.ps1 -Deploy

# 单独跑
dotnet build NewsanguoAutoAnthonyAdapter.csproj -c Release --no-incremental /p:DeployToMods=true
dotnet run --project tools\SmokeTest -- bin\Release\autoanthony_newsanguo.dll
python tools\derive_catalog.py --emit          # 反推 + 生成壳（改原子后必跑）
python tools\derive_catalog.py --min-rate 54   # 覆盖率断言
python tools\derive_catalog.py --ab trigger_   # 单变量 A/B：某前缀原子对覆盖率的影响
python tools\audit_baselines.py                # 三维审计（文案数字 / 缺基线 / 形态分布）
python tools\gen_extra_atoms.py refs\aa-catalog refs\aa-catalog\runtime-atoms.tsv
```

**两个必须知道的坑**：

1. **基线必须在"运行时目录"里选**，不能只查 `catalog_runtime_specs.json`（931 条是超集，
   运行时只有 467 条）。`gen_extra_atoms.py` 的第二个参数就是白名单。
2. **覆盖率数字只有一个来源：`derive_catalog.py` 的 `scan()`。** 别另写复刻（见坑 10）。

## 下一步候选

1. **补触发类待办** —— 种类库已就位：「每打出一张牌获得天意」已有壳；
   「每消耗一张牌永久增伤」要先判断是不是又一个"改自身结构"的死结。
2. **形态分布的下限可以再收紧**（当前留了余量，只抓坍塌）。
3. **补齐未验证维度**：读档 / 多人同步（最该先做）。
4. **C 类其余机制**：难以杀灭层数、附魔「灵魂之力」、延迟 Token 生成。

## 快照

- 构建产物哈希与已部署 DLL **一致**；
- `tools\verify.ps1` **全维度全绿**；
- 备份：`backup/SelfCatalog.cs.orig`（剪枝前的 39 个既有原子）。
