import re, collections, os, glob

_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
# 原子/壳现在分布在 SelfCatalog.cs 与 SelfCatalog.Extra.cs 两个文件里，必须一起读，
# 否则统计会漏掉 A 档新增的部分。
s = ""
for _p in sorted(glob.glob(os.path.join(_ROOT, "src", "SelfCatalog*.cs"))):
    s += open(_p, encoding="utf-8").read()

# 原子定义。注意结尾用 [^)]* 吞掉可选的 `, Values: [...]`/`CustomOpcode: ...` 尾巴，
# 否则带数值覆写的原子（SelfCatalog.Extra.cs）会匹配不上、统计漏掉。
atom_re = re.compile(
    r'new\("(newsanguo/ops/[a-z0-9_]+)"\s*,\s*"([a-z0-9_/]+)"\s*,\s*"([^"]*)"\s*,\s*"([^"]*)"'
    r'(?:\s*,\s*"([^"]*)"\s*,\s*"([^"]*)")?[^)]*\)')
atoms = []
for m in atom_re.finditer(s):
    atoms.append(dict(id=m.group(1), base=m.group(2), zh=m.group(3), en=m.group(4),
                      opcode=m.group(5), variant=m.group(6)))

# 配方（壳）
recipe_re = re.compile(
    r'new\("(NS\w+)"\s*,\s*"([^"]*)"\s*,\s*"([^"]*)"\s*,\s*(\d+)\s*,\s*GeneratedCardType\.(\w+)\s*,'
    r'\s*TargetMode\.(\w+)\s*,\s*GeneratedRarity\.(\w+)\s*,\s*\[([^\]]*)\]\s*,\s*\[([^\]]*)\]\s*\)')
recipes = []
for m in recipe_re.finditer(s):
    ids = re.findall(r'"(newsanguo/ops/[a-z0-9_]+)"', m.group(9))
    recipes.append(dict(id=m.group(1), zh=m.group(2), en=m.group(3), cost=int(m.group(4)),
                        type=m.group(5), target=m.group(6), rarity=m.group(7),
                        owners=[x.strip() for x in m.group(8).split(',')], atoms=ids))

print(f"原子数 = {len(atoms)}    配方(壳)数 = {len(recipes)}")
print("稀有度分布:", dict(collections.Counter(r['rarity'] for r in recipes)))
print()

use = collections.Counter()
for r in recipes:
    for a in set(r['atoms']):
        use[a] += 1

print("=== 原子清单（按分类）+ 被多少张壳引用 ===")
for a in atoms:
    extra = f"  [自定义 opcode={a['opcode']}/{a['variant']}]" if a['opcode'] else ""
    print(f"  {a['id']:34s} 基线={a['base']:26s} 引用={use.get(a['id'],0):2d}  「{a['zh']}」{extra}")

print()
never = [a['id'] for a in atoms if use.get(a['id'], 0) == 0]
print("未被任何壳引用的原子:", never if never else "无（Build 也会因此抛错）")
print()
print("=== 单张壳用到的原子数分布 ===")
print(dict(sorted(collections.Counter(len(r['atoms']) for r in recipes).items())))
print()
print("=== 触发器连接（Owners)用法：>-1 的壳 ===")
for r in recipes:
    if any(o != '-1' for o in r['owners']):
        print(f"  {r['id']:16s} owners=[{','.join(r['owners'])}]  atoms={r['atoms']}")

