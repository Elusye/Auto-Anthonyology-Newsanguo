#!/usr/bin/env python3
"""从 newsanguo 卡池反推自写目录（壳 + 原子需求）。

三份数据合并：
  1. 卡牌源码  Scripts/Cards/*.cs        -> 费用/类型/目标/稀有度 + 数值变量声明
  2. 本地化    localization/{zhs,eng}/cards.json -> 卡名与中英描述
  3. 我们已有原子 src/SelfCatalog*.cs    -> 判断描述能否用现有原子表达

关键手法：把描述与原子文案**都做数值无关化**（数字换成 #）再比对，
这样"造成13点伤害。"就能匹配到既有原子"造成6点伤害。"（同结构，数值另存）。

用法：python tools/derive_catalog.py [--full]
"""
import collections
import glob
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
NS = r"E:\games\杀戮尖塔2\newsanguo"
CARDS = os.path.join(NS, "Scripts", "Cards")
LOC = os.path.join(NS, "newsanguo", "localization")

CTOR = re.compile(
    r"base\s*\(\s*(-?\d+)\s*,\s*CardType\.(\w+)\s*,\s*CardRarity\.(\w+)\s*,\s*TargetType\.(\w+)", re.S)
VAR_GENERIC = re.compile(r"new\s+(\w+Var)\s*<\s*([\w.]+)\s*>\s*\(\s*(-?[\d.]+)")
VAR_NAMED = re.compile(r'new\s+(\w+Var)\s*\(\s*"([^"]+)"\s*,\s*(-?[\d.]+)')
ATOM_DEF = re.compile(
    r'new\("newsanguo/ops/([a-z0-9_]+)"\s*,\s*"([a-z0-9_/]+)"\s*,\s*"([^"]*)"\s*,\s*"([^"]*)"')
PLACEHOLDER = re.compile(r"\{([A-Za-z_][\w]*)(?::([^{}]*))?\}")
MARKUP = re.compile(r"\[/?[a-zA-Z_][\w]*(?:=[^\]]*)?\]")

# 变量声明 -> 本地化占位符名。多数是 <X>Var -> X；特例另列。
VAR_NAME_OVERRIDES = {"HeavensForceVar": "HeavensForcePower"}
# 非数值占位符：SmartFormat 条件/格式分支，取"未升级/常规"那一支或直接丢弃。
NON_NUMERIC = {"IfUpgraded", "InCombat", "energyPrefix", "Upgrade", "Upgraded",
               # 条件格式化器：`{X:有值|空}`，取最后一支（未触发的那支）才是常规描述。
               "IsTargeting", "IsClone", "IfCloned"}


def load_our_atoms():
    """既有原子：id -> 中文文案（数值无关化后的键用于匹配）。"""
    out = {}
    for p in sorted(glob.glob(os.path.join(ROOT, "src", "SelfCatalog*.cs"))):
        for m in ATOM_DEF.finditer(open(p, encoding="utf-8").read()):
            out[m.group(1)] = {"base": m.group(2), "zh": m.group(3), "en": m.group(4)}
    return out


def normalize(text):
    """字面键：数值无关化，用于与原子文案做结构比对。

    **只把"汉字数字 + 点"归一化**，不做全局汉字数字替换 —— 全局替换会把
    「上一张牌」「一名敌人」「攻击两次」这类量词也换成 #，污染键空间并制造误匹配
    （试过，已撤回）。而「三点」「二点」这种写法是**阈值**，必须与阿拉伯数字的
    「3点」「2点」等价，否则我们自己按显示规则改成汉字的原子就再也匹配不上卡池原文了
    （实测：覆盖率因此掉了 2 条）。
    """
    t = MARKUP.sub("", text)
    t = re.sub(r"[一二三四五六七八九十两]点", "#点", t)
    t = re.sub(r"\d+", "#", t)
    # **未解析的占位符也是一个"未知数值"**，对结构匹配而言等同于通配。
    # 这些变量声明在卡文件之外（基类或其他文件），静态解析不到；把它们留在键里
    # 会让整条子句永远匹配不上，尽管结构与我们的原子一致。
    # 注意只加在**字面键**里：变量键要保留 {Name} 供别名表使用。
    t = re.sub(r"\{[A-Za-z_][\w]*\}", "#", t)
    t = re.sub(r"\s+", "", t)
    return t.strip()


