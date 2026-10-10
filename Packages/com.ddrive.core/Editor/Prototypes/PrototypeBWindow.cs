using System;
using DDrive.Editor.Vfx;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.EditorPrototypes
{
    // B. ステップ型(選ぶ → 見る → 整える → 登録) + 雛形から始める(docs/1008 §3-B)。
    internal sealed class PrototypeBWindow : PrototypeWindowBase
    {
        private static readonly string[] StepNames = { "選ぶ", "見る", "整える", "登録" };

        [SerializeField] private int _step;

        private Label _progressLabel;
        private ProgressBar _progressBar;
        private VisualElement _page;
        private Button _back;
        private Button _next;
        private Label _templateNote;

        protected override string Aim =>
            "B ステップ型: 1 ページに 1〜3 欄だけ。雛形 → Prefab → ▶ → 位置と長さ → 登録の順に進めば完成する。新規作成のとき専用。";

        public static void Open() => OpenWindow<PrototypeBWindow>("B ステップ型");

        protected override void BuildBody(VisualElement body)
        {
            _step = Mathf.Clamp(_step, 0, StepNames.Length - 1);

            _progressLabel = new Label { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 4 } };
            body.Add(_progressLabel);
            _progressBar = new ProgressBar { lowValue = 0, highValue = 100 };
            body.Add(_progressBar);

            _page = new VisualElement { style = { marginTop = 6, marginBottom = 6 } };
            body.Add(_page);

            var nav = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _back = new Button(() => GoTo(_step - 1)) { text = "← 戻る" };
            _next = new Button(() => GoTo(_step + 1)) { text = "次へ →" };
            foreach (var b in new[] { _back, _next })
            {
                b.style.flexGrow = 1;
                b.style.flexShrink = 1;
                b.style.minWidth = 0;
                nav.Add(b);
            }

            body.Add(nav);
            ShowPage();
        }

        protected override void OnDataEdited() => UpdateNav();

        private void GoTo(int step)
        {
            _step = Mathf.Clamp(step, 0, StepNames.Length - 1);
            ShowPage();
        }

        private void UpdateNav()
        {
            if (_back == null || _next == null)
            {
                return;
            }

            _back.SetEnabled(_step > 0);
            var last = _step == StepNames.Length - 1;
            _next.style.display = last ? DisplayStyle.None : DisplayStyle.Flex;
            // ステップ 1 は Prefab が無いと先へ進めない(必須欄を最初に埋めさせる)。
            _next.SetEnabled(_step != 0 || (Target != null && Target.Prefab != null));
            _next.tooltip = _next.enabledSelf ? string.Empty : "Prefab を設定すると次へ進めます";
        }

        private void ShowPage()
        {
            _page.Clear();
            _progressLabel.text = $"ステップ {_step + 1}/{StepNames.Length}: {StepNames[_step]}";
            _progressBar.value = (_step + 1) * 100f / StepNames.Length;
            _progressBar.title = string.Empty;

            switch (_step)
            {
                case 0:
                    BuildSelectPage(_page);
                    break;
                case 1:
                    BuildWatchPage(_page);
                    break;
                case 2:
                    BuildTunePage(_page);
                    break;
                default:
                    BuildRegisterPage(_page);
                    break;
            }

            UpdateNav();
            RefreshStates();
        }

        // ── 1. 選ぶ ──

        private void BuildSelectPage(VisualElement page)
        {
            page.Add(new Label("どんなエフェクト?(雛形を選ぶと設定がまとめて入ります)") { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 2 } });

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            AddTemplate(row, "ヒット一発", "OneShot・1 秒・ワールド", t =>
            {
                t.LifeMode = VfxLifeMode.OneShot;
                t.Duration = 1f;
                t.Render = VfxRenderMode.World3D;
            });
            AddTemplate(row, "常駐ループ", "Loop・フェードアウト 0.3 秒", t =>
            {
                t.LifeMode = VfxLifeMode.Loop;
                t.FadeOutSec = 0.3f;
            });
            AddTemplate(row, "UI の上に出す", "描画 = UI の上", t => t.Render = VfxRenderMode.UIOverlay);
            AddTemplate(row, "空から", "何も変えない", null);
            page.Add(row);

            _templateNote = new Label(string.Empty) { style = { opacity = 0.7f, marginTop = 2, marginBottom = 6, whiteSpace = WhiteSpace.Normal } };
            page.Add(_templateNote);

            page.Add(MakeField("Prefab", true, true));
            if (Target.Prefab == null)
            {
                page.Add(new HelpBox("Prefab を設定すると「次へ」が押せます。", HelpBoxMessageType.Info));
            }
        }

        private void AddTemplate(VisualElement row, string name, string desc, Action<VfxData> apply)
        {
            var b = new Button(() =>
            {
                if (apply != null)
                {
                    Undo.RecordObject(Target, "雛形を適用");
                    apply(Target);
                    NotifyDataWritten();
                }

                _templateNote.text = apply != null ? $"「{name}」を適用しました({desc})。" : "雛形は使わず、現在の設定のまま進みます。";
            }) { text = name, tooltip = desc };
            b.style.flexGrow = 1;
            b.style.flexShrink = 1;
            b.style.minWidth = 0;
            b.style.whiteSpace = WhiteSpace.Normal;
            row.Add(b);
        }

        // ── 2. 見る ──

        private void BuildWatchPage(VisualElement page)
        {
            page.Add(new Label("▶ を押して確認用シーンで見てみましょう。良ければ「次へ」。") { style = { whiteSpace = WhiteSpace.Normal } });
            if (Target.Prefab == null)
            {
                page.Add(new HelpBox("Prefab が未設定です。ステップ 1 に戻って設定してください。", HelpBoxMessageType.Warning));
            }

            page.Add(BuildPlayRow());
            var open = DDrive.Editor.Preview.PreviewPlacementButton.Create(
                "確認用シーンを開いて再生", "ライト / カメラ / 床を備えた確認用シーンで再生する", OpenPreviewScene);
            page.Add(open);
        }

        // ── 3. 整える ──

        private void BuildTunePage(VisualElement page)
        {
            page.Add(new Label("出る位置(パッドをドラッグ)と高さ、長さだけ整えます。") { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 4 } });

            var pad = new OffsetPad(
                () => new Vector2(Target.Anchor.LocalOffset.x, Target.Anchor.LocalOffset.z),
                v =>
                {
                    Undo.RecordObject(Target, "出る位置を変更");
                    var a = Target.Anchor;
                    a.LocalOffset.x = v.x;
                    a.LocalOffset.z = v.y;
                    Target.Anchor = a;
                    EditorUtility.SetDirty(Target);
                    So.Update();
                    ReapplyAnchor();
                    RefreshStates();
                });
            page.Add(pad);

            var height = new Slider("高さ(Y)", -1f, 5f) { showInputField = true };
            height.BindProperty(So.FindProperty("Anchor.LocalOffset.y"));
            page.Add(height);

            if (Target.Anchor.Space != DDrive.Foundation.Data.AnchorSpace.World)
            {
                page.Add(new HelpBox($"Anchor の基準が {Target.Anchor.Space} です。位置は基準からの相対になります。", HelpBoxMessageType.Info));
            }

            page.Add(MakeField("LifeMode", true, true));
            page.Add(MakeField("Duration", true, true));
            page.Add(BuildPlayRow());
        }

        // ── 4. 登録 ──

        private void BuildRegisterPage(VisualElement page)
        {
            page.Add(Validation);
            page.Add(MakeField("DisplayName", true, true));
            page.Add(MakeField("Category", true, true));

            var save = new Button(() =>
            {
                EditorUtility.SetDirty(Target);
                DDrive.Editor.Versioning.DDriveAssetSave.SaveAllSuppressed();
                Debug.Log($"[Prototype B] 保存しました: {Target.name}");
            }) { text = "保存" };
            page.Add(save);

            var all = new Button(() => VfxEditorWindow.Open(Target)) { text = "すべての設定を見る(VFX Editor)", tooltip = "標準エディターで全欄を開く" };
            all.style.whiteSpace = WhiteSpace.Normal;
            page.Add(all);
        }

        // 2D パッド: 横 = X、縦 = Z(上が +Z)。±Range メートル。
        private sealed class OffsetPad : VisualElement
        {
            private const float Range = 3f;
            private readonly Func<Vector2> _get;
            private readonly Action<Vector2> _set;

            public OffsetPad(Func<Vector2> get, Action<Vector2> set)
            {
                _get = get;
                _set = set;
                style.height = 170;
                style.minWidth = 0;
                style.flexShrink = 1;
                style.marginBottom = 4;
                style.backgroundColor = new Color(0f, 0f, 0f, 0.25f);
                generateVisualContent += Draw;
                RegisterCallback<PointerDownEvent>(e =>
                {
                    this.CapturePointer(e.pointerId);
                    Apply(e.localPosition);
                });
                RegisterCallback<PointerMoveEvent>(e =>
                {
                    if (this.HasPointerCapture(e.pointerId))
                    {
                        Apply(e.localPosition);
                    }
                });
                RegisterCallback<PointerUpEvent>(e => this.ReleasePointer(e.pointerId));
                tooltip = "ドラッグで出る位置(X / Z)を変える。中心 = 原点、端 = ±3m";
            }

            private void Apply(Vector2 local)
            {
                var r = contentRect;
                if (r.width <= 0 || r.height <= 0)
                {
                    return;
                }

                var x = Mathf.Clamp((local.x / r.width - 0.5f) * 2f * Range, -Range, Range);
                var z = Mathf.Clamp((0.5f - local.y / r.height) * 2f * Range, -Range, Range);
                _set(new Vector2(Mathf.Round(x * 100f) / 100f, Mathf.Round(z * 100f) / 100f));
                MarkDirtyRepaint();
            }

            private void Draw(MeshGenerationContext mgc)
            {
                var r = contentRect;
                var p = mgc.painter2D;
                p.lineWidth = 1f;
                p.strokeColor = new Color(1f, 1f, 1f, 0.25f);
                p.BeginPath();
                p.MoveTo(new Vector2(r.width * 0.5f, 0));
                p.LineTo(new Vector2(r.width * 0.5f, r.height));
                p.MoveTo(new Vector2(0, r.height * 0.5f));
                p.LineTo(new Vector2(r.width, r.height * 0.5f));
                p.Stroke();

                var v = _get();
                var pos = new Vector2(
                    (v.x / (2f * Range) + 0.5f) * r.width,
                    (0.5f - v.y / (2f * Range)) * r.height);
                p.fillColor = new Color(1f, 0.65f, 0.15f);
                p.BeginPath();
                p.Arc(pos, 6f, 0f, 360f);
                p.Fill();
            }
        }
    }
}
