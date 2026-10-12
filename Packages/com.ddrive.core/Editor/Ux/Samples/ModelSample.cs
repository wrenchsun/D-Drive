using System.Collections.Generic;
using DDrive.Editor.Versioning;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Model
{
    // 実運用規模の確認用サンプル(docs/1010 D-10。「ユニティちゃん」= Material スロット 24 行〔Renderer 23 + 見つからない Renderer 1〕・BlendShape 付き・
    // Default Animation あり・Humanoid の Avatar あり)。
    // Prefab は Assets/SourceAssets/Data/UnityChan/Prefabs/unitychan.prefab(SkinnedMeshRenderer 23 個・BlendShape 31 本・Humanoid)。無ければ
    // 指定のフォルダに MeshRenderer 12 個の簡易 Prefab を作って代わりにする。
    // 指定のフォルダ(Assets/ 配下)に、MaterialData 8 つ・AnimData 1 つ・ModelData 2 つ(うち 1 つが対象)を作る。スロットは「割当済み 20 / 未割当 3 / Renderer が見つからない 1」。
    // 呼び出し側が使い終わったらフォルダごと消す(実データ Assets/GameData には作らない。Addressables にも登録しない)。スクリーンショット・テスト・見通しの確認用。
    internal static class ModelSample
    {
        public const ulong BaseId = 0xDD900000;
        public const string UnityChanPrefabPath = "Assets/SourceAssets/Data/UnityChan/Prefabs/unitychan.prefab";

        private static readonly string[] MaterialNames =
        {
            "肌", "髪", "服 上", "服 下", "目", "口", "眉", "小物",
        };

        public sealed class Result
        {
            public ModelData Model;
            public ModelData Alt;
            public AnimData Anim;
            public MaterialData[] Materials;
            public GameObject Prefab;
            public string Folder;
            public int SlotCount;
            public int UnassignedCount;
            public int MissingRendererCount;
        }

        public static Result Build(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                var parent = System.IO.Path.GetDirectoryName(folder).Replace((char)92, (char)47);
                AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UnityChanPrefabPath) ?? MakeFallbackPrefab(folder);

            var materials = new MaterialData[MaterialNames.Length];
            for (var i = 0; i < materials.Length; i++)
            {
                var m = New<MaterialData>(BaseId + 1 + (ulong)i, "ユニティちゃん " + MaterialNames[i]);
                m.Common = MaterialCommon.Default;
                m.Common.AlbedoTint = Color.HSVToRGB(i / (float)materials.Length, 0.25f, 1f);
                AssetDatabase.CreateAsset(m, $"{folder}/MAT_UnityChan_{i + 1}_Sample.asset");
                materials[i] = m;
            }

            var anim = New<AnimData>(BaseId + 50, "ユニティちゃん 待機");
            AssetDatabase.CreateAsset(anim, $"{folder}/ANIM_UnityChanIdle_Sample.asset");

            var animator = prefab.GetComponentInChildren<Animator>(true);
            var model = New<ModelData>(BaseId + 100, "ユニティちゃん");
            model.Prefab = prefab;
            model.Avatar = animator != null ? animator.avatar : null;
            model.DefaultAnimation = new AssetId<AnimMarker>(anim.Id, AssetType.Anim);
            model.RenderLayer = 0;
            model.LightLayerMask = 1;

            // スロット = Prefab の Renderer を走査した並び。8 つに 1 つ(番号 5・13・21)は未割当、そのほかは 8 つの MaterialData を順に割り当てる。
            var slots = new List<MaterialSlot>(ModelSlotBinder.BuildSlots(prefab, null, null, folder));
            var unassigned = 0;
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (i % 8 == 5)
                {
                    slot.Material = default;
                    unassigned++;
                }
                else
                {
                    slot.Material = new AssetId<MaterialMarker>(materials[i % materials.Length].Id, AssetType.Material);
                }

                slots[i] = slot;
            }

            // 名前を変えて Prefab に見つからなくなった Renderer 1 つ(検証のエラー 1 件。行にも ⚠ が付く)。
            slots.Add(new MaterialSlot
            {
                RendererPath = "Body/OldHair",
                SlotIndex = 0,
                Material = new AssetId<MaterialMarker>(materials[1].Id, AssetType.Material),
            });
            model.Slots = slots.ToArray();
            AssetDatabase.CreateAsset(model, $"{folder}/MODEL_UnityChan_Sample.asset");

            // 並べて確認・データ切替の確認用。
            var alt = New<ModelData>(BaseId + 101, "ユニティちゃん(スロットなし)");
            alt.Prefab = prefab;
            alt.Avatar = model.Avatar;
            AssetDatabase.CreateAsset(alt, $"{folder}/MODEL_UnityChanAlt_Sample.asset");

            DDriveAssetSave.SaveAllSuppressed();
            return new Result
            {
                Model = model,
                Alt = alt,
                Anim = anim,
                Materials = materials,
                Prefab = prefab,
                Folder = folder,
                SlotCount = model.Slots.Length,
                UnassignedCount = unassigned,
                MissingRendererCount = 1,
            };
        }

        // UnityChan が無い持ち込み先向けの代わり: 子オブジェクト 12 個(MeshRenderer 1 つずつ)の Prefab。
        private static GameObject MakeFallbackPrefab(string folder)
        {
            var root = new GameObject("SampleModel");
            for (var i = 0; i < 12; i++)
            {
                var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                part.name = "Part_" + (i + 1);
                part.transform.SetParent(root.transform, false);
                part.transform.localPosition = new Vector3(0f, i * 0.2f, 0f);
                part.transform.localScale = new Vector3(0.4f, 0.15f, 0.4f);
                Object.DestroyImmediate(part.GetComponent<Collider>());
            }

            var path = $"{folder}/SampleModel_Sample.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // 中身を作ってから CreateAsset する(CreateAsset のあとに書き換えると、取り込み直しで中身が消えることがあるため)。
        private static T New<T>(ulong id, string display) where T : DDrive.Foundation.Data.AssetDataBase
        {
            var d = ScriptableObject.CreateInstance<T>();
            d.Id = id;
            d.DisplayName = display;
            return d;
        }
    }
}
