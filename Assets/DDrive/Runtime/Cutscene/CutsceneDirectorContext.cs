using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Ui;
using DDrive.Runtime.Vfx;
using UnityEngine;

namespace DDrive.Runtime.Cutscene
{
    // [26_timeline.md] §4.4(Edit Mode プレビュー、2026-09-19 追加) — PlayableDirector の GameObject に付ける
    // 文脈。SE/VFX/UI/AnchorGroup クリップの `CreatePlayable(graph, owner)` が `owner` から
    // `owner.GetComponent<CutsceneDirectorContext>()` を引いて見る(`CutsceneCameraMixerBehaviour.Owner` と
    // 同じ「owner から辿る」手法)。CutsceneManager.Advance*Markers も同じ `FireEnabled` を見る。
    //
    // Play Mode: `CutsceneManager.RentDirector` が CutsceneRoot に付け、`FireEnabled = true` 固定にする
    //   (`ManagerRefs` は null のまま — 各クリップは Manager 参照が無ければ既存どおり静的ファサードを直接呼ぶ
    //   経路にフォールバックする。静的ファサードは `DDriveRuntimeBootstrap` が Bind 済みのため、Play Mode の
    //   挙動は本対応の前後で変わらない)。
    // Edit Mode: `CutsceneEditModePreviewProvider`(Editor asmdef)が Timeline ウィンドウで開いた
    //   プレビュー用 PlayableDirector にこれを付け、`ManagerRefs` に Editor 用 Manager 群を入れ、
    //   `FireEnabled` は「Timeline ウィンドウが実際に再生中か」(`PlayableDirector.state == PlayState.Playing`、
    //   §4.4 実装メモ参照)に同期する。
    public sealed class CutsceneDirectorContext : MonoBehaviour
    {
        [Tooltip("クリップ/マーカーを実際に発火してよいか([26] §4.4「ドラッグ中は無音、再生ボタン中のみ発音」)。" +
                 "Play Mode の CutsceneManager は常に true。Edit Mode は Timeline ウィンドウの再生状態に同期する。")]
        public bool FireEnabled;

        // Editor プレビュー専用の Manager 参照。プレーンな C# クラスなのでシーンへは保存されない
        // (ドメインリロード後は null に戻るため、Editor 側は毎回 EnsureContext 相当で入れ直す)。
        [System.NonSerialized]
        public CutsceneDirectorManagerRefs ManagerRefs;
    }

    // Edit Mode プレビュー用の Manager 参照の束(SE/VFX/UI/AnchorGroup クリップが使う 4 種のみ)。
    // Shake/Haptic マーカー・Camera クリップ・Event/Signal マーカーは Editor 側の監視役
    // (`CutsceneEditModePreviewProvider`)が別経路で直接持つ Manager を使うため、ここには含めない。
    // Presentation クリップは Edit Mode 未対応
    // (§4.4 実装メモ参照。ManagerRefs が無いので既存の静的ファサードにフォールバックし、Edit Mode では
    // 未 Bind のため no-op のまま安全に継続する)。
    public sealed class CutsceneDirectorManagerRefs
    {
        public AudioManager Audio;
        public VfxManager Vfx;
        public UiManager Ui;
        public AnchorGroupPlayer Groups;
    }
}
