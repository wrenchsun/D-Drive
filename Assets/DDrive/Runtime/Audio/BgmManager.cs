using DDrive.Foundation.Easing;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Values;
using UnityEngine;
using BgmId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Audio.BgmMarker>;

namespace DDrive.Runtime.Audio
{
    // BGM 再生の中核。AudioSource を 2 チャンネル保持し、Intro→Loop 接続とクロスフェードを
    // AudioSettings.dspTime ベースでスケジュールする(フレームレート非依存・サンプル精度)。
    //
    // 設計上の制約: Intro は「無音から再生開始する時」のみ使う。既に何か再生中の状態から
    // クロスフェードで切り替える場合は Intro を飛ばして LoopBody から始める
    // (outgoing 1 + incoming-intro 1 + incoming-loop 1 の 3 系統が同時に要る組み合わせは
    //  2ch 構成では表現できないため。Intro は「タイトル画面等、無音から始める最初の1回」
    //  という一般的な使われ方に絞る)。
    //
    // 「ループ継ぎ目にノイズなし」は dspTime スケジュールにより理論上ギャップ 0 で接続されるが、
    // 実際に無音・無クリックかは波形/耳での確認が要る(自動テストで検証できるのはスケジュール
    // 計算そのものと、実時間経過での再生継続まで)。
    public sealed class BgmManager : IAssetManager
    {
        private const double ScheduleLeadSeconds = 0.05;
        private const float LoopLookaheadSeconds = 0.2f;

        private readonly IAssetRegistry _registry;
        private readonly AudioSource _channelA;
        private readonly AudioSource _channelB;

        private AudioSource _current;
        private AudioSource _incoming;
        private BgmData _currentData;

        private bool _crossfading;
        private float _crossfadeElapsed;
        private ValueDef _fadeInCurve;
        private ValueDef _fadeOutCurve;
        private float _fadeInTargetVolume;
        private AudioSource _fadingOutSource;
        private float _fadeOutStartVolume;

        private bool _introHandoffPending;
        private double _introHandoffDspTime;
        private AudioSource _introHandoffTarget;

        private bool _loopSplicePending;
        private double _loopSpliceDspTime;

        private bool _stopFading;
        private float _stopFadeElapsed;
        private ValueDef _stopFadeCurve;
        private float _stopFadeStartVolume;
        private AudioSource _stopFadeSource;

        public BgmManager(IAssetRegistry registry, AudioSource channelA, AudioSource channelB)
        {
            _registry = registry;
            _channelA = channelA;
            _channelB = channelB;
            _current = channelA;
            _incoming = channelB;
        }

        public bool IsPlaying => _current != null && _current.isPlaying;

        public AssetType Type => AssetType.Bgm;

        public void PlayBgm(BgmId id, float fadeInOverride = -1f)
            => PlayBgmData(_registry.ResolveOrPlaceholder<BgmData>(id.Value), fadeInOverride);

        // [03_audio.md] §3 CrossFade: フェード時間を明示指定してのトラック切替。
        public void CrossFade(BgmId next, float duration)
            => PlayBgmData(_registry.ResolveOrPlaceholder<BgmData>(next.Value), duration, duration);

