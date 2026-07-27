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

        public static void Open(AudioClip[] pendingClips = null)
        {
            var window = GetWindow<NewAssetDialog>(utility: true, title: "新規アセット作成");
            window._pendingClips = pendingClips;
            window.minSize = new Vector2(380, 230);
            window.RefreshPreview();
        }

        private void CreateGUI()
        {
            _definitions = AssetIdLookup.GetAllDefinitions()
                .Where(d => d.dataType.Namespace?.Contains("Tests") != true)
                .OrderBy(d => d.assetType.ToString())
                .ToList();

            if (_definitions.Count == 0)
            {
                rootVisualElement.Add(new HelpBox("作成可能なアセット種別が見つかりません。", HelpBoxMessageType.Warning));
                return;
            }

            var root = rootVisualElement;
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 8;

            var choices = _definitions.Select(d => $"{d.assetType} ({d.dataType.Name})").ToList();
            _typeField = new DropdownField("種別", choices, 0);
            _typeField.RegisterValueChangedCallback(_ => RefreshPreview());
            root.Add(_typeField);

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