def normalize_var(text):
    """变量键：**保留变量名**（{Energy:energyIcons()} -> {Energy}），供别名表使用。

    两种键并存的原因：卡面常把单位省成图标（"获得{Energy:energyIcons()}。" 没有"点能量"字样），
    字面键永远匹配不到我们的 energy 原子；而变量键能表达"这就是 Energy 这一类效果"，
    于是可以用一张人工别名表把它接上，不必新造原子。
    """
    t = PLACEHOLDER.sub(lambda m: "{" + m.group(1) + "}", text)
    t = MARKUP.sub("", t)
    t = re.sub(r"\d+", "#", t)
    t = re.sub(r"\s+", "", t)
    return t.strip()


# 别名表：卡面子句（变量键） -> 已有原子 id。
# 只放"措辞不同、效果相同"的对应；语义不同的必须新造原子，不要塞进来。
ALIASES = {
    # 卡面用能量图标表示，没有"点能量"字样 -> 我们的 energy 原子
    "获得{Energy}。": "energy",
    # 卡面把数量写成汉字（"抽一张牌。"），与"抽3张牌。"同构。
    # 注意：**不要**用"汉字数字一律换成 #"这种全局规则 —— 那会把"上一张牌""一名敌人""攻击两次"
    # 这类量词也换掉，污染键空间并制造误匹配。逐条显式列出来更安全。
    "抽一张牌。": "draw",
    # 以下两条是**经核对同构**的措辞差异（不是猜测）：
    #   GetOut「消耗1张手牌。」      vs 我们的 exhaust_choose「消耗手牌中的一张牌。」（都是 exhaust_card/selected）
    #   SlamTheBowl「将一张此牌的复制品加入你的弃牌堆。」 vs 我们的 copy_to_discard
    #                「将此牌的一张复制加入弃牌堆。」（都是 create_copy/this_card -> discard）
    # 注意键必须是**变量键的原样文本**：卡面这两处数量写的是汉字「一」，
    # 不是阿拉伯数字，所以键里也得是「一」（我曾按已撤回的汉字归一化后的 # 写过，全部落空）。
    "消耗一张手牌。": "exhaust_choose",
    "将一张此牌的复制品加入你的弃牌堆。": "copy_to_discard",
    # ---- A 类：原子已经有了，只是**措辞**与我们的文案不同 ----
    # 逐个核对过是同一效果（对照我们的原子文案与基线 opcode 确认），不是猜的。
    "将一张不胜酒力加入你的弃牌堆。": "add_lightweight",      # 我们写「将1张不胜酒力加入你的弃牌堆。」
    "击晕一名敌人。": "stun",                                  # 我们写「击晕该敌人。」
    "将{Cards}张军杖添加到你的手牌。": "add_cudgel",            # 我们写「将1张军杖加入你的手牌。」
    "给予{DragonOmenPower}层帝王之征。": "dragon_omen",         # 我们写「给予目标/自己N层帝王之征。」
    # ---- newsanguo 的触发措辞 vs 官方（我们用的是官方原文）----
    # 卡池写「你每打出一张牌，」，官方/我们写「每当你打出一张牌时，」——同一件事。
    "你每打出一张牌，": "trigger_card_played",
    "每消耗一张牌，": "trigger_card_exhausted",
}


