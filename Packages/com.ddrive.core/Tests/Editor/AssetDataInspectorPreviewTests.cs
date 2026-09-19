using DDrive.Editor.Inspector;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Material;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [09_editor_tools.md] §8.2 — Project ウィンドウのグリッド表示サムネイル(5-10)。
    // AssetDataInspector.RenderStaticPreview が Icon を要求サイズに縮小して返すこと、
    // Icon 未設定でも例外を出さず null にフォールバックすることを検証する。
    public class AssetDataInspectorPreviewTests
    {
        [Test]
        public void RenderStaticPreview_WithIcon_ReturnsScaledTexture()
        {
            // AssetDataInspector は [CustomEditor(typeof(AssetDataBase), true)] なので、
            // 独自 Inspector を持たない Data 型(TextureData)にもそのまま適用される。
            var data = ScriptableObject.CreateInstance<TextureData>();
            var icon = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var editor = UnityEditor.Editor.CreateEditor(data);
            Texture2D preview = null;
            try
            {
                data.Icon = icon;
                preview = editor.RenderStaticPreview(string.Empty, null, 32, 32);

                Assert.IsNotNull(preview, "Icon が設定されていればサムネイルが生成される");
                Assert.AreEqual(32, preview.width);
                Assert.AreEqual(32, preview.height);
            }
            finally
            {
                if (preview != null) Object.DestroyImmediate(preview);
                Object.DestroyImmediate(icon);
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void RenderStaticPreview_WithoutIcon_ReturnsNullWithoutThrowing()
        {
            var data = ScriptableObject.CreateInstance<TextureData>();
            var editor = UnityEditor.Editor.CreateEditor(data);
            try
            {
                Texture2D preview = null;
                Assert.DoesNotThrow(() => preview = editor.RenderStaticPreview(string.Empty, null, 32, 32));
                Assert.IsNull(preview, "Icon 未設定なら既定の動作(スクリプトアイコン)に委ねて null を返す");
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(data);
            }
        }

        // 種別独自の Inspector(SeDataEditor)が AssetDataInspector を継承している場合も、
        // RenderStaticPreview を上書きしていなければ同じ挙動を自動で引き継ぐことの確認。
        [Test]
        public void RenderStaticPreview_OnSubclassEditor_InheritsBaseBehaviour()
        {
            var data = ScriptableObject.CreateInstance<SeData>();
            var icon = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var editor = UnityEditor.Editor.CreateEditor(data);
            Texture2D preview = null;
            try
            {
                Assert.IsInstanceOf<AssetDataInspector>(editor, "SeDataEditor は AssetDataInspector を継承している");

                data.Icon = icon;
                preview = editor.RenderStaticPreview(string.Empty, null, 48, 48);

                Assert.IsNotNull(preview);
                Assert.AreEqual(48, preview.width);
                Assert.AreEqual(48, preview.height);
            }
            finally
            {
                if (preview != null) Object.DestroyImmediate(preview);
                Object.DestroyImmediate(icon);
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void ScaleForPreview_NullSource_ReturnsNull()
        {
            Assert.IsNull(AssetIconService.ScaleForPreview(null, 32, 32));
        }

        [Test]
        public void ScaleForPreview_ResizesToRequestedDimensions()
        {
            var source = new Texture2D(200, 100, TextureFormat.RGBA32, false);
            Texture2D result = null;
            try
            {
                result = AssetIconService.ScaleForPreview(source, 40, 20);
                Assert.IsNotNull(result);
                Assert.AreEqual(40, result.width);
                Assert.AreEqual(20, result.height);
            }
            finally
            {
                if (result != null) Object.DestroyImmediate(result);
                Object.DestroyImmediate(source);
            }
        }
    }
}
