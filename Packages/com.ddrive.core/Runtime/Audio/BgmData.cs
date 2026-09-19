using DDrive.Foundation.Data;
using DDrive.Foundation.Easing;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Values;
using UnityEngine;
using UnityEngine.Audio;

namespace DDrive.Runtime.Audio
{
    public readonly struct BgmMarker
    {
    }

    [CreateAssetMenu(menuName = "D-Drive/Audio/BGM Data", fileName = "BGM_NewTrack")]
    [AssetIdDefinition(AssetType.Bgm, typeof(BgmMarker), "BGMID")]
    public class BgmData : AssetDataBase
    {
        [Header("Clips")]
        [Tooltip("イントロ(任意)。設定するとまずこちらを再生してから LoopBody へサンプル精度で繋ぐ。")]
        public AudioClip Intro;

        [Tooltip("ループ本体のクリップ。")]
        public AudioClip LoopBody;

        [Tooltip("ループ区間の開始位置(秒)。LoopBody の一部だけをループさせたい場合に指定。")]
        public double LoopStartSec;

        [Tooltip("ループ区間の終了位置(秒)。LoopBody の長さ以上なら「クリップ全体をループ」扱い。")]
        public double LoopEndSec;

        [Tooltip("出力先の AudioMixerGroup。未設定だと Validation で警告になる。")]
        public AudioMixerGroup Mixer;

        [Tooltip("基準音量(0〜1)。")]
        [Range(0f, 1f)] public float Volume = 1f;

        [Header("Fade")]
        [Tooltip("他BGMからクロスフェードで切り替わってくる時のフェードイン形状・秒数。")]
        public ValueDef FadeIn = DefaultFade(0.5f);

        [Tooltip("停止時/他BGMへ切り替わる時のフェードアウト形状・秒数。")]
        public ValueDef FadeOut = DefaultFade(1f);

        [Header("Beat")]
        [Tooltip("ビート同期演出用のBPM(任意)。BeatSyncBgmData 等の派生で利用する。")]
        public float Bpm;

        private static ValueDef DefaultFade(float seconds) => new()
        {
            Mode = ValueMode.Parametric,
            Parametric = EaseDef.Named(Ease.Linear),
            From = 0f,
            To = 1f,
            Time = TimeDef.Duration(seconds),
        };
    }
}