def parse_card(path):
    src = open(path, encoding="utf-8", errors="replace").read()
    mc = CTOR.search(src)
    if not mc:
        return None
    cost, ctype, rarity, target = mc.groups()

    # 变量：声明顺序即优先级，同名后出现的覆盖前者
    vars_map = {}
    for m in VAR_GENERIC.finditer(src):
        vars_map[m.group(2)] = m.group(3)
    for m in VAR_NAMED.finditer(src):
        vars_map[m.group(2)] = m.group(3)
    for m in re.finditer(r"new\s+(\w+Var)\s*\(\s*(-?[\d.]+)", src):
        name = VAR_NAME_OVERRIDES.get(m.group(1), m.group(1)[:-3])
        vars_map.setdefault(name, m.group(2))

    # 计算型变量：`CalculatedDamageVar` / `CalculatedBlockVar` 在卡面里是
    # `{CalculatedDamage:diff()}`，其**基础值**来自同一张卡的 `CalculationBaseVar`
    # （`CalculationExtraVar` 是升级增量，不是基础值）。不建模的话这类子句永远代不出数字、
    # 于是明明有 slash/guard 也匹配不上（SkywardBlade / UnclesAndAunts / WineTheOldHero / HeavenRevision）。
    if "CalculationBase" in vars_map:
        for calc in ("CalculatedDamage", "CalculatedBlock"):
            vars_map.setdefault(calc, vars_map["CalculationBase"])
    return {"cost": int(cost), "type": ctype, "rarity": rarity, "target": target, "vars": vars_map}


def raw_clauses(desc):
    """原始子句：保留占位符名（只去标记），**键要从这里算**。

    不能用 render() 的结果算键 —— 那时占位符已被替换成数字，变量名丢失，
    变量键（{Energy} 之类）就永远算不出来，别名表也就永远不生效。
    """
    out = []
    for line in re.split(r"(?<=。)", MARKUP.sub("", desc)):
        line = line.strip()
        if line:
            out.append(line)
    return out


def render(desc, vars_map):
    """把占位符换成实际数字；条件分支取常规那一支；返回按换行拆好的子句。"""
    def sub(m):
        name, fmt = m.group(1), (m.group(2) or "")
        if name in NON_NUMERIC:
            # {X:show:A|B} -> 取 B（未升级/常规）；无法判断就整体丢弃
            parts = fmt.split("|")
            return parts[-1] if len(parts) > 1 else ""
        if name in vars_map:
            return vars_map[name]
        return "{" + name + "}"

    out = []
    # 注意：json.load 已把 `\n` 解析成**真换行**，不能再按字面反斜杠-n 切。
    # 再按句号切成句级子句（一张卡的一行里常塞了多个效果）。
    for line in re.split(r"(?<=。)", PLACEHOLDER.sub(sub, desc)):
        line = line.strip()
        if line:
            out.append(line)
    return out


# 既有/已生成的壳：用于去重。形状 = (费用, 类型, 目标, 稀有度, 原子序列)。
# 原子序列**不排序** —— 顺序不同的两张牌是两张不同的牌。
RECIPE_DEF = re.compile(
    r'new\("(?P<id>\w+)"\s*,\s*"[^"]*"\s*,\s*"[^"]*"\s*,\s*(?P<cost>-?\d+)\s*,\s*'
    r'GeneratedCardType\.(?P<type>\w+)\s*,\s*TargetMode\.(?P<target>\w+)\s*,\s*'
    r'GeneratedRarity\.(?P<rarity>\w+)\s*,\s*\[(?P<owners>[^\]]*)\]\s*,\s*\[(?P<atoms>[^\]]*)\]\s*\)')

# 卡牌源码的 TargetType -> 目录的 TargetMode。
# 只有"需要玩家选敌人"的才用 SingleEnemy；随机/全体不需要选目标，归 Other。
TARGET_MAP = {
    "AnyEnemy": "SingleEnemy",
    "Self": "Other",
    "AllEnemies": "Other",
    "AnyAlly": "Other",
    "AllAllies": "Other",
    "RandomEnemy": "Other",
    "None": "Other",
}


def signature(cost, ctype, target, rarity, atom_ids):
    return (int(cost), ctype, target, rarity, tuple(atom_ids))


