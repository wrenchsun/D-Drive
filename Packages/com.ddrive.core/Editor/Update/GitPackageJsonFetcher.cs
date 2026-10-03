using System;
using System.IO;
using System.Threading;
using UnityEngine;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.2 P-15(2026-10-03) — `IRemotePackageJsonFetcher` の実配線。
    // 上げ先のタグの package.json だけを、作業ツリーを作らない浅い clone で取って読む(レビュー PC-R-01):
    //   git clone --depth 1 --filter=blob:none --no-checkout --no-tags --branch <タグ> -- <url> Temp/DDriveUpdate/<guid>
    //   git show HEAD:<?path=>/package.json     ← blob 1 個だけを遅延取得。チェックアウト時のフィルター(LFS 等)・
    //                                              シンボリックリンク・パスの解決が一切動かない
    // → 一時フォルダ(.git だけ)を削除。ユーザーの git 認証(認証ヘルパー・SSH 鍵)をそのまま使うので private リポジトリでも動く
    // (認証プロンプトで固まらない: GIT_TERMINAL_PROMPT=0 / GCM_INTERACTIVE=never)。全体 30 秒のタイムアウト・キャンセル可。
    // タイムアウト / キャンセルでは子プロセスごと止め(`GitProcess.KillTree`)、一時フォルダを消す。消せなかった分は
    // 次回(`CleanupStale`)掃除する。失敗しても例外は投げず `warningMessage` を返すだけ(「事前確認できなかった。更新後に確認します」)。
    // バックグラウンドスレッドから呼べる: Unity API(`Application.dataPath`)は使わず、一時フォルダの親は生成時に主スレッドで渡す。
    // 実プロセスに触れるため EditMode テストの対象外(引数の組み立て・検査は `GitArguments` の純関数でテスト)。
    public sealed class GitPackageJsonFetcher : IRemotePackageJsonFetcher
    {
        private const int TotalTimeoutMs = 30000;
        private const int MaxPackageJsonChars = 1024 * 1024;
        private static readonly TimeSpan StaleAge = TimeSpan.FromMinutes(10);

        private readonly string _tempRoot;
        private readonly CancellationToken _cancellation;

        // tempRoot: `Temp/DDriveUpdate` の絶対パス(`DefaultTempRoot()` を主スレッドで取って渡す)。
        public GitPackageJsonFetcher(string tempRoot, CancellationToken cancellation = default)
        {
            _tempRoot = tempRoot;
            _cancellation = cancellation;
        }

        // 主スレッドで呼ぶ(Application.dataPath を使う)。
        public static string DefaultTempRoot()
            => Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "Temp", "DDriveUpdate");

        public string FetchPackageJson(string cloneUrl, string subPath, string reference, out string warningMessage)
        {
            warningMessage = null;
            if (string.IsNullOrEmpty(cloneUrl) || string.IsNullOrEmpty(reference))
            {
                warningMessage = "リポジトリ URL または版を解決できませんでした。";
                return null;
            }

            // 外から来る値(URL・版・パス)が `-` で始まるとオプションとして解釈され得る・`?path=` が `..` 等でリポジトリの外を指す、
            // といったものは実行前に弾く(例外にはしない)。
            if (!GitArguments.TryNormalizePackagePath(subPath, out var cleanPath))
            {
                warningMessage = "パッケージのパス(?path=)が不正なため取得しませんでした(`..`・ドライブ名・`-` で始まる値などは使えません)。";
                return null;
            }

            var unsafeName = GitArguments.FirstUnsafe(
                new[] { "リポジトリ URL", "版(タグ)" },
                new[] { cloneUrl, reference });
            if (unsafeName != null)
            {
                warningMessage = GitArguments.UnsafeWarning(unsafeName);
                return null;
            }

            if (string.IsNullOrEmpty(_tempRoot))
            {
                warningMessage = "作業フォルダを解決できませんでした。";
                return null;
            }

            string workDir = null;
            try
            {
                CleanupStale(_tempRoot);
                workDir = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_tempRoot);

                var startedAt = DateTime.UtcNow;
                var clone = GitProcess.Run(GitArguments.CloneNoCheckout(cloneUrl, reference, workDir), null, TotalTimeoutMs, _cancellation);
                if (!clone.Success)
                {
                    warningMessage = clone.Error;
                    return null;
                }

                var remainingMs = TotalTimeoutMs - (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                if (remainingMs <= 0)
                {
                    warningMessage = "git がタイムアウトしました(30 秒)。";
                    return null;
                }

                var show = GitProcess.Run(GitArguments.ShowPackageJson(cleanPath), workDir, remainingMs, _cancellation, MaxPackageJsonChars);
                if (!show.Success)
                {
                    warningMessage = show.Cancelled ? show.Error : "その版に package.json が見つかりませんでした(" + show.Error + ")";
                    return null;
                }

                return show.Stdout;
            }
            catch (Exception e)
            {
                warningMessage = "git の実行に失敗しました: " + e.Message;
                return null;
            }
            finally
            {
                TryDelete(workDir);
            }
        }

        // 前回のタイムアウト / 異常終了で残った一時フォルダ(10 分より古いもの)を消す。同時に走っている取得のフォルダは新しいので残る。
        public static void CleanupStale(string tempRoot)
        {
            try
            {
                if (string.IsNullOrEmpty(tempRoot) || !Directory.Exists(tempRoot))
                {
                    return;
                }

                foreach (var dir in Directory.GetDirectories(tempRoot))
                {
                    if (DateTime.UtcNow - Directory.GetCreationTimeUtc(dir) > StaleAge)
                    {
                        TryDelete(dir);
                    }
                }
            }
            catch (Exception)
            {
                // 掃除の失敗は無視する(Temp/ は Unity が管理する場所)。
            }
        }

        private static void TryDelete(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                return;
            }

            try
            {
                // .git 配下の読み取り専用ファイル(オブジェクト)で Delete が失敗するため、属性を外してから消す。
                // 作業ツリーが無いのでシンボリックリンクは作られない(念のためリンクのディレクトリへは降りない)。
                ClearReadOnly(dir);
                Directory.Delete(dir, true);
            }
            catch (Exception)
            {
                // 一時フォルダの掃除失敗は無視する(次回 CleanupStale が消す)。
            }
        }

        private static void ClearReadOnly(string dir)
        {
            foreach (var file in Directory.GetFiles(dir))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            foreach (var sub in Directory.GetDirectories(dir))
            {
                if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                ClearReadOnly(sub);
            }
        }
    }
}
