using System.Collections.Generic;
using DDrive.Editor.Mcp.Tools;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor.Mcp
{
    // [1014_extension_platform.md] §2.1(X-1、2026-10-16) — ddrive_validate の行に Extension 種別の種別キーが出る。
    public class DDriveExtensionKindSummaryTests
    {
        private struct SummaryKindMarker
        {
        }

        [AssetIdDefinition(AssetType.Extension, typeof(SummaryKindMarker), "SUMKINDID", KindKey = "SummaryKind")]
        private sealed class SummaryKindData : AssetDataBase
        {
        }

        [Test]
        public void Rows_ForExtensionData_CarryKindKey_AndMapToItInTypeField()
        {
            var ext = ScriptableObject.CreateInstance<SummaryKindData>();
            var se = ScriptableObject.CreateInstance<DDrive.Runtime.Audio.SeData>();
            try
            {
                var reports = new List<ValidationReport>
                {
                    new ValidationReport(ext, ValidationResult.Warning("w", code: "W-X")),
                    new ValidationReport(se, ValidationResult.Warning("w", code: "W-X")),
                };

                var summary = ValidationSummary.Build(reports, null);
                Assert.AreEqual(2, summary.Rows.Count);

                Assert.AreEqual(AssetType.Extension, summary.Rows[0].Type);
                Assert.AreEqual("SummaryKind", summary.Rows[0].KindKey);
                Assert.AreEqual("SummaryKind", (string)ValidationSummary.MapRow(summary.Rows[0])["type"]);

                Assert.AreEqual(AssetType.Se, summary.Rows[1].Type);
                Assert.AreEqual(string.Empty, summary.Rows[1].KindKey);
                Assert.AreEqual("Se", (string)ValidationSummary.MapRow(summary.Rows[1])["type"], "組み込み種別の表示は従来どおり");
            }
            finally
            {
                Object.DestroyImmediate(ext);
                Object.DestroyImmediate(se);
            }
        }
    }
}
