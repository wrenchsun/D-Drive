using System.IO;
using DDrive.Editor.Materials;
using DDrive.Runtime.Material;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [06_material_texture.md] B-4 — TextureData の Usage / Channel / SliceBorder を Importer に自動反映する(2026-09-11)。
    // ファイル名は規約に当たらない名前(Plain.png)にして、Data 側の設定だけで決まることを確かめる。
    public class TextureDataImporterSyncTests
    {
        private const string TempFolder = "Assets/DDrive/Tests/Editor/Temp";
        private const string PlainPath = TempFolder + "/SyncPlain.png";
        private const string NormalNamedPath = TempFolder + "/SyncNamed_N.png";

        private TextureData _data;

        [SetUp]
        public void SetUp()
        {
            TextureDataImporterSync.AutoApply = false; // テスト中は明示呼び出しだけ
            TexturePostprocessor.Suppress = false;
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets/DDrive/Tests/Editor", "Temp");
            }

            WritePng(PlainPath);
            WritePng(NormalNamedPath);
            _data = ScriptableObject.CreateInstance<TextureData>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_data);
            AssetDatabase.DeleteAsset(PlainPath);
            AssetDatabase.DeleteAsset(NormalNamedPath);
            TextureDataImporterSync.AutoApply = true;
        }

        private static void WritePng(string path)
        {
            var png = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            File.WriteAllBytes(path, png.EncodeToPNG());
            Object.DestroyImmediate(png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        private static TextureImporter ImporterOf(string path) => (TextureImporter)AssetImporter.GetAtPath(path);

        [Test]
        public void UsageUI_SetsSpriteTypeBorder_AndAssignsSprite()
        {
            _data.Texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PlainPath);
            _data.Usage = TextureUsage.UI;
            _data.SliceBorder = new Vector4(2, 1, 3, 2);

            var changes = TextureDataImporterSync.Apply(_data);

            var importer = ImporterOf(PlainPath);
            Assert.AreEqual(TextureImporterType.Sprite, importer.textureType, string.Join(" / ", changes));
            Assert.AreEqual(SpriteImportMode.Single, importer.spriteImportMode);
            Assert.AreEqual(new Vector4(2, 1, 3, 2), importer.spriteBorder, "SliceBorder が 9-slice の境界に書かれる");
            Assert.IsNotNull(_data.Sprite, "Sprite が割り当てられる");

            // 2 回目は変更なし
            Assert.IsEmpty(TextureDataImporterSync.Apply(_data));
        }

        [Test]
        public void ChannelNormal_SetsNormalMap_AndBackToDefaultWhenChanged()
        {
            _data.Texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PlainPath);
            _data.Usage = TextureUsage.Model;
            _data.Channel = TextureChannel.Normal;

            TextureDataImporterSync.Apply(_data);
            Assert.AreEqual(TextureImporterType.NormalMap, ImporterOf(PlainPath).textureType);

            _data.Channel = TextureChannel.Albedo;
            TextureDataImporterSync.Apply(_data);
            var importer = ImporterOf(PlainPath);
            Assert.AreEqual(TextureImporterType.Default, importer.textureType, "Normal 以外に変えたら Default に戻す");
            Assert.IsTrue(importer.sRGBTexture, "Albedo は sRGB on");
        }

        [Test]
        public void ChannelMask_DisablesSrgb()
        {
            _data.Texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PlainPath);
            _data.Usage = TextureUsage.Model;
            _data.Channel = TextureChannel.Mask;

            TextureDataImporterSync.Apply(_data);

            Assert.IsFalse(ImporterOf(PlainPath).sRGBTexture);
        }

        [Test]
        public void ConflictWithNamingRule_DoesNotApply_AndWarns()
        {
            // "_N" は既定規約で NormalMap。Data は Albedo → 食い違いなので書かない。
            // AppliesTo は Tests 配下を常に除外するので、プロファイルを注入して(パス条件は省かれる)規約適用済みの状態を手で作る。
            var profile = ScriptableObject.CreateInstance<TextureImportProfile>();
            try
            {
                Assume.That(profile.TryMatch(NormalNamedPath, out var rule) && rule.Type == TextureImporterType.NormalMap, "既定規約 _N → NormalMap");
                var importer = ImporterOf(NormalNamedPath);
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();

                _data.Texture = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalNamedPath);
                _data.Usage = TextureUsage.Model;
                _data.Channel = TextureChannel.Albedo;

                UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("食い違う"));
                var changes = TextureDataImporterSync.Apply(_data, profile);

                Assert.IsEmpty(changes);
                Assert.AreEqual(TextureImporterType.NormalMap, ImporterOf(NormalNamedPath).textureType, "規約の NormalMap のまま");
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        // 9-slice の境界はテクスチャサイズに丸めてから Importer と比べる(丸めないと毎回「食い違い → 再インポート」になる)。
        [Test]
        public void ClampBorder_FitsInsideTexture()
        {
            Assert.AreEqual(new Vector4(2, 1, 3, 2), TextureDataImporterSync.ClampBorder(new Vector4(2, 1, 3, 2), 8, 8), "収まっていればそのまま");
            Assert.AreEqual(new Vector4(8, 0, 0, 0), TextureDataImporterSync.ClampBorder(new Vector4(100, 0, 100, 0), 8, 8), "左 + 右 ≤ 幅");
            Assert.AreEqual(new Vector4(0, 8, 0, 0), TextureDataImporterSync.ClampBorder(new Vector4(0, 100, 0, 100), 8, 8), "下 + 上 ≤ 高さ");
            Assert.AreEqual(Vector4.zero, TextureDataImporterSync.ClampBorder(new Vector4(-5, -5, -5, -5), 8, 8), "負の値は 0");
        }

        // AutoApply=true の通常経路(変更イベント → delayCall)。1 回適用したら収束して、2 回目は再インポートしない(2026-09-11)。
        [Test]
        public void PendingApply_Converges_AndPropertyEditDoesNotReimport()
        {
            var dataPath = TempFolder + "/SyncConverge.asset";
            TextureDataImporterSync.AutoApply = true;
            var asset = ScriptableObject.CreateInstance<TextureData>();
            AssetDatabase.CreateAsset(asset, dataPath);
            try
            {
                var data = AssetDatabase.LoadAssetAtPath<TextureData>(dataPath);
                Undo.RecordObject(data, "Edit TextureData");
                data.Texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PlainPath);
                data.Usage = TextureUsage.UI;
                data.SliceBorder = new Vector4(2, 1, 3, 2);
                EditorUtility.SetDirty(data);

                // 変更イベントは EditMode テストでは届かないので、積む → 処理する、を直接呼ぶ。
                TextureDataImporterSync.MarkPending(data);
                TextureDataImporterSync.ProcessPending();
                Assert.AreEqual(TextureImporterType.Sprite, ImporterOf(PlainPath).textureType);

                // DisplayName を変えただけ(Importer に関係ない編集)では再インポートする差分が出ない
                Undo.RecordObject(data, "Rename");
                data.DisplayName = "名前だけ変更";
                EditorUtility.SetDirty(data);
                TextureDataImporterSync.MarkPending(data);
                TextureDataImporterSync.ProcessPending();

                Assert.IsEmpty(TextureDataImporterSync.Apply(data), "2 回目は差分なし(収束している)");
            }
            finally
            {
                TextureDataImporterSync.AutoApply = false;
                AssetDatabase.DeleteAsset(dataPath);
            }
        }

        [Test]
        public void NoTexture_DoesNothing()
        {
            Assert.IsEmpty(TextureDataImporterSync.Apply(_data));
            Assert.IsEmpty(TextureDataImporterSync.Apply(null));
        }
    }
}
