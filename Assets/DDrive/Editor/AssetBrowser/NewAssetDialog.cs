using System;
using System.Collections.Generic;
using System.Linq;
using DDrive.Editor.Inspectors;
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
    public sealed class NewAssetDialog : EditorWindow
    {
        private List<(Type dataType, AssetType assetType)> _definitions;
        private DropdownField _typeField;
        private TextField _displayNameField;
        private TextField _categoryField;
        private TextField _identifierField;
        private Label _previewLabel;
        private Label _validationLabel;
        private Button _createButton;

        // D&D 由来の作成時に事前設定される(AudioClip → SeData の Clips 等)。
        private AudioClip[] _pendingClips;

        // 5-15: 各専用エディタの「＋ 新規作成」ボタンから開いたときに、種別選択をそのエディタの
        // 対応種別だけに絞り、作成後にコールバックでそのエディタへ切り替えるための状態。
        private Type[] _lockedTypes;
        private Action<AssetDataBase> _onCreated;

        // CreateGUI は GetWindow<T>() が新規ウィンドウを生成した瞬間に走るため、Open() の呼び出し側から
        // インスタンスフィールドへ値を渡すより前に実行されてしまう。static の受け渡し用領域を経由する。
        private static Type[] _pendingLockedTypes;
        private static Action<AssetDataBase> _pendingOnCreated;

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
            root.Add(_displayNameField);

            _categoryField = new TextField("カテゴリ") { tooltip = "例: Player, UI, Battle。階層表記(Audio/SE/Player)も可。ファイル名には最終セグメントのみ使われる。" };
            _categoryField.RegisterValueChangedCallback(_ => RefreshPreview());
            root.Add(_categoryField);

            _identifierField = new TextField("識別子(英語)") { tooltip = "ID 定数名になる。PascalCase 英数字。例: PlayerSlash → SEID.PlayerSlash" };
            _identifierField.RegisterValueChangedCallback(_ => RefreshPreview());
            root.Add(_identifierField);

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
                _previewLabel.text = $"生成先: {AssetCreationService.DefaultGameDataRoot}/{folder}/{fileName}.asset";
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
            var asset = AssetCreationService.Create(
                dataType,
                assetType,
                _displayNameField.value,
                _categoryField.value,
                _identifierField.value,
                configure: created => ApplyPendingClips(created, clips));

            if (asset != null)
            {
                EditorGUIUtility.PingObject(asset);
                Selection.activeObject = asset;

                // 開いている AssetBrowser に新規アセットを即時反映する。
                foreach (var browser in Resources.FindObjectsOfTypeAll<AssetBrowserWindow>())
                {
                    browser.Refresh();
                }

                // 5-15: 専用エディタの「＋ 新規作成」から開いた場合、作成後にそのエディタへ切り替える。
                // 呼び出し元のエディタが既に閉じていても例外で落とさない([00] §0-4: 例外で止めない)。
                try
                {
                    _onCreated?.Invoke(asset);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DDrive] 新規アセット作成後のエディタ切り替えに失敗しました: {e.Message}");
                }

                Close();
            }
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
