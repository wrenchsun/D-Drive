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

        // ── 初期アイコンの自動生成(2026-09-11) ──

        [Test]
        public void DefaultIcon_TextureData_UsesSourceTexture()
        {
            EnsureTestRoot();
            var source = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var pixels = new Color[64];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = Color.cyan;
            source.SetPixels(pixels);
            source.Apply();

            var data = ScriptableObject.CreateInstance<DDrive.Runtime.Material.TextureData>();
            data.Id = 2;
            data.Texture = source;
            AssetDatabase.CreateAsset(data, TestRoot + "/TEX_Icon_Test.asset");
            try
            {
                Assert.IsTrue(AssetIconService.CanCreateDefaultIcon(data), "Texture が入っていれば自動生成できる");
                Texture2D result = null;
                var started = AssetIconService.TryCreateDefaultIcon(data, icon => result = icon, 32, TestRoot + "/Icons");

                Assert.IsTrue(started);
                Assert.IsNotNull(result, "同期で生成される");
                Assert.AreEqual(32, result.width);
                Assert.AreSame(result, data.Icon);
                Assert.AreEqual($"{TestRoot}/Icons/{AssetType.Texture}/TEX_Icon_Test_Icon.png", AssetDatabase.GetAssetPath(result));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        // 一括生成 / ボタンはコールバック無しで呼ぶ。onDone?.Invoke(CropAndSave(...)) だと生成自体が飛ぶバグがあった(2026-09-11)。
        [Test]
        public void DefaultIcon_WithoutCallback_StillAssignsIcon()
        {
            EnsureTestRoot();
            var source = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var data = ScriptableObject.CreateInstance<DDrive.Runtime.Material.TextureData>();
            data.Id = 4;
            data.Texture = source;
            AssetDatabase.CreateAsset(data, TestRoot + "/TEX_Icon_NoCallback.asset");
            try
            {
                Assert.IsTrue(AssetIconService.TryCreateDefaultIcon(data, null, 16, TestRoot + "/Icons"));
                Assert.IsNotNull(data.Icon, "コールバック無しでも Icon が割り当てられる");
                Assert.AreEqual(16, data.Icon.width);
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void DefaultIcon_TextureData_WithoutSource_CannotCreate()
        {
            var data = ScriptableObject.CreateInstance<DDrive.Runtime.Material.TextureData>();
            try
            {
                Assert.IsFalse(AssetIconService.CanCreateDefaultIcon(data), "元アセット未設定なら生成できない");
                Assert.IsFalse(AssetIconService.TryCreateDefaultIcon(data, null, 32, TestRoot + "/Icons"));
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void DefaultIcon_MaterialData_RendersSphere()
        {
            EnsureTestRoot();
            var data = ScriptableObject.CreateInstance<DDrive.Runtime.Material.MaterialData>();
            data.Id = 3;
            data.Shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            data.Common = DDrive.Runtime.Material.MaterialCommon.Default;
            AssetDatabase.CreateAsset(data, TestRoot + "/MAT_Icon_Test.asset");

            Assert.IsTrue(AssetIconService.CanCreateDefaultIcon(data), "Material は元アセットが無くても描画で生成できる");
            Texture2D result = null;
            var started = AssetIconService.TryCreateDefaultIcon(data, icon => result = icon, 64, TestRoot + "/Icons");

            Assert.IsTrue(started);
            Assert.IsNotNull(result);
            Assert.AreEqual(64, result.width);
            Assert.AreSame(result, data.Icon);
            Assert.AreEqual($"{TestRoot}/Icons/{AssetType.Material}/MAT_Icon_Test_Icon.png", AssetDatabase.GetAssetPath(result));
        }

        private static void EnsureTestRoot()
        {
            if (!AssetDatabase.IsValidFolder(TestRoot))
            {
                AssetDatabase.CreateFolder("Assets/DDrive/Tests/Editor", "TempIcons");
            }
        }
    }
}