        public void PlayBgmData(BgmData data, float fadeInOverride = -1f, float fadeOutOverride = -1f)
        {
            if (data == null)
            {
                return;
            }

            // 進行中の停止フェードは打ち切る。残したままだと、そのフェードが新しく再生を始めた
            // トラック(同じチャンネルを使う)を巻き添えにしてフェードアウト→停止させてしまう。
            if (_stopFading)
            {
                _stopFadeSource.Stop();
                _stopFading = false;
            }

            var wasPlaying = _currentData != null && _current.isPlaying;
            var startDsp = AudioSettings.dspTime + ScheduleLeadSeconds;
            _introHandoffPending = false;
            _loopSplicePending = false;

            if (wasPlaying)
            {
                var outgoingSource = _current;
                var outgoingVolume = _current.volume;
                var target = _incoming;

                ConfigureCommon(target, data);
                StartLoopBody(target, data, startDsp);

                _current = target;
                _incoming = outgoingSource;

                _fadingOutSource = outgoingSource;
                _fadeOutStartVolume = outgoingVolume;
                _fadeOutCurve = MakeFadeCurve(fadeOutOverride, data.FadeOut);
                _fadeInCurve = MakeFadeCurve(fadeInOverride, data.FadeIn);
                _fadeInTargetVolume = data.Volume;
                target.volume = 0f;
                _crossfading = true;
                _crossfadeElapsed = 0f;
            }
            else
            {
                var target = _current;
                ConfigureCommon(target, data);
                target.volume = data.Volume;

                if (data.Intro != null)
                {
                    target.clip = data.Intro;
                    target.loop = false;
                    target.timeSamples = 0;
                    target.PlayScheduled(startDsp);

                    // clip.length(float) は圧縮クリップで不正確。サンプル数/周波数の double で繋ぐ。
                    var introLength = (double)data.Intro.samples / data.Intro.frequency;
                    var loopStartDsp = startDsp + introLength;
                    var loopSource = _incoming;
                    ConfigureCommon(loopSource, data);
                    loopSource.volume = data.Volume;
                    StartLoopBody(loopSource, data, loopStartDsp);

                    _introHandoffPending = true;
                    _introHandoffDspTime = loopStartDsp;
                    _introHandoffTarget = loopSource;
                }
                else
                {
                    StartLoopBody(target, data, startDsp);
                }

                // 無音からの開始でも FadeIn は尊重する(フェード相手なしの片側フェード)。
                var fadeIn = MakeFadeCurve(fadeInOverride, data.FadeIn);
                if (fadeIn.Duration > 0f)
                {
                    _fadingOutSource = null;
                    _fadeOutCurve = default;
                    _fadeInCurve = fadeIn;
                    _fadeInTargetVolume = data.Volume;
                    SetVolumeOnStartingChannels(0f);
                    _crossfading = true;
                    _crossfadeElapsed = 0f;
                }
            }

            _currentData = data;
        }

        // Intro 構成では intro チャンネルと loop チャンネルの両方が「今の曲」を担うため、
        // フェードイン等の音量操作は両方へ適用する。
        private void SetVolumeOnStartingChannels(float volume)
        {
            _current.volume = volume;
            if (_introHandoffPending && _introHandoffTarget != null)
            {
                _introHandoffTarget.volume = volume;
            }
        }

        public void StopBgm(float fadeOutOverride = -1f)
        {
            _crossfading = false;
            _introHandoffPending = false;
            _loopSplicePending = false;
            _incoming?.Stop();

            if (_currentData == null || _current == null || !_current.isPlaying)
            {
                _currentData = null;
                _current?.Stop();
                return;
            }

            _stopFadeSource = _current;
            _stopFadeStartVolume = _current.volume;
            _stopFadeCurve = MakeFadeCurve(fadeOutOverride, _currentData.FadeOut);
            _stopFadeElapsed = 0f;
            _stopFading = true;
            _currentData = null;
        }

        public void OnPause(PauseChannel channel, bool paused)
        {
            if (_currentData == null || _currentData.Flags.Pause != DDrive.Foundation.Data.PauseMode.PauseWithGame)
            {
                return;
            }

            // Intro→Loop 構成やスプライス待ちでは両チャンネルが「今の曲」を担っているため、
            // 片側だけ止めるとポーズ中にもう片方が勝手に鳴り出す。常に両方へ適用する。
            // 既知の制約: PlayScheduled の dspTime はポーズ中も進むため、長いポーズをまたぐと
            // スケジュール済みの継ぎ目タイミングはずれる(継ぎ目再スケジュールは今後の課題)。
            if (paused)
            {
                _channelA.Pause();
                _channelB.Pause();
            }
            else
            {
                _channelA.UnPause();
                _channelB.UnPause();
            }
        }

        public void StopAll(StopReason reason) => StopBgm();

        public void OnSceneUnload() => StopAll(StopReason.SceneUnload);

