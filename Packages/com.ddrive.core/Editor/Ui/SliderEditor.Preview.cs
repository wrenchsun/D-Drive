using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Foundation.Values;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ui
{
    // Slider Editor の確認用スライダー(配置・実操作・ドラッグ模擬・追従比較・全状態の並べ表示・SceneView のノッチ帯)。旧 SliderEditorWindow と同じ挙動。
    internal sealed partial class SliderEditor
    {
        // ── 実操作 ──

        private void MovePad(MoveDirection dir)
        {
            if (_target == null)
            {
                return;
            }

            // Move は UiSlider だけでなく子(Handle / Fill)の RectTransform も書き換えるため、実物は階層ごと Undo に積む
            // (UiSlider だけ RecordObject すると、Undo で値だけが戻り見た目が戻らない)。確認用シーンのサンプル(DontSave)は保存されないので Undo に積まない。
            var isPreview = IsPreviewObject(_target);
            if (!isPreview)
            {
                Undo.RegisterFullObjectHierarchyUndo(_target.gameObject, "UiSlider: パッド操作");
            }

            _target.Move(dir, _fineAdjust);
            if (!isPreview)
            {
                EditorUtility.SetDirty(_target);
            }
        }

        private void StartDragSimulate(bool includeCompare)
        {
            if (_target == null)
            {
                return;
            }

            // ドラッグ模擬は 20 フレームかけて値と見た目を書き換える。シーン上の実物には Undo 無しで掛かってしまうため、確認用シーンのサンプルだけに限る。
            if (!IsPreviewObject(_target))
            {
                Say("ドラッグ模擬は「サンプルを配置」したスライダーでのみ動きます(シーン上の実物は書き換えません)");
                AppendLog("ドラッグ模擬: 実物のため実行しません");
                return;
            }

            _dragSimActive = true;
            _dragSimStep = 0;
            _dragSimIncludesCompare = includeCompare && _compareSlider != null;
            _target.BeginDragAt(0f);
            if (_dragSimIncludesCompare)
            {
                _compareSlider.BeginDragAt(0f);
            }

            AppendLog("ドラッグ模擬開始");
        }

        private void TickDragSimulate()
        {
            if (!_dragSimActive || _target == null)
            {
                return;
            }

            _dragSimStep++;
            var t = Mathf.Clamp01(_dragSimStep / (float)DragSimSteps);
            _target.DragTo(t);
            if (_dragSimIncludesCompare && _compareSlider != null)
            {
                _compareSlider.DragTo(t);
            }

            if (_dragSimStep >= DragSimSteps)
            {
                _target.EndDrag();
                if (_dragSimIncludesCompare && _compareSlider != null)
                {
                    _compareSlider.EndDrag();
                }

                _dragSimActive = false;
                AppendLog("ドラッグ模擬完了");
            }
        }

        // ── 追従比較 ──

        private void SpawnCompareSlider()
        {
            if (_target == null)
            {
                Say("先に対象 UiSlider を用意してください");
                return;
            }

            if (_compareSlider != null)
            {
                Object.DestroyImmediate(_compareSlider.gameObject);
            }

            var canvas = EnsurePreviewCanvas();
            var slider = PreviewSliderFactory.Create(canvas.transform, "CompareSlider", new Vector2(320f, 0f));
            slider.Response = _target.Response;
            slider.FollowMotion = new ValueDef { Mode = ValueMode.Parametric, Parametric = EaseDef.Named(DDrive.Foundation.Easing.Ease.OutBack), Time = TimeDef.Duration(0.4f), Loop = LoopMode.Once };
            slider.SetRange(_target.Min, _target.Max);
            EditorPreviewRoots.MarkDontSaveRecursive(canvas);
            _compareSlider = slider;

            RebuildCompareSection();
            AppendLog("比較用 2 体目を配置しました");
        }

        private void RebuildCompareSection()
        {
            if (_compareHost == null)
            {
                return;
            }

            _compareHost.Clear();
            if (_compareSlider == null)
            {
                _compareHost.Add(Note("2 体目は未配置です"));
                return;
            }

            var so = new SerializedObject(_compareSlider);
            var field = new PropertyField(so.FindProperty(nameof(UiSlider.FollowMotion)), "2 体目の FollowMotion");
            field.AddToClassList("ux-ut-field");
            _compareHost.Add(field);
            _compareHost.Bind(so);
        }

        // ── Skin プレビュー ──

        private void SpawnSkinStatesPreview()
        {
            if (_target == null)
            {
                Say("先に対象 UiSlider を用意してください");
                return;
            }

            foreach (var s in _skinPreviewSliders)
            {
                if (s != null)
                {
                    Object.DestroyImmediate(s.gameObject);
                }
            }

            _skinPreviewSliders.Clear();

            var canvas = EnsurePreviewCanvas();
            var skin = GetTargetSkin();
            var states = new[] { ControlState.Normal, ControlState.Hover, ControlState.Pressed, ControlState.Selected, ControlState.Disabled, ControlState.Locked };
            for (var i = 0; i < states.Length; i++)
            {
                var slider = PreviewSliderFactory.Create(canvas.transform, $"SkinPreview_{states[i]}", new Vector2(i * 340f, -80f));
                if (skin != null)
                {
                    slider.SetVisual(skin);
                }

                slider.ForceStateForPreview(states[i]);
                _skinPreviewSliders.Add(slider);
            }

            EditorPreviewRoots.MarkDontSaveRecursive(canvas);
            AppendLog("6 状態を並べました");
        }

        // ── 確認用シーンへの配置 ──

        // 生の new GameObject + GameObject.Find ではなく、他のエディタと同じ EditorPreviewRoots を使う
        // (Find は非アクティブも見つかり、同名の本物のオブジェクト(DontSave でない)は拾わない)。
        private GameObject EnsurePreviewCanvas()
        {
            var existing = EditorPreviewRoots.Find(PreviewRootName);
            return existing != null ? existing : EditorPreviewRoots.CreateOverlayCanvas(PreviewRootName);
        }

        // 組み立ては Slider Skin Editor と共通の PreviewSliderFactory(子も DontSave)。
        // 左クリック = UI 確認用シーンを開いてから配置 / 右クリック = このシーンに配置・本配置。
        private void PlaceSampleInScene(SliderSkinData skin, PreviewPlaceMode mode)
        {
            if (!PreviewPlacement.PrepareScene(mode, CanvasPreviewSceneSetup.TryOpenOrCreate))
            {
                return;
            }

            if (PreviewPlacement.IsPersistent(mode))
            {
                // 本配置は共有のプレビュー Canvas(比較用スライダー等が同居する)ではなく、専用の Canvas を作って渡す。
                var ownCanvas = EditorPreviewRoots.CreateOverlayCanvas(PreviewRootName);
                var ownSlider = PreviewSliderFactory.Create(ownCanvas.transform, "Slider", Vector2.zero);
                if (skin != null)
                {
                    ownSlider.SetVisual(skin);
                }

                EditorPreviewRoots.MarkDontSaveRecursive(ownCanvas);
                PreviewPlacement.Persist(ownCanvas, skin != null ? skin.DisplayName ?? skin.name : null);
                return;
            }

            var canvas = EnsurePreviewCanvas();
            var slider = PreviewSliderFactory.Create(canvas.transform, "SampleSlider", Vector2.zero);
            if (skin != null)
            {
                slider.SetVisual(skin);
            }

            EditorPreviewRoots.MarkDontSaveRecursive(canvas);
            SetTarget(slider);
            _explicitSkin = skin;
            Selection.activeGameObject = slider.gameObject;
            PreviewPlacement.Focus(slider.gameObject);
        }

        private void RemoveFromScene()
        {
            UnsubscribeEvents();
            _dragSimActive = false;
            EditorPreviewRoots.DestroyAll(PreviewRootName);

            _target = null;
            _explicitSkin = null;
            _compareSlider = null;
            _skinPreviewSliders.Clear();
            if (_root != null)
            {
                _targetField?.SetValueWithoutNotify(null);
                RebuildCompareSection();
                RefreshAll();
            }
        }

        // ── EditorApplication.update: Advance(dt) 駆動(ADR-4。ExecuteAlways を付けない方針のため手動で進める) ──

        private void OnEditorUpdate()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = (float)(now - _lastEditorTime);
            _lastEditorTime = now;
            if (dt <= 0f || dt > 1f)
            {
                return;
            }

            TickDragSimulate();

            // 「対象 UiSlider」は ObjectField(allowSceneObjects)/「選択から取得」でシーン上の実物(ユーザーの配置物)を直接指せてしまう。
            // Advance() は anchoredPosition 等を Undo 無しで毎フレーム書き換えるため、実物を選ぶと気づかないうちに動かし続けてしまう。
            // DontSave のプレビュー配下(「サンプルを配置」で生成した実体)のときだけ駆動し、それ以外は駆動しない。
            if (IsPreviewObject(_target))
            {
                _target.Advance(dt);
            }
            else if (_target != null && _statusLabel != null)
            {
                _statusLabel.text = "シーン上の実物は駆動しません(「サンプルを配置」したサンプルを使ってください。値・刻み・雛形は実物にも当てられます)";
            }

            _compareSlider?.Advance(dt);
            foreach (var s in _skinPreviewSliders)
            {
                s?.Advance(dt);
            }
        }

        // _target がこの窓の「サンプルを配置」(PreviewRootName 配下、DontSave)で生成した実体かどうか。ユーザーがシーン上の実物を指定した場合は false。
        private static bool IsPreviewObject(UiSlider target)
        {
            if (target == null)
            {
                return false;
            }

            // 名前に加えて「[D-Drive] で始まる DontSave のルート」であることも見る(同じ名前を付けた本物のオブジェクトを駆動しない)。
            var root = target.transform.root;
            return root != null && root.name == PreviewRootName && EditorPreviewSweeper.IsPreviewRoot(root.gameObject);
        }

        // ── SceneView のノッチ帯 ──

        private void OnSceneGui(SceneView sceneView)
        {
            if (_target == null || _target.TrackRect == null || !SceneGuiOwner.IsOwner(this) || _target.Notches <= 0)
            {
                return;
            }

            var rt = _target.TrackRect;
            rt.GetWorldCorners(_corners);
            // corners[0]=左下, corners[2]=右上(uGUI の GetWorldCorners 順)。
            var x0 = HandleUtility.WorldToGUIPointWithDepth(_corners[0]).x;
            var x1 = HandleUtility.WorldToGUIPointWithDepth(_corners[2]).x;
            var y0 = HandleUtility.WorldToGUIPointWithDepth(_corners[2]).y;
            var y1 = HandleUtility.WorldToGUIPointWithDepth(_corners[0]).y;

            Handles.BeginGUI();
            DrawNotchOverlay(Mathf.Min(x0, x1), Mathf.Max(x0, x1), Mathf.Min(y0, y1), Mathf.Max(y0, y1));
            Handles.EndGUI();
        }

        // ノッチ位置 + SnapThreshold の帯を [x0,x1] x [y0,y1] の矩形へ描く(SceneView 用)。
        private void DrawNotchOverlay(float x0, float x1, float y0, float y1)
        {
            var threshold = _target.SnapThreshold;
            var mid = (y0 + y1) * 0.5f;
            var positions = SliderEditorMath.NotchPositions(_target.Notches);
            var prev = Handles.color;

            foreach (var frac in positions)
            {
                var x = Mathf.Lerp(x0, x1, frac);
                var bandHalf = threshold * (x1 - x0);

                Handles.color = new Color(1f, 0.85f, 0.2f, 0.25f);
                Handles.DrawAAConvexPolygon(
                    new Vector3(x - bandHalf, y0, 0f), new Vector3(x + bandHalf, y0, 0f),
                    new Vector3(x + bandHalf, y1, 0f), new Vector3(x - bandHalf, y1, 0f));

                Handles.color = Color.white;
                Handles.DrawAAPolyLine(2f, new Vector3(x, y0, 0f), new Vector3(x, y1, 0f));
            }

            var knobX = Mathf.Lerp(x0, x1, _target.NormalizedValue);
            Handles.color = Color.cyan;
            Handles.DrawSolidDisc(new Vector3(knobX, mid, 0f), Vector3.forward, 4f);
            Handles.color = prev;
        }
    }

    // 応答曲線の静的な図(Painter2D)。横 = つまみの位置、縦 = 値(正規化)、点 = 現在の値。値が変わったときだけ描き直す(Sync)。
    internal sealed class SliderResponseGraph : VisualElement
    {
        private readonly System.Func<UiSlider> _target;
        private readonly int _samples;
        private readonly UxPalette _pal;
        private float[] _curve = new float[0];
        private float _normalized;
        private float _knob;
        private int _key = int.MinValue;

        public SliderResponseGraph(System.Func<UiSlider> target, int samples)
        {
            _target = target;
            _samples = samples;
            AddToClassList("ux-sl-graph");
            _pal = UxPalette.Attach(this);
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(e => MarkDirtyRepaint());
            tooltip = "応答曲線: 横 = つまみの位置、縦 = 値。対角線(薄い線)は線形の目安";
        }

        // 描いている曲線(0..1 の値 × サンプル数)。テスト用。
        public float[] Curve => _curve;

        public float Normalized => _normalized;

        public void Sync(bool force = false)
        {
            var slider = _target();
            var key = Signature(slider);
            if (!force && key == _key)
            {
                return;
            }

            _key = key;
            _curve = slider != null ? SliderEditorMath.SampleResponse(slider, _samples) : new float[0];
            _normalized = slider != null ? slider.NormalizedValue : 0f;
            _knob = slider != null ? slider.InverseResponse(_normalized) : 0f;
            MarkDirtyRepaint();
        }

        private static int Signature(UiSlider s)
        {
            if (s == null)
            {
                return 0;
            }

            unchecked
            {
                var h = 17;
                h = h * 31 + s.Response.Mode.GetHashCode();
                h = h * 31 + s.Response.From.GetHashCode();
                h = h * 31 + s.Response.To.GetHashCode();
                h = h * 31 + s.Response.Constant.GetHashCode();
                h = h * 31 + s.Response.Parametric.Ease.GetHashCode();
                h = h * 31 + s.Response.Parametric.Kind.GetHashCode();
                h = h * 31 + s.Response.Parametric.BezierP1.GetHashCode();
                h = h * 31 + s.Response.Parametric.BezierP2.GetHashCode();
                h = h * 31 + (s.Response.Curve != null ? s.Response.Curve.length * 7 + 1 : 0);
                h = h * 31 + s.NormalizedValue.GetHashCode();
                h = h * 31 + s.Min.GetHashCode();
                h = h * 31 + s.Max.GetHashCode();
                return h;
            }
        }

        private void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width < 16f || r.height < 16f)
            {
                return;
            }

            _pal.Read();
            var p = mgc.painter2D;
            UxPalette.RoundRect(p, 0f, 0f, r.width, r.height, 8f);
            p.fillColor = _pal.Track;
            p.Fill();

            const float pad = 10f;
            var w = r.width - pad * 2f;
            var h = r.height - pad * 2f;

            // 対角線(線形の目安)
            p.strokeColor = _pal.LineStrong;
            p.lineWidth = 1f;
            p.BeginPath();
            p.MoveTo(new Vector2(pad, pad + h));
            p.LineTo(new Vector2(pad + w, pad));
            p.Stroke();

            if (_curve.Length < 2)
            {
                return;
            }

            p.strokeColor = _pal.Fill;
            p.lineWidth = 2.5f;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            for (var i = 0; i < _curve.Length; i++)
            {
                var x = pad + (i / (float)(_curve.Length - 1)) * w;
                var y = pad + h - Mathf.Clamp01(_curve[i]) * h;
                if (i == 0)
                {
                    p.MoveTo(new Vector2(x, y));
                }
                else
                {
                    p.LineTo(new Vector2(x, y));
                }
            }

            p.Stroke();

            // 現在の測定点(つまみ位置 = InverseResponse(現在の正規化値))
            var dot = new Vector2(pad + Mathf.Clamp01(_knob) * w, pad + h - Mathf.Clamp01(_normalized) * h);
            p.fillColor = _pal.Halo;
            p.BeginPath();
            p.Arc(dot, 9f, 0f, 360f);
            p.Fill();
            p.fillColor = _pal.Dot;
            p.BeginPath();
            p.Arc(dot, 4.5f, 0f, 360f);
            p.Fill();
        }
    }

    // ノッチ(目盛り)の位置と吸着の幅の静的な図(Painter2D)。Notches = 0 のときは「連続値」の注記だけ。
    internal sealed class SliderNotchBar : VisualElement
    {
        private readonly System.Func<UiSlider> _target;
        private readonly UxPalette _pal;
        private readonly Label _label;
        private int _notches;
        private float _threshold;
        private float _normalized;
        private int _key = int.MinValue;

        public SliderNotchBar(System.Func<UiSlider> target)
        {
            _target = target;
            AddToClassList("ux-sl-notch");
            _pal = UxPalette.Attach(this);
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(e => MarkDirtyRepaint());
            _label = new Label("Notches = 0(連続値)") { pickingMode = PickingMode.Ignore };
            _label.AddToClassList("ux-dim");
            _label.AddToClassList("ux-sl-notch__label");
            Add(_label);
            tooltip = "目盛りの位置(白い線)と、吸着が効く範囲(黄色の帯)。水色の点が現在の値";
        }

        public int Notches => _notches;

        public void Sync(bool force = false)
        {
            var s = _target();
            var key = s == null ? 0 : (s.Notches * 397) ^ s.SnapThreshold.GetHashCode() ^ (s.NormalizedValue.GetHashCode() * 31);
            if (!force && key == _key)
            {
                return;
            }

            _key = key;
            _notches = s != null ? s.Notches : 0;
            _threshold = s != null ? s.SnapThreshold : 0f;
            _normalized = s != null ? s.NormalizedValue : 0f;
            _label.text = s == null ? "対象がありません" : "Notches = 0(連続値)";
            _label.Show(s == null || s.Notches <= 0);
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width < 16f || r.height < 8f)
            {
                return;
            }

            _pal.Read();
            var p = mgc.painter2D;
            UxPalette.RoundRect(p, 0f, 0f, r.width, r.height, 8f);
            p.fillColor = _pal.Track;
            p.Fill();
            if (_notches <= 0)
            {
                return;
            }

            const float pad = 10f;
            var w = r.width - pad * 2f;
            var positions = SliderEditorMath.NotchPositions(_notches);
            foreach (var frac in positions)
            {
                var x = pad + frac * w;
                var half = _threshold * w;
                p.fillColor = _pal.Halo;
                p.BeginPath();
                p.MoveTo(new Vector2(x - half, 6f));
                p.LineTo(new Vector2(x + half, 6f));
                p.LineTo(new Vector2(x + half, r.height - 6f));
                p.LineTo(new Vector2(x - half, r.height - 6f));
                p.ClosePath();
                p.Fill();

                p.strokeColor = _pal.Handle;
                p.lineWidth = 2f;
                p.BeginPath();
                p.MoveTo(new Vector2(x, 6f));
                p.LineTo(new Vector2(x, r.height - 6f));
                p.Stroke();
            }

            var knob = new Vector2(pad + Mathf.Clamp01(_normalized) * w, r.height * 0.5f);
            p.fillColor = _pal.Fill;
            p.BeginPath();
            p.Arc(knob, 4.5f, 0f, 360f);
            p.Fill();
        }
    }
}
