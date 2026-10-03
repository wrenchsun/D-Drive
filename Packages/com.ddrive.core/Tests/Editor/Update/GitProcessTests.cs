using System.Diagnostics;
using System.Threading;
using DDrive.Editor.Update;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Update
{
    // 修正ラウンド 3(2026-10-03、docs/55 FX-R-05 / FX-R-06) — GitProcess の後始末。
    //   ・終了後は台帳(RunningCount)から外れる / 実行中が無いときの KillAllRunning は何もしない
    //   ・キャンセル済みのトークンでは長くかからず戻り、台帳に残らない
    // git が無い環境では Inconclusive にする(実ネットワークには接続しない: ローカルの --version と、到達しない宛先へのキャンセルだけ)。
    public class GitProcessTests
    {
        [Test]
        public void Run_Version_Succeeds_AndLeavesNothingRunning()
        {
            var outcome = GitProcess.Run(new[] { "--version" }, null, 10000, CancellationToken.None);
            Assume.That(outcome.Success, "git が使えること: " + outcome.Error);

            StringAssert.Contains("git version", outcome.Stdout);
            Assert.AreEqual(0, GitProcess.RunningCount, "終了した git は台帳に残らない");
        }

        [Test]
        public void KillAllRunning_WithNothingRunning_DoesNothing()
        {
            Assert.DoesNotThrow(GitProcess.KillAllRunning);
            Assert.AreEqual(0, GitProcess.RunningCount);
        }

        [Test]
        public void Run_WithCancelledToken_ReturnsPromptly_AndLeavesNothingRunning()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var stopwatch = Stopwatch.StartNew();

            // 到達しない宛先(TEST-NET-1)。キャンセル済みなので最初の 100 ms の確認で止まる(接続の成否に関わらず長くかからない)。
            var outcome = GitProcess.Run(new[] { "ls-remote", "--tags", "--", "http://192.0.2.1/ddrive-test.git" }, null, 60000, cts.Token);

            Assert.Less(stopwatch.ElapsedMilliseconds, 15000, "キャンセルしたら待たずに戻る(ツリーごと止める)");
            Assert.IsFalse(outcome.Success && !outcome.Cancelled);
            Assert.AreEqual(0, GitProcess.RunningCount);
        }
    }
}
