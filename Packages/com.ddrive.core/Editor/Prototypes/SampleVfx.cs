using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.EditorPrototypes
{
    // 試験版: 空状態の「サンプル VfxData を作る」。同梱の vfx_sample.prefab を使う VFX_Sample_Hit を作る(既にあれば選ぶだけ)。
    internal static class SampleVfx
    {
        private const string AssetName = "VFX_Sample_Hit";
        private const string PrefabFileName = "vfx_sample.prefab";

        public static Button CreateButton(System.Action<VfxData> onReady)
        {
            return new Button(() =>
            {
                var d = GetOrCreate();
                if (d != null)
                {
                    onReady(d);
                }
            })
            { text = "サンプル VfxData を作る（vfx_sample）", tooltip = "同梱の vfx_sample を使う VFX_Sample_Hit を作って選びます（既にあればそれを選びます）" };
        }

        public static VfxData GetOrCreate()
        {
            var existing = AssetDatabase.FindAssets(AssetName + " t:VfxData")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => System.IO.Path.GetFileNameWithoutExtension(p) == AssetName)
                .Select(AssetDatabase.LoadAssetAtPath<VfxData>)
                .FirstOrDefault(d => d != null);
            if (existing != null)
            {
                return existing;
            }

            var prefab = FindPrefab();
            if (prefab == null)
            {
                Debug.LogWarning("[Prototype] 同梱の vfx_sample.prefab が見つかりません。");
                return null;
            }

            var created = AssetCreationService.Create(
                typeof(VfxData), AssetType.Vfx, "サンプル: ヒット", "Sample", "Hit",
                configure: asset =>
                {
                    var v = (VfxData)asset;
                    v.Prefab = prefab;
                    v.LifeMode = VfxLifeMode.OneShot;
                    v.Duration = 1f;
                    EditorUtility.SetDirty(v);
                }) as VfxData;
            if (created != null)
            {
                DDrive.Editor.Versioning.DDriveAssetSave.SaveAllSuppressed();
            }

            return created;
        }

        private static GameObject FindPrefab()
        {
            foreach (var guid in AssetDatabase.FindAssets("vfx_sample t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("Prototypes/Samples/" + PrefabFileName, System.StringComparison.Ordinal))
                {
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
                }
            }

            return null;
        }
    }
}
