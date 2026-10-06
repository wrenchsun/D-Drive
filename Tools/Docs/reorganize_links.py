#!/usr/bin/env python3
"""docs/ の整理(ファイルの移動)と、それに伴うリンク・パス表記の一括書き換え。

移動の対応表は下の GROUPS(サブフォルダ -> 番号のリスト)。ファイル名は変えず、
`docs/<NN>_*.md` を `docs/<サブフォルダ>/<NN>_*.md` へ `git mv` する。再利用するときは GROUPS を書き換える。

書き換える対象(git 管理下のテキストファイル。.cs は触らない。Library/ Temp/ node_modules/ .git/ は除外):
  - Markdown のリンク `](path)` ・参照定義 `[id]: path`・HTML の href / src
  - 本文中のパス表記(`docs/NN_x.md`・`NN_x.md`・`../NN_x.md`・`../Assets/...` など)
どれも「リンク元ファイルの(移動後の)位置から見た正しい相対パス」に直す。移動するファイル自身の中の
相対リンク(`../CLAUDE.md` など)も、深さが変わるので同じ規則で直す。
次のミラー(Tools/Release/bump-version.ps1 が docs/ の正本から同期するコピー)は、正本と同じ文面にそろえる:
  Packages/com.ddrive.core/Documentation~/{DesignerManual,ProgrammerManual,migrations}  <- docs/ の同名フォルダ
  Packages/com.ddrive.core/Documentation~/ConsumerGuide                                  <- docs/50_consumer_guide
  Packages/com.ddrive.core/CHANGELOG.md                                                  <- CHANGELOG.md

使い方(リポジトリのどこからでも):
  python Tools/Docs/reorganize_links.py            # ドライラン(書き換え内容の要約だけ表示)
  python Tools/Docs/reorganize_links.py --apply    # リンクを書き換えてから git mv で移動する
  python Tools/Docs/reorganize_links.py --apply --no-move   # 書き換えだけ(移動済みでなく、移動もしない)
"""
import argparse
import os
import posixpath
import re
import subprocess
import sys
from collections import Counter

# サブフォルダ -> docs/ 直下の番号(NN_*.md)。
GROUPS = {
    "reviews": [19, 24, 25, 30, 41, 44, 45, 47, 53, 54, 55, 56, 57, 58, 59, 61, 62, 63, 64, 65, 66],
    "verification": [23, 28, 29, 37, 43, 52],
    "archive": [31, 35, 36, 38, 39, 40, 46, 48, 49, 60],
}

TEXT_EXT = (".md", ".html", ".htm", ".js", ".json", ".txt", ".ps1", ".cmd", ".yml", ".yaml", ".py")
EXCLUDE_PREFIX = ("Library/", "Temp/", "node_modules/", ".git/", "Tools/Docs/")
MIRRORS = [  # (パッケージ側の接頭辞, 正本側の接頭辞)
    ("Packages/com.ddrive.core/Documentation~/DesignerManual/", "docs/DesignerManual/"),
    ("Packages/com.ddrive.core/Documentation~/ProgrammerManual/", "docs/ProgrammerManual/"),
    ("Packages/com.ddrive.core/Documentation~/migrations/", "docs/migrations/"),
    ("Packages/com.ddrive.core/Documentation~/ConsumerGuide/", "docs/50_consumer_guide/"),
    ("Packages/com.ddrive.core/CHANGELOG.md", "CHANGELOG.md"),
]


def repo_root():
    return subprocess.check_output(["git", "rev-parse", "--show-toplevel"], text=True).strip()


def git_files(root):
    out = subprocess.check_output(["git", "-C", root, "ls-files", "-z"]).decode("utf-8")
    return [p for p in out.split("\0") if p]


def build_moves(files):
    """old path -> new path(repo ルート相対、/ 区切り)。"""
    moves = {}
    for sub, nums in GROUPS.items():
        for n in nums:
            pre = "docs/%02d_" % n
            hit = [f for f in files if f.startswith(pre) and f.endswith(".md") and "/" not in f[len("docs/"):]]
            if len(hit) != 1:
                raise SystemExit("番号 %d の docs/%02d_*.md が 1 つに決まりません: %s" % (n, n, hit))
            moves[hit[0]] = "docs/%s/%s" % (sub, posixpath.basename(hit[0]))
    return moves


