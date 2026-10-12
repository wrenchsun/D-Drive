using DDrive.Editor.Versioning;
using DDrive.Foundation.Data;
using DDrive.Runtime.Prefab;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.PrefabTool
{
    // 確認用サンプル(docs/1010 UX-14 / D-10。「爆発する樽」= 種類 Gimmick・ゲームロジック用タグ 4 つ〔重複 1 を含む〕・
    // コリジョンレイヤー Default・Pool = Pooled)。Prefab は指定のフォルダに作る円柱の簡易 Prefab。
    // 指定のフォルダ(Assets/ 配下)に、Prefab 1 つ・PrefabData 2 つ(うち 1 つが対象)を作る。呼び出し側が使い終わったらフォルダごと消す
    // (実データ Assets/GameData には作らない。Addressables にも登録しない)。スクリーンショット・テスト・見通しの確認用。
    internal static class PrefabSample
    {
        public const ulong BaseId = 0xDDA00000;

        public sealed class Result
        {
            public PrefabData Prefab;
            public PrefabData Alt;
            public GameObject PrefabObject;
            public string Folder;
        }

        public static Result Build(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                var parent = System.IO.Path.GetDirectoryName(folder).Replace((char)92, (char)47);
                AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
            }

            var root = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            root.name = "ExplosiveBarrel";
            root.transform.localScale = new Vector3(0.8f, 0.6f, 0.8f);
            var path = $"{folder}/ExplosiveBarrel_Sample.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            var data = New(BaseId + 1, "爆発する樽");
            data.Prefab = prefab;
            data.Kind = PrefabKind.Gimmick;
            data.GameplayTags = new[] { "Destructible", "Explosive", "Burnable", "Explosive" };
            data.CollisionLayer = 0;
            data.Flags.Pool = PoolPolicy.Pooled(8, 32);
            AssetDatabase.CreateAsset(data, $"{folder}/PREFAB_ExplosiveBarrel_Sample.asset");

            // データ切替の確認用。
            var alt = New(BaseId + 2, "木箱");
            alt.Prefab = prefab;
            alt.Kind = PrefabKind.Environment;
            AssetDatabase.CreateAsset(alt, $"{folder}/PREFAB_Crate_Sample.asset");

            DDriveAssetSave.SaveAllSuppressed();
            return new Result { Prefab = data, Alt = alt, PrefabObject = prefab, Folder = folder };
        }

        private static PrefabData New(ulong id, string display)
        {
            var d = ScriptableObject.CreateInstance<PrefabData>();
            d.Id = id;
            d.DisplayName = display;
            return d;
        }
    }
}
