using DDrive.Foundation.Identity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // AssetId<TMarker> はかつて `readonly struct` + readonly フィールドで宣言されていたため、
    // Unity のシリアライズ/Inspector編集ができず SeEmitter 等のフィールドが Inspector に
    // 何も表示されないバグがあった(2026-07-27 発見・修正)。この非readonly化が本当に
    // ラウンドトリップすることを実アセットの保存/再読込で直接検証する。
    public class AssetIdSerializationTests
    {
        private sealed class IdHolder : ScriptableObject
        {
            public AssetId<TestAssetMarker> Id;
        }

        private const string TempDir = "Packages/com.ddrive.core/Tests/Editor/Temp";
        private const string AssetPath = TempDir + "/IdHolder_RoundTrip.asset";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.LoadAssetAtPath<IdHolder>(AssetPath) != null)
            {
                AssetDatabase.DeleteAsset(AssetPath);
            }
        }

        [Test]
        public void AssetId_Field_SurvivesAssetSaveAndReload()
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
            {
                AssetDatabase.CreateFolder("Packages/com.ddrive.core/Tests/Editor", "Temp");
            }

            var holder = ScriptableObject.CreateInstance<IdHolder>();
            holder.Id = new AssetId<TestAssetMarker>(0x1234_5678, AssetType.Se);

            AssetDatabase.CreateAsset(holder, AssetPath);
            using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }
            AssetDatabase.Refresh();

            var reloaded = AssetDatabase.LoadAssetAtPath<IdHolder>(AssetPath);

            Assert.IsNotNull(reloaded);
            Assert.AreEqual(0x1234_5678UL, reloaded.Id.Value);
            Assert.AreEqual(AssetType.Se, reloaded.Id.Type);
        }

        [Test]
        public void AssetId_Field_IsEditableViaSerializedProperty()
        {
            var holder = ScriptableObject.CreateInstance<IdHolder>();
            var so = new SerializedObject(holder);
            var idProp = so.FindProperty("Id");

            Assert.IsNotNull(idProp, "SerializedProperty for the AssetId field should be found");

            var valueProp = idProp.FindPropertyRelative("value");
            var typeProp = idProp.FindPropertyRelative("type");

            Assert.IsNotNull(valueProp, "readonly fields would make this null - regression guard");
            Assert.IsNotNull(typeProp);

            valueProp.ulongValue = 999UL;
            typeProp.enumValueIndex = (int)AssetType.Vfx;
            so.ApplyModifiedProperties();

            Assert.AreEqual(999UL, holder.Id.Value);
            Assert.AreEqual(AssetType.Vfx, holder.Id.Type);

            Object.DestroyImmediate(holder);
        }
    }
}