# check_links.py から使えるよう、import 時点でも MOVES を用意する(git 管理下の現在のファイルから作る)。
def _initial_moves():
    try:
        files = git_files(repo_root())
        return build_moves(files)
    except BaseException:  # 移動済みのツリーでは対応表を作れない(SystemExit を含む)
        return {}


MOVES = _initial_moves()

# リンク記法(] の後の ( や href= の直後)または本文中のパス表記を 1 つの正規表現で拾う。
FRAG = r'(?:#[^\s)"\'`>\]<|,;]*)'
PATTERN = re.compile(
    r'(?P<lead>\]\(\s*<?|\bhref\s*=\s*["\']|\bsrc\s*=\s*["\']|^[ ]{0,3}\[[^\]\n]+\]:[ ]*<?)'
    r'(?P<ltarget>[^)\s>"\']+)'
    r'|(?<![A-Za-z0-9_\-./~])(?P<dot>(?:\.\./)+[^\s)\]"\'`<>|,;#]+)(?P<dfrag>' + FRAG + r')?'
    r'|(?<![A-Za-z0-9_\-./~])(?P<tok>(?:[A-Za-z0-9_.~\-]+/)*[A-Za-z0-9_.~\-]+\.(?:md|html))'
    r'(?![A-Za-z0-9_\-])(?P<tfrag>' + FRAG + r')?',
    re.M,
)
SCHEME = re.compile(r'^[A-Za-z][A-Za-z0-9+.\-]*:')


