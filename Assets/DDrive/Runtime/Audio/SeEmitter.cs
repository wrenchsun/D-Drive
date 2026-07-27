using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using UnityEngine;

namespace DDrive.Runtime.Audio
{
    // シーン配置型の環境音マーカー(滝・焚き火等)。OnEnable で自動再生、OnDisable で自動停止するだけの
    // 薄いラッパ。AudioSource.Play を直接呼ばない正規の配置手段(禁止事項 [00_requirements.md] §5)。
    // カリングは AssetFlags.Priority + Pool の既存機構(MaxDistance は AudioSource 自体の減衰)に乗る。
    public sealed class SeEmitter : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("鳴らす SE。ループ設定(Loop=on)の SE を指定するとこの場所で鳴り続ける。")]
        private AssetId<SeMarker> seId;

        private Handle<SeMarker> _handle;
        private bool _started;

        private void OnEnable()
        {
            TryPlay();
        }

        // シーンロード順によっては OnEnable 時点で Audio ファサードが未 Bind のことがある。
        // その場合は再生できるまで Update で軽くリトライする(成功後は何もしない)。
        private void Update()
        {
            if (!_started)
            {
                TryPlay();
            }
        }

        private void TryPlay()
        {
            if (_started || !seId.IsValid)
            {
                return;
            }

            _handle = Audio.PlaySe(seId, transform);
            _started = _handle != Handle<SeMarker>.Invalid;
        }

        private void OnDisable()
        {
            if (_started)
            {
                Audio.Stop(_handle);
                _started = false;
            }
        }
    }
}
