using System.Threading;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.2/§6 P-14(2026-09-20) — `IGitTagLister` の実配線(実 `git` CLI を起動する)。
    // EditMode テストの対象外(実プロセスに触れるため)。例外で呼び出し元を止めない(CLAUDE.md §0-4): PATH に `git`
    // が無い・タイムアウト・キャンセル・非 0 終了・例外のいずれも `warningMessage` を添えて null を返すだけにする。
    // 2026-10-03(レビュー PC-R-03/21): 起動は `GitProcess`(GIT_TERMINAL_PROMPT=0・標準入力を閉じる・タイムアウト / キャンセルでツリーごと停止)。
    // バックグラウンドスレッドから呼べる(Unity API を使わない)。
    public sealed class GitCliTagLister : IGitTagLister
    {
        private const int TimeoutMs = 30000;
        private readonly CancellationToken _cancellation;

        public GitCliTagLister() : this(CancellationToken.None)
        {
        }

        public GitCliTagLister(CancellationToken cancellation)
        {
            _cancellation = cancellation;
        }

        public string ListTags(string repoUrl, out string warningMessage)
        {
            warningMessage = null;

            if (string.IsNullOrEmpty(repoUrl))
            {
                warningMessage = "リポジトリ URL を解決できませんでした。";
                return null;
            }

            // `-` で始まる URL はオプションとして解釈され得るため実行前に弾く(例外にはしない)。
            if (!GitArguments.IsSafeValue(repoUrl))
            {
                warningMessage = GitArguments.UnsafeWarning("リポジトリ URL");
                return null;
            }

            var outcome = GitProcess.Run(GitArguments.LsRemoteTags(repoUrl), null, TimeoutMs, _cancellation);
            if (!outcome.Success)
            {
                warningMessage = outcome.Cancelled ? outcome.Error : "git ls-remote: " + outcome.Error;
                return null;
            }

            return outcome.Stdout;
        }
    }
}
