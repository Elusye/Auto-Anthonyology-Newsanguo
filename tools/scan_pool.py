import os, re, sys, collections

CARDS = sys.argv[1]
rows = []
for fn in sorted(os.listdir(CARDS)):
    if not fn.endswith(".cs"):
        continue
    src = open(os.path.join(CARDS, fn), encoding="utf-8", errors="replace").read()
    if "RegisterCard(typeof(NewsanguoCardPool))" not in src:
        continue
    m = re.search(r"public\s+(?:sealed\s+|abstract\s+)?(?:partial\s+)?class\s+(\w+)", src)
    cls = m.group(1) if m else fn[:-3]
    b = re.search(r"base\s*\(\s*([^;]*?)\)\s*(\{|$)", src, re.S)
    rarity = None
    if b:
        r = re.search(r"CardRarity\.(\w+)", b.group(1))
        rarity = r.group(1) if r else None
    rows.append((cls, rarity))

print(f"NewsanguoCardPool 成员总数: {len(rows)}")
print("按稀有度:", dict(collections.Counter(r for _, r in rows)))
print()
for rar in ("Basic", "Common", "Uncommon", "Rare", "Ancient", None):
    sel = sorted(c for c, r in rows if r == rar)
    if sel:
        print(f"  {str(rar):9s} {len(sel):3d}  {', '.join(sel)}")
