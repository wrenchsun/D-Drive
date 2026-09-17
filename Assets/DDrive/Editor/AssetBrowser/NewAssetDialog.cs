using System;
using System.Collections.Generic;
using System.Linq;
using DDrive.Editor.Inspectors;
using DDrive.Editor.Spec;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.AssetBrowser
{
    // 新規アセット作成ダイアログ(FR-1.5)。人が入力するのは意味情報のみ:
    // 種別 / 表示名(日本語可) / カテゴリ / 識別子(英語 PascalCase)。
    // ファイル名・ID・カタログ登録は AssetCreationService が自動生成する。
    // 5-16: 上部の「仕様書から選ぶ」から、まだ Data が無い仕様書の行を選ぶと各欄が入力済みになる
    // (手入力も従来どおり可)。[27_spec_sheet.md] §4.5。
    public sealed class NewAssetDialog : EditorWindow
    {
        // 5-16: 仕様書キャッシュ(SpecCache.LastFetchUtc)がこれより古いと「古い可能性があります」を出す。
        // 起動時自動同期はドメインリロードごとに 1 回走るため、長時間ドメインリロード無しで開き続けた
        // 場合の目安として 1 時間にした(要判断。docs/28 参照)。
        private static readonly TimeSpan SpecCacheStaleThreshold = TimeSpan.FromHours(1);

        private List<(Type dataType, AssetType assetType)> _definitions;
        private DropdownField _typeField;
        private TextField _displayNameField;
        private TextField _categoryField;
        private TextField _identifierField;
        private TextField _noteField;
        private TextField _specLinkField;
        private Label _previewLabel;
        private Label _validationLabel;
        private Button _createButton;

        // D&D 由来の作成時に事前設定される(AudioClip → SeData の Clips 等)。
        private AudioClip[] _pendingClips;

        // 5-15: 各専用エディタの「＋ 新規作成」ボタンから開いたときに、種別選択をそのエディタの
        // 対応種別だけに絞り、作成後にコールバックでそのエディタへ切り替えるための状態。
        private Type[] _lockedTypes;
        private Action<AssetDataBase> _onCreated;

        // 5-16: 「仕様書から選ぶ」で選択中の行(未選択なら null)。作成時に Status/Assignee を
        // (UI に専用欄が無いため)ここから引く。
        // P5 レビュー対応(2026-09-14): 以前は選択後に識別子/表示名/カテゴリを手で書き換えても保持したままに
        // していたが、それだと書き換え後に「作成」すると別アセットに仕様書行の Status/Assignee が付いてしまう
        // (著者認識済みの要判断だった)。識別子/表示名/カテゴリを手で書き換えたら選択を解除する
        // (OnManuallyEditedField)。選択中の行は UI に明示し、明示的に外せる「解除」ボタンも用意する
        // (ClearSelectedSpecRow / RefreshSelectedSpecRowIndicator)。
        private SpecAssetRow _selectedSpecRow;
        private VisualElement _specSection;
        private VisualElement _specListContainer;
        private VisualElement _selectedSpecRowIndicator;
        private TextField _specSearchField;

        // OnSpecRowSelected が各フィールドへ値を代入している間は、その ValueChangedCallback から
        // OnManuallyEditedField を呼んで選択を即座に解除してしまわないようにするガード。
        private bool _applyingSpecRowValues;

        // CreateGUI は GetWindow<T>() が新規ウィンドウを生成した瞬間に走るため、Open() の呼び出し側から
        // インスタンスフィールドへ値を渡すより前に実行されてしまう。static の受け渡し用領域を経由する。
        private static Type[] _pendingLockedTypes;
        private static Action<AssetDataBase> _pendingOnCreated;

        // テスト専用の差し替え口(P5 テスト隔離、2026-09-14)。本番は常に null で、AssetCreationService.
        // Create の既定(gameDataRoot=DefaultGameDataRoot)/DDriveSpecSettings.Load() の実シングルトンを
        // そのまま使う。テストだけ ImportRuleService.ProcessPaths(sourceRoot, gameDataRoot) や
        // AssetCreationService.Create(gameDataRoot: TestRoot) と同じ流儀で一時フォルダ・メモリ上の設定に
        // 差し替えて、実 Assets/GameData・実カタログ・実 Addressables グループ・実 DDriveSpecSettings.asset
        // に書き込まないようにする。テストは使い終わったら必ず null に戻すこと。
        // public(SpecDiffService.BuildExistingIndex と同じ理由: InternalsVisibleTo 未設定のため、
        // テスト asmdef から直接差し替えられるようにする)。
        public static string TestGameDataRootOverride;
        public static DDriveSpecSettings TestSpecSettingsOverride;

        public static void Open(AudioClip[] pendingClips = null)
        {
            var window = GetWindow<NewAssetDialog>(utility: true, title: "新規アセット作成");
            window._pendingClips = pendingClips;
            window.minSize = new Vector2(380, 230);
            window.RefreshPreview();
        }

        // 5-15: 種別をそのエディタの対応種別(1 つ以上)に固定して開く。作成が完了したら onCreated(created) を呼ぶ
        // (呼び出し側はここで DataEditorRegistry 経由の Open を叩いて自分のエディタへ切り替える想定)。
        public static void Open(Type[] lockedTypes, Action<AssetDataBase> onCreated)
        {
            _pendingLockedTypes = lockedTypes != null && lockedTypes.Length > 0 ? lockedTypes : null;
            _pendingOnCreated = onCreated;

            // 既存のウィンドウが(ロック無しの通常の「新規」等で)既に開いていると GetWindow は CreateGUI を
            // 呼び直さず種別ロックが反映されないため、開き直して確実に反映する。
            if (HasOpenInstances<NewAssetDialog>())
            {
                GetWindow<NewAssetDialog>().Close();
            }

            var window = GetWindow<NewAssetDialog>(utility: true, title: "新規アセット作成");
            window.minSize = new Vector2(380, 230);
            window.RefreshPreview();
        }

        private void CreateGUI()
        {
            _lockedTypes = _pendingLockedTypes;
            _onCreated = _pendingOnCreated;
            _pendingLockedTypes = null;
            _pendingOnCreated = null;

            _definitions = AssetIdLookup.GetAllDefinitions()
                .Where(d => d.dataType.Namespace?.Contains("Tests") != true)
                .Where(d => _lockedTypes == null || _lockedTypes.Contains(d.dataType))
                .OrderBy(d => d.assetType.ToString())
                .ToList();

            if (_definitions.Count == 0)
            {
                var message = _lockedTypes == null
                    ? "作成可能なアセット種別が見つかりません。"
                    : "このエディタに対応する作成可能な種別が見つかりません。";
                rootVisualElement.Add(new HelpBox(message, HelpBoxMessageType.Warning));
                return;
            }

            // ウィンドウが小さい/項目が増えても内容が見切れないよう、ルートをスクロール可能にする
            // ([09_editor_tools.md] §7 拡縮前提のUI規約)。
            var scrollView = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1f } };
            rootVisualElement.Add(scrollView);

            var root = scrollView;
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 8;

            // 5-16: 「仕様書から選ぶ」セクション(先頭)。選ぶと下の各欄が入力済みになる。
            root.Add(new Label("仕様書から選ぶ") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            _specSection = new VisualElement();
            root.Add(_specSection);
            RebuildSpecSection();

            var divider = new VisualElement
            {
                style =
                {
                    height = 1,
                    marginTop = 8,
                    marginBottom = 8,
                    backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.4f),
                },
            };
            root.Add(divider);

            var choices = _definitions.Select(d => $"{d.assetType} ({d.dataType.Name})").ToList();
            _typeField = new DropdownField("種別", choices, 0);
            _typeField.RegisterValueChangedCallback(_ => RefreshPreview());
            root.Add(_typeField);

            if (_lockedTypes != null)
            {
                // 5-15: エディタの「＋ 新規作成」から開いた場合は種別をそのエディタの対応種別に固定する。
                // 候補が 1 つだけなら選ぶ必要が無いのでドロップダウンごと無効化する。
                var lockHelp = new HelpBox("このエディタに対応する種別のみ選べます。", HelpBoxMessageType.Info);
                root.Add(lockHelp);
                if (choices.Count == 1)
                {
                    _typeField.SetEnabled(false);
                }
            }

            _displayNameField = new TextField("表示名(日本語可)") { tooltip = "AssetBrowser での表示・検索に使う名前。例:「剣の斬撃音」" };
            _displayNameField.RegisterValueChangedCallback(_ => OnManuallyEditedField());
            root.Add(_displayNameField);

            _categoryField = new TextField("カテゴリ") { tooltip = "例: Player, UI, Battle。階層表記(Audio/SE/Player)も可。ファイル名には最終セグメントのみ使われる。" };
            _categoryField.RegisterValueChangedCallback(_ =>
            {
                OnManuallyEditedField();
                RefreshPreview();
            });
            root.Add(_categoryField);

            _identifierField = new TextField("識別子(英語)") { tooltip = "ID 定数名になる。PascalCase 英数字。例: PlayerSlash → SEID.PlayerSlash" };
            _identifierField.RegisterValueChangedCallback(_ =>
            {
                OnManuallyEditedField();
                RefreshPreview();
            });
            root.Add(_identifierField);

            // 5-16: 仕様書から選ぶと備考・仕様リンクも入力済みになる(手入力も可)。
            _noteField = new TextField("備考") { tooltip = "Description に反映される。仕様書の「備考」列と同じ欄。" };
            root.Add(_noteField);

            _specLinkField = new TextField("仕様リンク") { tooltip = "SpecUrl に反映される。Inspector の「仕様書を開く」ボタンから開ける。空でも作成可。" };
            root.Add(_specLinkField);

            _previewLabel = new Label();
            _previewLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _previewLabel.style.marginTop = 6;
            root.Add(_previewLabel);

            _validationLabel = new Label();
            _validationLabel.style.color = new Color(0.9f, 0.4f, 0.3f);
            root.Add(_validationLabel);

            var help = new HelpBox("ファイル名・ID・カタログ登録はツールが自動生成します。人がファイル名を決める必要はありません。", HelpBoxMessageType.Info);
            help.style.marginTop = 6;
            root.Add(help);

            _createButton = new Button(CreateAsset) { text = "作成" };
            _createButton.style.marginTop = 8;
            _createButton.style.height = 26;
            root.Add(_createButton);

            RefreshPreview();
        }

        private (Type dataType, AssetType assetType) SelectedDefinition()
        {
            var index = Mathf.Max(0, _typeField.choices.IndexOf(_typeField.value));
            return _definitions.Count > 0 ? _definitions[Mathf.Clamp(index, 0, _definitions.Count - 1)] : (null, AssetType.None);
        }

        private void RefreshPreview()
        {
            if (_previewLabel == null || _definitions == null || _definitions.Count == 0)
            {
                return;
            }

            var (dataType, assetType) = SelectedDefinition();
            var identifier = _identifierField?.value ?? string.Empty;
            var valid = AssetNamingService.IsValidIdentifier(identifier);

            _validationLabel.text = valid ? string.Empty : "識別子は英語 PascalCase(例: PlayerSlash)で入力してください";
            _createButton?.SetEnabled(valid && dataType != null);

            if (valid && dataType != null)
            {
                var fileName = AssetNamingService.BuildFileName(assetType, _categoryField?.value, identifier);
                var folder = AssetNamingService.GetTargetFolder(assetType, _categoryField?.value);
                var gameDataRoot = TestGameDataRootOverride ?? AssetCreationService.DefaultGameDataRoot;
                _previewLabel.text = $"生成先: {gameDataRoot}/{folder}/{fileName}.asset";
            }
            else
            {
                _previewLabel.text = "生成先: (識別子を入力すると表示)";
            }
        }

        private void CreateAsset()
        {
            var (dataType, assetType) = SelectedDefinition();
            if (dataType == null)
            {
                return;
            }

            var clips = _pendingClips;

            // 5-16: 状態タグ/Assignee/Description/SpecUrl の反映は SpecSyncService.ApplyExtraFields を
            // そのまま再利用する(同期の「新規 → Placeholder 作成」と同じ結果になるようにコピペしない)。
            // 仕様書の行を選んでいなければ Status/Assignee は空(= 従来どおり何も設定しない)。
            var extraFieldsRow = new SpecAssetRow
            {
                Status = _selectedSpecRow?.Status ?? string.Empty,
                Assignee = _selectedSpecRow?.Assignee ?? string.Empty,
                Note = _noteField?.value ?? string.Empty,
                SpecLink = _specLinkField?.value ?? string.Empty,
            };

            var asset = AssetCreationService.Create(
                dataType,
                assetType,
                _displayNameField.value,
                _categoryField.value,
                _identifierField.value,
                configure: created =>
                {
                    ApplyPendingClips(created, clips);
                    SpecSyncService.ApplyExtraFields(created, extraFieldsRow);
                },
                gameDataRoot: TestGameDataRootOverride ?? AssetCreationService.DefaultGameDataRoot);

            if (asset != null)
            {
                EditorGUIUtility.PingObject(asset);
                Selection.activeObject = asset;

                // 開いている AssetBrowser に新規アセットを即時反映する。
                foreach (var browser in Resources.FindObjectsOfTypeAll<AssetBrowserWindow>())
                {
                    browser.Refresh();
                }

                // 5-16: 仕様書の行から作った場合、その行を「未作成」一覧(次回このダイアログを開いた時や
                // AssetBrowser の変更バッジ)から消す。ネットへは行かず、既存の取得結果を使って
                // 既存アセットとの照合だけ再計算する(SpecCache.RecomputeDiff)。
                if (_selectedSpecRow != null)
                {
                    SpecCache.RecomputeDiff();
                }

                // 5-15: 専用エディタの「＋ 新規作成」から開いた場合、作成後にそのエディタへ切り替える。
                // 呼び出し元のエディタが既に閉じていても例外で落とさない([00] §0-4: 例外で止めない)。
                // U-16(2026-09-17): 呼び出し元の指定が無い場合(AssetBrowser の「新規」/ D&D / Project 右クリック)は、
                // その種別の専用エディタ(DataEditorRegistry の主エディタ)で作ったアセットをそのまま開く。
                // 専用エディタが無い種別は上の Ping + Selection(Inspector で選択状態)のままにする。
                try
                {
                    if (_onCreated != null)
                    {
                        _onCreated.Invoke(asset);
                    }
                    else
                    {
                        DDrive.Editor.Inspector.CreatedAssetOpener.Reveal(asset);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DDrive] 新規アセット作成後のエディタ切り替えに失敗しました: {e.Message}");
                }

                Close();
            }
        }

        // ── 5-16: 仕様書から選ぶ ──

        private void RebuildSpecSection()
        {
            _specSection.Clear();

            var settings = TestSpecSettingsOverride ?? DDriveSpecSettings.Load();
            if (settings == null || string.IsNullOrEmpty(settings.WebAppUrl))
            {
                // [27] §4.5 / [32] §5.1: 設定 URL 未設定時は案内文だけ出す(この場から設定 SO を自動生成しない)。
                //
                // U-15(2026-09-17 修正): ここだけ W-9 以前の旧フィールド SpreadsheetUrl を見ていたため、
                // 「仕様書と同期」で Web API URL(WebAppUrl)を設定しても「未設定です」のままだった。
                // 取得・同期の実装(SpecAutoSync / SpecSyncWindow)はすべて WebAppUrl を見ているので、
                // 判定もそちらに合わせる(旧フィールドは [32] §9 の要判断のため残置)。
                _specSection.Add(new HelpBox(
                    "仕様書の URL が未設定です。Tools > D-Drive > 仕様書と同期 で設定すると、ここから仕様書の未作成アセットを選べます。",
                    HelpBoxMessageType.Info));
                return;
            }

            var (statusText, needsRefreshButton) = DescribeCacheFreshness();
            _specSection.Add(new Label(statusText) { style = { marginBottom = 2 } });

            if (needsRefreshButton)
            {
                _specSection.Add(new Button(() => OnRefetchClicked(settings)) { text = "仕様書を再取得" });
            }

            if (!SpecCache.HasData)
            {
                return; // まだ一度も取得していない(このセッションでドメインリロード後の起動時自動取得もまだ)
            }

            _specSearchField = new TextField("検索(表示名・識別子)") { value = string.Empty };
            _specSearchField.RegisterValueChangedCallback(_ => RenderSpecList());
            _specSection.Add(_specSearchField);

            // P5 レビュー対応(2026-09-14): 選択中の行を(検索で一覧から外れても分かるよう)常に明示し、
            // 「解除」で手動でも選択を外せるようにする。
            _selectedSpecRowIndicator = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 2, marginBottom = 2 } };
            _specSection.Add(_selectedSpecRowIndicator);

            _specListContainer = new VisualElement { style = { marginTop = 2 } };
            _specSection.Add(_specListContainer);

            RenderSpecList();
            RefreshSelectedSpecRowIndicator();
        }

        private void RefreshSelectedSpecRowIndicator()
        {
            if (_selectedSpecRowIndicator == null)
            {
                return;
            }

            _selectedSpecRowIndicator.Clear();

            if (_selectedSpecRow == null)
            {
                return;
            }

            var label = new Label($"選択中の仕様書行: {_selectedSpecRow.Type} / {_selectedSpecRow.Category} / {_selectedSpecRow.Identifier} — {_selectedSpecRow.DisplayName}")
            {
                style = { flexGrow = 1, unityFontStyleAndWeight = FontStyle.Bold },
            };
            _selectedSpecRowIndicator.Add(label);
            _selectedSpecRowIndicator.Add(new Button(ClearSelectedSpecRow) { text = "解除" });
        }

        // P5 レビュー対応(2026-09-14): 識別子/表示名/カテゴリを手で書き換えたら仕様書行の選択を解除する
        // (書き換え後に「作成」すると別アセットに Status/Assignee が付いてしまう問題への対応)。
        // OnSpecRowSelected が値を代入している最中(_applyingSpecRowValues)は無視する。
        private void OnManuallyEditedField()
        {
            if (_applyingSpecRowValues || _selectedSpecRow == null)
            {
                return;
            }

            ClearSelectedSpecRow();
        }

        private void ClearSelectedSpecRow()
        {
            if (_selectedSpecRow == null)
            {
                return;
            }

            _selectedSpecRow = null;
            RenderSpecList();
            RefreshSelectedSpecRowIndicator();
        }

        // このダイアログで選べる種別(ロック時はロック対象だけ)に絞って「未作成」行を出す。
        private void RenderSpecList()
        {
            if (_specListContainer == null)
            {
                return;
            }

            _specListContainer.Clear();

            var allowedTypes = new HashSet<AssetType>(_definitions.Select(d => d.assetType));
            var rows = SpecCache.GetUncreatedRows(null).Where(r => allowedTypes.Contains(r.Type));

            var search = _specSearchField?.value?.Trim();
            if (!string.IsNullOrEmpty(search))
            {
                rows = rows.Where(r =>
                    (r.DisplayName?.IndexOf(search, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (r.Identifier?.IndexOf(search, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
            }

            var rowList = rows.ToList();
            if (rowList.Count == 0)
            {
                _specListContainer.Add(new Label("(該当する未作成行はありません)") { style = { color = new Color(0.6f, 0.6f, 0.6f) } });
                return;
            }

            foreach (var row in rowList)
            {
                _specListContainer.Add(BuildSpecRowElement(row));
            }
        }

        private VisualElement BuildSpecRowElement(SpecAssetRow row)
        {
            var isSelected = ReferenceEquals(_selectedSpecRow, row);
            var container = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 1 } };
            var label = new Label($"{row.Type} / {row.Category} / {row.Identifier} — {row.DisplayName}")
            {
                style = { flexGrow = 1, unityFontStyleAndWeight = isSelected ? FontStyle.Bold : FontStyle.Normal },
            };
            container.Add(label);

            var button = new Button(() => OnSpecRowSelected(row)) { text = isSelected ? "選択中" : "選ぶ" };
            button.SetEnabled(!isSelected);
            container.Add(button);
            return container;
        }

        private void OnSpecRowSelected(SpecAssetRow row)
        {
            _selectedSpecRow = row;

            // P5 レビュー対応(2026-09-14): ここでの各 TextField への代入は ValueChangedCallback
            // (OnManuallyEditedField)を経由して「手で書き換えた」と誤判定され、直後に選択を解除して
            // しまう。代入中だけそのガードを効かせる。
            _applyingSpecRowValues = true;
            try
            {
                // 種別: ロック済みならそのまま。そうでなければ row.Type に一致する最初の選択肢に切り替える
                // (ControlSkin のように 1 AssetType に複数の具象 Data 型がある場合、どちらを作るかは
                // ドロップダウンで人が選ぶ。ここでは最初の候補を仮に選ぶだけで、必要なら手で変更できる)。
                var index = _definitions.FindIndex(d => d.assetType == row.Type);
                if (index >= 0 && index < _typeField.choices.Count)
                {
                    _typeField.value = _typeField.choices[index];
                }

                _categoryField.value = row.Category ?? string.Empty;
                _identifierField.value = row.Identifier ?? string.Empty;
                _displayNameField.value = row.DisplayName ?? string.Empty;
                _noteField.value = row.Note ?? string.Empty;
                _specLinkField.value = row.SpecLink ?? string.Empty;
            }
            finally
            {
                _applyingSpecRowValues = false;
            }

            RenderSpecList(); // 選択中の行の見た目(太字/ボタン)を更新
            RefreshSelectedSpecRowIndicator();
            RefreshPreview();
        }

        private void OnRefetchClicked(DDriveSpecSettings settings)
        {
            _specSection.Clear();
            _specSection.Add(new Label("仕様書を取得中..."));

            // 非同期(UnityWebRequest)。手動の「取得」と同じく、新規行の自動作成はしない(プレビューのみ)。
            SpecAutoSync.Run(settings, applyAutoPlaceholders: false, onComplete: () =>
            {
                // ウィンドウが既に閉じられていたら何もしない(Unity の破棄済みオブジェクト判定)。
                if (this == null)
                {
                    return;
                }

                RebuildSpecSection();
            });
        }

        // (表示文, 再取得ボタンを出すか)。キャッシュが無い/古い ときだけボタンを出す([27] §4.5)。
        private (string text, bool needsRefreshButton) DescribeCacheFreshness()
        {
            if (!SpecCache.HasData)
            {
                return ("仕様書のキャッシュがまだありません。", true);
            }

            var age = DateTime.UtcNow - SpecCache.LastFetchUtc;
            var ageText = FormatAge(age);
            return age > SpecCacheStaleThreshold
                ? ($"最終取得: {ageText}前(古い可能性があります)", true)
                : ($"最終取得: {ageText}前", false);
        }

        private static string FormatAge(TimeSpan age)
        {
            if (age.TotalMinutes < 1)
            {
                return "1分未満";
            }

            if (age.TotalHours < 1)
            {
                return $"{(int)age.TotalMinutes}分";
            }

            if (age.TotalDays < 1)
            {
                return $"{(int)age.TotalHours}時間";
            }

            return $"{(int)age.TotalDays}日";
        }

        private static void ApplyPendingClips(AssetDataBase asset, AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0)
            {
                return;
            }

            switch (asset)
            {
                case SeData se:
                    se.Clips = clips;
                    se.Sources = clips.Select(c => new SeClipSource { Source = c }).ToArray();
                    break;

                case BgmData bgm:
                    bgm.LoopBody = clips[0];
                    break;
            }
        }
    }
}
