using System.Collections.Generic;
using DDrive.Editor.Codegen;
using DDrive.Editor.Menu;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace DDrive.Editor.Spec
{
    // [32_spec_web.md] §4.1/§5(旧 [27_spec_sheet.md] §4.3/§4.4)— 仕様書 Web アプリ(GAS)との
    // 差分プレビュー + 適用画面 + D-Drive → Web 送信(W-12)。
    // [09_editor_tools.md] §6-7: ScrollView ルート必須の新規 EditorWindow。
    //
    // W-9(2026-09-14): スプレッドシート URL/タブ名の入力欄を Web API URL・人向け URL・
    // 読み取り/書き込みトークン(EditorPrefs、伏せ字)の入力欄に差し替えた。
    public sealed class SpecSyncWindow : EditorWindow
    {
        private DDriveSpecSettings _settings;
        private TextField _webAppUrlField;
        private TextField _humanAppUrlField;
        private TextField _readTokenField;
        private TextField _writeTokenField;
        private Toggle _autoFetchToggle;
        private Toggle _autoApplyToggle;
        private Toggle _applyTuningToggle;
        private Label _statusLabel;
        private Label _sendStatusLabel;

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

            _applyTuningToggle = new Toggle("調整値も同期する(適用時、スカラー+テーブル)") { value = true };
            scrollView.Add(_applyTuningToggle);

            var copyRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4, marginBottom = 6 } };
            copyRow.Add(new Button(OnCopyChoicesClicked) { text = "選択肢をコピー", style = { flexGrow = 1 } });
            copyRow.Add(new Button(OnCopyExistingClicked) { text = "既存アセットをコピー", style = { flexGrow = 1 } });
            scrollView.Add(copyRow);

            _statusLabel = new Label();
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            scrollView.Add(_statusLabel);

            // W-12: D-Drive → Web 送信(選択肢・アセット実状態・TUNING コード参照)。
            scrollView.Add(new Label("D-Drive → Web 送信(W-12)") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10 } });
            scrollView.Add(new Button(OnSendToWebClicked) { text = "Web に送信(選択肢 / 実状態 / 調整値使用状況)" });
            _sendStatusLabel = new Label { style = { whiteSpace = WhiteSpace.Normal } };
            scrollView.Add(_sendStatusLabel);

            scrollView.Add(new Label("新規") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });
            _newContainer = new VisualElement();
            scrollView.Add(_newContainer);

            scrollView.Add(new Label("変更") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });
            _changedContainer = new VisualElement();
            scrollView.Add(_changedContainer);

            scrollView.Add(new Label("Web から消えた(Archive 候補・表示のみ)") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });
            _archivedContainer = new VisualElement();
            scrollView.Add(_archivedContainer);

            scrollView.Add(new Label("衝突(適用不可)") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });
            _conflictContainer = new VisualElement();
            scrollView.Add(_conflictContainer);

            RenderFromCache();
        }

        private VisualElement BuildSettingsSection()
        {
            var box = new VisualElement();

            _webAppUrlField = new TextField("Web API URL(デプロイ②)") { value = _settings != null ? _settings.WebAppUrl : string.Empty };
            box.Add(_webAppUrlField);

            _humanAppUrlField = new TextField("人向け SPA URL(デプロイ①、SpecUrl 組み立て用)") { value = _settings != null ? _settings.HumanAppUrl : string.Empty };
            box.Add(_humanAppUrlField);

            _readTokenField = new TextField("読み取りトークン") { value = DDriveSpecSettings.ReadToken, isPasswordField = true };
            box.Add(_readTokenField);

            _writeTokenField = new TextField("書き込みトークン") { value = DDriveSpecSettings.WriteToken, isPasswordField = true };
            box.Add(_writeTokenField);

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
            settings.WebAppUrl = _webAppUrlField.value;
            settings.HumanAppUrl = _humanAppUrlField.value;
            settings.AutoFetchOnStartup = _autoFetchToggle.value;
            settings.AutoApplyNewPlaceholders = _autoApplyToggle.value;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            _settings = settings;

            // トークンは .asset(git 管理)には書かない。EditorPrefs(マシンごと)へ保存する([32] §7)。
            DDriveSpecSettings.ReadToken = _readTokenField.value;
            DDriveSpecSettings.WriteToken = _writeTokenField.value;

            _statusLabel.text = "設定を保存しました。";
        }

        private void OnFetchClicked()
        {
            var settings = DDriveSpecSettings.GetOrCreate();
            settings.WebAppUrl = _webAppUrlField.value;
            settings.HumanAppUrl = _humanAppUrlField.value;
            if (string.IsNullOrEmpty(settings.WebAppUrl))
            {
                _statusLabel.text = "Web API URL が未設定です。先に「設定を保存」してください。";
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

            if (_applyTuningToggle != null && _applyTuningToggle.value)
            {
                var table = settings.GetOrCreateTuningTable();
                if (SpecCache.LastTuningRows != null)
                {
                    SpecSyncService.ApplyTuning(SpecCache.LastTuningRows, table);
                }

                if (SpecCache.LastTuningTableRows != null)
                {
                    SpecSyncService.ApplyTuningTable(SpecCache.LastTuningTableRows, table);
                }

                TuningCodegen.Regenerate(table);
            }

            _statusLabel.text = $"適用しました: 新規 {createdCount} 件 / 変更 {changedCount} 件。";
            OnFetchClicked(); // 適用後の状態で差分を再計算する
        }

        private void OnSendToWebClicked()
        {
            var settings = _settings ?? DDriveSpecSettings.Load();
            if (settings == null || string.IsNullOrEmpty(settings.WebAppUrl))
            {
                _sendStatusLabel.text = "Web API URL が未設定です。先に「設定を保存」してください。";
                return;
            }

            var writeToken = DDriveSpecSettings.WriteToken;
            if (string.IsNullOrEmpty(writeToken))
            {
                _sendStatusLabel.text = "書き込みトークンが未設定です。";
                return;
            }

            _sendStatusLabel.text = "送信中...";
            SpecWebSender.SendChoices(settings.WebAppUrl, writeToken, choicesResult =>
            {
                SpecWebSender.SendAssetState(settings.WebAppUrl, writeToken, assetStateResult =>
                {
                    var table = settings.GetOrCreateTuningTable();
                    SpecWebSender.SendTuningUsage(settings.WebAppUrl, writeToken, table, tuningUsageResult =>
                    {
                        var ok = choicesResult.Success && assetStateResult.Success && tuningUsageResult.Success;
                        _sendStatusLabel.text = ok
                            ? "送信しました(選択肢 / 実状態 / 調整値使用状況)。"
                            : $"送信に失敗しました: {choicesResult.Error ?? assetStateResult.Error ?? tuningUsageResult.Error}";
                    });
                });
            });
        }

        private void OnCopyChoicesClicked()
        {
            EditorGUIUtility.systemCopyBuffer = SpecSyncService.BuildChoicesTsv();
            _statusLabel.text = "選択肢一覧をクリップボードにコピーしました。";
        }

        private void OnCopyExistingClicked()
        {
            EditorGUIUtility.systemCopyBuffer = SpecSyncService.BuildExistingAssetsTsv();
            _statusLabel.text = "既存アセット一覧をクリップボードにコピーしました。";
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
