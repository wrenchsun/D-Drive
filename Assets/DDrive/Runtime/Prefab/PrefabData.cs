using System;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Model;
using UnityEngine;

namespace DDrive.Runtime.Prefab
{
    public readonly struct PrefabMarker
    {
    }

    // [07_canvas_prefab.md] Part B-1 — ゲームプレイ用の汎用 Prefab の種類。用途分けの目安であり、
    // ロジックの分岐には使わない(GameplayTags でゲーム固有の判定をする)。
    public enum PrefabKind
    {
        Gimmick,
        Projectile,
        Pickup,
        Environment,
        Character,
        Other,
    }

    // [07_canvas_prefab.md] Part B-2 — 種類 / タグ / コリジョンレイヤーを管理するだけの汎用 Prefab Data。
    // ModelData(05_model_animation.md)と違い、Slot/Avatar 等の見た目差し替えは持たない
    // (見た目を差し替えたいものは ModelData を使う。両者は別概念。[02] §「AssetType に Model を追加」)。
    [CreateAssetMenu(menuName = "D-Drive/Prefab/Prefab Data", fileName = "PREFAB_New")]
    [AssetIdDefinition(AssetType.Prefab, typeof(PrefabMarker), "PREFABID")]
    public class PrefabData : AssetDataBase
    {
        [Header("Prefab")]
        [Tooltip("Spawn する実体。未設定の場合は空の GameObject を Placeholder として生成する。")]
        public GameObject Prefab;

        [Header("Gameplay")]
        [Tooltip("用途の目安(ロジック分岐には使わない)。")]
        public PrefabKind Kind;

        [Tooltip("ゲームロジック用タグ(\"Destructible\" 等)。GameplayTagDictionary によるタイポ検査の対象。")]
        public string[] GameplayTags;

        [Tooltip("生成時に再帰的に設定するレイヤー。-1 = Prefab のレイヤーをそのまま使う。")]
        public int CollisionLayer = -1;

        [Header("Render")]
        [Tooltip("任意。LODGroup の閾値をデータ側から上書きしたい場合に使う([05] LodProfile を再利用)。")]
        public LodProfile Lod;

        // 完全一致(ordinal、大文字小文字を区別する)の線形探索。タイポ検査自体は Validator 側(GameplayTagDictionary)が担う。
        public bool HasTag(string tag)
        {
            if (GameplayTags == null || string.IsNullOrEmpty(tag))
            {
                return false;
            }

            for (var i = 0; i < GameplayTags.Length; i++)
            {
                if (string.Equals(GameplayTags[i], tag, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
