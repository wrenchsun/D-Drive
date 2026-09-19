using DDrive.Editor.Inspectors;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    public class AssetIdLookupTests
    {
        private const string TempDir = "Packages/com.ddrive.core/Tests/Editor/Temp";
        private const string AssetPath = TempDir + "/Lookup_TestAssetData.asset";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.LoadAssetAtPath<TestAssetData>(AssetPath) != null)
            {
                AssetDatabase.DeleteAsset(AssetPath);
            }
        }

        [Test]
        public void GetCandidates_FindsRegisteredAssetByMarkerType()
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
            {
                AssetDatabase.CreateFolder("Packages/com.ddrive.core/Tests/Editor", "Temp");
            }

            var data = ScriptableObject.CreateInstance<TestAssetData>();
            data.Id = 0xABCDEF;
            data.DisplayName = "LookupCandidate";
            AssetDatabase.CreateAsset(data, AssetPath);
            using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }

            var candidates = AssetIdLookup.GetCandidates(typeof(TestAssetMarker));

            Assert.IsTrue(System.Array.Exists(candidates, c => c.Id == 0xABCDEF && c.DisplayName == "LookupCandidate"));
        }

        [Test]
        public void GetCandidates_UnknownMarkerType_ReturnsEmpty()
        {
            var candidates = AssetIdLookup.GetCandidates(typeof(UnknownMarkerForTest));
            Assert.AreEqual(0, candidates.Length);
        }

        private readonly struct UnknownMarkerForTest
        {
        }
    }
}
