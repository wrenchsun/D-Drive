#!/usr/bin/env python3
"""リポジトリ内の Markdown / HTML の相対リンクが実在するか検査する。

対象: git 管理下の *.md / *.html(Library/ Temp/ node_modules/ .git/ は除外)。
検査するのはリンク記法のみ(`[text](path)` / 参照定義 `[id]: path` / HTML の href・src)。
外部 URL・mailto・ページ内 `#` のみ・絶対パス(`/...`)は対象外。ファイルの存在だけを見る(アンカーは見ない)。

使い方:
  python Tools/Docs/check_links.py                       # 件数と一覧を表示。壊れていれば終了コード 1
  python Tools/Docs/check_links.py --json before.json    # 結果を JSON にも保存
  python Tools/Docs/check_links.py --baseline before.json
        # 以前の結果と比べ、新しく壊れたリンクが 0 かを確認
        # (移動前の送り元パスは reorganize_links.MOVES で写像して照合する)
"""
import argparse
import json
import os
import re
import subprocess
import sys
from urllib.parse import unquote

EXCLUDE_DIRS = ("Library/", "Temp/", "node_modules/", ".git/")
MD_LINK = re.compile(r'!?\[[^\]\n]*\]\(\s*<?([^)\s>]+)>?(?:\s+"[^"]*")?\s*\)')
MD_REF = re.compile(r'^\s{0,3}\[[^\]\n]+\]:\s*<?(\S+?)>?\s*$', re.M)
HTML_ATTR = re.compile(r'\b(?:href|src)\s*=\s*(?:"([^"]*)"|\'([^\']*)\')', re.I)
FENCE = re.compile(r'^(```|~~~).*?^\1', re.M | re.S)


def repo_root():
    return subprocess.check_output(["git", "rev-parse", "--show-toplevel"], text=True).strip()


def tracked_files(root):
    out = subprocess.check_output(["git", "-C", root, "ls-files", "-z"]).decode("utf-8")
    return [p for p in out.split("\0") if p]


def is_external(t):
    return (not t or t.startswith("#") or t.startswith("/")
            or re.match(r'^[A-Za-z][A-Za-z0-9+.\-]*:', t) is not None
            or t.startswith("{{") or "${" in t or t.startswith("<%"))


def extract(path, text):
    targets = []
    if path.endswith(".md"):
        body = FENCE.sub(lambda m: "\n" * m.group(0).count("\n"), text)
        body = re.sub(r'`[^`\n]*`', lambda m: " " * len(m.group(0)), body)
        targets += [m.group(1) for m in MD_LINK.finditer(body)]
        targets += [m.group(1) for m in MD_REF.finditer(body)]
    else:
        for m in HTML_ATTR.finditer(text):
            targets.append(m.group(1) if m.group(1) is not None else m.group(2))
    return targets


def check(root):
    files = tracked_files(root)
    existing = set(files)
    dirs = set()
    for f in files:
        d = os.path.dirname(f)
        while d:
            dirs.add(d)
            d = os.path.dirname(d)
    broken, total = [], 0
    for f in files:
        if not f.endswith((".md", ".html")) or f.startswith(EXCLUDE_DIRS):
            continue
        full = os.path.join(root, f)
        if not os.path.isfile(full):
            continue
        try:
            text = open(full, encoding="utf-8").read()
        except UnicodeDecodeError:
            continue
        for raw in extract(f, text):
            if is_external(raw):
                continue
            p = unquote(raw.split("#", 1)[0].split("?", 1)[0])
            if not p:
                continue
            total += 1
            res = os.path.normpath(os.path.join(os.path.dirname(f), p)).replace("\\", "/")
            if res in existing or res in dirs or os.path.exists(os.path.join(root, res)):
                continue
            broken.append({"source": f, "link": raw})
    return total, broken


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--json")
    ap.add_argument("--baseline")
    a = ap.parse_args()
    root = repo_root()
    total, broken = check(root)
    print(f"checked links: {total} / broken: {len(broken)}")
    for b in broken:
        print(f"  {b['source']}: {b['link']}")
    if a.json:
        with open(a.json, "w", encoding="utf-8") as fp:
            json.dump({"total": total, "broken": broken}, fp, ensure_ascii=False, indent=1)
    if a.baseline:
        with open(a.baseline, encoding="utf-8") as fp:
            before = json.load(fp)
        # 移動したファイルは同じファイル名を現在の場所から探して送り元を対応づける。
        # リンクの文面は書き換えで変わるので、送り元ごとの壊れたリンクの件数で比べる。
        current = set(tracked_files(root))
        by_name = {}
        for f in current:
            by_name.setdefault(os.path.basename(f), []).append(f)

        def forward(src):
            if src in current:
                return src
            c = [f for f in by_name.get(os.path.basename(src), []) if f.startswith(os.path.dirname(src) + "/")]
            return c[0] if len(c) == 1 else src

        before_n = {}
        for b in before["broken"]:
            k = forward(b["source"])
            before_n[k] = before_n.get(k, 0) + 1
        after_n = {}
        for b in broken:
            after_n[b["source"]] = after_n.get(b["source"], 0) + 1
        new = {k: v - before_n.get(k, 0) for k, v in after_n.items() if v > before_n.get(k, 0)}
        print(f"before broken: {len(before['broken'])} / after broken: {len(broken)} / newly broken: {sum(new.values())}")
        for k, v in new.items():
            print(f"  NEW {k}: +{v}")
        sys.exit(1 if new else 0)
    sys.exit(1 if broken else 0)


if __name__ == "__main__":
    main()
