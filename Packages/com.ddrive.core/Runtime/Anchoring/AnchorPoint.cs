using UnityEngine;

namespace DDrive.Runtime.Anchoring
{
    // シーン/プレハブに配置する「アタッチ位置マーカー」([04_vfx.md] §2.5)。
    // ボーンが無いオブジェクトでもアンカーを定義でき、SceneView で Transform を直接動かして調整できる。
    //
    // AnchorResolver が NamedObject/BoneName で解決した Transform にこのコンポーネントが付いている場合、
    // 各 Manager(Audio/Vfx)は Spawn/Play 時に SpawnOffset とランダム変化を追加適用する。
    // ゲーム実行中もただの GameObject としてシーンに存在するため、子に Collider を持たせて
    // 当たり判定の基準点として再利用する、といった使い方もできる。
    public sealed class AnchorPoint : MonoBehaviour
    {
        [Tooltip("生成位置に常に加算されるローカルオフセット。")]
        public Vector3 SpawnOffset;

        [Tooltip("生成位置のランダム半径(m)。0 で無効。この半径の球内に散らばる。")]
        [Min(0f)] public float PositionJitterRadius;

        [Tooltip("生成回転(オイラー角)への ±ランダム範囲。(0,0,0) で無効。")]
        public Vector3 EulerJitter;

        [Tooltip("生成スケール倍率のランダム範囲(x=最小, y=最大)。両方 1 で無効。VFX のみに適用(SE には無関係)。")]
        public Vector2 ScaleRange = new(1f, 1f);

        [Tooltip("SceneView に表示するギズモの色。")]
        public Color GizmoColor = new(0.3f, 0.9f, 0.9f, 0.9f);

        // baseLocalOffset は AnchorDef.LocalOffset(データ側の固定オフセット)。それにこのアンカー固有の
        // SpawnOffset とランダム散らばりを足したローカルオフセットを返す。
        public Vector3 SampleLocalOffset(Vector3 baseLocalOffset)
        {
            var offset = baseLocalOffset + SpawnOffset;
            if (PositionJitterRadius > 0f)
            {
                offset += Random.insideUnitSphere * PositionJitterRadius;
            }

            return offset;
        }

        public Quaternion SampleRotation(Quaternion baseRotation)
        {
            if (EulerJitter == Vector3.zero)
            {
                return baseRotation;
            }

            var jitter = new Vector3(
                Random.Range(-EulerJitter.x, EulerJitter.x),
                Random.Range(-EulerJitter.y, EulerJitter.y),
                Random.Range(-EulerJitter.z, EulerJitter.z));
            return baseRotation * Quaternion.Euler(jitter);
        }

        public float SampleScaleMultiplier()
        {
            // 未初期化(両方0以下)は無効扱い。
            if (ScaleRange.x <= 0f && ScaleRange.y <= 0f)
            {
                return 1f;
            }

            return Random.Range(Mathf.Min(ScaleRange.x, ScaleRange.y), Mathf.Max(ScaleRange.x, ScaleRange.y));
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = GizmoColor;
            var center = transform.TransformPoint(SpawnOffset);

            // ワイヤ球のみだと小さすぎて視認できないため、ズーム距離に応じた一定の見た目サイズの
            // 塗りつぶし球を本体として描く(カメラが引いていても常に見える)。
            var size = 0.1f;
#if UNITY_EDITOR
            size = UnityEditor.HandleUtility.GetHandleSize(center) * 0.12f;
#endif
            Gizmos.DrawSphere(center, size);
            Gizmos.DrawLine(transform.position, center);
            Gizmos.DrawRay(center, transform.forward * size * 3f);

            // ランダム半径は「散らばる範囲」の実寸なのでワールドサイズのまま別途表示する。
            if (PositionJitterRadius > 0f)
            {
                Gizmos.color = new Color(GizmoColor.r, GizmoColor.g, GizmoColor.b, 0.35f);
                Gizmos.DrawWireSphere(center, PositionJitterRadius);
            }

#if UNITY_EDITOR
            UnityEditor.Handles.color = GizmoColor;
            UnityEditor.Handles.Label(center + Vector3.up * size * 1.5f, name);
#endif
        }
    }
}
