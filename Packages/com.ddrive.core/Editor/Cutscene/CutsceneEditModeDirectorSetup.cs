using System.Collections.Generic;
using DDrive.Editor.Preview;
using DDrive.Foundation.Handle;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Cutscene.Tracks;
using DDrive.Runtime.Model;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace DDrive.Editor.Cutscene
{
    // [26_timeline.md] §4.4(Edit Mode プレビュー「Timeline ウィンドウ主導」、2026-09-19 追加) —
    // `CutsceneDataEditor` の「▶ Timeline ウィンドウで開く」から呼ぶ。確認用シーン
    // (`CutscenePreviewSceneSetup`)にプレビュー用 PlayableDirector を 1 つ用意し(既にあれば使い回す)、
    // `CutsceneData.Bindings` を解決してから、その GameObject を選択した状態で標準 Timeline ウィンドウを
    // 開く。Play Mode 用の経路(`CutsceneManager`/`CutscenePreviewHarness`)とは完全に別物 — こちらは
    // Edit Mode 専用の「見るだけ」の Director で、ネット・入力ロック・Skip の概念を持たない
    // (§4.4 実装メモ: Edit Mode の逸脱として受け入れた範囲)。
    //
    // public(InternalsVisibleTo 未設定のため、テスト asmdef から直接検証できるようにする。
    // CutsceneEditModePreviewProvider.cs 冒頭コメントと同じ理由。docs/45 P1-5、2026-09-20)。
    public static class CutsceneEditModeDirectorSetup
    {
        public const string DirectorName = "Cutscene Timeline Preview (Edit Mode)";

        // [26_timeline.md] §4.4 / docs/45 P1-5(2026-09-20) — ApplyBindings が Target=SpawnModel で
        // 生成した Model のハンドル。押し直す(EnsureDirector を再度呼ぶ)たびに、前回分をここから
        // Despawn してから新しく Spawn する(溜めない)。ドメインリロードで自動的に空になる。
        private static readonly List<Handle<ModelMarker>> _spawnedModels = new();

        public static void OpenTimelineWindow(CutsceneData cutscene)
        {
            if (cutscene == null || cutscene.Timeline == null)
            {
                return;
            }

            if (!CutscenePreviewSceneSetup.TryOpenOrCreate())
            {
                return; // 保存ダイアログでキャンセルされた(現在の作業を失わせない)。
            }

            var director = EnsureDirector(cutscene);

            CutsceneEditModePreviewProvider.PrepareContext(director.gameObject);

            Selection.activeGameObject = director.gameObject;
            FocusTimelineWindowOn(director);
        }

        // [26_timeline.md] §4.4 / docs/45 P1-5(2026-09-20) — `OpenTimelineWindow` の本体部分(確認用シーンの
        // セットアップ・Timeline ウィンドウのフォーカスを含まない)。テストから直接呼べるように切り出した。
        // 押し直しても Director を溜めず(DontSave の同名 GameObject を使い回す)、前回 Spawn した
        // Model は返却してから作り直す。
        public static PlayableDirector EnsureDirector(CutsceneData cutscene)
        {
            var directorGo = GameObject.Find(DirectorName);
            if (directorGo == null)
            {
                // [26_timeline.md] §4.4 / docs/45 P1-5 — `CutsceneEditModeManagers.PreviewRootName` と同じ
                // 流儀で DontSave にする(確認用シーンを保存してもこの Director は書き出されない)。
                directorGo = new GameObject(DirectorName) { hideFlags = HideFlags.DontSave };
            }

            var director = directorGo.GetComponent<PlayableDirector>();
            if (director == null)
            {
                director = directorGo.AddComponent<PlayableDirector>();
            }

            // docs/45 P1-5 — 既定値 true のままだと、保存されたシーンで Play Mode に入ったときに
            // CutsceneManager を経由せずこの Director が独自に再生してしまう(Play Mode 用の
            // `RentDirector` は明示的に false を入れている。同じ規約に揃える)。
            director.playOnAwake = false;

            if (directorGo.GetComponent<CutsceneCameraStateHolder>() == null)
            {
                directorGo.AddComponent<CutsceneCameraStateHolder>();
            }

            director.playableAsset = cutscene.Timeline;
            director.time = 0d;
            director.Evaluate();

            ApplyOrigin(cutscene, directorGo.transform);
            ApplyBindings(cutscene, director);

            return director;
        }

        // [26_timeline.md] §4.4 / docs/45 P1-5(2026-09-20) — Play Mode 突入・シーン切替・ドメインリロードで
        // プレビュー用 Director と SpawnModel した Model が残らないようにする
        // (`ScenePresentationPreviewDriver.OnStageChanged` と同じ「都度作り直す」方針)。
        public static void TearDown()
        {
            DespawnPreviewModels();

            var directorGo = GameObject.Find(DirectorName);
            if (directorGo != null)
            {
                Object.DestroyImmediate(directorGo);
            }
        }

        private static void DespawnPreviewModels()
        {
            if (_spawnedModels.Count == 0)
            {
                return;
            }

            // 既存の Manager 群があれば使う(無ければテアダウンのためだけに新規生成しない。
            // CutsceneEditModePreviewProvider.PeekManagers は無ければ null を返す)。
            var models = CutsceneEditModePreviewProvider.PeekManagers()?.Models;
            if (models != null)
            {
                for (var i = 0; i < _spawnedModels.Count; i++)
                {
                    models.Despawn(_spawnedModels[i]);
                }
            }

            _spawnedModels.Clear();
        }

        // 実機確認(2026-09-19、Unity MCP `execute_code`)で発見: `Selection.activeGameObject` を変えて
        // Timeline ウィンドウを開くだけでは、`TimelineEditor.inspectedDirector` がこの Director に
        // 更新されないことがある(既に開いている Timeline ウィンドウをフォーカスするだけの経路だと
        // 選択変更の通知が効かないケースがあった)。`UnityEditor.Timeline.TimelineEditorWindow` は
        // `Unity.Timeline.Editor`(Editor 専用アセンブリ)にあり、`DDrive.Editor.asmdef` は
        // `Unity.Timeline`(ランタイム側)しか参照していない(§4.4 の方針どおり asmdef は変更しない)ため、
        // reflection で `TimelineEditorWindow.SetTimeline(PlayableDirector)` を直接呼んで確実に紐付ける。
        // 型/メソッドが見つからない場合(将来の Unity バージョン差異)は Selection ベースの経路(既定の
        // 挙動、ユーザーが手で選び直せば済む)へ安全に諦める。
        private static void FocusTimelineWindowOn(PlayableDirector director)
        {
            EditorApplication.ExecuteMenuItem("Window/Sequencing/Timeline");

            var editorType = System.Type.GetType("UnityEditor.Timeline.TimelineEditor, Unity.Timeline.Editor");
            var getWindow = editorType?.GetMethod("GetWindow", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, System.Type.EmptyTypes, null);
            var window = getWindow?.Invoke(null, null);
            if (window == null)
            {
                return;
            }

            var setTimeline = window.GetType().GetMethod("SetTimeline", new[] { typeof(PlayableDirector) });
            setTimeline?.Invoke(window, new object[] { director });
        }

        // `CutsceneManager.ApplyOrigin`(Play Mode)の Edit Mode 簡略版。ネット・PlayContext は無いため、
        // Self は確認用シーンのアクター(`CutscenePreviewSceneSetup.ActorName`)の姿勢で代用する。
        private static void ApplyOrigin(CutsceneData cutscene, Transform root)
        {
            switch (cutscene.Origin)
            {
                case CutsceneOrigin.Self:
                    var actor = GameObject.Find(CutscenePreviewSceneSetup.ActorName);
                    if (actor != null)
                    {
                        var yaw = actor.transform.eulerAngles.y;
                        root.SetPositionAndRotation(actor.transform.position, Quaternion.Euler(0f, yaw, 0f));
                    }
                    else
                    {
                        root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    }

                    break;

                case CutsceneOrigin.AnchorPoint:
                    var anchor = FindAnchorPointByName(cutscene.OriginAnchorName);
                    if (anchor != null)
                    {
                        root.SetPositionAndRotation(anchor.transform.position, anchor.transform.rotation);
                    }
                    else
                    {
                        root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    }

                    break;

                default: // World
                    root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    break;
            }
        }

        // `CutsceneManager.ApplyBindings`(Play Mode)の Edit Mode 簡略版。Target=Target(ctx.Target)は
        // Edit Mode に「相手」の概念が無いため Self と同じアクターへ代用する(未解決よりはましな近似)。
        private static void ApplyBindings(CutsceneData cutscene, PlayableDirector director)
        {
            if (cutscene.Timeline == null || cutscene.Bindings == null)
            {
                return;
            }

            var managers = CutsceneEditModePreviewProvider.EnsureAndGetManagers();

            // docs/45 P1-5(2026-09-20) — 押し直すたびに前回 Spawn した Model を返却してから作り直す
            // (溜めない)。
            DespawnPreviewModels();

            var actor = GameObject.Find(CutscenePreviewSceneSetup.ActorName);

            foreach (var binding in cutscene.Bindings)
            {
                if (string.IsNullOrEmpty(binding.TrackName))
                {
                    continue;
                }

                var track = FindTrackByName(cutscene.Timeline, binding.TrackName);
                if (track == null)
                {
                    continue;
                }

                var resolved = ResolveBindingObject(in binding, actor, managers, director.transform);
                director.SetGenericBinding(track, resolved);
            }
        }

        private static TrackAsset FindTrackByName(TimelineAsset timeline, string name)
        {
            foreach (var track in timeline.GetOutputTracks())
            {
                if (track != null && track.name == name)
                {
                    return track;
                }
            }

            return null;
        }

        private static Object ResolveBindingObject(in CutsceneBinding binding, GameObject actor, CutsceneEditModeManagers managers, Transform directorRoot)
        {
            Transform target = null;

            switch (binding.Target)
            {
                case CutsceneBindTarget.MainCamera:
                    var cam = Camera.main;
                    target = cam != null ? cam.transform : null;
                    break;

                case CutsceneBindTarget.Self:
                case CutsceneBindTarget.Target:
                    target = actor != null ? actor.transform : null;
                    break;

                case CutsceneBindTarget.SpawnModel:
                    if (managers.Models != null && binding.Model.IsValid)
                    {
                        var handle = managers.Models.Spawn(binding.Model, directorRoot);
                        if (managers.Models.IsValid(handle))
                        {
                            // docs/45 P1-5(2026-09-20) — 次回 ApplyBindings/TearDown で返却するために覚えておく。
                            _spawnedModels.Add(handle);
                            var animator = managers.Models.GetAnimator(handle);
                            if (animator != null)
                            {
                                return animator;
                            }

                            var go = managers.Models.GetGameObject(handle);
                            target = go != null ? go.transform : null;
                        }
                    }

                    break;

                case CutsceneBindTarget.SceneObjectByName:
                    if (!string.IsNullOrEmpty(binding.SceneObjectName))
                    {
                        var found = GameObject.Find(binding.SceneObjectName);
                        target = found != null ? found.transform : null;
                    }

                    break;

                case CutsceneBindTarget.AnchorPoint:
                    var anchor = FindAnchorPointByName(binding.SceneObjectName);
                    target = anchor != null ? anchor.transform : null;
                    break;
            }

            if (target == null)
            {
                return null;
            }

            var boundAnimator = target.GetComponent<Animator>();
            return boundAnimator != null ? (Object)boundAnimator : target;
        }

        private static AnchorPoint FindAnchorPointByName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var all = Object.FindObjectsByType<AnchorPoint>(FindObjectsSortMode.None);
            foreach (var a in all)
            {
                if (a != null && a.name == name)
                {
                    return a;
                }
            }

            return null;
        }
    }
}
