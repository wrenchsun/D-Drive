using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Vfx
{
    // 確認用サンプル(docs/1010 UX-19)。パッケージ同梱の vfx_sample.prefab(Editor/Ux/Samples/)を使う VfxData を 1 つ作る。
    // Build(folder) は他のサンプルと同じ流儀(指定のフォルダに作る。Id は固定、Addressables には登録しない。テスト・撮影用)。
    // 空状態の「サンプルを作る」は UxSamples.Vfx()(Assets/GameData/Vfx/Sample/ に実データとして作る)が同じ素材を使う。
    internal static class VfxSample
    {
        public const ulong BaseId = 0xDDF00000;
        public const string PrefabFileName = "vfx_sample.prefab";

        public sealed class Result
        {
            public VfxData Vfx;
            public GameObject Prefab;
            public string Folder;
        }

        // 同梱の vfx_sample.prefab(見つからなければ null)。
        public static GameObject FindPrefab()
        {
            foreach (var guid in AssetDatabase.FindAssets("vfx_sample t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("Ux/Samples/" + PrefabFileName, System.StringComparison.Ordinal))
                {
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
                }
            }

            return null;
        }

        public static void Configure(VfxData v, GameObject prefab)
        {
            v.Prefab = prefab;
            v.LifeMode = VfxLifeMode.OneShot;
            v.Duration = 1f;
        }

        public static Result Build(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                var parent = System.IO.Path.GetDirectoryName(folder).Replace((char)92, (char)47);
                AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
            }

            var prefab = FindPrefab();
            var v = ScriptableObject.CreateInstance<VfxData>();
            v.Id = BaseId + 1;
            v.DisplayName = "サンプル: ヒット";
            v.Category = "Sample";
            Configure(v, prefab);
            AssetDatabase.CreateAsset(v, $"{folder}/VFX_Sample_Hit.asset");
            DDrive.Editor.Versioning.DDriveAssetSave.SaveAllSuppressed();
            return new Result { Vfx = v, Prefab = prefab, Folder = folder };
        }
    }
}
