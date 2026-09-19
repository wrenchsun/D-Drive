using System;
using DDrive.Foundation.Event;
using UnityEngine;
using UnityEngine.Timeline;

namespace DDrive.Runtime.Cutscene.Tracks
{
    // [26_timeline.md] §4.3(6-10b) — 「一点」を通知する D-Drive のマーカー群。
    //
    // 実装メモ: Unity 標準の Signal(Marker+INotification+INotificationReceiver、native の通知配送)は
    // 検証の結果採用しなかった。`TimeNotificationBehaviour.PrepareFrame` が
    // `FrameData.EvaluationType.Evaluate`(= `PlayableDirector.Evaluate()` 単体呼び出し)のフレームで
    // 通知を一切送らない実装になっており(コメント「Never trigger on scrub」)、CutsceneManager は
    // DirectorUpdateMode.Manual + 毎 Tick `Evaluate()` で駆動するため、native 通知に頼るとビルド構成や
    // Unity バージョンの詳細次第で発火しない/しにくいリスクがある。代わりに、これらのマーカーは
    // `Marker`(IMarker)だけを実装する「時刻付きデータ」として扱い、CutsceneManager が Play() 時に
    // `TrackAsset.GetMarkers()` で一覧を集めて時刻順に保持し、Tick() で `elapsed` がその時刻を跨いだら
    // 直接発火する(EventBus.Tick の Frame/Time 判定・PresentationManager.FireDueTracks と同じ「跨いだら
    // 発火、Seek は跨いだ分を無音でスキップ」という既存の定常経路パターンに揃える)。
    // Unity 標準 Timeline ウィンドウ上での見え方・ドラッグ移動・スナップは Marker 基底だけで機能する。

    // ── D-Drive Event マーカー: AssetEvent(PlayAsset 等)を 1 件持つ。AssetEventDispatcher へ委譲する ──
    [Serializable]
    public sealed class CutsceneEventNotification : Marker
    {
        [Tooltip("通過時に発火する AssetEvent(Action=PlayAsset のみ AssetEventDispatcher が処理する。他の Action は現状 no-op)。")]
        public AssetEvent Event;
    }

    [TrackColor(0.5f, 0.7f, 0.9f)]
    public sealed class CutsceneEventTrack : MarkerTrack
    {
    }

    // ── D-Drive Signal マーカー: 文字列キーをコードへ通知する(CutsceneHandle.OnMarker)。
    //    Skip=ToMarker の目標(CutsceneData.SkipToMarkerKey)としても使う([26] §4.1/§4.3) ──
    [Serializable]
    public sealed class CutsceneSignalNotification : Marker
    {
        [Tooltip("cutscene/xxx 規約のキー(CutsceneHandle.OnMarker(key) で受け取る)。")]
        public string Key;
    }

    [TrackColor(0.9f, 0.6f, 0.2f)]
    public sealed class CutsceneSignalTrack : MarkerTrack
    {
    }

    // ── D-Drive Shake マーカー: CameraFx.Shake(id) を委譲する([26] §4.3 の「Shake/Haptic マーカー」) ──
    [Serializable]
    public sealed class CutsceneShakeNotification : Marker
    {
        public DDrive.Foundation.Identity.AssetId<DDrive.Runtime.CameraShake.ShakeMarker> ShakeId;
    }

    [TrackColor(0.8f, 0.3f, 0.3f)]
    public sealed class CutsceneShakeTrack : MarkerTrack
    {
    }

    // ── D-Drive Haptic マーカー: Haptics.Play(id) を委譲する ──
    [Serializable]
    public sealed class CutsceneHapticNotification : Marker
    {
        public DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Haptics.HapticMarker> HapticId;
    }

    [TrackColor(0.3f, 0.6f, 0.3f)]
    public sealed class CutsceneHapticTrack : MarkerTrack
    {
    }
}
