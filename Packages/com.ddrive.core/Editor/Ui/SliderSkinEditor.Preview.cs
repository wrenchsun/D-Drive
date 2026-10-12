using DDrive.Editor.Preview;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ui
{
    // Slider Skin Editor の確認用スライダー(配置・動かしてみる・イベント → SE・雛形の適用)。旧 SliderSkinEditorWindow と同じ挙動。
    internal sealed partial class SliderSkinEditor
    {
        // 「動かしてみる」
        private UnityEngine.UIElements.Slider _moveValue;
        private Label _moveStatus;
        private UiSlider _subscribedSlider;
        private bool _dragSimActive;
        private float _dragFrom;
        private float _dragTo;
        private float _dragT;
        private double _lastTime;
        private double _repaintUntil;
        private string _lastEvent = string.Empty;

        // イベント → SE の対応と目盛り SE の間引きは Slider Editor と共通の SliderSePreview、描き直しは 30fps 上限の ViewRepaintThrottle。
        private readonly SliderSePreview _sePreview = new SliderSePreview();
        private readonly ViewRepaintThrottle _repaint = new ViewRepaintThrottle();

        // ── 動かしてみる ──

        private VisualElement BuildMoveBlock()
        {
            var block = new VisualElement();
            block.AddToClassList("ux-sk-move");

            var title = new Label("動かしてみる");
            title.AddToClassList("ux-sk-move__title");
            block.Add(title);

            var row = new VisualElement();
            row.AddToClassList("ux-toolsline");
            row.Add(MakeButton("◀", null, () => PadMove(MoveDirection.Left), "キーボード / パッドの左 1 回分"));
            row.Add(MakeButton("▶", null, () => PadMove(MoveDirection.Right), "キーボード / パッドの右 1 回分"));
            row.Add(MakeButton("ドラッグ 0 → 1", null, () => StartDragSim(0f, 1f), "マウスで端から端まで掴んで動かす操作を再現(掴む・目盛り・端・離すの SE も鳴る)"));
            row.Add(MakeButton("ドラッグ 1 → 0", null, () => StartDragSim(1f, 0f), "逆向きのドラッグを再現"));
            block.Add(row);

            _moveValue = new UnityEngine.UIElements.Slider("値(0〜1)", 0f, 1f) { showInputField = true };
            _moveValue.AddToClassList("ux-sk-move__value");
            _moveValue.tooltip = "ゲームのコードから値を変えたとき(HP の増減など)と同じ動き。Follow Motion の追従も確認できる";
            _moveValue.RegisterValueChangedCallback(evt => SetPreviewNormalized(evt.newValue));
            block.Add(_moveValue);

            _moveStatus = new Label();
            _moveStatus.AddToClassList("ux-dim");
            _moveStatus.AddToClassList("ux-note");
            block.Add(_moveStatus);

            var note = new Label("掴む・離す・目盛り(Notch)・端(Limit)・拒否(Denied)の SE と、表示が追いつく動き(Follow Motion)をここで確認できます。「入力の許可」を OFF にした部品(HP バー等)でも、ここからは動かせます。");
            note.AddToClassList("ux-dim");
            note.AddToClassList("ux-note");
            block.Add(note);
            return block;
        }

        private void ClearMoveBlock()
        {
            _moveValue = null;
            _moveStatus = null;
        }

        private void PadMove(MoveDirection dir)
        {
            var s = EnsurePreviewSlider();
            if (s == null)
            {
                return;
            }

            s.Move(dir);
            s.MoveRelease(); // 押しっぱなしのリピート扱いにしない
            KeepRepainting();
        }

        private void StartDragSim(float from, float to)
        {
            var s = EnsurePreviewSlider();
            if (s == null)
            {
                return;
            }

            if (_dragSimActive)
            {
                s.EndDrag();
            }

            _dragFrom = from;
            _dragTo = to;
            _dragT = 0f;
            _dragSimActive = true;
            s.BeginDragAt(from);
            KeepRepainting();
        }

        private void SetPreviewNormalized(float normalized)
        {
            var s = EnsurePreviewSlider();
            if (s == null)
            {
                return;
            }

            s.Value = Mathf.Lerp(s.Min, s.Max, normalized);
            KeepRepainting();
        }

        private void KeepRepainting() => _repaintUntil = EditorApplication.timeSinceStartup + 1.5;

        // ── イベント → SE ──

        private void Subscribe(UiSlider slider)
        {
            Unsubscribe();
            if (slider == null)
            {
                return;
            }

            slider.OnDragBegin += OnPreviewDragBegin;
            slider.OnDragEnd += OnPreviewDragEnd;
            slider.OnNotchPassed += OnPreviewNotch;
            slider.OnLimitReached += OnPreviewLimit;
            slider.OnValueChanged += OnPreviewValueChanged;
            slider.OnDenied += OnPreviewDenied;
            _subscribedSlider = slider;
            UpdateMoveStatus();
        }

        private void Unsubscribe()
        {
            if (_subscribedSlider == null)
            {
                return;
            }

            _subscribedSlider.OnDragBegin -= OnPreviewDragBegin;
            _subscribedSlider.OnDragEnd -= OnPreviewDragEnd;
            _subscribedSlider.OnNotchPassed -= OnPreviewNotch;
            _subscribedSlider.OnLimitReached -= OnPreviewLimit;
            _subscribedSlider.OnValueChanged -= OnPreviewValueChanged;
            _subscribedSlider.OnDenied -= OnPreviewDenied;
            _subscribedSlider = null;
        }

        // Editor では Audio が未 Bind のため UiSlider 自身の SE は鳴らない。同じタイミングのイベントで試聴側から鳴らす。
        private void OnPreviewDragBegin()
        {
            PlaySliderSe(SliderSeEvent.Grab);
            Log("掴む");
        }

        private void OnPreviewDragEnd()
        {
            PlaySliderSe(SliderSeEvent.Release);
            Log("離す");
        }

        private void OnPreviewNotch(int index)
        {
            PlaySliderSe(SliderSeEvent.Notch);
            Log($"目盛り {index}");
        }

        private void OnPreviewLimit(bool isMax)
        {
            PlaySliderSe(SliderSeEvent.Limit);
            Log(isMax ? "端(Max)" : "端(Min)");
        }

        private void OnPreviewDenied()
        {
            PlaySliderSe(SliderSeEvent.Denied);
            Log("拒否(Disabled / Locked)");
        }

        // 鳴らすのは設定欄の SE 行と同じ経路(試聴の状態表示・■ 停止と揃える)。目盛り SE の間引きは SliderSePreview。
        private void PlaySliderSe(SliderSeEvent e)
        {
            if (_sePreview.ShouldPlay(Target, e))
            {
                _check?.PlaySeField(SliderSePreview.PropertyName(e));
            }
        }

        private void OnPreviewValueChanged(float value)
        {
            if (_moveValue != null && _previewSlider != null)
            {
                _moveValue.SetValueWithoutNotify(_previewSlider.NormalizedValue);
            }

            KeepRepainting();
            UpdateMoveStatus();
        }

        private void Log(string text)
        {
            _lastEvent = text;
            UpdateMoveStatus();
        }

        private void UpdateMoveStatus()
        {
            if (_moveStatus == null)
            {
                return;
            }

            _moveStatus.text = _previewSlider == null
                ? "(未配置。操作すると自動で確認用シーンに配置します)"
                : $"値 {_previewSlider.Value:0.###}(範囲 {_previewSlider.Min:0.##}〜{_previewSlider.Max:0.##})  {_lastEvent}";
        }

        // ── 毎フレームの駆動(ExecuteAlways を付けない方針のため手動で Advance) ──

        private void OnEditorUpdate()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = Mathf.Min((float)(now - _lastTime), 0.1f);
            _lastTime = now;
            if (dt <= 0f || _previewSlider == null)
            {
                return;
            }

            if (_dragSimActive)
            {
                _dragT += dt / DragSimSeconds;
                _previewSlider.DragTo(Mathf.Lerp(_dragFrom, _dragTo, Mathf.Clamp01(_dragT)));
                if (_dragT >= 1f)
                {
                    _previewSlider.EndDrag();
                    _dragSimActive = false;
                }

                KeepRepainting();
            }

            // FollowMotion / DelayFill の追従は Advance が進める。
            _previewSlider.Advance(dt);

            // Edit Mode の Game ビューは自動では再描画されないため、動いている間だけ描き直す(30fps 上限)。
            if (now < _repaintUntil)
            {
                _repaint.Request();
            }
        }

        // ── 配置・撤去・雛形 ──

        private UiInteractable EnsurePreview() => EnsurePreviewSlider();

        private UiSlider EnsurePreviewSlider()
        {
            if (_previewSlider == null)
            {
                // ▶ 等からの暗黙の配置ではシーンを勝手に切り替えない(今開いているシーンに置く)。
                PlaceInScene(PreviewPlaceMode.CurrentScene);
            }

            return _previewSlider;
        }

        // Data 自体は編集しない。実配置での見た目確認だけをシーン上で行う(ADR-4)。
        // 左クリック = UI 確認用シーンを開いてから配置 / 右クリック = このシーンに配置・本配置(PreviewPlacementButton)。
        private void PlaceInScene(PreviewPlaceMode mode)
        {
            if (Target == null)
            {
                return;
            }

            RemoveFromScene();
            if (!PreviewPlacement.PrepareScene(mode, CanvasPreviewSceneSetup.TryOpenOrCreate))
            {
                return;
            }

            // Slider Editor と共通の PreviewSliderFactory(子も DontSave)。
            var canvasGo = EditorPreviewRoots.CreateOverlayCanvas(PreviewCanvasName);
            _previewSlider = PreviewSliderFactory.Create(canvasGo.transform, "PreviewSlider", Vector2.zero);
            _previewSlider.SetVisual(Target);
            EditorPreviewRoots.MarkDontSaveRecursive(canvasGo);

            if (PreviewPlacement.IsPersistent(mode))
            {
                // Canvas ごと本配置する(スライダー単体を外すと描画できないため)。以後この窓の所有物ではない。
                PreviewPlacement.Persist(canvasGo, Target.DisplayName ?? Target.name);
                _previewSlider = null;
                return;
            }

            Subscribe(_previewSlider);
            Selection.activeGameObject = _previewSlider.gameObject;
            PreviewPlacement.Focus(_previewSlider.gameObject);
        }

        private void RemoveFromScene()
        {
            // 遷移の自動再生・演出・SE も止める(止めないと次の段がプレビューを置き直してしまう)。
            _check?.StopAll();
            Unsubscribe();
            _dragSimActive = false;
            EditorPreviewRoots.DestroyAll(PreviewCanvasName);
            _previewSlider = null;
            UpdateMoveStatus();
        }

        // 実体は共有の SliderPresets(Slider Editor と共通)。未配置なら配置してから当てる。Undo は 1 回(SliderPresets が RecordObject)。
        private void ApplyPreset(SliderPresets.SliderPreset preset)
        {
            if (preset == SliderPresets.SliderPreset.None)
            {
                return;
            }

            var slider = EnsurePreviewSlider();
            if (slider == null)
            {
                return;
            }

            SliderPresets.Apply(slider, preset);
            UpdateMoveStatus();
        }
    }
}
