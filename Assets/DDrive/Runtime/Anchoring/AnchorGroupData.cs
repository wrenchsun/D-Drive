using System;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Vfx;
using UnityEngine;

namespace DDrive.Runtime.Anchoring
{
    public readonly struct AnchorGroupMarker
    {
    }

    public enum AnchorLayoutKind
    {
        Manual,   // Points だけ
        Grid,     // 格子(XZ 平面 + 任意で Y 段)
        Circle,   // 円周(扇形も可)
        Line,     // 直線
        Random,   // 球内ランダム
    }

    // 手置きの点(Layout=Manual、またはパターンに追加する分)。
    [Serializable]
    public struct AnchorGroupPoint
    {
        public string Name;
        public Vector3 LocalOffset;
        public Vector3 LocalEuler;
        public Vector3 LocalScale;
    }

    // 特定の点だけ「出さない / 別のアセットにする」。Index はパターン生成後の通し番号(SceneView に表示される番号)。
    [Serializable]
    public struct AnchorGroupOverride
    {
        public int Index;
        public bool Skip;
        public AssetId<VfxMarker>[] Vfx;
        public AssetId<SeMarker>[] Se;
    }

    // 入れ子: 各点(AtIndex=-1 なら全点)に別の配置セットを出す。子の Origin は無視され、その点が基準になる。
    [Serializable]
    public struct AnchorGroupChild
    {
        public AssetId<AnchorGroupMarker> Group;
        public int AtIndex;
    }

    // [22_anchor_group.md] — 配置セット: 原点 + パターン/手置きの点の集合 + 各点に出すアセット。
    // AnchorData(1 つの位置)とは逆に「Anchor 側にアセットを登録」する単位で、Anchors.Play(groupId, ctx) の 1 行で全点を再生する。
    // 点の生成は AnchorLayout(純粋関数)、再生計画は AnchorGroupPlanner、実行は AnchorGroupPlayer(ランタイム)/エディタの Driver。
    [CreateAssetMenu(menuName = "D-Drive/Anchor/Anchor Group", fileName = "ANCG_NewGroup")]
    [AssetIdDefinition(AssetType.AnchorGroup, typeof(AnchorGroupMarker), "ANCHORGROUPID")]
    public sealed class AnchorGroupData : AssetDataBase
    {
        public const int MaxPoints = 256;

        [Header("原点(基準ボーン + ずれ)")]
        [Tooltip("原点として使う Anchor アセット。設定するとこちらが優先され、下の埋め込み Origin は無視される。")]
        public AssetId<AnchorMarker> OriginAnchorId;

        [Tooltip("埋め込みの原点定義(OriginAnchorId が 0 のときだけ使う)。Space/Path で基準を決める。")]
        public AnchorDef Origin = AnchorDef.WorldDefault;

        [Header("配置パターン")]
        public AnchorLayoutKind Layout = AnchorLayoutKind.Grid;

        [Header("Grid")]
        [Min(1)] public int GridCountX = 3;
        [Min(1)] public int GridCountY = 1;
        [Min(1)] public int GridCountZ = 3;
        public Vector3 GridSpacing = Vector3.one;
        [Tooltip("ON: 原点を格子の中心にする。OFF: 原点を角(最小側)にする。")]
        public bool GridCentered = true;

        [Header("Circle")]
        [Min(1)] public int CircleCount = 6;
        [Min(0f)] public float CircleRadius = 1f;
        public float CircleStartAngle;
        [Range(0f, 360f)] public float CircleArc = 360f;
        [Tooltip("ON: 各点の向きを外向きにする(放射状)。OFF: 原点と同じ向き。")]
        public bool CircleFaceOutward = true;

        [Header("Line")]
        [Min(1)] public int LineCount = 3;
        [Min(0f)] public float LineLength = 2f;
        public Vector3 LineDirection = Vector3.forward;
        public bool LineCentered = true;

        [Header("Random")]
        [Min(1)] public int RandomCount = 8;
        [Min(0f)] public float RandomRadius = 1f;
        [Tooltip("0 = 再生ごとに違う配置。0 以外 = 毎回同じ配置(エディタ表示も固定)。")]
        public int RandomSeed;

        [Header("手置きの点(Manual のとき、またはパターンへ追加)")]
        public AnchorGroupPoint[] Points;

        [Header("生成タイミング(点ごと)")]
        [Tooltip("番号順に少しずつ遅らせる秒数(端から順に出す・波紋)。")]
        [Min(0f)] public float DelayPerIndex;
        [Min(0f)] public float DelayJitterSec;
        [Tooltip("各点が出る確率(0〜1)。1 未満でランダムに欠ける。")]
        [Range(0f, 1f)] public float ChancePerPoint = 1f;

        [Header("ランダム(点ごとに個別サンプリング)")]
        [Min(0f)] public float PositionJitterRadius;
        public Vector3 EulerJitter;
        public Vector2 ScaleRange = new(1f, 1f);

        [Header("出すもの(全点共通)")]
        public AssetId<VfxMarker>[] SharedVfx;
        public AssetId<SeMarker>[] SharedSe;

        [Header("特定の点だけ変える / 出さない")]
        public AnchorGroupOverride[] Overrides;

        [Header("入れ子(各点に別の配置セットを出す)")]
        public AnchorGroupChild[] Children;

        // 点 index の上書きを探す(無ければ -1)。定常経路で呼ばれるためループ検索(点数は小さい前提)。
        public int FindOverride(int index)
        {
            if (Overrides == null)
            {
                return -1;
            }

            for (var i = 0; i < Overrides.Length; i++)
            {
                if (Overrides[i].Index == index)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
