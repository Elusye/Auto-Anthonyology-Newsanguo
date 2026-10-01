#!/usr/bin/env python3
"""审计：找出"文案承诺"与"基线行为"可能不一致的原子。

**为什么需要这个**：一个原子如果**没有自定义 opcode**，它的执行行为就**等于基线的行为**。
此时若中文文案与基线语义不符，玩家看到的就是"描述和实际效果不一样"。
已经因此踩过两次：

  · 4 个原子用了 `regent/voidform/0`（opcode = `end_turn`）当"中性基线"
    → 打出这些牌直接结束回合；
  · `upgrade_self` 用了 `ironclad/armaments/1`（`i_upgrade` = 军备：升级手牌中的一张牌）
    → 文案写"升级自身"，实际选择一张手牌升级。

两次的共同点都是"挑基线只看有没有数值槽 / Flags，没看 opcode 本身干什么"。
所以凡是无自定义 opcode 的原子，都必须人工确认：**基线 opcode+variant 与中文文案说的是同一件事**。

用法：python tools/audit_baselines.py
输出：refs/audit-baselines.txt（UTF-8；直接 print 会被控制台代码页弄成乱码）
"""
import glob
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# 结尾用 [^)]* 吞掉可选的 `CustomOpcode: ...` / `Values: [...]` 尾巴
ATOM = re.compile(
    r'new\("newsanguo/ops/([a-z0-9_]+)"\s*,\s*"([a-z0-9_/]+)"\s*,\s*"([^"]*)"\s*,\s*"([^"]*)"([^)]*)\)')

# 认为"行为会改变战局"的 opcode —— 无自定义 opcode 却用这些做基线时，最需要人工确认
LOUD = {
    "end_turn": "结束回合",
    "discard_card": "弃牌",
    "exhaust_card": "消耗牌",
    "deal_damage": "造成伤害",
    "lose_hp": "失去生命",
    "create_card": "生成卡牌",
    "create_copy": "生成复制品",
    "move_card": "移动牌",
    "draw_cards": "抽牌",
    "gain_energy": "获得能量",
    "apply_power": "施加能力",
    "transform": "变化卡牌",
    "upgrade_card": "升级卡牌",
}


def load_baselines():
    """运行时目录：(SID) -> (opcode, variant, target)。"""
    path = os.path.join(ROOT, "refs", "aa-catalog", "runtime-atoms.tsv")
    out = {}
    if not os.path.exists(path):
        return out
    for line in open(path, encoding="utf-8"):
        f = line.rstrip("\n").split("\t")
        if len(f) >= 7 and f[0] == "SID":
            out[f[2]] = (f[4], f[5], f[6])
    return out




def slot_values(baseline, overrides_text):
    """把"基线槽位 + 本原子的覆写"合成**有效槽位值（按顺序）**。

    AA 渲染卡面描述时，会把文案里的数字**按位置**绑定到槽位、再用槽位值渲染回去，
    所以文案里的数字必须与该位置的槽位值一致，否则卡面显示的数字与实际定义不符。
    实测：文案写"耗能变为0"，而基线 anger/1 的 count 槽是 1 → 卡面显示"变为1"。
    **hidden 槽也不例外**（这一点当初判断错了）。
    """
    row = None
    for line in open(os.path.join(ROOT, "refs", "aa-catalog", "runtime-atoms.tsv"), encoding="utf-8"):
        f = line.rstrip("\n").split("\t")
        if len(f) >= 8 and f[0] == "SID" and f[2] == baseline:
            row = f
            break
    if row is None:
        return []
    ov = dict(re.findall(r'"([a-z_]+)"\s*,\s*(-?[\d.]+)', overrides_text or ""))
    out = []
    for part in row[7].split(","):
        m = re.match(r"(\w+)=(-?[\d.]+)", part.strip())
        if not m:
            continue
        sid, val = m.group(1), m.group(2)
        out.append((sid, ov.get(sid, val)))
    return out


