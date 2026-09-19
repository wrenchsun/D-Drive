using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using UnityEngine;

namespace DDrive.Runtime.Anchoring
{
    public readonly struct AnchorMarker
    {
    }

    // [21_anchor_spec.md] §3.1 — 「どこに・いつ・どう置くか」だけを持つアセット。何を出すか(VFX/SE)は持たない。
    // VfxData.AnchorId / SeData.AnchorId、または Spawn/PlaySe の引数から ID で参照される。
    // Parent で連鎖(入れ子)でき、子は親の姿勢を基準にオフセットを積む(AnchorChain 参照)。
    [CreateAssetMenu(menuName = "D-Drive/Anchor/Anchor Data", fileName = "ANC_NewAnchor")]
    [AssetIdDefinition(AssetType.Anchor, typeof(AnchorMarker), "ANCHORID")]
    public sealed class AnchorData : AssetDataBase
    {
        [Header("入れ子")]
        [Tooltip("親 Anchor。0(未設定)ならルート。子は親で決まった姿勢を基準に、自分のオフセットを積む。")]
        public AssetId<AnchorMarker> Parent;

        [Header("基準(ルートのみ有効。子は親から継承)")]
        [Tooltip("World=固定座標 / BoneName・NamedObject=スポーン先の階層から Path の名前を検索 / ContextTarget=スポーン先そのもの。子では無視される。")]
        public AnchorSpace Space;

        [Tooltip("Space=BoneName/NamedObject のときに探す名前(ボーン名またはオブジェクト名)。子では無視される。")]
        public string Path;

        [Header("オフセット")]
        [Tooltip("基準(ルートは解決先 Transform、子は親の姿勢)からのローカル位置オフセット。")]
        public Vector3 LocalOffset;

        [Tooltip("基準からのローカル回転オフセット(オイラー角)。")]
        public Vector3 LocalEuler;

        [Tooltip("ローカルスケール。(0,0,0) は 1 扱い。SE には無関係。")]
        public Vector3 LocalScale = Vector3.one;

        [Tooltip("アタッチ後、アタッチ先の回転に追従するか。子では無視される(ルートの値が効く)。")]
        public bool FollowRotation;

        [Tooltip("アタッチ先が破棄された後もその場に残して再生完了まで続けるか。子では無視される(ルートの値が効く)。")]
        public bool DetachOnStop;

        [Header("ランダム(Spawn/Play 時に 1 回サンプリング)")]
        [Tooltip("生成位置のランダム半径(m)。0 で無効。この半径の球内に散らばる。")]
        [Min(0f)] public float PositionJitterRadius;

        [Tooltip("生成回転(オイラー角)への ±ランダム範囲。(0,0,0) で無効。")]
        public Vector3 EulerJitter;

        [Tooltip("生成スケール倍率のランダム範囲(x=最小, y=最大)。両方 1 で無効。SE には無関係。")]
        public Vector2 ScaleRange = new(1f, 1f);

        [Header("生成タイミング")]
        [Tooltip("生成を遅らせる秒数。連鎖している場合は各段の値が加算される。")]
        [Min(0f)] public float DelaySec;

        [Tooltip("DelaySec に加算されるランダム秒数(0〜この値)。")]
        [Min(0f)] public float DelayJitterSec;

        [Tooltip("生成確率(0〜1)。1 未満で確率生成、0 は「出さない」。連鎖している場合は各段の値が乗算される。")]
        [Range(0f, 1f)] public float SpawnChance = 1f;

        // 埋め込み AnchorDef と同じ形に変換する(合成前の 1 ノード分)。
        public AnchorDef ToDef() => new()
        {
            Space = Space,
            Path = Path,
            LocalOffset = LocalOffset,
            LocalEuler = LocalEuler,
            LocalScale = LocalScale == Vector3.zero ? Vector3.one : LocalScale,
            FollowRotation = FollowRotation,
            DetachOnStop = DetachOnStop,
        };

        // 埋め込み AnchorDef から値を写す(エディタの「アセット化」用)。
        public void CopyFrom(in AnchorDef def)
        {
            Space = def.Space;
            Path = def.Path;
            LocalOffset = def.LocalOffset;
            LocalEuler = def.LocalEuler;
            LocalScale = def.LocalScale == Vector3.zero ? Vector3.one : def.LocalScale;
            FollowRotation = def.FollowRotation;
            DetachOnStop = def.DetachOnStop;
        }
    }
}
