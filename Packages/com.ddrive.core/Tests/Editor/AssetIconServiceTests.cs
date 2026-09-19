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
        private const string TestRoot = "Packages/com.ddrive.core/Tests/Editor/TempIcons";
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
                AssetDatabase.CreateFolder("Packages/com.ddrive.core/Tests/Editor", "TempIcons");
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
                Assert.AreEqual($"{TestRoot}/Icons/{AssetType.Se}/{AssetIconService.IconFileName(data)}.png", path,
                    "<Icons>/<種別>/<アセット名>_<GUID8>_Icon.png に保存される");
                StringAssert.StartsWith($"{TestRoot}/Icons/{AssetType.Se}/SE_Icon_Test_", path);
                StringAssert.EndsWith("_Icon.png", path);

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.IsNotNull(importer);
                Assert.IsFalse(importer.mipmapEnabled, "Editor 表示用にミップ無し");

                // 2 回目は上書き(ファイルが増えない)
                AssetIconService.CropAndSave(data, shot, crop, 128, TestRoot + "/Icons");
                DDrive.Editor.AssetSearch.Invalidate(); // 作りたてのアセットを同じフレームで数えるため([09] §9)
                Assert.AreEqual(1, DDrive.Editor.AssetSearch.FindAssets("t:Texture2D", new[] { TestRoot + "/Icons" }).Length);

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
                Assert.AreEqual($"{TestRoot}/Icons/{AssetType.Texture}/{AssetIconService.IconFileName(data)}.png", AssetDatabase.GetAssetPath(result));
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
            Assert.AreEqual($"{TestRoot}/Icons/{AssetType.Material}/{AssetIconService.IconFileName(data)}.png", AssetDatabase.GetAssetPath(result));

            // 2026-09-11 レビュー対応: サイズとパスだけだと「背景だけの真っ黒 PNG」でも通ってしまうので、
            // サムネイル背景色(0.18)と違うピクセルが十分にあること(= 球が描けていること)を見る。
            AssertHasForeground(result, MaterialThumbnailBackground, 0.05f);
        }

        // MaterialThumbnailRenderer.BackgroundColor と同じ値。
        private static readonly Color MaterialThumbnailBackground = new(0.18f, 0.18f, 0.18f, 1f);

        // background と目に見えて違うピクセルが minRatio 以上あることを確かめる。
        private static void AssertHasForeground(Texture2D texture, Color background, float minRatio)
        {
            var path = AssetDatabase.GetAssetPath(texture);
            // インポート済みテクスチャは Read/Write off なので、いったん RenderTexture 経由で読み取る。
            var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
            var readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            try
            {
                var previous = RenderTexture.active;
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                readable.Apply();
                RenderTexture.active = previous;

                var pixels = readable.GetPixels();
                var differing = 0;
                for (var i = 0; i < pixels.Length; i++)
                {
                    var d = pixels[i];
                    if (Mathf.Abs(d.r - background.r) > 0.02f || Mathf.Abs(d.g - background.g) > 0.02f || Mathf.Abs(d.b - background.b) > 0.02f)
                    {
                        differing++;
                    }
                }

                var ratio = (float)differing / pixels.Length;
                Assert.Greater(ratio, minRatio, $"'{path}' が背景色だけの画像になっている(異なるピクセル {ratio:P1})");
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(readable);
            }
        }

        private static void EnsureTestRoot()
        {
            if (!AssetDatabase.IsValidFolder(TestRoot))
            {
                AssetDatabase.CreateFolder("Packages/com.ddrive.core/Tests/Editor", "TempIcons");
            }
        }
    }
}
