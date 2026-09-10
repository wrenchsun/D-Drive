using DDrive.Editor.Inspector;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [09_editor_tools.md] §8.1 — Data アイコンの「シーンから作成」(カメラ撮影 → 切り出し → PNG → Icon 割り当て)の検証。
    public class AssetIconServiceTests
    {
        private const string TestRoot = "Assets/DDrive/Tests/Editor/TempIcons";
        private const string DataPath = TestRoot + "/SE_Icon_Test.asset";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AssetDatabase.DeleteAsset(TestRoot);
            }
        }

        [Test]
        public void RenderView_CropAndSave_WritesSquarePng_UnderTypeFolder_AndAssignsIcon()
        {
            if (!AssetDatabase.IsValidFolder(TestRoot))
            {
                AssetDatabase.CreateFolder("Assets/DDrive/Tests/Editor", "TempIcons");
            }

            var data = ScriptableObject.CreateInstance<SeData>();
            data.Id = 1;
            AssetDatabase.CreateAsset(data, DataPath);

            var camGo = new GameObject("IconTestCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.magenta;
            Texture2D shot = null;
            try
            {
                shot = AssetIconService.RenderView(cam, 320);
                Assert.IsNotNull(shot, "カメラの見た目を読める");
                Assert.GreaterOrEqual(shot.width, 64);

                var crop = new RectInt(10, 10, 100, 100);
                var icon = AssetIconService.CropAndSave(data, shot, crop, 128, TestRoot + "/Icons");

                Assert.IsNotNull(icon, "PNG が書き出されてテクスチャとして読める");
                Assert.AreEqual(128, icon.width);
                Assert.AreEqual(128, icon.height);
                Assert.AreSame(icon, data.Icon, "Icon に割り当てられる");
                var path = AssetDatabase.GetAssetPath(icon);
                Assert.AreEqual($"{TestRoot}/Icons/{AssetType.Se}/SE_Icon_Test_Icon.png", path, "<Icons>/<種別>/<アセット名>_Icon.png に保存される");

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.IsNotNull(importer);
                Assert.IsFalse(importer.mipmapEnabled, "Editor 表示用にミップ無し");

                // 2 回目は上書き(ファイルが増えない)
                AssetIconService.CropAndSave(data, shot, crop, 128, TestRoot + "/Icons");
                Assert.AreEqual(1, AssetDatabase.FindAssets("t:Texture2D", new[] { TestRoot + "/Icons" }).Length);

                AssetIconService.Clear(data);
                Assert.IsNull(data.Icon);
            }
            finally
            {
                if (shot != null)
                {
                    Object.DestroyImmediate(shot);
                }

                Object.DestroyImmediate(camGo);
            }
        }
    }
}
