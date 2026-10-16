using DDrive.Foundation.Data;
using DDrive.Foundation.Easing;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Values;
using UnityEngine;

namespace SampleExtension
{
    // 種別 "Sample" の ID の型(AssetId<SampleMarker>)。ID 定数は [AssetIdDefinition] の "SMPID" クラス(SMPID.Xxx)に生成される。
    public readonly struct SampleMarker
    {
    }

    // 持ち込み先が定義する種別の Data(docs/1014 §2.1)。AssetType.Extension + KindKey で種別を区別する(enum は増やさない)。
    // ScriptableObject として保存するので、クラス名 = ファイル名(SampleData.cs)にする。
    [CreateAssetMenu(menuName = "Sample/Sample Data", fileName = "SMP_NewSample")]
    [AssetIdDefinition(AssetType.Extension, typeof(SampleMarker), "SMPID", KindKey = SampleKind.Key)]
    public sealed class SampleData : AssetDataBase
    {
        [Header("再生")]
        [Tooltip("効果の強さ(Manager が参照する値の例)。")]
        public float Intensity = 1f;

        [Tooltip("続く秒数。Loop が ON のときは無視される。")]
        public float Duration = 1f;

        [Tooltip("止めるまで続けるか。")]
        public bool Loop;

        [Header("形")]
        [Tooltip("強さの時間変化(形 + 尺は ValueDef。既定は 1 → 0 へ減衰)。")]
        public ValueDef Fade = DefaultFade();

        private static ValueDef DefaultFade() => new ValueDef
        {
            Mode = ValueMode.Parametric,
            Parametric = EaseDef.Named(Ease.OutQuad),
            From = 1f,
            To = 0f,
            Time = new TimeDef { Mode = TimeMode.Duration, Value = 1f, SpeedScale = 1f },
            Loop = LoopMode.Once,
        };
    }
}
