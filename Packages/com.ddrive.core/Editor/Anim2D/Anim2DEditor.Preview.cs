using DDrive.Editor.Anim;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Editor.Versioning;
using DDrive.Foundation.Event;
using DDrive.Foundation.Handle;
using DDrive.Runtime.Anim;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DDrive.Editor.Anim2D
{
    // 「確認する」の欄: SpriteRenderer + Animator のプレビュー物(DontSave。開いているシーン / プレハブモードに置く)を実 AnimManager(SceneAnimPreviewDriver)で動かし、
    // SceneView で確認する(ADR-4。2026-09-10 決定の方式)。ウィンドウには再生中の絵(プレビュー物の SpriteRenderer が持つ Sprite)を映す。
    // ▶ ■ ・リピート・速度は固定バー。ここはプレビュー物の配置 / 撤去・状態・イベントログ。
    internal sealed partial class Anim2DEditor
    {
        private Image _mirror;
        private Label _previewStatus;
        private Label _statusLabel;
        private Label _logLabel;

        // ── 欄 ──

        private UxRow CreatePreviewRow(FieldGuideEntry e)
        {
            var box = new VisualElement();

            _mirror = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            _mirror.AddToClassList("ux-a2d-mirror");
            box.Add(_mirror);

            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            var scene = PreviewPlacementButton.Create(
                "確認用シーン",
                "確認用シーンを開き(無ければ生成)、SpriteRenderer 付きのプレビュー物を配置する。右クリックで、いま開いているシーンへ配置",
                OpenPreviewScene);
            scene.AddToClassList("ux-btn");
            line.Add(scene);
            line.Add(MakeButton("今のシーンに配置", null, PlacePreview, "開いているシーン / プレハブモードに、保存されないプレビュー物を置く"));
            line.Add(MakeButton("撤去", null, RemovePreview, "プレビュー物をシーンから消す"));
            box.Add(line);

            _previewStatus = new Label(_previewObject != null ? "プレビュー物を配置済み(SceneView を確認)" : "プレビュー物は未配置");
            _previewStatus.AddToClassList("ux-dim");
            _previewStatus.AddToClassList("ux-note");
            box.Add(_previewStatus);

            _statusLabel = new Label("■ 停止中");
            _statusLabel.AddToClassList("ux-dim");
            _statusLabel.AddToClassList("ux-pstatus");
            box.Add(_statusLabel);
            return new AnimAuxRow(e, e.Label, e.Hint, e.Tier, box, null);
        }

        private UxRow CreateLogRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            _logLabel = new Label(string.Join("\n", _log));
            _logLabel.AddToClassList("ux-plog");
            box.Add(_logLabel);
            box.Add(MakeButton("ログを消す", "ux-btn--small", () =>
            {
                _log.Clear();
                _logLabel.text = string.Empty;
            }, "ログを空にする"));
            return new AnimAuxRow(e, e.Label, e.Hint, e.Tier, box, null);
        }

        // ── 配置 ──

        // 左クリック = 確認用シーンを開いて配置 / 右クリック = このシーンに配置・本配置。
        private void OpenPreviewScene(PreviewPlaceMode mode)
        {
            StopMain();
            if (!PreviewPlacement.PrepareScene(mode, VfxPreviewSceneSetup.TryOpenOrCreate))
            {
                return;
            }

            PlacePreview();
            if (PreviewPlacement.IsPersistent(mode) && _previewObject != null)
            {
                // 置いたプレビュー物をそのままシーンの住人にする。引き渡した後はこちらが所有しない(撤去・再生の対象から外す)。
                PreviewPlacement.Persist(_previewObject, Target != null ? Target.DisplayName ?? Target.name : null);
                _scene?.ReleaseTarget();
                _previewObject = null;
                SetLabel(_previewStatus, "このシーンに本配置しました(プレビュー対象にはしていません)");
            }
        }

        private void PlacePreview()
        {
            if (Target == null)
            {
                SetLabel(_previewStatus, "編集対象の Anim2DData を選んでください");
                return;
            }

            if (_scene == null)
            {
                return;
            }

            if (_previewObject == null)
            {
                _previewObject = Anim2DPreviewObject.FindOrCreate(Target);
            }

            Anim2DPreviewObject.ApplyFirstSprite(_previewObject, Target);
            _scene.SetTarget(_previewObject.GetComponent<Animator>());
            SetLabel(_previewStatus, "プレビュー物を配置済み(SceneView を確認)");
            PreviewPlacement.Focus(_previewObject);
            RefreshMirror();
            SceneView.RepaintAll();
        }

        private void RemovePreview()
        {
            StopMain();
            _scene?.ReleaseTarget();
            Anim2DPreviewObject.Destroy(_previewObject);
            _previewObject = null;
            SetLabel(_previewStatus, "プレビュー物は未配置");
            RefreshMirror();
            SceneView.RepaintAll();
        }

        // シーン切替: プレビュー物(DontSave)はシーンと一緒に消える。参照を捨てる(次の ▶ で置き直す)。
        private void OnActiveSceneChanged(Scene previous, Scene current)
        {
            _handle = Handle<AnimMarker>.Invalid;
            _previewObject = null;
            SetLabel(_previewStatus, "プレビュー物は未配置");
        }

        // ── 再生 ──

        private void PlayFresh()
        {
            if (Target == null || _scene == null)
            {
                return;
            }

            if (Target.Clip == null)
            {
                AppendLog("⚠ Clip が未設定です(再生できません)");
                return;
            }

            if (_previewObject == null)
            {
                PlacePreview();
            }

            if (_previewObject == null)
            {
                return;
            }

            _scene.StopSpawned(); // 最初に戻るので前回の SE / VFX は切る
            // イベントに新規割り当てした SE / VFX(ウィンドウを開いた後に作った Data)を ID 解決できるようにする。
            EditorAnchorRegistry.Refresh(_scene.Registry);
            _handle = _scene.Play(Target, _previewObject.GetComponent<Animator>(), 0f);
            AppendLog($"▶ '{Target.DisplayName ?? Target.name}'");
            SetLabel(_previewStatus, _scene.Manager.IsPlaying(_handle) ? "● 再生中(SceneView)" : "再生できません");
        }

        private void StopAnim()
        {
            if (_scene != null)
            {
                if (_scene.Manager.IsPlaying(_handle))
                {
                    _scene.Manager.Stop(_handle);
                }

                _scene.Stop();
            }

            _handle = Handle<AnimMarker>.Invalid;
            if (_previewObject != null)
            {
                SetLabel(_previewStatus, "■ 停止");
            }
        }

        // イベントのタイムライン編集は Anim Editor へ。プレビュー物はシーンに残したまま(絵が消えないように)、こちらの再生とドライバの対象だけ手放す。
        private void OpenAnimEditorForEvents()
        {
            if (Target == null)
            {
                return;
            }

            StopMain();
            _scene?.ReleaseTarget();
            _previewObject = null; // 引き渡した後はこちらが所有しない
            SetLabel(_previewStatus, "Anim Editor に引き渡し済み");
            AnimEditor.OpenForAnim2D(Target);
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
            _log.Add(line);
            while (_log.Count > MaxLog)
            {
                _log.RemoveAt(0);
            }

            if (_logLabel != null)
            {
                _logLabel.text = string.Join("\n", _log);
            }
        }

        // ── 毎フレーム: 再生状態とウィンドウの絵 ──

        private void OnEditorUpdate()
        {
            if (_scene == null || Target == null)
            {
                return;
            }

            var anim = _scene.Manager;
            var playing = anim.IsPlaying(_handle);
            if (!playing)
            {
                _handle = Handle<AnimMarker>.Invalid; // 終了済み Handle を毎フレーム問い合わせて警告を出さない
            }

            RefreshMirror();
            if (_statusLabel == null)
            {
                return;
            }

            var t = anim.GetNormalizedTime(_handle);
            SetLabel(_statusLabel, playing
                ? $"● 再生中  {t:P0}  (周回 {anim.GetLoopCount(_handle)})"
                : Repeat && WantPlaying ? "↻ リピート待機" : "■ 停止中");
        }

        // ウィンドウの絵: プレビュー物があればその SpriteRenderer の Sprite(再生中に変わる)、無ければ Clip の先頭コマ。
        private void RefreshMirror()
        {
            if (_mirror == null)
            {
                return;
            }

            Sprite sprite = null;
            if (_previewObject != null && _previewObject.TryGetComponent<SpriteRenderer>(out var sr))
            {
                sprite = sr.sprite;
            }

            if (sprite == null && _frames != null && _frames.Length > 0)
            {
                sprite = _frames[0];
            }

            if (_mirror.sprite != sprite)
            {
                _mirror.sprite = sprite;
            }
        }

        // ── リタイミングの適用(Clip に焼き込む) ──

        private void ApplyRetiming()
        {
            var d = Target;
            var clip = d != null ? d.Clip : null;
            if (d == null || clip == null || _frames == null || _frames.Length == 0)
            {
                SetLabel(_retimeStatus, "Clip が未設定、またはスプライトのキーフレームを読み込めませんでした。");
                return;
            }

            var total = clip.length > 0f ? clip.length : _frames.Length / Mathf.Max(1f, clip.frameRate);

            // 潰れたカーブ(既定の Constant(1) 等)では主 Clip も方向 Clip も書き換えない(警告 + no-op)。
            if (!AnimationClipEditorUtility.TryBuildTimes(_frames.Length, _placement, d.Retiming, out var times))
            {
                SetLabel(_retimeStatus, "リタイミングのカーブが単調増加していないため適用しませんでした(Console 参照)。");
                return;
            }

            Undo.RecordObject(clip, "Anim2D Retiming");
            Undo.RecordObject(d, "Anim2D Retiming");
            if (!AnimationClipEditorUtility.RebuildClip(clip, _frames, times, total))
            {
                SetLabel(_retimeStatus, "Clip の再構築に失敗しました(Console 参照)。");
                return;
            }

            // 方向 Clip(4 / 8 方向)にも同じ配置をまとめて適用する。
            var applied = Anim2DRetiming.ApplyToDirectionClips(d, _placement, total, _frames.Length, out var skipped);

            EditorUtility.SetDirty(d);
            // 触った対象(d・主 Clip・方向 Clip)だけを保存する(無関係な実アセットの dirty を巻き込まない。docs/44 P1-1)。
            DDriveAssetSave.SaveDirty(d);
            DDriveAssetSave.SaveDirty(clip);
            if (d.DirectionClips != null)
            {
                foreach (var directionClip in d.DirectionClips)
                {
                    DDriveAssetSave.SaveDirty(directionClip);
                }
            }

            var message = $"{clip.name} のリタイミングを適用しました({_frames.Length} 枚)。方向 Clip: 適用 {applied} / スキップ {skipped}(枚数が違う・Sprite キー無し)。";
            SetLabel(_retimeStatus, message);
            AppendLog(message);
            ReloadFrames();
            AfterDataWrite();
        }
    }
}
