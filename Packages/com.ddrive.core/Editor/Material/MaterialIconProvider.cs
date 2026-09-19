using DDrive.Editor.Inspector;
using DDrive.Editor.Preview;
using DDrive.Foundation.Registry;
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

        // 2026-09-11 レビュー対応: 以前は 1 マテリアルごとに EditorAnchorRegistry.Build()(12 型分の FindAssets + 全 Data ロード)と
        // PreviewRenderUtility を作り直していたため、一括生成で数百回の走査が走っていた。Registry / Manager / Renderer を静的に持ち、
        // プロジェクトが変わったときだけ作り直す([09] §9 の FindAssets キャッシュと同じ方針)。
        private static AssetRegistry _registry;
        private static MaterialManager _manager;
        private static MaterialThumbnailRenderer _renderer;
        private static bool _registryDirty = true;

        [InitializeOnLoadMethod]
        private static void Register()
        {
            AssetIconService.RegisterRenderer<MaterialData>(Render);
            EditorApplication.projectChanged += () => _registryDirty = true;
            // ドメインリロードで PreviewRenderUtility が残らないよう明示的に片付ける。
            AssemblyReloadEvents.beforeAssemblyReload += DisposeShared;
        }

        private static void DisposeShared()
        {
            _renderer?.Dispose();
            _renderer = null;
            _manager?.Clear();
            _manager = null;
            _registry = null;
            _registryDirty = true;
        }

        // 共有の Registry / Manager を用意する(プロジェクト変更後は作り直す)。
        private static MaterialManager EnsureManager()
        {
            if (_registry == null)
            {
                _registry = EditorAnchorRegistry.Build();
                _registryDirty = false;
            }
            else if (_registryDirty)
            {
                _registryDirty = false;
                EditorAnchorRegistry.Refresh(_registry);
                _manager?.Clear(); // 古い ID 解決で作った共有 Material を捨てる
            }

            return _manager ??= new MaterialManager(_registry);
        }

        // size×size の読める Texture2D を返す(呼び出し側が破棄する)。失敗なら null。
        public static Texture2D Render(MaterialData data, int size)
        {
            if (data == null)
            {
                return null;
            }

            // Renderer / Manager は共有なのでここでは破棄しない(DisposeShared / projectChanged で片付ける)。
            var manager = EnsureManager();
            var renderer = _renderer ??= new MaterialThumbnailRenderer();
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
    }
}
