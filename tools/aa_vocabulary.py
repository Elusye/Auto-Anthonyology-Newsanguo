#!/usr/bin/env python3
"""导出/查询东尼算法的 opcode 词汇表。

用法：
  1) 先从已安装的 AutoAnthony.dll 里把内嵌目录数据导出：
       dotnet run --project tools\\SmokeTest -- <你的dll> --extract-embedded <目录>
  2) 再跑本脚本：
       python tools\\aa_vocabulary.py <目录>
     （目录里应有 AutoAnthony.Data.catalog_runtime_specs.json）

输出的三张表是写原子时的全部"合法词汇"：
  · opcode / variant / 数值槽 —— RuntimeSpec 能表达什么效果
  · condition Kind            —— 现成的条件判定
  · Trigger Kind              —— 现成的触发器时点
凡是这里查不到的效果，都必须走"自定义 opcode + 我们自己的 IComponentRuntimeHandler"
（本 mod 的 ns_gain_wine / ns_gain_heaven 就是这个做法）。
"""
import collections
import json
import os
import sys


def load(specs_path):
    return json.load(open(specs_path, encoding="utf-8"))


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1
    d = sys.argv[1]
    path = os.path.join(d, "AutoAnthony.Data.catalog_runtime_specs.json")
    if not os.path.exists(path):
        print(f"找不到 {path}\n先跑 --extract-embedded 导出内嵌目录。")
        return 1
    specs = load(path)

    op_variants = collections.defaultdict(set)
    op_count = collections.Counter()
    op_values = collections.defaultdict(set)
    conditions = set()
    triggers = collections.Counter()

    for e in specs:
        s = e["Spec"]
        op = s.get("Opcode") or ""
        op_variants[op].add(s.get("Variant") or "")
        op_count[op] += 1
        for v in (s.get("Values") or []):
            if isinstance(v, dict) and v.get("Id"):
                op_values[op].add(v["Id"])
        c = s.get("Condition")
        if c:
            conditions.add(c.get("Kind"))
        t = s.get("Trigger")
        if t:
            triggers[t.get("Kind")] += 1

    print(f"官方 spec 共 {len(specs)} 条，涉及 {len(op_variants)} 个 opcode\n")
    print("=" * 100)
    print("opcode / variant / 数值槽")
    print("=" * 100)
    for op, n in sorted(op_count.items(), key=lambda kv: (-kv[1], kv[0])):
        vals = ",".join(sorted(op_values[op]))
        print(f"\n### {op}   (spec {n} 条)" + (f"   数值槽: {vals}" if vals else ""))
        vs = sorted(v for v in op_variants[op] if v)
        if not vs:
            print("    (无 variant)")
        for i in range(0, len(vs), 3):
            print("    " + "".join(f"{v:44s}" for v in vs[i:i + 3]))

    print("\n" + "=" * 100)
    print("condition 的 Kind（现成条件判定）")
    print("=" * 100)
    for c in sorted(x for x in conditions if x):
        print("   ", c)

    print("\n" + "=" * 100)
    print("Trigger 的 Kind（现成触发器时点）")
    print("=" * 100)
    for t, n in sorted(triggers.items(), key=lambda kv: (-kv[1], kv[0])):
        print(f"    {t:44s} {n}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