def existing_signatures():
    """既有壳形状。**必须排除自己生成的 Derived 文件** ——
    否则重跑时会把上次的输出当成"已存在"，于是全部跳过、把文件清空（踩过）。"""
    sigs = set()
    for p in sorted(glob.glob(os.path.join(ROOT, "src", "SelfCatalog*.cs"))):
        if os.path.basename(p) == "SelfCatalog.Derived.cs":
            continue
        for m in RECIPE_DEF.finditer(open(p, encoding="utf-8").read()):
            atoms = re.findall(r'"newsanguo/ops/([a-z0-9_]+)"', m.group("atoms"))
            sigs.add(signature(m.group("cost"), m.group("type"), m.group("target"),
                               m.group("rarity"), atoms))
    return sigs


def emit_derived(rows):
    """把完全可推导的卡写成 src/SelfCatalog.Derived.cs（只生成壳，原子全部复用既有）。"""
    existing = existing_signatures()
    lines, emitted, skipped = [], [], []
    for cls, info, hits, miss, zt, et in rows:
        if miss or not hits:
            continue
        atoms = [aid for _txt, aid in hits]
        target = TARGET_MAP.get(info["target"], "Other")
        sig = signature(info["cost"], info["type"], target, info["rarity"], atoms)
        if sig in existing:
            skipped.append(f"{cls}（形状与既有壳重复：{info['cost']}费 {info['type']} "
                           f"{info['rarity']} {target} [{' + '.join(atoms)}]）")
            continue
        existing.add(sig)
        atom_refs = ", ".join(f'"newsanguo/ops/{a}"' for a in atoms)
        # owner 约定（照抄官方 GoForTheEyes）：条件原子 owner=-1，
        # 被它门控的效果 owner=条件原子的下标；下一个条件出现前都归它管。
        owners_list, cond_idx = [], -1
        for idx, a in enumerate(atoms):
            # 条件原子与**触发原子**都是门控：它们后面的效果 owner 指向它们的下标。
            if a.startswith("cond_") or a.startswith("trigger_"):
                cond_idx = idx
                owners_list.append(-1)
            else:
                owners_list.append(cond_idx)
        owners = ", ".join(str(o) for o in owners_list)
        lines.append(
            f'        new("NSD{cls}", "{zt}", "{et}", {info["cost"]}, '
            f'GeneratedCardType.{info["type"]}, TargetMode.{target}, '
            f'GeneratedRarity.{info["rarity"]}, [{owners}], [{atom_refs}]),')
        emitted.append(f"{cls} -> NSD{cls}")

    out = os.path.join(ROOT, "src", "SelfCatalog.Derived.cs")
    with open(out, "w", encoding="utf-8") as f:
        f.write("// ============================================================================================\n")
        f.write("// SelfCatalog.Derived —— 从 newsanguo 卡池**反推**出来的壳（由 tools/derive_catalog.py 生成，请勿手改）\n")
        f.write("//\n")
        f.write("// 数据来源：卡牌源码（费用/类型/目标/稀有度）+ localization/{zhs,eng}/cards.json（卡名）\n")
        f.write("//          + 匹配到既有原子的效果序列。原子全部复用，本文件不新增原子。\n")
        f.write("//\n")
        f.write("// ⚠ 数值不随卡面固定：AA 的组件身份只认结构，具体数字由数值策略围绕中心值采样生成，\n")
        f.write("//    因此这里保留的是「形状」（费用/类型/目标/稀有度/效果序列），不是原卡的确切数字。\n")
        f.write("// ============================================================================================\n")
        f.write("using ChaosCardGenerator;\n\n")
        f.write("namespace Newsanguo.AutoAnthony;\n\n")
        f.write("internal static partial class SelfCatalog\n{\n")
        f.write("    private static readonly RecipeDef[] DerivedRecipeDefs =\n    [\n")
        f.write("\n".join(lines))
        f.write("\n    ];\n}\n")
    return emitted, skipped, out


