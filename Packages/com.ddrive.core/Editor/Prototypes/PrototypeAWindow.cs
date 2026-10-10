using System.Collections.Generic;
using DDrive.Editor.Validation;
using DDrive.Foundation.Validation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.EditorPrototypes
{
    // A. 段階表示(かんたん / 標準 / 詳細) + 「次にやること」(docs/1008 §3-A)。
    internal sealed class PrototypeAWindow : PrototypeWindowBase
    {
        private const string ModePrefKey = "DDrive.EditorPrototypes.A.Mode";
        private static readonly string[] ModeNames = { "かんたん", "標準", "詳細" };

        private sealed class SectionView
        {
            public string Name;
            public Foldout Foldout;
            public List<GuidedField> Fields = new List<GuidedField>();
            public Button More;
            public VisualElement ReadOnlyInfo;
        }

        private int _mode;
        private readonly List<SectionView> _sections = new List<SectionView>();
        private readonly List<Button> _modeButtons = new List<Button>();
        private readonly List<Label> _stepLabels = new List<Label>();
        private Label _nextHeadline;
        private Label _errorLabel;
        private int _errorCount;
        private string _firstError;

        protected override string Aim =>
            "A 段階表示: 「何を触ればよいか」をモードが答える。かんたん = Prefab と ▶ だけ / 標準 = よく使う欄まで / 詳細 = 全欄。";

        public static void Open() => OpenWindow<PrototypeAWindow>("A 段階表示");

        protected override void BuildBody(VisualElement body)
        {
            _sections.Clear();
            _modeButtons.Clear();
            _stepLabels.Clear();
            _mode = Mathf.Clamp(EditorPrefs.GetInt(ModePrefKey, 0), 0, 2);

            body.Add(BuildModeRow());
            body.Add(BuildNextCard());
            body.Add(BuildPlayRow());

            foreach (var sec in Guide.Sections)
            {
                var view = new SectionView { Name = sec, Foldout = new Foldout { text = sec, value = true } };
                foreach (var entry in Guide.Entries)
                {
                    if (entry.Section != sec)
                    {
                        continue;
                    }

                    var f = MakeField(entry.Field, true, true);
                    view.Fields.Add(f);
                    view.Foldout.Add(f);
                }

                if (sec == VfxFieldGuide.SecAdmin)
                {
                    view.ReadOnlyInfo = FieldGuideUi.MakeReadOnlyInfo(Target);
                    view.Foldout.Add(view.ReadOnlyInfo);
                }

                var captured = view;
                view.More = new Button { text = string.Empty };
                view.More.clicked += () => SetMode(RevealingMode(captured));
                view.More.style.alignSelf = Align.FlexStart;
                view.More.style.marginBottom = 4;
                view.Foldout.Add(view.More);
                _sections.Add(view);
                body.Add(view.Foldout);
            }

            body.Add(Validation);
            ApplyMode();
            RecomputeErrors();
            UpdateNext();
        }

        private VisualElement BuildModeRow()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4, marginBottom = 4 } };
            row.Add(new Label("表示モード") { style = { alignSelf = Align.Center, marginRight = 6 } });
            for (var i = 0; i < ModeNames.Length; i++)
            {
                var m = i;
                var b = new Button(() => SetMode(m)) { text = ModeNames[i] };
                b.style.flexGrow = 1;
                b.style.flexShrink = 1;
                b.style.minWidth = 0;
                _modeButtons.Add(b);
                row.Add(b);
            }

            return row;
        }

        private VisualElement BuildNextCard()
        {
            var card = FieldGuideUi.Card("次にやること", out var content);
            _nextHeadline = new Label { style = { whiteSpace = WhiteSpace.Normal, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 2 } };
            content.Add(_nextHeadline);
            for (var i = 0; i < 3; i++)
            {
                var l = new Label { style = { whiteSpace = WhiteSpace.Normal } };
                _stepLabels.Add(l);
                content.Add(l);
            }

            _errorLabel = new Label { style = { whiteSpace = WhiteSpace.Normal, color = new Color(1f, 0.45f, 0.4f), marginTop = 2 } };
            content.Add(_errorLabel);

            var save = new Button(() =>
            {
                if (Target != null)
                {
                    SaveTarget();
                    UpdateNext();
                }
            }) { text = "保存", tooltip = "この Data を保存する(版数が進む)" };
            save.style.alignSelf = Align.FlexStart;
            content.Add(save);
            return card;
        }

        private void SetMode(int mode)
        {
            _mode = mode;
            EditorPrefs.SetInt(ModePrefKey, mode);
            ApplyMode();
        }

        // 段 → 最小のモード(Required = かんたん / Common = 標準 / Advanced = 詳細)。
        private static int ModeOf(FieldTier tier) => (int)tier;

        private int RevealingMode(SectionView view)
        {
            var need = _mode;
            foreach (var f in view.Fields)
            {
                need = Mathf.Max(need, ModeOf(f.Entry.Tier));
            }

            return need;
        }

        private void ApplyMode()
        {
            for (var i = 0; i < _modeButtons.Count; i++)
            {
                _modeButtons[i].style.unityFontStyleAndWeight = i == _mode ? FontStyle.Bold : FontStyle.Normal;
                _modeButtons[i].style.backgroundColor = i == _mode ? new Color(0.25f, 0.45f, 0.75f, 0.8f) : StyleKeyword.Null;
            }

            foreach (var s in _sections)
            {
                var visible = 0;
                var hidden = 0;
                var maxHiddenMode = 0;
                foreach (var f in s.Fields)
                {
                    var show = ModeOf(f.Entry.Tier) <= _mode;
                    f.Apply(show);
                    if (show)
                    {
                        visible++;
                    }
                    else
                    {
                        hidden++;
                        maxHiddenMode = Mathf.Max(maxHiddenMode, ModeOf(f.Entry.Tier));
                    }
                }

                s.Foldout.style.display = visible > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (s.ReadOnlyInfo != null)
                {
                    s.ReadOnlyInfo.style.display = _mode == 2 ? DisplayStyle.Flex : DisplayStyle.None;
                }

                s.More.style.display = hidden > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (hidden > 0)
                {
                    s.More.text = $"あと {hidden} 個の設定({ModeNames[maxHiddenMode]}で表示)";
                }
            }
        }

        // ── 次にやること ──

        protected override void OnDataEdited()
        {
            RecomputeErrors();
            UpdateNext();
        }

        protected override void OnTick() => UpdateNext();

        protected override void OnTargetChanged()
        {
            _errorCount = 0;
            _firstError = null;
        }

        private void RecomputeErrors()
        {
            _errorCount = 0;
            _firstError = null;
            if (Target == null)
            {
                return;
            }

            foreach (var r in DataValidationRunner.Run(Target))
            {
                if (r.Severity == ValidationSeverity.Error)
                {
                    _errorCount++;
                    _firstError ??= r.Message;
                }
            }
        }

        private void UpdateNext()
        {
            if (_nextHeadline == null || Target == null)
            {
                return;
            }

            var prefabOk = Target.Prefab != null;
            var played = Played;
            var saved = prefabOk && played && !EditorUtility.IsDirty(Target);
            var done = new[] { prefabOk, played, saved };
            var names = new[] { "Prefab を設定する", "▶ 再生で確認する", "保存する" };

            var current = -1;
            for (var i = 0; i < 3; i++)
            {
                if (!done[i])
                {
                    current = i;
                    break;
                }
            }

            _nextHeadline.text = current < 0
                ? "✓ 完了です。必要なら下の「標準」「詳細」で細かく調整できます。"
                : $"次: {current + 1}. {names[current]}";

            for (var i = 0; i < 3; i++)
            {
                var mark = done[i] ? "✓" : i == current ? "→" : "　";
                _stepLabels[i].text = $"{mark} {i + 1}. {names[i]}";
                _stepLabels[i].style.opacity = done[i] ? 0.55f : 1f;
                _stepLabels[i].style.unityFontStyleAndWeight = i == current ? FontStyle.Bold : FontStyle.Normal;
            }

            _errorLabel.text = _errorCount > 0 ? $"⚠ 検証エラー {_errorCount} 件: {_firstError}" : string.Empty;
            _errorLabel.style.display = _errorCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
