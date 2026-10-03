using DDrive.Foundation.Handle;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace DDrive.Runtime.Cutscene
{
    // [26_timeline.md] §4.3 / [51_tdrive_integration.md] §4.5(FC-4、2026-10-03) — 外部パッケージが自前の
    // `Marker` 派生クラスに実装する、汎用のカットシーン・マーカー受け口。
    // `CutsceneManager`(Play)/ `CutsceneEditModePreviewProvider`(Edit Mode)が、既存の D-Drive 4 種
    // (Event/Signal/Shake/Haptic)と同じ規則で `Fire` を呼ぶ:
    //   - 再生位置がマーカー時刻を跨いだ Tick で 1 回だけ(巻き戻しても再発火しない)
    //   - Seek / Skip / ネット遅延復元で跨いだ分は無音(呼ばれない)
    //   - 発火が許可されていない間(Edit Mode のスクラブ中など)は呼ばれない
    // `Fire` が投げた例外は D-Drive が捕まえて `Debug.LogException` し、他のマーカー・Tick を止めない。
    // 呼び出しは各クライアントのローカル処理(ネットワークには流れない)。全員で同じ処理をしたいときは、
    // Cosmetic で再生される Cutscene を使う(各クライアントが同じ時刻に Fire する)か、外部側で同期する。
    public interface ICutsceneMarker
    {
        void Fire(in CutsceneMarkerContext context);
    }

    // `ICutsceneMarker.Fire` に渡す文脈。欄は意図的に最小限(公開すると改名・削除できないため)。
    public readonly struct CutsceneMarkerContext
    {
        // マーカーの時刻(秒、Timeline 上の位置)。
        public readonly double MarkerTime;

        // 発火時点の再生位置(秒)。`>= MarkerTime`(跨いだ Tick の終端)。
        public readonly double Elapsed;

        // 再生に使っている PlayableDirector(Edit Mode は Timeline ウィンドウが開いている Director)。
        public readonly PlayableDirector Director;

        // Play Mode: 再生中のカットシーンのハンドル(`Cutscene.*` / `CutsceneManager.*` に渡せる)。
        // Edit Mode のプレビューでは `Handle<CutsceneMarker>.Invalid`。
        public readonly Handle<CutsceneMarker> Handle;

        // Edit Mode の Timeline ウィンドウ再生 / プレビューか(Play Mode は false)。
        public readonly bool IsEditPreview;

        public CutsceneMarkerContext(
            double markerTime,
            double elapsed,
            PlayableDirector director,
            Handle<CutsceneMarker> handle,
            bool isEditPreview)
        {
            MarkerTime = markerTime;
            Elapsed = elapsed;
            Director = director;
            Handle = handle;
            IsEditPreview = isEditPreview;
        }
    }
}