def text_numbers(zh):
    """文案里**可被 AA 绑定**的数字（按出现顺序）。

    只认**阿拉伯数字**：汉字数字（一/两/三…）不会被绑定，因此可以安全地用来写
    "不属于任何槽位"的数字（例如条件阈值）。反过来，属于槽位值的数字**必须**写阿拉伯数字，
    否则 AA 找不到锚点。

    这条区分是踩出来的：阈值写成阿拉伯数字时会被槽位值覆盖 ——
    实测「若你的酒力不小于3点」在卡面显示成「不小于1点」（基线 count 槽的值）。
    """
    return [int(m.group(0)) for m in re.finditer(r"\d+", zh or "")]


def check_text_matches_slots():
    """文案数字 vs 有效槽位值：不一致就会导致卡面显示与定义不符。"""
    lines = []
    bad = 0
    for path in sorted(glob.glob(os.path.join(ROOT, "src", "SelfCatalog*.cs"))):
        src = open(path, encoding="utf-8").read()
        for m in ATOM.finditer(src):
            aid, baseline, zh, en, tail = m.groups()
            slots = slot_values(baseline, tail)
            if not slots:
                continue
            nums = text_numbers(zh)
            vals = [v for _sid, v in slots]
            # 只比**值**，不比数量：槽位比数字多的情况是良性的
            # （无锚点时 AA 不会插值 —— stun/entangled/exhume 这些无数字原子已实机验证正常）；
            # 而"数字比槽位多"或"同位置值不同"才是真问题。
            if any(abs(a - float(b)) > 1e-9 for a, b in zip(nums, vals)) or len(nums) > len(vals):
                bad += 1
                lines.append(f"{aid:30s} 文案数字={nums}  槽位={slots}  文案={zh}")
    return bad, lines




RECIPE = re.compile(
    r'new\("(?P<id>\w+)"\s*,\s*"[^"]*"\s*,\s*"[^"]*"\s*,\s*-?\d+\s*,\s*'
    r'GeneratedCardType\.(?P<type>\w+)\s*,\s*TargetMode\.\w+\s*,\s*'
    r'GeneratedRarity\.(?P<rarity>\w+)')

# 每个 (稀有度, 类型) 格子的**下限**。
# 为什么需要：实测出现过「Rare 只有 1 张能力牌」—— 12 个触发能力牌全被设成了 Uncommon。
# 这种「某一格空缺」的问题，人眼要把 96 张卡走一遍才发现；设成下限就能机械判定。
# 数值留有余量（明显低于当前值），目的是抓"某格被清空/塌陷"，而不是锁死配比。
SHAPE_MINIMUMS = {
    # **语义：任何非空格子都不得低于当前值。**
    # 由 tools/reset_shape_floors.py 从真实分布生成 —— 手写过两次都错：
    # 一次留了余量（导致剪枝后才发现掉格），一次用了剪枝前的旧表。
    ("Basic", "Attack"): 3,
    ("Basic", "Skill"): 2,
    ("Common", "Attack"): 13,
    ("Common", "Skill"): 11,
    ("Uncommon", "Attack"): 11,
    ("Uncommon", "Skill"): 25,
    ("Uncommon", "Power"): 4,
    ("Rare", "Attack"): 5,
    ("Rare", "Skill"): 14,
    ("Rare", "Power"): 6,
}
RARITIES = ["Basic", "Common", "Uncommon", "Rare"]
CARD_TYPES = ["Attack", "Skill", "Power"]


