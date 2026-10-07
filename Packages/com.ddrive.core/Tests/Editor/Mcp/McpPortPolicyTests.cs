using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DDrive.Editor.Mcp;
using DDrive.Editor.Validation;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §6.1 (c) / §6.3 MCP-8 後半(2026-10-07) — isuzu のポート規則の写し(McpPortProbe)が実物と一致すること、
    // D-Drive と MS2026 のパスでポートが分かれること、固定ポートの検出(DD-MCP-FIXED-PORT)。
    // isuzu を上げて規則が変わればここが赤くなる。
    public class McpPortPolicyTests
    {
        private const string DDriveAssets = "C:/Users/yamag/wrench/D-Drive/Assets";
        private const string Ms2026Assets = "C:/Users/yamag/wrench/MS2026/Assets";

        [TearDown]
        public void TearDown()
        {
            McpPortProbe.DescriptorDirectoryOverride = null;
        }

        [Test]
        public void DerivePort_ForThisProject_IsInIsuzuRange()
        {
            var port = McpInstanceInfo.DerivePort(Application.dataPath);
            Assert.GreaterOrEqual(port, 27200);
            Assert.LessOrEqual(port, 27999);
        }

        [Test]
        public void DerivePort_DiffersBetweenDDriveAndMs2026()
        {
            Assert.AreNotEqual(McpInstanceInfo.DerivePort(DDriveAssets), McpInstanceInfo.DerivePort(Ms2026Assets));
        }

        // isuzu の McpPortPolicy は internal。リフレクションで実物と突き合わせる(到達できなければスキップ)。
        [Test]
        public void DerivePort_MatchesIsuzuMcpPortPolicy()
        {
            var type = Type.GetType("UnityMCP.Editor.Core.McpPortPolicy, UnityMCP.Editor");
            var derive = type?.GetMethod("Derive", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (derive == null)
            {
                Assert.Ignore("McpPortPolicy.Derive に到達できないためスキップ(isuzu の構造が変わった。記述子との照合テストで代替)");
            }

            foreach (var path in new[] { DDriveAssets, Ms2026Assets, Application.dataPath })
            {
                var real = (int)derive.Invoke(null, new object[] { path });
                Assert.AreEqual(real, McpInstanceInfo.DerivePort(path), "isuzu の規則とずれている: " + path);
            }
        }

        [Test]
        public void LiveDescriptor_PreferredPort_IsDerivedPort_WhenNotFixed()
        {
            if (!McpInstanceInfo.TryReadDescriptor(Application.dataPath, out _, out var preferred, out _, out _, out _))
            {
                Assert.Ignore("この Editor の記述子が無い(isuzu のサーバーが起動していない)");
            }

            if (McpPortProbe.LooksFixed(Application.dataPath))
            {
                Assert.Ignore("この環境はポートを固定している(DD-MCP-FIXED-PORT の対象)");
            }

            Assert.AreEqual(McpInstanceInfo.DerivePort(Application.dataPath), preferred);
        }

        // isuzu が規則(ハッシュ)を変えたら赤。2026-10-07 に実機の記述子と一致を確認した値。
        [Test]
        public void HashProjectPath_KnownValue()
        {
            Assert.AreEqual("a26b71fdfd662823", McpInstanceInfo.HashProjectPath(DDriveAssets));
        }

        // ── DD-MCP-FIXED-PORT ──

        private static string WriteFakeDescriptor(string dataPath, int port, int preferredPort)
        {
            var dir = Path.Combine(Path.GetTempPath(), "ddrive_mcp_probe_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            McpPortProbe.DescriptorDirectoryOverride = dir;
            File.WriteAllText(
                McpPortProbe.DescriptorPath(dataPath),
                "{\"port\":" + port + ",\"preferredPort\":" + preferredPort + ",\"pid\":1234,\"projectName\":\"Fake\",\"token\":\"secret\"}");
            return dir;
        }

        private static int OtherThan(int derived) => derived == 28000 ? 28001 : 28000;

        [Test]
        public void LooksFixed_PreferredDiffersFromDerived_IsTrue()
        {
            var dir = WriteFakeDescriptor(DDriveAssets, 28000, OtherThan(McpPortProbe.DerivePort(DDriveAssets)));
            try
            {
                Assert.IsTrue(McpPortProbe.LooksFixed(DDriveAssets));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void LooksFixed_PreferredEqualsDerived_IsFalse_AndNoDescriptorIsFalse()
        {
            var derived = McpPortProbe.DerivePort(DDriveAssets);
            var dir = WriteFakeDescriptor(DDriveAssets, derived, derived);
            try
            {
                Assert.IsFalse(McpPortProbe.LooksFixed(DDriveAssets));
                Assert.IsFalse(McpPortProbe.LooksFixed(Ms2026Assets), "別パスの記述子は無い");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void ProjectSetupValidator_EmitsInfo_OnlyWhenFixed()
        {
            // Validator は Application.dataPath で記述子を探す。偽の記述子をこの Editor のパスで置く(フォルダは差し替え)。
            var dataPath = Application.dataPath;
            var derived = McpPortProbe.DerivePort(dataPath);
            var dummy = ScriptableObject.CreateInstance<SeData>();
            var dir = WriteFakeDescriptor(dataPath, OtherThan(derived), OtherThan(derived));
            try
            {
                var found = false;
                foreach (var r in new ProjectSetupValidator().Validate(dummy, NewContext(dummy)))
                {
                    if (r.Code == ProjectSetupValidator.CodeMcpFixedPort)
                    {
                        found = true;
                        Assert.AreEqual(ValidationSeverity.Info, r.Severity);
                    }
                }

                Assert.IsTrue(found, "固定ポートの記述子で DD-MCP-FIXED-PORT(Info)が出る");

                // 固定でなければ出ない(新しい ValidationContext = 新しい Run All)。
                File.WriteAllText(
                    McpPortProbe.DescriptorPath(dataPath), "{\"port\":" + derived + ",\"preferredPort\":" + derived + "}");
                foreach (var r in new ProjectSetupValidator().Validate(dummy, NewContext(dummy)))
                {
                    Assert.AreNotEqual(ProjectSetupValidator.CodeMcpFixedPort, r.Code);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(dummy);
                Directory.Delete(dir, true);
            }
        }

        private static ValidationContext NewContext(AssetDataBase data) =>
            new ValidationContext(new List<AssetDataBase> { data });
    }
}
