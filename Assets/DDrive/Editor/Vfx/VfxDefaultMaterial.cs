using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Menu;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Vfx
{
    // [04_vfx.md] §2 — URP プロジェクトで Built-in Render Pipeline 用のパーティクルマテリアル
    // (Default-Particle 等)を使うと、シーン/ゲームビューで完全に透明になって描画されない落とし穴が
    // ある(プレハブのアセットプレビューだけは Built-in 経路で描画されるため正しく見えてしまう)。
    // 新規 VFX 作成時に迷わず使える URP 対応の既定マテリアルを 1 つ用意しておく。
    public static class VfxDefaultMaterial
    {
        public const string Path = AssetCreationService.DefaultGameDataRoot + "/Materials/Default/M_DefaultParticleUnlit.mat";

        [MenuItem(DDriveMenu.Generate + "デフォルトパーティクルマテリアルを生成")]
        public static void GenerateFromMenu()
        {
            var material = EnsureDefaultMaterial();
            Debug.Log($"[DDrive] URP 対応の既定パーティクルマテリアルを用意しました: {AssetDatabase.GetAssetPath(material)}");
            EditorGUIUtility.PingObject(material);
            Selection.activeObject = material;
        }

        // 既存があればそれを返す(上書きしない。手で調整した設定を壊さない)。
        public static Material EnsureDefaultMaterial(string path = Path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogError("[DDrive] 'Universal Render Pipeline/Particles/Unlit' シェーダーが見つかりません。URP パッケージが正しく導入されているか確認してください。");
                return null;
            }

            var material = new Material(shader) { name = "M_DefaultParticleUnlit" };

            // テクスチャ無し(白)でも常に見える、加算合成の透明マテリアルを既定値にする。
            // "_Surface"=1(Transparent), "_Blend"=1(Additive) は URP Particles/Unlit の標準プロパティ名。
            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }

            if (material.HasProperty("_Blend"))
            {
                material.SetFloat("_Blend", 1f);
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.white);
            }

            // Surface/Blend をプロパティだけでなくキーワード・レンダーキューにも反映させる
            // (ShaderGUI 経由の設定と同じ状態にしないと、Inspector 上の見た目と実描画がずれる)。
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_ZWrite", 0);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            var folder = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            AssetCreationService.EnsureFolder(folder);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