        public void Tick(float dt)
        {
            if (_stopFading)
            {
                _stopFadeElapsed += dt;
                var duration = Mathf.Max(_stopFadeCurve.Duration, 0.0001f);
                var t = Mathf.Clamp01(_stopFadeElapsed / duration);
                _stopFadeSource.volume = Mathf.Lerp(_stopFadeStartVolume, 0f, _stopFadeCurve.Evaluate(t));

                if (t >= 1f)
                {
                    _stopFadeSource.Stop();
                    _stopFading = false;
                }
            }

            if (_introHandoffPending && AudioSettings.dspTime >= _introHandoffDspTime)
            {
                _current = _introHandoffTarget;
                _incoming = _current == _channelA ? _channelB : _channelA;
                _introHandoffPending = false;
            }

            if (_crossfading)
            {
                _crossfadeElapsed += dt;

                // スプライス(ループ継ぎ目)が迫っている場合、継ぎ目は「もう一方のチャンネル」を
                // 必要とするが、そこはフェードアウト中の旧トラックが使用中。奪い合うと
                // _current と _fadingOutSource が同一ソースを指して無音化するため、
                // 先にクロスフェードを即時完了させてチャンネルを空ける。
                var spliceImminent = _loopSplicePending && _loopSpliceDspTime - AudioSettings.dspTime <= LoopLookaheadSeconds;

                var fadeInDuration = Mathf.Max(_fadeInCurve.Duration, 0.0001f);
                var fadeOutDuration = Mathf.Max(_fadeOutCurve.Duration, 0.0001f);

                var inT = spliceImminent ? 1f : Mathf.Clamp01(_crossfadeElapsed / fadeInDuration);
                var outT = spliceImminent ? 1f : Mathf.Clamp01(_crossfadeElapsed / fadeOutDuration);

                SetVolumeOnStartingChannels(Mathf.Lerp(0f, _fadeInTargetVolume, _fadeInCurve.Evaluate(inT)));

                if (_fadingOutSource != null)
                {
                    _fadingOutSource.volume = Mathf.Lerp(_fadeOutStartVolume, 0f, _fadeOutCurve.Evaluate(outT));
                }

                if (inT >= 1f && outT >= 1f)
                {
                    _crossfading = false;
                    _fadingOutSource?.Stop();
                    _fadingOutSource = null;
                }
            }

            if (!_crossfading && _loopSplicePending && _current != null && _currentData != null)
            {
                var remaining = _loopSpliceDspTime - AudioSettings.dspTime;
                if (remaining <= LoopLookaheadSeconds)
                {
                    var next = _current == _channelA ? _channelB : _channelA;
                    ConfigureCommon(next, _currentData);
                    next.volume = _current.volume;
                    StartLoopBody(next, _currentData, _loopSpliceDspTime);
                    _current = next;
                    _incoming = next == _channelA ? _channelB : _channelA;
                }
            }
        }

        private void StartLoopBody(AudioSource source, BgmData data, double startDsp)
        {
            source.clip = data.LoopBody;

            var hasCustomLoopPoints = data.LoopBody != null &&
                                       (data.LoopStartSec > 0.0 || data.LoopEndSec < data.LoopBody.length);

            if (!hasCustomLoopPoints)
            {
                source.loop = true;
                source.timeSamples = 0;
                source.PlayScheduled(startDsp);
                _loopSplicePending = false;
            }
            else
            {
                // サンプル単位で範囲を確定させる(float の clip.length/time 依存だとクリック音の原因になる)。
                var frequency = data.LoopBody.frequency;
                var startSample = (int)System.Math.Round(data.LoopStartSec * frequency);
                startSample = Mathf.Clamp(startSample, 0, data.LoopBody.samples - 1);
                var endSample = data.LoopEndSec > data.LoopStartSec
                    ? (int)System.Math.Round(data.LoopEndSec * frequency)
                    : data.LoopBody.samples;
                endSample = Mathf.Clamp(endSample, startSample + 1, data.LoopBody.samples);

                source.loop = false;
                source.timeSamples = startSample;
                source.PlayScheduled(startDsp);

                var segmentLength = (endSample - startSample) / (double)frequency;
                _loopSpliceDspTime = startDsp + segmentLength;

                // LoopEnd を超えた残り(クリップ末尾)が次のセグメントに重なって鳴らないよう、
                // このセグメントの終了時刻を明示する。
                source.SetScheduledEndTime(_loopSpliceDspTime);
                _loopSplicePending = true;
            }
        }

        private static ValueDef MakeFadeCurve(float overrideSeconds, ValueDef dataDefault)
        {
            if (overrideSeconds < 0f)
            {
                return dataDefault;
            }

            return new ValueDef
            {
                Mode = ValueMode.Parametric,
                Parametric = EaseDef.Named(Ease.Linear),
                From = 0f,
                To = 1f,
                Time = TimeDef.Duration(overrideSeconds),
            };
        }

        private static void ConfigureCommon(AudioSource source, BgmData data)
        {
            source.outputAudioMixerGroup = data.Mixer;
            source.spatialBlend = 0f;
            source.playOnAwake = false;
        }
    }
}
