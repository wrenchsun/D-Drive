using DDrive.Foundation.Data;
using DDrive.Foundation.Easing;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Values;
using UnityEngine;

namespace DDrive.Runtime.CameraShake
{
    public readonly struct ShakeMarker
    {
    }

    // [16_camera_haptics.md] Part A — 揺れの波形。実装メモ(2026-09-14)も参照。
    //   PerlinNoise  : 軸ごとに位相をずらした Perlin ノイズ(手ブレ・地響き系)
    //   DecaySine    : Frequency で振動する正弦波(減衰そのものは Envelope が担う)
    //   Impulse      : 振動せず一定方向のみ(方向感のある一撃。減衰は Envelope のみ)
    //   CustomCurve  : 現状は Impulse と同じ(専用の波形カーブ入力は未追加。5-2c で要否判断)
    public enum ShakePattern
    {
        PerlinNoise,
        DecaySine,
        Impulse,
        CustomCurve,
    }

    // Amplitude の基準空間。
    //   CameraLocal : Shake ノードのローカル軸にそのまま適用(既定。カメラの向きに追従)
    //   World       : 常に同じワールド方向に揺れる(親の回転を打ち消して変換する)
    //   FromSource  : Shake() 呼び出し時の sourcePos → カメラの方向へ押し出す(奥から手前)
    public enum ShakeSpace
    {
        CameraLocal,
        World,
        FromSource,
    }

    [CreateAssetMenu(menuName = "D-Drive/Camera/Camera Shake Data", fileName = "SHAKE_NewShake")]
    [AssetIdDefinition(AssetType.Shake, typeof(ShakeMarker), "SHAKEID")]
    public class CameraShakeData : AssetDataBase
    {
        [Header("波形")]
        public ShakePattern Pattern = ShakePattern.PerlinNoise;

        [Header("強さ")]
        [Tooltip("位置の揺れ幅(m)。軸ごとに設定。")]
        public Vector3 PosAmplitude = new Vector3(0.1f, 0.1f, 0f);

        [Tooltip("回転の揺れ幅(deg)。Roll だけ等も可。")]
        public Vector3 RotAmplitude;

        [Tooltip("Hz。PerlinNoise/DecaySine で使う。時間変化する周波数も表現可能。")]
        public ValueDef Frequency = ValueDef.Constant01(20f);

        [Header("時間・減衰")]
        [Tooltip("減衰カーブ + 尺(TimeDef)の統一表現。既定 0.3s で 1→0 に減衰する。")]
        public ValueDef Envelope = new ValueDef
        {
            Mode = ValueMode.Parametric,
            Parametric = EaseDef.Named(Ease.OutQuad),
            From = 1f,
            To = 0f,
            Time = new TimeDef { Mode = TimeMode.Duration, Value = 0.3f, SpeedScale = 1f },
            Loop = LoopMode.Once,
        };

        [Header("方向")]
        public ShakeSpace Space = ShakeSpace.CameraLocal;

        [Header("合成")]
        [Tooltip("多重シェイク時の寄与度(Trauma への加算量)。既定 1。")]
        public float TraumaWeight = 1f;

        [Tooltip("同一 Shake の同時許容数。超過した Shake() 呼び出しは無視する(多重発火で破綻しないための上限)。")]
        public int MaxStack = 3;

        // Flags.Net は既定 Cosmetic 相当(全クライアントで再生・結果に影響しない)として扱う想定だが、
        // v1 は NGO 未統合のためローカル再生のみ([16] Part A 実装メモ参照)。
    }
}
