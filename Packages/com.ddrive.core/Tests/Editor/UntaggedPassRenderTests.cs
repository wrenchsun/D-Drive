using System.Collections.Generic;
using DDrive.Runtime.Material;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DDrive.Tests.Editor
{
    // FC-R-06(2026-10-03): LightMode タグの無いパス(URP では SRPDefaultUnlit 扱い。T-Drive の輪郭線パスなど)を
    // Material.SetShaderPassEnabled("SRPDefaultUnlit", false) で実際に止められることを、実描画(PreviewRenderUtility)で確認する。
    // 描画できない環境(-nographics のバッチ実行・URP 以外)では Inconclusive にする。
    public class UntaggedPassRenderTests
    {
        private static Shader RenderShader()
        {
            var shader = Shader.Find("Hidden/DDriveTests/UntaggedRender");
            Assert.IsNotNull(shader, "Tests/Editor/Shaders/DDriveTestUntaggedRender.shader が見つからない");
            return shader;
        }

        [Test]
        public void CollectLightModes_CountsUntaggedPassAsSrpDefaultUnlit()
        {
            var modes = new List<string>();
            MaterialShaderInfo.CollectLightModes(RenderShader(), modes);

            CollectionAssert.Contains(modes, "SRPDefaultUnlit");
            CollectionAssert.Contains(modes, "UniversalForward");
        }

        // 赤(タグ無し)→ 緑(UniversalForward)の順に描かれる。どちらを止めたかで中央ピクセルの色が変わる。
        [Test]
        public void SetShaderPassEnabled_SrpDefaultUnlit_StopsTheUntaggedPass_InRealRendering()
        {
            Assume.That(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "描画できる GPU が必要");
            Assume.That(GraphicsSettings.currentRenderPipeline != null, "SRP(URP)が必要");

            var forwardOff = CenterPixel(disable: "UniversalForward");
            Assume.That(forwardOff.r > 0.5f, $"前提: 緑のパスを止めるとタグ無しの赤が見える(実際: {forwardOff})");

            var both = CenterPixel("UniversalForward", "SRPDefaultUnlit");
            Assert.Less(both.r, 0.1f, $"SRPDefaultUnlit を止めると、タグ無しのパスは描かれない(実際: {both})");
            Assert.Less(both.g, 0.1f);
        }

        private static Color CenterPixel(params string[] disable)
        {
            var material = new UnityEngine.Material(RenderShader());
            var pru = new PreviewRenderUtility();
            try
            {
                foreach (var pass in disable)
                {
                    material.SetShaderPassEnabled(pass, false);
                }

                pru.camera.transform.position = new Vector3(0f, 0f, -2f);
                pru.camera.transform.rotation = Quaternion.identity;
                pru.camera.clearFlags = CameraClearFlags.SolidColor;
                pru.camera.backgroundColor = Color.black;
                pru.camera.nearClipPlane = 0.1f;
                pru.camera.farClipPlane = 10f;
                pru.BeginPreview(new Rect(0, 0, 64, 64), GUIStyle.none);
                pru.DrawMesh(Resources.GetBuiltinResource<Mesh>("Quad.fbx"), Matrix4x4.identity, material, 0);
                pru.Render(true, true);
                var rt = pru.EndPreview() as RenderTexture;
                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                tex.Apply();
                RenderTexture.active = previous;
                var pixel = tex.GetPixel(32, 32);
                Object.DestroyImmediate(tex);
                return pixel;
            }
            finally
            {
                pru.Cleanup();
                Object.DestroyImmediate(material);
            }
        }
    }
}