class Rewriter:
    def __init__(self, files, moves):
        self.files = set(files)
        self.dirs = set()
        for f in files:
            d = posixpath.dirname(f)
            while d:
                self.dirs.add(d)
                d = posixpath.dirname(d)
        self.moves = moves
        self.moved_names = {posixpath.basename(o) for o in moves}
        self.log = []  # (file, old, new)

    def exists(self, p):
        return p in self.files or p in self.dirs

    def virtual(self, path):
        """ミラー先のファイルは、正本の位置にあるものとして扱う。"""
        for mirror, canon in MIRRORS:
            if path.startswith(mirror):
                return canon + path[len(mirror):]
        return path

    def new_of(self, path):
        return self.moves.get(path, path)

    def relpath(self, target, from_dir):
        r = posixpath.relpath(target, from_dir or ".")
        return r

    def resolve(self, src_virtual, p, allow_root):
        """リンクのパス p を解決して (旧パス, 'rel'|'root') を返す。解決できなければ None。"""
        base = posixpath.dirname(src_virtual)
        trailing = p.endswith("/")
        cand = posixpath.normpath(posixpath.join(base, p))
        if cand.startswith("..") or cand == ".":
            cand = None
        if cand and self.exists(cand):
            return cand, "rel", trailing
        if allow_root:
            c2 = posixpath.normpath(p)
            if not c2.startswith("..") and self.exists(c2):
                return c2, "root", trailing
        return None

    def rewrite_path(self, src_old, p, allow_root, bare_ok):
        """パス文字列 p を書き換えた結果を返す(変更なしなら None)。"""
        if SCHEME.match(p) or p.startswith("/") or p.startswith("#") or not p:
            return None
        src_v = self.virtual(src_old)
        src_new_v = self.new_of(src_v)
        r = self.resolve(src_v, p, allow_root)
        if r is None:
            # 相対でも root 相対でも解決できない。移動対象のファイル名が単独で書かれているだけなら、
            # docs/ 直下にあるものとして扱う(例: 他のフォルダの文書が `47_x.md` とだけ書く)
            name = posixpath.basename(p)
            if bare_ok and "/" not in p and name in self.moved_names:
                old = "docs/" + name
                new = self.moves.get(old)
                if new:
                    return new
            return None
        target, kind, trailing = r
        if "/" not in p and not bare_ok:
            return None
        new_target = self.new_of(target)
        if new_target == target and src_new_v == src_v:
            return None  # 参照先もリンク元も動かないなら書き換えない
        if kind == "root":
            out = new_target
        else:
            out = self.relpath(new_target, posixpath.dirname(src_new_v))
        if trailing and not out.endswith("/"):
            out += "/"
        if p.startswith("./") and not out.startswith("."):
            out = "./" + out
        return None if out == p else out

    def process(self, src_old, text, is_link_file):
        changed = []

        def sub(m):
            if m.group("ltarget") is not None:
                t = m.group("ltarget")
                path, sep, frag = t.partition("#")
                new = self.rewrite_path(src_old, path, allow_root=False, bare_ok=True)
                if new is None:
                    return m.group(0)
                changed.append((path, new))
                return m.group("lead") + new + sep + frag
            if m.group("dot") is not None:
                full = m.group("dot")
                stripped = full.rstrip(".:")
                tail = full[len(stripped):]
                new = self.rewrite_path(src_old, stripped, allow_root=False, bare_ok=False)
                if new is None:
                    return m.group(0)
                changed.append((stripped, new))
                return new + tail + (m.group("dfrag") or "")
            tok = m.group("tok")
            s, e = m.start(), m.end()
            name = posixpath.basename(tok)
            # 数字つきの文書名か、スラッシュ付きのパス表記だけを対象にする(単独の README.md などは触らない)
            if "/" not in tok and not re.match(r"\d\d_", name):
                return m.group(0)
            # [47_x.md](...) のリンク文字列(ファイル名だけの表示名)は書き換えない。
            # `docs/...` で始まるパス表記の表示名と、リンクでない [47_x.md] 形式の短縮参照は書き換える
            before = m.string[s - 1] if s > 0 else ""
            if before == "[" and m.string[e:e + 2] == "](" and not tok.startswith("docs/") and not m.group("tfrag"):
                return m.group(0)
            if "/" not in tok and m.string[max(0, s - 5):s] == "docs" + chr(92):
                # バッチ等の docs<バックスラッシュ>NN_x.md(バックスラッシュ区切り)
                mv = self.moves.get("docs/" + tok)
                if mv is None:
                    return m.group(0)
                new = mv[len("docs/"):].replace("/", chr(92))
            else:
                new = self.rewrite_path(src_old, tok, allow_root=True, bare_ok=True)
            if new is None:
                return m.group(0)
            changed.append((tok, new))
            return new + (m.group("tfrag") or "")

        out = PATTERN.sub(sub, text)
        for old, new in changed:
            self.log.append((src_old, old, new))
        return out, len(changed)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="書き換えて git mv する(省略時はドライラン)")
    ap.add_argument("--no-move", action="store_true", help="git mv をしない")
    ap.add_argument("--verbose", action="store_true")
    a = ap.parse_args()
    root = repo_root()
    os.chdir(root)
    files = git_files(root)
    moves = build_moves(files)
    rw = Rewriter(files, moves)
    per_file = Counter()
    total = 0
    for f in files:
        if not f.endswith(TEXT_EXT) or f.startswith(EXCLUDE_PREFIX) or not os.path.isfile(f):
            continue
        try:
            with open(f, encoding="utf-8", newline="") as fp:
                text = fp.read()
        except UnicodeDecodeError:
            continue
        out, n = rw.process(f, text, True)
        if n:
            per_file[f] += n
            total += n
            if a.apply and out != text:
                with open(f, "w", encoding="utf-8", newline="") as fp:
                    fp.write(out)
    print("rewritten references: %d in %d files" % (total, len(per_file)))
    by_ext = Counter(os.path.splitext(f)[1] for f in per_file)
    print("by extension:", dict(by_ext))
    if a.verbose:
        for src, old, new in rw.log:
            print("  %s: %s -> %s" % (src, old, new))
    non_doc = [f for f in per_file if not f.endswith((".md", ".html"))]
    if non_doc:
        print("non md/html files changed:", non_doc)
    if a.apply and not a.no_move:
        for sub in GROUPS:
            os.makedirs(os.path.join("docs", sub), exist_ok=True)
        for old, new in moves.items():
            subprocess.check_call(["git", "mv", old, new])
        print("moved files: %d" % len(moves))
    elif not a.apply:
        print("moves planned: %d (dry run)" % len(moves))
    return 0


if __name__ == "__main__":
    sys.exit(main())
