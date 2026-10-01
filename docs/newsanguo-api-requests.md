# newsanguo 公开 API 追加请求

> 来源：`autoanthony_newsanguo`（东尼算法 × newsanguo 适配器）侧。
> 收件方无需了解适配器内部，按本文档实现即可。

## 背景

- 独立 mod `autoanthony_newsanguo` 把东尼算法（AutoAnthony）的随机卡池接到 newsanguo 角色上。
- 它**硬引用 `newsanguo.dll`**，清单声明 `newsanguo >= 0.2.39`。
- 原则：适配器**只调用 `NewsanguoPublicApi`**，不直接引用 `Scripts/Powers`、`Scripts/Combat`、
  `Scripts/Cards` 里的实现类型。那些虽然多数是 `public`，但不属于契约面 —— 一旦改名或挪命名空间，
  适配器会**在加载时**抛 `MissingMethod` / `TypeLoadException` 直接崩，而不是运行时优雅降级。

## 0.2.39 已交付的入口（**不要重复实现**）

`GetDrunkenMight` · `AddTokenToHand(player, kind, count)` · `CopyCardToHand(player, sourceCard, count)`
· `ApplyTraitorTyranny` · `ApplyEntangled` · `ApplyDragonOmen` · `ApplyFlight` · `Scry`
· `NewsanguoTokenKind { MilitaryCudgel = 0, Soldier = 1, Lightweight = 2 }`

## 通用兼容规则（所有改动都适用）

1. **不要修改已有方法的签名。**
   在末尾追加可选参数**等于改签名**——已编译的调用方会抛 `MissingMethodException`，
   而**不是**"用默认值继续执行"。需要加参数时：**保留旧签名作为转发，新增一个不带默认值的重载。**

   ```csharp
   // 旧签名保留，仅转发
   public static Task<int> Existing(Player p, int n = 1) => Existing(p, n, PileType.Hand);
   // 新重载不带默认值，避免与旧签名产生重载歧义
   public static async Task<int> Existing(Player p, int n, PileType destination) { ... }
   ```

2. 枚举成员**只追加**：不改名、不修改已有成员的值。
3. 新增成员一律 `public static`，放在 `NewsanguoPublicApi`。
4. 每个新入口加一行 XML 注释说明它**不做什么**（例如"不施加额外效果"），避免适配器误判语义。

---

## 请求 1（高优先级）：Token 生成支持指定去向

**现状问题**：`AddTokenToHand` 只能进手牌，而 newsanguo 自己的卡
「真的是要醉啦！」（`ImGettingDrunk`）是把「不胜酒力」加入**弃牌堆**。
适配器现在无法表达这个去向，因此这张卡对应的原子无法实现。

**`PileType` 的可用值**：`Hand` / `Draw` / `Discard` / `Exhaust` / `Deck` / `Play`。

### 1a. `NewsanguoPublicApi.AddTokenToHand` 加重载

```csharp
// 旧签名原样保留，只转发（保证已编译的调用方不炸）
public static Task<int> AddTokenToHand(Player player, NewsanguoTokenKind kind, int count = 1)
    => AddTokenToHand(player, kind, count, PileType.Hand);

// 新签名：不加默认值，避免重载歧义
public static async Task<int> AddTokenToHand(
    Player player, NewsanguoTokenKind kind, int count, PileType destination)
{
    if (player is null || count <= 0 || CombatManager.Instance.IsOverOrEnding) return 0;
    if (player.Creature?.CombatState is not { } combatState) return 0;

    int created = 0;
    for (int i = 0; i < count; i++)
    {
        CardModel? token = kind switch
        {
            NewsanguoTokenKind.MilitaryCudgel
                => await MilitaryCudgel.CreateInPile(player, combatState, destination),
            NewsanguoTokenKind.Soldier
                => await AddGeneratedToPile(combatState.CreateCard<Soldier>(player), player, destination),
            NewsanguoTokenKind.Lightweight
                => await AddGeneratedToPile(combatState.CreateCard<Lightweight>(player), player, destination),
            _ => null,
        };
        if (token is not null) created++;
    }
    return created;
}
```

### 1b. `MilitaryCudgel` 目前把 `PileType.Hand` 写死了，需要开对应口子

```csharp
public static Task<CardModel?> CreateInHand(Player owner, ICombatState combatState)
    => CreateInPile(owner, combatState, PileType.Hand);

public static async Task<CardModel?> CreateInPile(
    Player owner, ICombatState combatState, PileType destination)
{
    if (CombatManager.Instance.IsOverOrEnding) return null;
    CardModel cudgel = combatState.CreateCard<MilitaryCudgel>(owner);
    await CardPileCmd.AddGeneratedCardsToCombat([cudgel], destination, owner);
    return cudgel;
}
```

### 1c. 私有辅助 `AddGeneratedToHand(card, player)` 泛化为 `AddGeneratedToPile(card, player, destination)`

把其中的 `PileType.Hand` 换成参数。

### 1d. 三个请顺手挡掉的坑

1. **只放行 `Hand` / `Discard` / `Draw`，`Play` 与 `Deck` 应拒绝或断言。**
   战斗中往这两个堆塞生成卡几乎不会是调用方的本意，静默接受会变成难查的怪 bug。
