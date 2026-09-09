using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // Test Runner の失敗(メッセージ + スタックトレース)を Logs/ddrive-test-failures.log と Console に書き出す。
    // MCP の run_tests は失敗メッセージしか返さず、Editor.log にも NUnit の例外は出ないため、
    // 順序依存の失敗などを追うときにここを見る(2026-09-09)。
    [InitializeOnLoad]
    public static class TestFailureLogger
    {
        public const string LogPath = "Logs/ddrive-test-failures.log";

        static TestFailureLogger()
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks());
        }

        private sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                    File.WriteAllText(LogPath, $"# {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} run started: {testsToRun?.Name}\n");
                }
                catch (System.Exception)
                {
                    // ログが書けなくてもテストは止めない
                }
            }

            public void RunFinished(ITestResultAdaptor result)
            {
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result == null || result.Test == null || result.Test.IsSuite)
                {
                    return;
                }

                if (result.TestStatus != TestStatus.Failed)
                {
                    return;
                }

                var text = $"FAILED {result.FullName}\n{result.Message}\n{result.StackTrace}\n--- output ---\n{result.Output}\n\n";
                try
                {
                    File.AppendAllText(LogPath, text);
                }
                catch (System.Exception)
                {
                }

                Debug.LogWarning("[DDrive][TestFailure] " + text);
            }
        }
    }
}