# 条件句拆开后，效果那半常带一个连接词（"如果…，**则**获得3点能量。"），
# 而原子文案里没有它。只在**查表时**尝试去掉，不改写原文。
CONNECTIVES = ("则", "就", "那么", "即", "然后", "并", "且")


def strip_connective(s):
    for c in CONNECTIVES:
        if s.startswith(c):
            return s[len(c):]
    return s


def match_atom(raw, shown, by_struct):
    """单个子句 -> 原子 id。两个键各取所需：
    字面键用**已代入数值**的文本（才能对上原子文案里的字面量），
    变量键用**保留占位符**的原始文本（才能表达"这是 Energy 这一类效果"）。"""
    for r, s in ((raw, shown), (strip_connective(raw), strip_connective(shown))):
        aid = by_struct.get(normalize(s), [None])[0] or ALIASES.get(normalize_var(r))
        if aid:
            return aid
    return None


def split_keep_commas(s):
    """按中文逗号切开，逗号**留在前半段**结尾。"""
    out, start = [], 0
    for i, ch in enumerate(s):
        if ch == "，":
            out.append(s[start:i + 1])
            start = i + 1
    out.append(s[start:])
    return out


def match_compound(raw, shown, by_struct):
    """把「条件，效果。」这种**同句复合**拆成两个原子。

    原卡常把条件和效果写在一句里（"如果敌人的意图是攻击，则获得3点能量。"），
    而 AA 的模型要求它们是两个原子（条件在前、效果以 owner 指向条件的下标做门控）。
    只按句号切会整句对不上，于是用已有裸条件原子的卡全被判成"缺原子"——其实是切句的问题。

    ⚠ raw 与 shown 长度不同（占位符已代入数值），**不能共用下标**；
    两者按逗号切出的**段数相同**，所以按段配对。"""
    rp, sp = split_keep_commas(raw), split_keep_commas(shown)
    if len(rp) < 2 or len(rp) != len(sp):
        return None
    for k in range(1, len(rp)):
        a1 = match_atom("".join(rp[:k]), "".join(sp[:k]), by_struct)
        # 前半必须是**门控原子**（条件或触发器），否则"a，b"只是普通并列、不该切。
        # 触发原子同样要认：官方触发文案是「在你的回合开始时，」这种带逗号的短句，
        # 与后半段效果写在同一句里，和条件类是同一个形态。
        if not a1 or not (a1.startswith("cond_") or a1.startswith("trigger_")):
            continue
        a2 = match_atom("".join(rp[k:]), "".join(sp[k:]), by_struct)
        if a2:
            return [("".join(sp[:k]), a1), ("".join(sp[k:]), a2)]
    return None


class Scan:
    """一次完整扫描的结果。

    这是**覆盖率数字的唯一来源** —— main()、A/B 对比、基线断言都复用它。
    此前我为了做 A/B 另写了一份复刻，结果它与真工具差了 1 条子句，
    差异里混进了工具自身的噪声（差点把噪声当成发现）。一份代码就不会漂移。
    """

    def __init__(self):
        self.total = 0
        self.clause_total = 0
        self.clause_matched = 0
        self.matched_cards = 0
        self.unmatched = collections.Counter()
        self.rows = []
        self.clause_key = {}      # (卡类名, 子句文本) -> 匹配到的原子 id 或 None

    @property
    def rate(self):
        return self.clause_matched * 100 // max(1, self.clause_total)