2. **`Draw` 有排序语义。** 往抽牌堆加卡时 `AddGeneratedCardsToCombat` 是插顶还是插底、是否洗牌，
   请在注释里写明 —— 否则"加入抽牌堆"与"下回合抽到"在调用方看来是两件事。`Discard` 无此问题。
3. **手牌溢出不是错误。** `CopyCardToHand` 的注释已写明"手牌溢出由引擎自动转入弃牌堆"。
   这意味着调用方指定 `Hand` 而手牌已满时结果与指定 `Discard` 相同，**调用方无法区分**，
   所以不要把它当异常路径处理。

**解锁**：`ImGettingDrunk`「真的是要醉啦！」→ 适配器可加一个「将 1 张不胜酒力加入你的弃牌堆」的原子。

---

## 请求 2（中优先级）：击晕 —— **先确认，可能不需要新代码**

`Scripts/Cards/MindControlSpell.cs`（心灵控制术）的描述是「击晕 N 名敌人」。
**请先确认这张卡的 `OnPlay` 调用了什么**（原版某个命令？还是 newsanguo 自己的实现？）。

- 如果是可复用的命令 → 请开一个公开入口（形状参照 `ApplyFlight`）：
  ```csharp
  public static Task Stun(PlayerChoiceContext choiceContext, Creature target, decimal amount,
                          Creature? applier, CardModel? cardSource, bool silent = false)
  ```
- 如果它就是某个已有 Power 的施加 → 请开 `ApplyStun` 包装即可。

**这条不需要猜**：实现就在你们自己的卡里，照抄调用即可。

---

## 请求 3（中优先级）：消耗非攻击牌 + 按张数结算天意之力

**背景**：newsanguo 的「从来就没有这些！」是「消耗手牌中所有非攻击牌，**每张**获得 N 点天意之力」。
适配器已经有办法表达"消耗所有非攻击牌"（复用 `exhaust_card` + `CardFilter=non_attack`），
但**拿不到消耗了几张**，因此无法算出该给多少天意之力。

拆成两步做不到：消耗动作由适配器发起，张数必须在**同一时刻**观察。

```csharp
/// 消耗 player 手牌中所有非攻击牌；每消耗一张，为 player 增加 perCard 点天意之力。
/// 返回实际消耗张数（0 表示没有可消耗的牌）。
public static Task<int> ExhaustNonAttackCardsForHeavens(
    PlayerChoiceContext choiceContext, Player player, decimal perCard, CardModel? cardSource)
```

**解锁**：`NeverHadThese`「从来就没有这些！」。

---

## 请求 4（低优先级）：将选中的手牌变化为指定 Token

**背景**：「人体炼成术」是「将你手牌中的任意张变化为士兵」。
适配器的词汇表里有"变化选中的手牌"（`transform`/`cl_transformselectedhandcards`），
但**变化成什么**只能是随机/同名，无法指定是"士兵"。

```csharp
/// 让玩家选择任意张手牌，把它们变化为指定种类的 Token。
/// 返回变化张数。
public static Task<int> TransformSelectedHandCardsToToken(Player player, NewsanguoTokenKind kind)
```

（如果实现成本高，这条可以缓 —— 只解锁 1 张卡。）

---

## 请求 5（低优先级）：音量副作用的包装

`HearingVolumeController` 目前是 `public`，适配器能直接调，所以**不阻塞**。
但按"调用面收敛到 `NewsanguoPublicApi`"的原则，建议包一层：

```csharp
/// 把本场战斗的听觉音量降到 percentage（0~100）。战斗结束后自动恢复。
public static void SetCombatHearingVolume(decimal percentage)
/// 立即恢复满音量。
public static void RestoreHearingVolume()
```

**解锁**：`DeafenMe`「扎聋我自己的耳朵！」的音量副作用。

---

## 明确**不需要**做的（避免重复劳动）

| 项 | 原因 |
| --- | --- |
| 易伤不减少 | 0.2.39 的 `ApplyTraitorTyranny` 已解决（`TraitorTyrannyPower` 就是现成能力） |
| 挖坟（从消耗牌堆取牌入手） | 属引擎侧能力，适配器直接调 `CardPileCmd` 即可，不需要 newsanguo 出入口 |
| 「每当你抽到这张牌，增加一张其复制品到你的手牌」 | 已由 `CopyCardToHand` + 适配器侧的 `r_wheneverdrawn` 触发器覆盖 |
| 「每点酒力额外格挡」等动态数值 | `GetDrunkenMight` 已够，适配器自己按层数换算 |
| 「获得 N 层飞行」「施加剧毒/脆弱/缠身/帝王之征」 | 0.2.39 已交付 |
| 延迟效果（如「N 个回合结束后加士兵」） | 属触发器机制，适配器侧先尝试用自带 trigger，暂不提需求 |

---

## 验收方式

1. 编译 0 error。
2. **不要**只做编译期验证 —— 请求 1 与请求 3 都需要实机跑一次：
   - 指定 `PileType.Discard` 生成 Token，确认真的落在弃牌堆；
   - 触发一次 `ExhaustNonAttackCardsForHeavens`，确认返回张数与天意之力增加量一致。
3. 改动是纯追加，应保持向后兼容；若不得不改签名，请按"通用兼容规则 1"保留旧重载。