def check_shape_distribution():
    """稀有度 x 类型的交叉分布。返回 (表格行, 违规列表)。"""
    tab = {}
    for path in sorted(glob.glob(os.path.join(ROOT, "src", "SelfCatalog*.cs"))):
        for line in open(path, encoding="utf-8").read().split("\n"):
            # **跳过注释行**：否则形态表会多算（实测 Common 多出 3 条，
            # 与冒烟检查的数字对不上 —— 表印错了会误导人，而这正是本次要防的事）。
            if line.lstrip().startswith("//"):
                continue
            m = RECIPE.search(line)
            if not m:
                continue
            key = (m.group("rarity"), m.group("type"))
            tab[key] = tab.get(key, 0) + 1

    lines = ["", "=== 形态分布（稀有度 x 类型）===",
             "        " + "".join(f"{t:>10s}" for t in CARD_TYPES) + "      下限"]
    bad = []
    for r in RARITIES:
        cells, mins = [], []
        for t in CARD_TYPES:
            n = tab.get((r, t), 0)
            lo = SHAPE_MINIMUMS.get((r, t))
            mark = ""
            if lo is not None:
                mins.append(f"{t[:2]}>={lo}")
                if n < lo:
                    mark = "  <== 低于下限"
                    bad.append(f"{r}x{t}: {n} < {lo}")
            cells.append(f"{n:>10d}")
        lines.append(f"{r:10s}" + "".join(cells) + "     " + " ".join(mins) + mark)
    return lines, bad

TRIGGER_ATOM = re.compile(
    r'new\("newsanguo/ops/(trigger_[a-z0-9_]+)"\s*,\s*"([a-z0-9_/]+)"\s*,\s*"([^"]*)"')


def check_trigger_baselines():
    """触发原子的**基线必须真的是一个触发原子**，并把 文案 vs Trigger.Kind 列出来供人核对。

    为什么需要：触发条件不是我写的 —— 它是从克隆的官方基线整体继承的
    （`Trigger.Kind` 是封闭词汇表，一共 35 种，mod 造不出来）。
    所以"文案说获得格挡"与"Kind 真的是 block_gained"的一致性，
    只能靠"两者抄自同一个原子"来保证 —— 那就必须**核对**，不能默认。
    另外这类原子在"基线语义"那一栏会被归为 harmless（`trigger` 不在危险 opcode 集合里），
    等于绕过了人工审查，所以单独成一项。
    """
    spec_path = os.path.join(ROOT, "refs", "aa-catalog", "AutoAnthony.Data.catalog_runtime_specs.json")
    specs = {e["Id"]: e["Spec"] for e in json.load(open(spec_path, encoding="utf-8"))}

    lines = ["", "=== 触发原子：文案 vs 基线的 Trigger.Kind ==="]
    bad = []
    for path in sorted(glob.glob(os.path.join(ROOT, "src", "SelfCatalog*.cs"))):
        for m in TRIGGER_ATOM.finditer(open(path, encoding="utf-8").read()):
            aid, base, zh = m.groups()
            tr = (specs.get(base) or {}).get("Trigger") or {}
            kind = tr.get("Kind")
            if not kind:
                bad.append(f"{aid}: 基线 {base} 不是触发原子")
                lines.append(f"  {aid:34s} 基线={base:26s} ** Kind 缺失 **  <== 异常")
                continue
            lines.append(f"  {aid:34s} 基线={base:26s} Kind={kind:30s} Lifetime={tr.get('Lifetime')}")
            lines.append(f"      文案：{zh}")
    lines.append(f"  异常数 = {len(bad)}（每条都要能说出「文案与 Kind 是同一件事」）")
    return lines, bad

