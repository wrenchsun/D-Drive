using DDrive.Editor.Inspector;
using DDrive.Editor.Preview;
using DDrive.Runtime.Material;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Materials
{
    // [09_editor_tools.md] §8.1 — Material の初期アイコン(2026-09-11)。元アセットは無いが描画で表現できるので、
    // 実 MaterialManager が生成した共有 Material を MaterialThumbnailRenderer で球に描いてアイコンにする。
    internal static class MaterialIconProvider
    {
        private const float YawDeg = 30f;
        private const float PitchDeg = 10f;
        private const float LightDeg = 30f;

        [InitializeOnLoadMethod]
        private static void Register() => AssetIconService.RegisterRenderer<MaterialData>(Render);

        // size×size の読める Texture2D を返す(呼び出し側が破棄する)。失敗なら null。
        public static Texture2D Render(MaterialData data, int size)
        {
            if (data == null)
            {
                return null;
            }

            var manager = new MaterialManager(EditorAnchorRegistry.Build());
            var renderer = new MaterialThumbnailRenderer();
            try
            {
                var rendered = renderer.Render(manager.GetData(data), MaterialPreviewShape.Sphere, YawDeg, PitchDeg, LightDeg, size, size);
                if (rendered == null)
                {
                    return null;
                }

                // PreviewRenderUtility の RenderTexture は DPI 倍率で大きくなることがあるので、そのままの解像度で読み取る(縮小は CropAndSave)。
                var previous = RenderTexture.active;
                var rt = rendered as RenderTexture;
                var readable = new Texture2D(rendered.width, rendered.height, TextureFormat.RGBA32, false);
                try
                {
                    if (rt != null)
                    {
                        RenderTexture.active = rt;
                        readable.ReadPixels(new Rect(0, 0, rendered.width, rendered.height), 0, 0);
                        readable.Apply();
                    }
                    else
                    {
                        var temp = RenderTexture.GetTemporary(rendered.width, rendered.height, 0, RenderTextureFormat.ARGB32);
                        Graphics.Blit(rendered, temp);
                        RenderTexture.active = temp;
                        readable.ReadPixels(new Rect(0, 0, rendered.width, rendered.height), 0, 0);
                        readable.Apply();
                        RenderTexture.ReleaseTemporary(temp);
                    }
                }
                finally
                {
                    RenderTexture.active = previous;
                }

                return readable;
            }
            finally
            {
                renderer.Dispose();
                manager.Clear();
            }
        }
    }
}