def scan(exclude_prefix=None):
    """扫一遍卡池，返回 Scan。

    exclude_prefix：排除该前缀的原子（如 "trigger_"）——**专供 A/B 用**，
    这样"某个改动对覆盖率的影响"只是一次调用，不必再写临时脚本。
    """
    our = load_our_atoms()
    if exclude_prefix:
        our = {k: v for k, v in our.items() if not k.startswith(exclude_prefix)}

    by_struct = collections.defaultdict(list)
    for aid, a in our.items():
        by_struct[normalize(a["zh"])].append(aid)

    zh = json.load(open(os.path.join(LOC, "zhs", "cards.json"), encoding="utf-8"))
    en = json.load(open(os.path.join(LOC, "eng", "cards.json"), encoding="utf-8"))

    res = Scan()
    for fn in sorted(os.listdir(CARDS)):
        if not fn.endswith(".cs"):
            continue
        cls = os.path.splitext(fn)[0]
        raw = open(os.path.join(CARDS, fn), encoding="utf-8", errors="replace").read()
        # 只要**玩家卡池成员**：Token/Status/Curse 等注册在别的池子里，不属于接手范围。
        if "RegisterCard(typeof(NewsanguoCardPool))" not in raw:
            continue
        info = parse_card(os.path.join(CARDS, fn))
        if not info:
            continue
        snake = re.sub(r"(?<!^)(?=[A-Z])", "_", cls).upper()
        key = f"NEWSANGUO_CARD_{snake}"
        desc = zh.get(key + ".description")
        if not desc:
            continue

        res.total += 1
        clauses = render(desc, info["vars"])          # 显示用（已代入数值）
        raws = raw_clauses(desc)                      # 匹配用（保留变量名）
        pairs = list(zip(raws, clauses)) or [(c, c) for c in clauses]
        hits, miss = [], []
        for raw, shown in pairs:
            res.clause_total += 1
            aid = match_atom(raw, shown, by_struct)
            if aid:
                res.clause_matched += 1
                hits.append((shown, aid))
                res.clause_key[(cls, shown)] = aid
                continue
            comp = match_compound(raw, shown, by_struct)
            if comp:
                res.clause_matched += 1   # 按**子句**计覆盖率：这一句解决了
                hits.extend(comp)
                res.clause_key[(cls, shown)] = "+".join(a for _t, a in comp)
                continue
            miss.append(shown)
            res.unmatched[(normalize(raw), normalize_var(raw))] += 1
            res.clause_key[(cls, shown)] = None

        if clauses and not miss:
            res.matched_cards += 1
        res.rows.append((cls, info, hits, miss, zh.get(key + ".title", "?"),
                         en.get(key + ".title", "")))
    return res


def ab_diff(prefix, emit):
    """A/B：对比"含全部原子"与"排除某前缀原子"的匹配结果。

    这是查"覆盖率为何变化"的正确工具 —— 只切换一个变量，且两侧走**同一份**匹配代码。
    """
    a, b = scan(), scan(exclude_prefix=prefix)
    emit(f"\n=== A/B：排除前缀「{prefix}」 ===")
    emit(f"  当前 {a.clause_matched}/{a.clause_total}（{a.rate}%）"
         f"    排除后 {b.clause_matched}/{b.clause_total}（{b.rate}%）")

    reg = [k for k, v in b.clause_key.items() if v and not a.clause_key.get(k)]
    imp = [k for k, v in a.clause_key.items() if v and not b.clause_key.get(k)]
    emit(f"\n  回归（原本匹配、排除后不匹配）：{len(reg)} 条")
    for cls, shown in reg:
        emit(f"    {cls:26s} 「{shown[:44]}」")
    emit(f"\n  新增匹配（排除后反而匹配）：{len(imp)} 条")
    for cls, shown in imp:
        emit(f"    {cls:26s} 「{shown[:44]}」")
    return a, b, reg, imp