def main():
    base = load_baselines()
    lines = []
    lines.append("无自定义 opcode 的原子 = 行为完全等于基线，**文案必须与基线语义一致**")
    lines.append("（有 CustomOpcode 的原子由我们自己的 handler 决定行为，不受基线语义影响）\n")

    risky, ok, custom, unknown = [], [], [], []
    for path in sorted(glob.glob(os.path.join(ROOT, "src", "SelfCatalog*.cs"))):
        for m in ATOM.finditer(open(path, encoding="utf-8").read()):
            aid, baseline, zh, en, tail = m.groups()
            # 自定义 opcode 有两种写法，必须都认：
            #   具名：`, CustomOpcode: "ns_x", CustomVariant: ""`
            #   位置：`, "ns_x", ""`   ← 旧文件（SelfCatalog.cs）用这种
            # 只认具名会把 wine/heaven 这类误判成"无自定义 opcode"，从而错报成高危。
            # 注意不能只看"tail 里有没有引号"—— `Values: [("amount", 2)]` 也有引号。
            has_custom = ("CustomOpcode" in tail) or re.match(r'\s*,\s*"', tail) is not None
            if has_custom:
                custom.append((aid, baseline, zh))
                continue
            info = base.get(baseline)
            if info is None:
                unknown.append((aid, baseline, zh))
                continue
            opcode, variant, target = info
            row = (aid, baseline, opcode, variant, target, zh)
            (risky if opcode in LOUD else ok).append(row)

    lines.append(f"=== 需要人工确认（{len(risky)} 个，基线 opcode 会改变战局）===")
    lines.append(f"{'原子':32s} {'基线':30s} {'opcode':24s} {'variant':26s} {'目标':14s} 文案")
    for aid, b, op, va, tg, zh in sorted(risky, key=lambda r: (r[2], r[0])):
        lines.append(f"{aid:32s} {b:30s} {op:24s} {str(va):26s} {tg:14s} {zh}")
        if op in LOUD:
            lines.append(f"{'':32s} └─ 基线语义：{LOUD[op]} —— 确认与上面的文案是同一件事")

    lines.append(f"\n=== 基线为无害 opcode（{len(ok)} 个，低风险）===")
    for aid, b, op, va, tg, zh in sorted(ok, key=lambda r: (r[2], r[0])):
        lines.append(f"{aid:32s} {b:30s} {op:24s} {str(va):26s} {tg:14s} {zh}")

    lines.append(f"\n=== 有自定义 opcode（{len(custom)} 个，行为由 handler 决定）===")
    for aid, b, zh in sorted(custom):
        lines.append(f"{aid:32s} 基线={b:30s} {zh}")

    if unknown:
        lines.append(f"\n=== 基线不在运行时目录里（{len(unknown)} 个 —— Build() 会抛错，必须修）===")
        for aid, b, zh in unknown:
            lines.append(f"{aid:32s} {b:30s} {zh}")

    bad, badlines = check_text_matches_slots()
    lines.append(f"\n=== 文案数字 vs 槽位值（不一致 = 卡面显示与定义不符）：{bad} 处 ===")
    lines.append("   AA 会把文案里的数字**按位置**绑定到槽位、再用槽位值渲染回去，hidden 槽也不例外。")
    lines.append("   实测：文案写\"耗能变为0\"、基线槽位是 1 → 卡面显示\"变为1\"。")
    for bl in badlines:
        lines.append("   " + bl)

    shape_lines, shape_bad = check_shape_distribution()
    lines.extend(shape_lines)

    trig_lines, trig_bad = check_trigger_baselines()
    lines.extend(trig_lines)
    if shape_bad:
        lines.append("")
        for b in shape_bad:
            lines.append(f"   低于下限: {b}")

    out = os.path.join(ROOT, "refs", "audit-baselines.txt")
    with open(out, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print(f"text_vs_slot={bad} ")
    print(f"risky={len(risky)} harmless={len(ok)} custom={len(custom)} missing_baseline={len(unknown)}")
    print(f"report -> {out}")

    # 门禁用：**可机械判定**的两项必须为 0 才返回成功。
    # "基线语义"（risky）需要人工判断，不做断言；"缺基线"会让 Build() 抛错，必须为 0。
    print(f"shape_violations={len(shape_bad)} trigger_baseline_errors={len(trig_bad)}")
    if bad > 0 or unknown or shape_bad or trig_bad:
        print(f"AUDIT FAILED: text_vs_slot={bad} missing_baseline={len(unknown)} "
              f"shape_violations={len(shape_bad)}")
        for b in shape_bad:
            print(f"  形态不足: {b}")
        for b in trig_bad:
            print(f"  触发基线异常: {b}")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
