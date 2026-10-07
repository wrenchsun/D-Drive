#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
D-Drive MCP のトークン計測 (MCP-10、docs/1002_ddrive_mcp.md §5.5 / §10)。

isuzu 版 Unity MCP のサーバー(記述子 %LOCALAPPDATA%\\UnityMCP\\instances\\<hash>.json)に
HTTP で接続し、(1) tools/list のツール定義の文字数、(2) scenarios.json の代表 5 シナリオ
(読み取り / preview の呼び出しだけ)の「引数 + 返り値」の文字数を測る。
トークンは文字数 / 3 で近似する(英数字 JSON と日本語が混ざるため一律)。
「前」は scenarios.json の before_chars(従来の execute_code 方式の見積もり)。

使い方:
  python Tools/Mcp/measure-tokens.py                # 表を標準出力へ
  python Tools/Mcp/measure-tokens.py --write-doc    # docs/1002 §10 の表を置き換える
  python Tools/Mcp/measure-tokens.py --project C:/path/to/Project   # 別プロジェクト

Python 3.8 互換・標準ライブラリのみ。Unity は起動しない(起動済みのサーバーを使う)。
"""
import argparse
import glob
import hashlib
import json
import os
import re
import sys
import time
import urllib.request

CHARS_PER_TOKEN = 3.0
RETRIES = 5
RETRY_SLEEP = 10
BUSY_HINTS = ("main thread", "busy", "refused", "timed out", "timeout", "10061", "domain reload", "compiling")

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
DOC = os.path.join(REPO, "docs", "1002_ddrive_mcp.md")
BEGIN = "<!-- measure-tokens:begin -->"
END = "<!-- measure-tokens:end -->"


def find_descriptor(project):
    inst_dir = os.path.join(os.environ.get("LOCALAPPDATA", ""), "UnityMCP", "instances")
    if project:
        p = os.path.join(os.path.abspath(project), "Assets").replace("\\", "/")
        h = hashlib.sha256(p.encode("utf-8")).digest()[:8].hex()
        f = os.path.join(inst_dir, h + ".json")
        if os.path.exists(f):
            return f
        sys.exit("記述子が無い: " + f)
    files = sorted(glob.glob(os.path.join(inst_dir, "*.json")), key=os.path.getmtime)
    if not files:
        sys.exit("記述子が無い: " + inst_dir)
    return files[-1]


class Client(object):
    def __init__(self, desc):
        with open(desc, encoding="utf-8") as fp:
            cfg = json.load(fp)
        self.url = cfg["mcpUrl"]
        self.token = cfg["token"]
        self.retried = 0
        self.seq = 0

    def _post(self, method, params, timeout=180):
        self.seq += 1
        body = json.dumps({"jsonrpc": "2.0", "id": self.seq, "method": method, "params": params}).encode("utf-8")
        req = urllib.request.Request(self.url, data=body, headers={
            "Content-Type": "application/json",
            "Accept": "application/json, text/event-stream",
            "Authorization": "Bearer " + self.token})
        with urllib.request.urlopen(req, timeout=timeout) as r:
            raw = r.read().decode("utf-8", "replace")
        data = [l[5:].strip() for l in raw.splitlines() if l.startswith("data:")]
        return json.loads("\n".join(data) if data else raw)

    def rpc(self, method, params):
        """忙しいとき(接続拒否・メインスレッド待ち)は 10 秒おきに最大 5 回再試行する。"""
        last = None
        for i in range(RETRIES):
            try:
                j = self._post(method, params)
                res = j.get("result") if isinstance(j.get("result"), dict) else {}
                text = json.dumps(j.get("error", ""), ensure_ascii=False)
                if res.get("isError"):
                    text += "".join(c.get("text", "") for c in res.get("content", []))
                if ("error" in j or res.get("isError")) and any(h in text.lower() for h in BUSY_HINTS):
                    raise RuntimeError(text[:120])
                return j
            except Exception as e:  # 接続拒否・タイムアウト・忙しい
                last = str(e)
                self.retried += 1
                print("  [retry %d/%d] %s" % (i + 1, RETRIES, last[:100]), file=sys.stderr)
                time.sleep(RETRY_SLEEP)
        raise RuntimeError("サーバー使用中で失敗: " + str(last))

    def list_tools(self):
        return self.rpc("tools/list", {}).get("result", {}).get("tools", [])

    def call(self, tool, args):
        j = self.rpc("tools/call", {"name": tool, "arguments": args})
        res = j.get("result", {})
        text = "".join(c.get("text", "") for c in res.get("content", []) if c.get("type") == "text")
        if not text and "error" in j:
            text = json.dumps(j["error"], ensure_ascii=False)
        return text


def defsize(t):
    d = {"name": t.get("name"), "description": t.get("description"), "inputSchema": t.get("inputSchema")}
    return len(json.dumps(d, ensure_ascii=False, separators=(",", ":")))


def tok(c):
    return int(round(c / CHARS_PER_TOKEN))


def first_id(text):
    try:
        j = json.loads(text)
        for k in ("items", "assets", "list"):
            if isinstance(j.get(k), list) and j[k] and isinstance(j[k][0], dict):
                return str(j[k][0].get("id"))
    except Exception:
        pass
    m = re.search(r'"id"\s*:\s*"?(\d+)', text)
    return m.group(1) if m else None


def run_scenarios(cl, scen):
    results = []
    for sc in scen["scenarios"]:
        rows = []
        fid = None
        done = True
        for c in sc["calls"]:
            raw = json.dumps(c["args"]).replace('"$first_id"', json.dumps(fid or "0"))
            args = json.loads(raw)
            req = len(c["tool"]) + len(json.dumps(args, ensure_ascii=False, separators=(",", ":")))
            s = time.time()
            try:
                txt = cl.call(c["tool"], args)
            except Exception as e:
                print("  [%s] %s 失敗: %s" % (sc["id"], c["tool"], e), file=sys.stderr)
                done = False
                break
            if c["tool"] == "ddrive_asset_list":
                fid = first_id(txt)
            rows.append({"tool": c["tool"], "req": req, "res": len(txt), "sec": time.time() - s})
        results.append({"sc": sc, "rows": rows, "done": done})
    return results


def build_tables(defs, results):
    dd = defs["ddrive_chars"]
    out = []
    out.append("| シナリオ | 呼び出し数 | 前（文字・概算トークン） | 後（文字・概算トークン、定義コスト込み） | 後（呼んだツールの定義だけ） | 比（定義込み / 呼んだ分だけ） |")
    out.append("|---|---|---|---|---|---|")
    notes = []
    for r in results:
        sc = r["sc"]
        before = sc["before_chars"]
        if not r["done"]:
            out.append("| %s %s | %d | %d・%d | 未計測（サーバー使用中） | — | — |" % (sc["id"], sc["name"], len(sc["calls"]), before, tok(before)))
            continue
        n = len(r["rows"])
        body = sum(x["req"] + x["res"] for x in r["rows"])
        full = body + dd * n
        lazy = body + sum(defs["by_tool"].get(x["tool"], 0) for x in r["rows"])
        out.append("| %s %s | %d | %d・%d | %d・%d | %d・%d | %.2f / %.2f |" % (
            sc["id"], sc["name"], n, before, tok(before), full, tok(full), lazy, tok(lazy),
            full / float(before), lazy / float(before)))
        notes.append((sc["id"], r["rows"], body))
    out.append("")
    out.append("| 項目 | ツール数 | 定義の文字数 | 概算トークン |")
    out.append("|---|---|---|---|")
    out.append("| `ddrive_*` | %d | %d | %d |" % (defs["ddrive_n"], dd, tok(dd)))
    out.append("| isuzu 標準（`ddrive_` 以外） | %d | %d | %d |" % (defs["other_n"], defs["other_chars"], tok(defs["other_chars"])))
    out.append("| 合計 | %d | %d | %d |" % (defs["ddrive_n"] + defs["other_n"], dd + defs["other_chars"], tok(dd + defs["other_chars"])))
    out.append("")
    out.append("呼び出しごとの内訳（引数文字数 / 返り値文字数 / 秒）。引数 + 返り値だけの合計（定義抜き）は括弧内:")
    out.append("")
    for sid, rows, body in notes:
        out.append("- %s（%d）: " % (sid, body) + "、".join("`%s` %d / %d / %.1fs" % (x["tool"], x["req"], x["res"], x["sec"]) for x in rows))
    return "\n".join(out)


def write_doc(table, cl):
    with open(DOC, encoding="utf-8") as fp:
        s = fp.read()
    stamp = time.strftime("%Y-%m-%d")
    block = BEGIN + "\n" + table + "\n\n" + \
        "計測日 %s（`python Tools/Mcp/measure-tokens.py --write-doc`、再試行 %d 回）。トークン = 文字数 / 3 の一律近似。\n\n" % (stamp, cl.retried) + \
        "「前」は実測ではなく**見積もり**（従来の `execute_code` / `read_console` / docs 参照の流れ。算式は `Tools/Mcp/scenarios.json` の `rationale`）。" + \
        "「後（定義コスト込み）」は 1 呼び出し = 1 ターンとして毎ターン `ddrive_*` の全定義を再送する前提（上限）、「呼んだツールの定義だけ」は遅延ロード時の下限。\n" + END
    if BEGIN in s and END in s:
        s = re.sub(re.escape(BEGIN) + r".*?" + re.escape(END), lambda m: block, s, flags=re.S)
    else:
        m = re.search(r"(## 10\. 計測結果[^\n]*\n\n)(\|.*\n)+", s)
        if not m:
            sys.exit("§10 の表が見つからない")
        s = s[:m.end(1)] + block + "\n" + s[m.end():]
    with open(DOC, "w", encoding="utf-8", newline="\n") as fp:
        fp.write(s)
    print("書き込み: " + DOC)


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    ap = argparse.ArgumentParser(description="D-Drive MCP のトークン計測")
    ap.add_argument("--project", default="", help="対象プロジェクト(既定: 最新の記述子)")
    ap.add_argument("--scenarios", default=os.path.join(HERE, "scenarios.json"))
    ap.add_argument("--write-doc", action="store_true", help="docs/1002 §10 の表を置き換える")
    a = ap.parse_args()
    with open(a.scenarios, encoding="utf-8") as fp:
        scen = json.load(fp)
    cl = Client(find_descriptor(a.project))
    tools = cl.list_tools()
    by_tool = {t["name"]: defsize(t) for t in tools}
    dd = [n for n in by_tool if n.startswith("ddrive_")]
    defs = {"by_tool": by_tool, "ddrive_n": len(dd), "ddrive_chars": sum(by_tool[n] for n in dd),
            "other_n": len(by_tool) - len(dd),
            "other_chars": sum(v for k, v in by_tool.items() if not k.startswith("ddrive_"))}
    results = run_scenarios(cl, scen)
    table = build_tables(defs, results)
    print(table)
    if cl.retried:
        print("\n(再試行 %d 回)" % cl.retried)
    if a.write_doc:
        write_doc(table, cl)


if __name__ == "__main__":
    main()
