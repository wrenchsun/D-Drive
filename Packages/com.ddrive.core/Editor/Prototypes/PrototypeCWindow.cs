using System.Collections.Generic;
using DDrive.Editor.Manual;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.EditorPrototypes
{
    // C. 目的別カード + 設定の検索(docs/1008 §3-C)。
    internal sealed class PrototypeCWindow : PrototypeWindowBase
    {
        private const string ManualPage = "vfx-editor";

        private sealed class CardView
        {
            public VisualElement Card;
            public List<GuidedField> Fields = new List<GuidedField>();
        }

        [SerializeField] private string _query = string.Empty;
        [SerializeField] private bool _modifiedOnly;
        [SerializeField] private bool _requiredOnly;

        private readonly List<CardView> _cards = new List<CardView>();
        private Label _hitLabel;

        protected override string Aim =>
            "C 目的別カード: 欄数が多くても「何をしたい?」で束ね、検索と「変更済みだけ」で探せる。Canvas のように欄が多い種別向け。";

        public static void Open() => OpenWindow<PrototypeCWindow>("C 目的別カード");

        protected override void BuildBody(VisualElement body)
        {
            _cards.Clear();

            var search = new ToolbarSearchField { value = _query };
            search.style.flexGrow = 1;
            search.style.marginLeft = 0;
            search.style.marginRight = 0;
            search.tooltip = "ラベル・説明・検索語・フィールド名に一致する欄だけ出す(例: 高さ / ループ / レイヤー)";
            search.RegisterValueChangedCallback(e =>
            {
                _query = e.newValue ?? string.Empty;
                ApplyFilter();
            });
            body.Add(search);

            var filters = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 2 } };
            var modified = new Toggle("変更済みだけ") { value = _modifiedOnly, tooltip = "既定値と違う欄だけ表示" };
            modified.RegisterValueChangedCallback(e =>
            {
                _modifiedOnly = e.newValue;
                ApplyFilter();
            });
            var required = new Toggle("必須だけ") { value = _requiredOnly, tooltip = "Validator が Error にする欄だけ表示" };
            required.style.marginLeft = 10;
            required.RegisterValueChangedCallback(e =>
            {
                _requiredOnly = e.newValue;
                ApplyFilter();
            });
            filters.Add(modified);
            filters.Add(required);
            _hitLabel = new Label { style = { marginLeft = 10, opacity = 0.6f, alignSelf = Align.Center } };
            filters.Add(_hitLabel);
            body.Add(filters);

            body.Add(BuildPlayRow());

            foreach (var purpose in Guide.Purposes)
            {
                var help = new Button(() => ManualLauncher.OpenPage(ManualPage)) { text = "？", tooltip = "マニュアルの VFX Editor のページを開く" };
                help.style.flexShrink = 0;
                var card = FieldGuideUi.Card(purpose, out var content, help);
                var view = new CardView { Card = card };
                foreach (var entry in Guide.Entries)
                {
                    if (entry.Purpose != purpose)
                    {
                        continue;
                    }

                    var f = MakeField(entry.Field, true, true);
                    view.Fields.Add(f);
                    content.Add(f);
                }

                if (purpose == VfxFieldGuide.PurAdmin)
                {
                    content.Add(FieldGuideUi.MakeReadOnlyInfo(Target));
                }

                _cards.Add(view);
                body.Add(card);
            }

            body.Add(Validation);
            ApplyFilter();
        }

        protected override void OnDataEdited() => ApplyFilter();

        private void ApplyFilter()
        {
            if (_hitLabel == null)
            {
                return;
            }

            var hits = 0;
            var total = 0;
            foreach (var c in _cards)
            {
                var visible = 0;
                foreach (var f in c.Fields)
                {
                    total++;
                    var show = FieldGuide.Matches(f.Entry, _query)
                        && (!_modifiedOnly || f.IsModified)
                        && (!_requiredOnly || f.Entry.Tier == FieldTier.Required);
                    f.HiddenByFilter = !show;
                    f.Apply(true);
                    if (show)
                    {
                        visible++;
                    }
                }

                hits += visible;
                c.Card.style.display = visible > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            var filtering = !string.IsNullOrWhiteSpace(_query) || _modifiedOnly || _requiredOnly;
            _hitLabel.text = filtering ? $"{hits} / {total} 欄" : string.Empty;
        }
    }
}
