using System.Collections.Generic;
using DDrive.Editor.Menu;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace DDrive.Editor.Spec
{
    // [27_spec_sheet.md] §4.3/§4.4 — 仕様書スプレッドシートとの差分プレビュー + 適用画面。
    // [09_editor_tools.md] §6-7: ScrollView ルート必須の新規 EditorWindow。
    public sealed class SpecSyncWindow : EditorWindow
    {
        private DDriveSpecSettings _settings;
        private TextField _urlField;
        private TextField _assetSheetField;
        private TextField _tuningSheetField;
        private Toggle _autoFetchToggle;
        private Toggle _autoApplyToggle;
        private Toggle _applyTuningToggle;
        private Label _statusLabel;

        private VisualElement _newContainer;
        private VisualElement _changedContainer;
        private VisualElement _archivedContainer;
        private VisualElement _conflictContainer;

        // 行キー(SpecAssetRow.Key)→適用対象として選択されているか。取得のたびに新しい行として
        // 再構築されるため、無ければ既定 ON。
        private readonly Dictionary<string, bool> _selected = new();

        [MenuItem(DDriveMenu.Root + "仕様書と同期")]
        public static void Open()
        {
            var window = GetWindow<SpecSyncWindow>("仕様書と同期");
            window.minSize = new Vector2(480, 360);
        }

        private void CreateGUI()
        {
            _settings = DDriveSpecSettings.Load();

            var scrollView = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1f } };
            rootVisualElement.Add(scrollView);
            scrollView.style.paddingLeft = 6;
            scrollView.style.paddingRight = 6;
            scrollView.style.paddingTop = 6;

            scrollView.Add(BuildSettingsSection());

            var actionRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 6, marginBottom = 6 } };
            actionRow.Add(new Button(OnFetchClicked) { text = "取得", style = { flexGrow = 1 } });
            actionRow.Add(new Button(OnApplySelectedClicked) { text = "適用", style = { flexGrow = 1 } });
            scrollView.Add(actionRow);

            _applyTuningToggle = new Toggle("調整値も同期する(適用時)") { value = true };
            scrollView.Add(_applyTuningToggle);

            var copyRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4, marginBottom = 6 } };
            copyRow.Add(new Button(OnCopyChoicesClicked) { text = "選択肢をコピー", style = { flexGrow = 1 } });
            copyRow.Add(new Button(OnCopyExistingClicked) { text = "既存アセットをコピー", style = { flexGrow = 1 } });
            scrollView.Add(copyRow);

            _statusLabel = new Label();
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            scrollView.Add(_statusLabel);

            scrollView.Add(new Label("新規") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });
            _newContainer = new VisualElement();
            scrollView.Add(_newContainer);

            scrollView.Add(new Label("変更") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });
            _changedContainer = new VisualElement();
            scrollView.Add(_changedContainer);

            scrollView.Add(new Label("シートから消えた(Archive 候補・表示のみ)") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });
            _archivedContainer = new VisualElement();
            scrollView.Add(_archivedContainer);

            scrollView.Add(new Label("衝突(行番号付き。適用不可)") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });
            _conflictContainer = new VisualElement();
            scrollView.Add(_conflictContainer);

            RenderFromCache();
        }

        private VisualElement BuildSettingsSection()
        {
            var box = new VisualElement();

            _urlField = new TextField("スプレッドシート URL") { value = _settings != null ? _settings.SpreadsheetUrl : string.Empty };
            box.Add(_urlField);

            _assetSheetField = new TextField("アセットタブ名") { value = _settings != null ? _settings.AssetSheetName : DDriveSpecSettings.DefaultAssetSheetName };
            box.Add(_assetSheetField);

            _tuningSheetField = new TextField("調整値タブ名") { value = _settings != null ? _settings.TuningSheetName : DDriveSpecSettings.DefaultTuningSheetName };
            box.Add(_tuningSheetField);

            _autoFetchToggle = new Toggle("起動時に自動取得(通知のみ)") { value = _settings == null || _settings.AutoFetchOnStartup };
            box.Add(_autoFetchToggle);

            _autoApplyToggle = new Toggle("未着手の新規行を自動で Placeholder 作成") { value = _settings != null && _settings.AutoApplyNewPlaceholders };
            box.Add(_autoApplyToggle);

            box.Add(new Button(OnSaveSettingsClicked) { text = "設定を保存" });

            return box;
        }

        private void OnSaveSettingsClicked()
        {
            var settings = DDriveSpecSettings.GetOrCreate();
            Undo.RecordObject(settings, "仕様書設定を保存");
            settings.SpreadsheetUrl = _urlField.value;
            settings.AssetSheetName = string.IsNullOrEmpty(_assetSheetField.value) ? DDriveSpecSettings.DefaultAssetSheetName : _assetSheetField.value;
            settings.TuningSheetName = string.IsNullOrEmpty(_tuningSheetField.value) ? DDriveSpecSettings.DefaultTuningSheetName : _tuningSheetField.value;
            settings.AutoFetchOnStartup = _autoFetchToggle.value;
            settings.AutoApplyNewPlaceholders = _autoApplyToggle.value;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            _settings = settings;
            _statusLabel.text = "設定を保存しました。";
        }

        private void OnFetchClicked()
        {
            var settings = DDriveSpecSettings.GetOrCreate();
            settings.SpreadsheetUrl = _urlField.value;
            if (string.IsNullOrEmpty(settings.SpreadsheetUrl))
            {
                _statusLabel.text = "URL が未設定です。先に「設定を保存」してください。";
                return;
            }

            _statusLabel.text = "取得中...";
            SpecAutoSync.Run(settings, applyAutoPlaceholders: false, onComplete: RenderFromCache);
        }

        private void OnApplySelectedClicked()
        {
            var diff = SpecCache.LastDiff;
            if (diff == null)
            {
                _statusLabel.text = "先に「取得」を実行してください。";
                return;
            }

            var settings = _settings ?? DDriveSpecSettings.GetOrCreate();
            var createdCount = 0;
            var changedCount = 0;

            foreach (var change in diff.New)
            {
                if (!IsSelected(change.Row.Key))
                {
                    continue;
                }

                if (SpecSyncService.ApplyNew(change, settings.GameDataRoot) != null)
                {
                    createdCount++;
                }
            }

            foreach (var change in diff.Changed)
            {
                if (!IsSelected(change.Row.Key))
                {
                    continue;
                }

                SpecSyncService.ApplyChanged(change);
                changedCount++;
            }

            if (_applyTuningToggle != null && _applyTuningToggle.value && SpecCache.LastTuningRows != null)
            {
                var table = settings.GetOrCreateTuningTable();
                SpecSyncService.ApplyTuning(SpecCache.LastTuningRows, table);
            }

            _statusLabel.text = $"適用しました: 新規 {createdCount} 件 / 変更 {changedCount} 件。";
            OnFetchClicked(); // 適用後の状態で差分を再計算する
        }

        private void OnCopyChoicesClicked()
        {
            EditorGUIUtility.systemCopyBuffer = SpecSyncService.BuildChoicesTsv();
            _statusLabel.text = "選択肢一覧をクリップボードにコピーしました(「_選択肢」タブへ貼り付けてください)。";
        }

        private void OnCopyExistingClicked()
        {
            EditorGUIUtility.systemCopyBuffer = SpecSyncService.BuildExistingAssetsTsv();
            _statusLabel.text = "既存アセット一覧をクリップボードにコピーしました(「アセット」タブへ貼り付けてください)。";
        }

        private bool IsSelected(string key) => !_selected.TryGetValue(key, out var value) || value;

        private void RenderFromCache()
        {
            _newContainer.Clear();
            _changedContainer.Clear();
            _archivedContainer.Clear();
            _conflictContainer.Clear();

            var diff = SpecCache.LastDiff;
            if (diff == null)
            {
                _statusLabel.text = "まだ取得していません。";
                return;
            }

            foreach (var change in diff.New)
            {
                _newContainer.Add(BuildAssetChangeRow(change, "新規作成"));
            }

            foreach (var change in diff.Changed)
            {
                _changedContainer.Add(BuildAssetChangeRow(change, string.Join(", ", change.ChangedFields)));
            }

            foreach (var archived in diff.Archived)
            {
                _archivedContainer.Add(new Label($"{archived.Type} / {archived.Identifier}({archived.Asset.name})"));
            }

            foreach (var issue in diff.Conflicts)
            {
                _conflictContainer.Add(new Label($"行 {issue.RowNumber}: {issue.Message}") { style = { color = new Color(0.85f, 0.35f, 0.25f) } });
            }

            var warningPart = string.IsNullOrEmpty(SpecCache.LastWarning) ? string.Empty : $" / {SpecCache.LastWarning}";
            _statusLabel.text = $"新規 {diff.New.Count} / 変更 {diff.Changed.Count} / Archive候補 {diff.Archived.Count} / 衝突 {diff.Conflicts.Count}{warningPart}";
        }

        private VisualElement BuildAssetChangeRow(SpecAssetChange change, string detail)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var toggle = new Toggle { value = IsSelected(change.Row.Key) };
            toggle.RegisterValueChangedCallback(evt => _selected[change.Row.Key] = evt.newValue);
            row.Add(toggle);
            row.Add(new Label($"{change.Row.Type} / {change.Row.Identifier} / {change.Row.DisplayName}  —  {detail}") { style = { flexGrow = 1 } });
            return row;
        }
    }
}
