using System.Collections.Generic;
using System.Text;
using DDrive.Editor.Ux;
using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Model;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DDrive.Editor.Anim
{
    // 「確認する」の欄のうち対象まわり: 動かす相手(シーン上の Animator)・確認用のモデルと情報(照合)・一時停止 / ポーズを戻す / 状態・イベントログ。
    // 再生・停止・対象の決め方は旧 AnimEditorWindow と同じ(シーン / プレハブモードのモデルを実 AnimManager で動かす)。
    internal sealed partial class AnimEditor
    {
        private ObjectField _sceneTargetField;
        private Label _sceneHelpLabel;
        private ObjectField _modelField;
        private Label _modelSummaryLabel;
        private Foldout _modelDetailFoldout;
        private Label _modelDetailLabel;
        private Toggle _showExternalShapesToggle;
        private VisualElement _modelIssueBox;
        private Button _modelEditorButton;
        private Label _statusLabel;
        private Label _logLabel;
        private Button _pauseButton;

        // 対象モデルと照合した結果(StateName が Controller に無い・BlendShape がモデルに無い)。Validator は対象モデルを知らないのでここで出す。
        private readonly List<ValidationResult> _modelIssues = new();
        private readonly HashSet<string> _missingShapes = new();

        // ── 欄(確認する) ──

        private UxRow CreateTargetRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            _sceneTargetField = new ObjectField { objectType = typeof(Animator), allowSceneObjects = true, tooltip = "動かす相手。確認用シーン / プレハブモードを開くと自動で入る。Hierarchy の別のモデルを入れてもよい" };
            _sceneTargetField.SetValueWithoutNotify(_sceneTarget);
            _sceneTargetField.RegisterValueChangedCallback(evt => SetSceneTarget(evt.newValue as Animator));
            box.Add(_sceneTargetField);

            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            line.Add(MakeButton("選択から取得", null, PickFromSelection, "Hierarchy の選択(または開いているプレハブモードのルート)の Animator を対象にする"));
            box.Add(line);

            _sceneHelpLabel = new Label();
            _sceneHelpLabel.AddToClassList("ux-dim");
            _sceneHelpLabel.AddToClassList("ux-note");
            box.Add(_sceneHelpLabel);
            return new AnimAuxRow(e, e.Label, e.Hint, e.Tier, box, null);
        }

        private void PickFromSelection()
        {
            var suggested = SceneAnimPreviewDriver.SuggestTarget();
            if (suggested != null)
            {
                SetSceneTarget(suggested);
            }
            else
            {
                AppendLog("⚠ Hierarchy で Animator を持つオブジェクトを選ぶか、「確認用シーン」「Prefab で開く」を押してください");
            }
        }

        private UxRow CreateModelRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            _modelField = new ObjectField { objectType = typeof(ModelData), allowSceneObjects = false, tooltip = "Animator を持つモデル。「確認用シーン」でシーンに配置され、対象になる" };
            _modelField.SetValueWithoutNotify(_model);
            _modelField.RegisterValueChangedCallback(evt =>
            {
                _model = evt.newValue as ModelData;
                OnModelChanged();
            });
            box.Add(_modelField);

            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            _modelEditorButton = MakeButton("Model Editor で開く", null, () => OpenModelEditor(_model), "確認用モデルを専用エディター(Model Editor)で開く(Material スロット・Avatar などの確認)");
            _modelEditorButton.SetEnabled(_model != null);
            line.Add(_modelEditorButton);
            box.Add(line);

            _modelSummaryLabel = new Label();
            _modelSummaryLabel.AddToClassList("ux-dim");
            _modelSummaryLabel.AddToClassList("ux-note");
            box.Add(_modelSummaryLabel);

            _modelDetailFoldout = new Foldout { text = "モデル情報の詳細(BlendShape 一覧など)", value = false };
            _modelDetailLabel = new Label();
            _modelDetailLabel.AddToClassList("ux-dim");
            _modelDetailLabel.AddToClassList("ux-note");
            _modelDetailFoldout.Add(_modelDetailLabel);
            // FC-20: 外部パッケージが管理するシェイプ(FC_ / fcs_)は既定で一覧から隠す。
            _showExternalShapesToggle = new Toggle("外部管理のシェイプも表示") { value = false };
            _showExternalShapesToggle.RegisterValueChangedCallback(_ => RefreshModelInfo());
            _modelDetailFoldout.Add(_showExternalShapesToggle);
            box.Add(_modelDetailFoldout);

            _modelIssueBox = new VisualElement();
            _modelIssueBox.AddToClassList("ux-issues");
            box.Add(_modelIssueBox);
            return new AnimAuxRow(e, e.Label, e.Hint, e.Tier, box, null);
        }

        private static void OpenModelEditor(ModelData model)
        {
            if (model != null)
            {
                DDrive.Editor.Inspector.DataEditorRegistry.OpenDefault(model);
            }
        }

        private UxRow CreatePlayControlRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            _pauseButton = MakeButton("⏸ 一時停止", null, TogglePause, "その瞬間で止める(タイムラインをクリックしても止まる)。もう一度押すか、停止中なら最初から再生");
            line.Add(_pauseButton);
            line.Add(MakeButton("↺ ポーズを戻す", null, () => _scene?.RestorePoseNow(), "シーン上の対象を再生前のポーズに戻す"));
            box.Add(line);
            _statusLabel = new Label("■ 停止中");
            _statusLabel.AddToClassList("ux-dim");
            _statusLabel.AddToClassList("ux-pstatus");
            box.Add(_statusLabel);
            return new AnimAuxRow(e, e.Label, e.Hint, e.Tier, box, null);
        }

        private UxRow CreateLogRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            _logLabel = new Label(string.Join("\n", _eventLog));
            _logLabel.AddToClassList("ux-plog");
            box.Add(_logLabel);
            var clear = MakeButton("ログを消す", "ux-btn--small", () =>
            {
                _eventLog.Clear();
                _logLabel.text = string.Empty;
            }, "ログを空にする");
            box.Add(clear);
            return new AnimAuxRow(e, e.Label, e.Hint, e.Tier, box, null);
        }

        // ── ログ ──

        private void OnEventFired(DDrive.Foundation.Manager.InstanceContext ctx, AssetEvent evt)
        {
            var when = evt.Trigger switch
            {
                EventTrigger.Frame => $"Frame {evt.Time:0}",
                EventTrigger.Time => $"{evt.Time:0.##}s",
                _ => evt.Trigger.ToString(),
            };
            AppendLog($"{when}: {evt.Action}{(evt.Target.IsAssigned ? $" {evt.Target.Type} 0x{evt.Target.Id:X}" : string.Empty)}");
        }

        private void AppendLog(string line)
        {
            _eventLog.Add(line);
            while (_eventLog.Count > MaxEventLog)
            {
                _eventLog.RemoveAt(0);
            }

            if (_logLabel != null)
            {
                _logLabel.text = string.Join("\n", _eventLog);
            }
        }

        // ── シーン・プレハブモードの変化 ──

        // シーン切替: ドライバは自分でリセット済み。対象を作り直す(確認用モデルがあれば次の ▶ で配置)。
        private void OnActiveSceneChanged(Scene previous, Scene current)
        {
            _animHandle = Handle<AnimMarker>.Invalid;
            _seqIndex = -1;
            SetSceneTarget(null);
        }

        // プレハブモードに入ったら、その Prefab の Animator を自動で対象にする(モデル Prefab の中で動かす導線)。
        private void OnPrefabStageOpened(PrefabStage stage)
        {
            _animHandle = Handle<AnimMarker>.Invalid;
            _seqIndex = -1;
            var animator = stage != null && stage.prefabContentsRoot != null ? stage.prefabContentsRoot.GetComponentInChildren<Animator>(true) : null;
            SetSceneTarget(animator);
            if (animator != null)
            {
                AppendLog($"対象: プレハブモード '{animator.name}'");
            }
        }

        private void OnPrefabStageClosing(PrefabStage stage)
        {
            _animHandle = Handle<AnimMarker>.Invalid;
            _seqIndex = -1;
            SetSceneTarget(null);
        }

        // ── 対象(シーン上の Animator) ──

        // 左クリック = 確認用シーンを開いて配置 / 右クリック = このシーンに配置・本配置。
        private void OpenPreviewScene(DDrive.Editor.Preview.PreviewPlaceMode mode)
        {
            StopMain();
            if (!DDrive.Editor.Preview.PreviewPlacement.PrepareScene(mode, DDrive.Editor.Preview.VfxPreviewSceneSetup.TryOpenOrCreate))
            {
                return;
            }

            if (DDrive.Editor.Preview.PreviewPlacement.IsPersistent(mode))
            {
                if (_model == null || _model.Prefab == null)
                {
                    AppendLog("⚠ 本配置するには確認用モデル(Prefab 付きの ModelData)を入れてください");
                    return;
                }

                // 本配置は ModelsManager が追跡しない実体(Prefab リンク付き)にする。動かす対象にはしない。
                DDrive.Editor.Preview.PreviewPlacement.PlacePrefabPersistent(_model.Prefab, Vector3.zero, Quaternion.identity);
                AppendLog("このシーンに本配置しました(プレビュー対象にはしていません)");
                return;
            }

            // シーンが開いた(または既に開いていた)ので、確認用モデルを配置して対象にする。
            var animator = EnsureSceneTarget();
            if (animator == null)
            {
                AppendLog(_model == null ? "⚠ 確認用モデルを入れると、確認用シーンに配置して動かせます" : "⚠ 確認用モデルの Prefab に Animator がありません");
            }
            else
            {
                DDrive.Editor.Preview.PreviewPlacement.Focus(animator.gameObject); // SceneView のカメラを配置先へ寄せる
            }
        }

        private void OpenModelPrefab()
        {
            if (_model != null && _model.Prefab != null)
            {
                StopMain();
                AssetDatabase.OpenAsset(_model.Prefab); // prefabStageOpened で対象が自動で入る
            }
            else
            {
                AppendLog("⚠ 確認用モデル(Prefab 付き)が未設定です");
            }
        }

        private void OnModelChanged()
        {
            StopMain();
            // 自前で配置していた分は手放す(次の ▶ / 確認用シーンで新しいモデルを配置)。
            if (_scene != null && _scene.OwnsCurrent)
            {
                _scene.ReleaseTarget();
                _sceneTarget = null;
                _sceneTargetField?.SetValueWithoutNotify(null);
            }

            _modelEditorButton?.SetEnabled(_model != null);
            RefreshSceneUi();
        }

        // 「手動で Animator を差し替える」経路。Presentation の「一緒に調整」もここを使う。
        private void SetSceneTarget(Animator animator)
        {
            StopMain();
            if (_scene != null && animator != _scene.Current)
            {
                _scene.ReleaseTarget();
            }

            _sceneTarget = animator;
            _sceneTargetField?.SetValueWithoutNotify(animator);
            RefreshSceneUi();
        }

        // 再生対象を確定する: 指定 Animator > 配置済みの自前モデル > 確認用モデルを配置。配置したものは欄にも反映する。
        private Animator EnsureSceneTarget()
        {
            if (_scene == null)
            {
                return null;
            }

            if (_sceneTarget != null)
            {
                if (_scene.Current != _sceneTarget)
                {
                    _scene.SetTarget(_sceneTarget);
                }

                return _scene.Current;
            }

            if (_scene.Current != null && _scene.OwnsCurrent)
            {
                return _scene.Current;
            }

            if (_model != null)
            {
                var animator = _scene.SpawnModel(_model, Vector3.zero, Quaternion.identity);
                if (animator != null)
                {
                    _sceneTarget = animator;
                    _sceneTargetField?.SetValueWithoutNotify(animator);
                    AppendLog($"確認用モデル '{_model.DisplayName ?? _model.name}' を配置");
                }

                RefreshSceneUi();
                return animator;
            }

            return null;
        }

        // 対象が変わったあとの表示まとめ(対象の説明・モデル情報・照合・ステート一覧・ブレンド確認)。
        private void RefreshSceneUi()
        {
            RefreshSceneHelp();
            RefreshModelInfo();
            RefreshStateChoices();
        }

        private void RefreshSceneHelp()
        {
            if (_sceneHelpLabel == null)
            {
                return;
            }

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            var where = stage != null
                ? $"プレハブモード '{System.IO.Path.GetFileNameWithoutExtension(stage.assetPath)}'"
                : $"シーン '{SceneManager.GetActiveScene().name}'";
            if (_sceneTarget != null)
            {
                var owned = _scene != null && _scene.OwnsCurrent && _scene.Current == _sceneTarget;
                _sceneHelpLabel.text = owned
                    ? $"対象: {_sceneTarget.name}({where} に配置した確認用モデル、保存されません)。SceneView で確認できます。"
                    : $"対象: {_sceneTarget.name}({where})。停止すると再生前のポーズに戻し、シーン / Prefab には保存されません。";
            }
            else if (_model != null)
            {
                _sceneHelpLabel.text = $"対象なし。「確認用シーン」で確認用モデル '{_model.DisplayName ?? _model.name}' を配置するか、「Prefab で開く」でプレハブモードのモデルを対象にします(▶ でも {where} の原点に配置します)。";
            }
            else
            {
                _sceneHelpLabel.text = "確認用モデルを入れて「確認用シーン」を押すか、Hierarchy でモデルを選んで「選択から取得」を押してください。";
            }
        }

        private Animator CurrentAnimator => _scene?.Current != null ? _scene.Current : _sceneTarget;

        // モデル情報: 1 行の要約 + 折りたたみの詳細(BlendShape 名の一覧は長いので畳んでおく)+ 対象モデルとの照合。
        private void RefreshModelInfo()
        {
            var animator = CurrentAnimator;
            RefreshBlendUi(animator);
            CollectModelIssues(animator);
            ApplyShapeFlags();

            if (_modelSummaryLabel == null)
            {
                return;
            }

            if (animator == null)
            {
                _modelSummaryLabel.text = _model == null
                    ? "モデル情報: 対象が決まるとここに Controller と BlendShape の要約が出ます。"
                    : $"モデル情報: 確認用モデル '{_model.DisplayName ?? _model.name}'(未配置)";
                _modelDetailLabel.text = string.Empty;
                _modelDetailFoldout.Show(false);
                return;
            }

            var shapes = new List<string>();
            foreach (var smr in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null)
                {
                    continue;
                }

                for (var i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                {
                    shapes.Add(smr.sharedMesh.GetBlendShapeName(i));
                }
            }

            // FC-20: 所有接頭辞のシェイプは既定で隠し、件数だけ出す(Data に入っている値は触らない)。
            var showExternal = _showExternalShapesToggle != null && _showExternalShapesToggle.value;
            var externalCount = 0;
            var listed = new List<string>(shapes.Count);
            foreach (var shape in shapes)
            {
                if (ExternalBlendShapePrefixes.IsOwnedExternally(shape))
                {
                    externalCount++;
                    if (!showExternal)
                    {
                        continue;
                    }
                }

                listed.Add(shape);
            }

            var controller = animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "なし(Clip を直接サンプリング)";
            var proxy = animator.GetComponent<AnimatorProxy>();
            var ik = proxy != null && (proxy.LeftHandTarget != null || proxy.RightHandTarget != null || proxy.LeftFootTarget != null || proxy.RightFootTarget != null) ? " / IK ターゲットあり" : string.Empty;
            _modelSummaryLabel.text = $"Controller: {controller} / BlendShape {shapes.Count - externalCount} 個{(externalCount > 0 ? $"(外部管理 {externalCount} 件を除く)" : string.Empty)}{ik}";

            var sb = new StringBuilder();
            sb.Append("Animator: ").Append(animator.name).Append('\n');
            sb.Append("Controller: ").Append(controller).Append('\n');
            sb.Append(listed.Count > 0 ? "BlendShape: " + string.Join(", ", listed) : "BlendShape: なし");
            if (externalCount > 0 && !showExternal)
            {
                sb.Append('\n').Append($"外部管理 {externalCount} 件(FC_ / fcs_ で始まるシェイプ。外部パッケージが書くため AnimData では指定しません)");
            }

            _modelDetailLabel.text = sb.ToString();
            _modelDetailFoldout.Show(true);
        }

        // StateName が対象モデルの Controller に無い・BlendShape がモデルに無い(静的な Validator の対象外。対象モデルが要るのでここで出す)。
        private void CollectModelIssues(Animator animator)
        {
            _modelIssues.Clear();
            _missingShapes.Clear();
            var data = Target;
            if (data != null && animator != null)
            {
                var state = data.ResolvedStateName;
                if (animator.runtimeAnimatorController != null && !string.IsNullOrEmpty(state) &&
                    (data.Layer >= animator.layerCount || !animator.HasState(data.Layer, Animator.StringToHash(state))))
                {
                    _modelIssues.Add(ValidationResult.Error($"StateName '{state}'(Layer {data.Layer}) が対象モデルの Controller にありません").WithField(nameof(AnimData.StateName)));
                }

                if (data.BlendShapes != null)
                {
                    foreach (var track in data.BlendShapes)
                    {
                        if (!string.IsNullOrEmpty(track.ShapeName) && !HasBlendShape(animator, track.ShapeName))
                        {
                            _missingShapes.Add(track.ShapeName);
                            _modelIssues.Add(ValidationResult.Warning($"BlendShape '{track.ShapeName}' が対象モデルにありません").WithField(nameof(AnimData.BlendShapes)));
                        }
                    }
                }
            }

            if (_modelIssueBox == null)
            {
                return;
            }

            _modelIssueBox.Clear();
            for (var i = 0; i < _modelIssues.Count && i < 4; i++)
            {
                _modelIssueBox.Add(UxRow.MakeIssue(_modelIssues[i], null));
            }

            if (_modelIssues.Count > 4)
            {
                var more = new Label($"ほか {_modelIssues.Count - 4} 件(表情の行の ⚠ に出ます)");
                more.AddToClassList("ux-dim");
                more.AddToClassList("ux-note");
                _modelIssueBox.Add(more);
            }
        }

        // シーン上の借用モデルにコンポーネントを足さずに BlendShape 名を確認する。
        private static bool HasBlendShape(Animator animator, string shapeName)
        {
            foreach (var smr in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh != null && smr.sharedMesh.GetBlendShapeIndex(shapeName) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        // ── 再生 ──

        // 最初から再生する(固定バーの ▶)。一時停止中の再開は「⏸ 一時停止 / ▶ 再開」ボタンで行う。
        private void PlayFresh()
        {
            if (Target == null || _scene == null)
            {
                return;
            }

            var animator = EnsureSceneTarget();
            if (animator == null)
            {
                AppendLog("⚠ 対象がありません。「確認用シーン」「Prefab で開く」か、シーン上の Animator を指定してください");
                return;
            }

            _seqIndex = -1;
            _scene.StopSpawned(); // 最初に戻るので前回の SE / VFX は切る
            // イベントに新規割り当てした SE / VFX(ウィンドウを開いた後に作った Data)を ID 解決できるようにする。
            // Registry は OnEnable で 1 回作るだけなので、再生のたびに最新のアセットを登録し直す。
            DDrive.Editor.Preview.EditorAnchorRegistry.Refresh(_scene.Registry);
            _animHandle = _scene.Play(Target, animator);
            AppendLog($"▶ '{Target.DisplayName ?? Target.name}' on {animator.name}");
            UpdatePauseButtonLabel();
        }

        private void StopAnim()
        {
            _seqIndex = -1;
            if (_scene != null)
            {
                if (_scene.Manager.IsPlaying(_animHandle))
                {
                    _scene.Manager.Stop(_animHandle);
                }

                _scene.Stop(); // ポーズはその瞬間のまま(戻すのは「↺ ポーズを戻す」)
            }

            _animHandle = Handle<AnimMarker>.Invalid;
            UpdatePauseButtonLabel();
        }

        // 一時停止 / 再開。停止中なら最初から再生する。
        private void TogglePause()
        {
            if (_scene == null)
            {
                return;
            }

            if (!_scene.Manager.IsPlaying(_animHandle))
            {
                PlayMain(false);
                return;
            }

            var paused = _scene.IsPaused;
            _scene.SetPaused(_animHandle, !paused);
            AppendLog(paused ? "▶ 再開" : "⏸ 一時停止");
            UpdatePauseButtonLabel();
        }

        private void UpdatePauseButtonLabel()
        {
            if (_pauseButton == null)
            {
                return;
            }

            var paused = _scene != null && _scene.IsPaused && _scene.Manager.IsPlaying(_animHandle);
            var text = paused ? "▶ 再開" : "⏸ 一時停止";
            if (_pauseButton.text != text)
            {
                _pauseButton.text = text;
            }
        }

        // OnEditorUpdate(フレームごと): 再生状態・ブレンド確認のシーケンス・タイムライン・状態表示。
        private void OnEditorUpdate()
        {
            if (_scene == null || Target == null)
            {
                return;
            }

            var anim = _scene.Manager;
            var playing = anim.IsPlaying(_animHandle);
            if (!playing)
            {
                // 終了済み Handle を毎フレーム問い合わせて無効 Handle 警告を出さないよう Invalid に戻す。
                _animHandle = Handle<AnimMarker>.Invalid;
            }

            // ブレンド確認: 遷移シーケンスの進行(AnimEditor.Blend.cs)。
            TickSequence(anim, ref playing);

            if (playing || _scene.HasActive)
            {
                _timelineContainer?.MarkDirtyRepaint();
            }

            if (_statusLabel == null)
            {
                return;
            }

            var t = anim.GetNormalizedTime(_animHandle);
            var status = playing
                ? (anim.IsPaused(_animHandle)
                    ? $"⏸ 一時停止  {t:P0}  (Frame {Mathf.RoundToInt(t * Target.LengthSec * Target.FrameRate)})"
                    : $"● 再生中  {t:P0}  (周回 {anim.GetLoopCount(_animHandle)})")
                : Repeat && WantPlaying ? "↻ リピート待機" : "■ 停止中";
            SetLabel(_statusLabel, status);
            UpdatePauseButtonLabel();
        }
    }
}
