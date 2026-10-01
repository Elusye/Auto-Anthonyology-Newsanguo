#!/usr/bin/env python3
"""按"保留与新清单重复者"的规则剪枝 SelfCatalog.cs 的既有原子与壳。

规则：
  · KEEP = 与新清单确实重复的既有原子（同结构；数值/升级对不构成区别）；
  · 其余既有原子删除；
  · **引用了被删原子的壳一并删除** —— 否则 SelfCatalog.Build() 会因
    "recipe references unknown self atom" 直接抛错（每个原子必须至少被一张壳引用，反之亦然）。

用法：python tools/prune_legacy_atoms.py [--apply]
不带 --apply 只打印计划。
"""
import re
import sys
import os

# 既有原子 -> 新清单里对应的条目（决定去留）。None 表示新清单里没有 → 删。
LEGACY = [
    ("slash",          "造成6点伤害",              "造成6点伤害",                 True),
    ("sweep_all",      "对所有敌人造成9点伤害",     "对所有敌人造成8/12点伤害",     True),
    ("strike_wave",    "对所有敌人造成2点伤害4次",  "对所有敌人造成4/5点伤害4次",   True),
    ("flurry",         "随机对敌人造成3点伤害3次",  None,                          False),
    ("snipe",          "随机对敌人造成6点伤害",     None,                          False),
    ("guard",          "获得5点格挡",              "获得5/8点格挡",               True),
    ("draw",           "抽3张牌",                  "抽3/4张牌",                   True),
    ("energy",         "获得2点能量",              "获得2/3点能量",               True),
    ("hp_loss",        "失去2点生命",              "失去2点生命",                 True),
    ("heal",           "回复10点生命",             None,                          False),
    ("max_hp",         "永久获得3点最大生命",       None,                          False),
    ("temp_strength",  "本回合获得3点力量",         None,                          False),
    ("exhaust_choose", "消耗手牌中的一张牌",        "消耗一张手牌",                True),
    ("exhaust_random", "随机消耗手牌中的一张牌",    None,                          False),
    ("return_to_top",  "将弃牌堆一张牌放到抽牌堆顶", None,                          False),
    ("attack_back",    "弃牌堆随机攻击牌入手",      None,                          False),
    ("copy_to_discard", "将此牌的复制加入弃牌堆",   "将一张此牌的复制品加入弃牌堆", True),
    ("weak_1",         "给予1层虚弱",              "给予1层虚弱",                 True),
    ("vuln_2",         "给予2层易伤",              "给予目标1/2层易伤",           True),
    ("vuln_double",    "易伤层数翻倍",             None,                          False),
    ("vuln_all_1",     "给予所有敌人1层易伤",       "给予所有敌人1层虚弱和易伤",    True),
    ("str_loss_turn",  "该敌人本回合失去10点力量",  None,                          False),
    ("strength",       "获得1点力量",              None,                          False),
    ("plating",        "获得4层覆甲",              None,                          False),
    ("enemy_strength", "使该敌人获得1点力量",      None,                          False),
    ("wine",           "获得1点酒力",              "获得4/6点酒力",               True),
    ("heaven",         "获得1点天意之力",          "获得2/3点天意之力",           True),
    ("trigger_start",  "在你的回合开始时",          None,                          False),
    ("trigger_end",    "在你的回合结束时",          None,                          False),
    ("when_block",     "每当你获得格挡时",          None,                          False),
    ("when_exhaust",   "每当有牌被消耗时",          None,                          False),
    ("when_vuln",      "每当你施加易伤时",          None,                          False),
    ("when_hp_loss",   "每当你在回合内失去生命时",  None,                          False),
    ("rule_vuln_amp",  "易伤敌人受伤+25%",         None,                          False),
    ("m_dmg_per_vuln", "每层易伤额外2点伤害",       None,                          False),
    ("m_block_per_str", "每点力量额外5点格挡",      None,                          False),
    ("m_extra_hit",    "额外造成1次伤害",           "则攻击两次",                  True),
    ("m_per_exhaust",  "消耗堆每张额外3点伤害",     None,                          False),
    ("m_body_slam",    "造成等同于当前格挡的伤害",  None,                          False),
]

KEEP = {a for a, _, _, k in LEGACY if k}
DROP = {a for a, _, _, k in LEGACY if not k}
assert len(LEGACY) == 39, len(LEGACY)


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    path = os.path.join(root, "src", "SelfCatalog.cs")
    src = open(path, encoding="utf-8").read()

    # ---- 原子块 ----
    m = re.search(r"(private static readonly AtomDef\[\] AtomDefs =\s*\[\n)(.*?)(\n    \];)", src, re.S)
    if not m:
        print("找不到 AtomDefs 块")
        return 1
    kept_lines, dropped_atoms = [], []
    for line in m.group(2).split("\n"):
        mm = re.search(r'new\("newsanguo/ops/([a-z0-9_]+)"', line)
        if mm:
            if mm.group(1) in KEEP:
                kept_lines.append(line)
            else:
                dropped_atoms.append(mm.group(1))
        elif line.strip().startswith("//"):
            pass  # 分类注释一并去掉，避免留下空洞的分组标题
    src = src[:m.start(2)] + "\n".join(kept_lines) + src[m.end(2):]

    # ---- 壳块：只保留"引用的原子都还在"的壳 ----
    m2 = re.search(r"(private static readonly RecipeDef\[\] RecipeDefs =\s*\[\n)(.*?)(\n    \];)", src, re.S)
    if not m2:
        print("找不到 RecipeDefs 块")
        return 1
    kept_recipes, dropped_recipes = [], []
    for line in m2.group(2).split("\n"):
        mm = re.search(r'new\("(NS\w+)"', line)
        if mm:
            refs = re.findall(r'"(newsanguo/ops/[a-z0-9_]+)"', line)
            if all(r.split("/")[-1] in KEEP for r in refs):
                kept_recipes.append(line)
            else:
                dropped_recipes.append(f"{mm.group(1)} -> {','.join(r.split('/')[-1] for r in refs)}")
        elif line.strip().startswith("//"):
            pass
    src = src[:m2.start(2)] + "\n".join(kept_recipes) + src[m2.end(2):]

    print(f"保留原子 {len(kept_lines)} 个：{', '.join(sorted(KEEP))}")
    print(f"\n删除原子 {len(dropped_atoms)} 个：{', '.join(dropped_atoms)}")
    print(f"\n保留壳 {len(kept_recipes)} 张")
    print(f"删除壳 {len(dropped_recipes)} 张：")
    for d in dropped_recipes:
        print("   ", d)

    if "--apply" in sys.argv:
        open(path, "w", encoding="utf-8").write(src)
        print(f"\n已写入 {path}")
    else:
        print("\n（未加 --apply，仅预览）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
