namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.2/§6 P-14(2026-09-20) — 「最新の版を確認」ボタンが呼ぶ実プロセス起動を
    // インターフェースに分離する。`UpdateWindow` は既定で `GitCliTagLister`(実 `git` CLI)を使うが、
    // 実プロセスに触れない解析ロジック(`GitTagListParser`)自体は EditMode テストで直接検証できる。
    public interface IGitTagLister
    {
        // 成功時は `git ls-remote --tags <repoUrl>` の標準出力をそのまま返し、warningMessage は null。
        // 失敗時(git が PATH に無い/タイムアウト/非 0 終了/例外)は null を返し、warningMessage に
        // ユーザーへ表示する 1 行の理由を入れる(例外で呼び出し元を止めない。CLAUDE.md §0-4)。
        string ListTags(string repoUrl, out string warningMessage);
    }
}