def main():
    res = scan()

    # 报告含大量中文：**必须自己写成 UTF-8 文件**。
    # 直接 print 会被 PowerShell 的管道按控制台代码页(GBK)解码，落盘就是乱码。
    report = []
    emit = report.append

    emit(f"扫到卡池成员 {res.total} 张（有本地化描述的）")
    emit(f"子句 {res.clause_matched}/{res.clause_total} 能匹配到现有原子（{res.rate}%）")
    emit(f"整张卡全部子句都能匹配的：{res.matched_cards}/{res.total} 张")
    emit(f"\n未被现有原子覆盖的子句（去重 {len(res.unmatched)} 种）——这就是**还缺的原子**：")
    emit("  [字面键 | 变量键]")
    for (kn, kv), n in res.unmatched.most_common(60):
        emit(f"  {n:3d}x  {kn}   |   {kv}")

    # "差一条就成"：已经匹配到原子、只差 1~2 条子句的卡 —— 这些子句是性价比最高的补原子目标。
    emit("\n=== 差 1~2 条就完全可推导的卡（优先补这些）===")
    near = [(cls, info, hits, miss, zt, et) for cls, info, hits, miss, zt, et in res.rows
            if hits and 0 < len(miss) <= 2]
    near.sort(key=lambda r: len(r[3]))
    for cls, info, hits, miss, zt, et in near[:30]:
        emit(f"  {cls:28s} 「{zt}」 已匹配 {len(hits)} 条，缺：")
        for c in miss:
            emit(f"        MISS  {normalize_var(c)}")

    emit(f"\n=== 完全可推导的卡（= 可以直接生成壳）===")
    for cls, info, hits, miss, zt, et in res.rows:
        if miss or not hits:
            continue
        atoms = " + ".join(f"{aid}" for _txt, aid in hits)
        emit(f"  {cls:28s} cost={info['cost']} {info['type']:6s} {info['rarity']:9s} "
             f"{info['target']:14s} 「{zt}」/「{et}」 = {atoms}")

    if "--full" in sys.argv:
        emit("\n=== 全部卡牌明细 ===")
        for cls, info, hits, miss, zt, et in res.rows:
            emit(f"\n{cls}  cost={info['cost']} {info['type']} {info['rarity']} {info['target']} 「{zt}」/「{et}」")
            for txt, aid in hits:
                emit(f"    OK   {aid:24s} {txt}")
            for c in miss:
                emit(f"    MISS                 {c}")

    # A/B：查"覆盖率为何变化"的正确工具（只切换一个变量、两侧同一份匹配代码）
    ab = None
    if "--ab" in sys.argv:
        i = sys.argv.index("--ab")
        prefix = sys.argv[i + 1] if i + 1 < len(sys.argv) and not sys.argv[i + 1].startswith("--") else "trigger_"
        ab = ab_diff(prefix, emit)

    # 覆盖率基线断言：低于阈值就返回非 0，防止覆盖率悄悄漂移而无人察觉。
    min_rate = None
    if "--min-rate" in sys.argv:
        i = sys.argv.index("--min-rate")
        min_rate = int(sys.argv[i + 1])

    out = os.path.join(ROOT, "refs", "derive-report.txt")
    with open(out, "w", encoding="utf-8") as f:
        f.write("\n".join(report) + "\n")

    if "--emit" in sys.argv:
        emitted, skipped, path = emit_derived(res.rows)
        with open(out, "a", encoding="utf-8") as f:
            f.write("\n\n=== 已生成壳（--emit）===\n")
            for e in emitted:
                f.write(f"  {e}\n")
            f.write(f"\n跳过（与既有壳形状重复）：{len(skipped)} 张\n")
            for s in skipped:
                f.write(f"  {s}\n")
        print(f"emitted={len(emitted)} skipped_dup={len(skipped)} -> {path}")

    # stdout 只留 ASCII 摘要，避免被控制台代码页破坏
    print(f"cards={res.total} clauses={res.clause_matched}/{res.clause_total} "
          f"fully_derivable={res.matched_cards} missing_clause_kinds={len(res.unmatched)} "
          f"rate={res.rate}%")
    if ab is not None:
        print(f"ab: regressed={len(ab[2])} improved={len(ab[3])}")
    print(f"report -> {out}")

    if min_rate is not None and res.rate < min_rate:
        print(f"COVERAGE REGRESSION: rate={res.rate}% < min_rate={min_rate}%")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
