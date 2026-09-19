using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace DDrive.Editor.Materials
{
    // [06_material_texture.md] A-2 — FBX 内の aiStandardSurface(Maya)を DDrive/AiStandardSurface に割り当てる前処理(2026-09-11)。
    // Unity(URP)は同じ材質を URP の ArnoldStandardSurface ShaderGraph(_BASE_COLOR 等の独自名)に割り当てるため、その後(順序 -950 >
    // URP の -960)に走って上書きする。値の写像は AiStandardSurfaceMapper(純関数)。
    // その後 MayaModelPostprocessor → MayaMaterialImporter が、このシェーダーのまま MaterialData を作る(Common + Arnold 固有の引き継ぎ)。
    public sealed class AiStandardSurfacePreprocessor : AssetPostprocessor
    {
        // シェーダーが見つからないときのフォールバック用パス(Shader.Find が null でも依存として登録する)。
        // [42_distribution.md] §2.3-1(P-4、2026-09-20) — Assets/SourceAssets/Shaders から
        // Assets/DDrive/Runtime/Shaders(パッケージ側)へ移設した。
        public const string ShaderPath = "Assets/DDrive/Runtime/Shaders/AiStandardSurface/DDrive_AiStandardSurface.shader";

        // テストや一括インポート中の抑止。
        public static bool Suppress;

        public override int GetPostprocessOrder() => -950;

        // 写像(AiStandardSurfaceMapper)を変えたときに上げる。上げると対象の FBX が再インポートされる(2026-09-11 レビュー対応)。
        public override uint GetVersion() => 1;

        public void OnPreprocessMaterialDescription(MaterialDescription description, UnityEngine.Material material, AnimationClip[] clips)
        {
            if (Suppress || Path.GetExtension(assetPath).ToLowerInvariant() != ".fbx")
            {
                return;
            }

            var source = new DescriptionSource(description);
            if (!AiStandardSurfaceMapper.IsMayaArnoldStandardSurface(source))
            {
                return;
            }

            var shader = Shader.Find(AiStandardSurfaceMapper.ShaderName);

            // シェーダーを依存に登録する。クリーンインポート(シェーダーより先に FBX が処理される)で Shader.Find が
            // null になっても、シェーダーが入った時点で FBX が再インポートされる(2026-09-11 レビュー対応)。
            var shaderPath = shader != null ? AssetDatabase.GetAssetPath(shader) : ShaderPath;
            if (context != null && !string.IsNullOrEmpty(shaderPath))
            {
                context.DependsOnSourceAsset(shaderPath);
            }

            if (shader == null)
            {
                Debug.LogWarning($"[DDrive] {AiStandardSurfaceMapper.ShaderName} が見つからないため、aiStandardSurface '{material.name}' は Unity 標準の割り当てのままです({assetPath})");
                return;
            }

            foreach (var clip in clips)
            {
                clip.ClearCurves(); // Unity の Arnold 前処理と同じ(マテリアルアニメは扱わない)
            }

            AiStandardSurfaceMapper.Apply(source, material, shader);
        }

        private sealed class DescriptionSource : IArnoldSource
        {
            private readonly MaterialDescription _description;

            public DescriptionSource(MaterialDescription description) => _description = description;

            public bool TryGetFloat(string name, out float value) => _description.TryGetProperty(name, out value);

            public bool TryGetColor(string name, out Vector4 value) => _description.TryGetProperty(name, out value);

            public bool TryGetTexture(string name, out Texture texture, out Vector2 offset, out Vector2 scale)
            {
                if (_description.TryGetProperty(name, out TexturePropertyDescription tex) && tex.texture != null)
                {
                    texture = tex.texture;
                    offset = tex.offset;
                    scale = tex.scale;
                    return true;
                }

                texture = null;
                offset = Vector2.zero;
                scale = Vector2.one;
                return false;
            }
        }
    }
}
