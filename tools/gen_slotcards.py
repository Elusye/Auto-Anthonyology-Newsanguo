import os

# 从脚本自身位置推导仓库根 —— 不硬编码绝对路径（工作区目录改名后不会失效）
_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(_ROOT, "src", "SlotCards.cs")

# 稀有度计划与 newsanguo 0.2.38 实际池成员一致（除 2 张 Ancient 由手工保留）：
#   Basic 10（承担初始牌组：10 槽 × 1 张 = 10 张开局牌）
#   Common 20 / Uncommon 40 / Rare 26  ← 对应手工池 20/40/26，替换后稀有度分布不变
PLAN = [("Basic", 10), ("Common", 20), ("Uncommon", 40), ("Rare", 26)]
total = sum(n for _, n in PLAN)

lines = []
lines.append("// ============================================================================================")
lines.append("// SlotCards —— 固定卡槽模型（96 个）")
lines.append("//")
lines.append("// 东尼算法要求外部角色**预先声明**足够数量的具体卡牌类（ModelDb 需要发现具体类型，")
lines.append("// 不能在启动后动态生成模型类型）。旧实现用 Reflection.Emit + ModContentRegistry 注入，")
lines.append("// 本独立 mod 改为静态声明，符合 AA 官方 examples/WatcherComponentAdapter 的推荐形态。")
lines.append("//")
lines.append("// 槽位数与 newsanguo 0.2.38 的 NewsanguoCardPool 实际成员对齐：")
lines.append("//   手工可获取卡 = Basic 4 + Common 20 + Uncommon 40 + Rare 26 = 90（另 2 张 Ancient 手工保留）")
lines.append("//   本 mod 槽位   = Basic 10 + Common 20 + Uncommon 40 + Rare 26 = 96")
lines.append("// Basic 取 10 是为承担“初始牌组替换”：每槽 1 张 → 开局 10 张独一无二的生成基础卡。")
lines.append("//")
lines.append("// 本文件由 tools/gen_slotcards.py 生成，请勿手改。")
lines.append("// ============================================================================================")
lines.append("using System;")
lines.append("using AutoAnthony;")
lines.append("using MegaCrit.Sts2.Core.Models;")
lines.append("using newsanguo.Scripts.Characters;")
lines.append("")
lines.append("namespace Newsanguo.AutoAnthony;")
lines.append("")
lines.append("/// <summary>")
lines.append("/// 三国随机卡槽基类：只提供 ProfileId 与所属卡池，其余（描述/升级/触发器/执行）全部继承自东尼算法。")
lines.append("/// </summary>")
lines.append("public abstract class NewsanguoChaosCard : ExternalChaosCardModel")
lines.append("{")
lines.append("    protected sealed override string ComponentProfileId => Adapter.ProfileId;")
lines.append("")
lines.append("    public sealed override CardPoolModel Pool => ModelDb.CardPool<NewsanguoCardPool>();")
lines.append("}")
lines.append("")

slot = 0
plan_comment = []
for rarity, count in PLAN:
    plan_comment.append(f"{rarity} {slot}..{slot + count - 1}")
    lines.append(f"// ---- {rarity}：槽 {slot}..{slot + count - 1}（{count} 个）----")
    for _ in range(count):
        lines.append("public sealed class NewsanguoChaosCard" + str(slot).zfill(3) + " : NewsanguoChaosCard "
                     + "{ protected override int Slot => " + str(slot) + "; }")
        slot += 1
    lines.append("")

lines.append("/// <summary>槽位表：供 ExternalComponentCharacterApi.RegisterRuntime 按槽取类型。</summary>")
lines.append("internal static class SlotTable")
lines.append("{")
lines.append(f"    internal const int Count = {total};")
lines.append("")
lines.append("    internal static readonly Type[] Types =")
lines.append("    [")
for i in range(total):
    lines.append("        typeof(NewsanguoChaosCard" + str(i).zfill(3) + "),")
lines.append("    ];")
lines.append("")
lines.append("    internal static Type TypeForSlot(int slot) => Types[slot];")
lines.append("}")

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8") as f:
    f.write("\n".join(lines) + "\n")

print(f"wrote {OUT}")
print(f"total slots = {total}")
for c in plan_comment:
    print("  " + c)
