using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Bootstrap;
using DDrive.Editor.Menu;
using DDrive.Editor.Settings;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Setup
{
    // [42_distribution.md] §3.6/§6 P-6(2026-09-20) — 持ち込み先で最初に 1 回、更新後にも再実行できる
    // セットアップウィザード。手順は上から順に「検査 → 提案 → 適用(ボタン)」で、各段は独立して
    // 再実行できる([09_editor_tools.md] §7 の規約どおり ScrollView ルート、[06] メニューは
    // DDriveMenu 経由)。実際の検査/計算ロジックは ProjectSetupInspector、副作用のある適用は
    // ProjectSetupActions に置き、このクラスは UI の組み立てとイベント配線だけを持つ。
    public sealed class ProjectSetupWizardWindow : EditorWindow
    {
        [MenuItem(DDriveMenu.Setup + "セットアップウィザード")]
        public static void Open()
        {
            var window = GetWindow<ProjectSetupWizardWindow>("D-Drive セットアップ");
            window.minSize = new Vector2(480, 360);
        }

        private readonly List<AddRequest> _pendingRequests = new();
        private VisualElement _dependenciesBody;
        private VisualElement _settingsBody;
        private VisualElement _folderBody;
        private VisualElement _defaultsBody;
        private VisualElement _addressablesBody;
        private VisualElement _bootstrapBody;
        private VisualElement _testablesBody;
        private VisualElement _skillBody;
        private VisualElement _summaryBody;

        private FolderLayoutPreset _preset = FolderLayoutPreset.Default;
        private string _parentFolder = "Assets/_Project/DDrive";
        private string _customGameDataRoot = AssetCreationService.DefaultGameDataRoot;
        private string _customGeneratedRoot = "Assets/Generated";
        private string _customSourceAssetsRoot = DDrive.Editor.Import.ImportRuleService.DefaultSourceRoot;
        private string _customSpecsRoot = "Specs";

        private void OnEnable()
        {
            EditorApplication.update += PollPendingRequests;
        }

        private void OnDisable()
        {
            EditorApplication.update -= PollPendingRequests;
        }

        private void PollPendingRequests()
        {
            if (_pendingRequests.Count == 0)
            {
                return;
            }

            for (var i = _pendingRequests.Count - 1; i >= 0; i--)
            {
                var request = _pendingRequests[i];
                if (request == null || request.IsCompleted)
                {
                    _pendingRequests.RemoveAt(i);
                    if (request != null && request.Status == StatusCode.Failure && request.Error != null)
                    {
                        Debug.LogError($"[DDrive] 依存パッケージの追加に失敗しました: {request.Error.message}");
                    }

                    RefreshDependenciesSection();
                }
            }
        }

        public void CreateGUI()
        {
            var scrollView = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            rootVisualElement.Add(scrollView);

            scrollView.Add(new Label("D-Drive セットアップウィザード") { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 14, marginBottom = 4 } });
            scrollView.Add(WrappingLabel("上から順に「検査 → 提案 → 適用」を行います。各段は単独で再実行できます([docs/42_distribution.md] §3.6)。"));

            scrollView.Add(BuildSection("1. 依存パッケージ", out _dependenciesBody));
            scrollView.Add(BuildSection("2. ProjectSettings", out _settingsBody));
            scrollView.Add(BuildSection("3. 置き場所", out _folderBody));
            scrollView.Add(BuildSection("4. 既定フォルダ・設定の生成", out _defaultsBody));
            scrollView.Add(BuildSection("5. Addressables 同期", out _addressablesBody));
            scrollView.Add(BuildSection("6. 起動オブジェクト", out _bootstrapBody));
            scrollView.Add(BuildSection("7. テストを有効化する(既定 OFF)", out _testablesBody));
            scrollView.Add(BuildSection("8. エージェント向けスキル", out _skillBody));
            scrollView.Add(BuildSection("9. 完了チェック", out _summaryBody));

            RefreshDependenciesSection();
            RefreshProjectSettingsSection();
            RefreshFolderSection();
            RefreshDefaultsSection();
            RefreshAddressablesSection();
            RefreshBootstrapSection();
            RefreshTestablesSection();
            RefreshSkillSection();
            RefreshSummarySection();
        }

        private static VisualElement BuildSection(string title, out VisualElement body)
        {
            var foldout = new Foldout { text = title, value = true };
            body = new VisualElement();
            foldout.Add(body);
            return foldout;
        }

        private static Label WrappingLabel(string text) => new(text)
        {
            style = { whiteSpace = WhiteSpace.Normal, flexShrink = 1, minWidth = 0, marginBottom = 4 },
        };

        // ── 1. 依存パッケージ ──

        private void RefreshDependenciesSection()
        {
            _dependenciesBody.Clear();

            var manifest = ManifestJson.LoadProjectManifest();
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DDriveProjectSettings).Assembly);
            var sourceText = packageInfo != null ? $"com.ddrive.core の解決方式: {packageInfo.source}({packageInfo.resolvedPath})" : "com.ddrive.core の解決方式: 不明";
            _dependenciesBody.Add(WrappingLabel(sourceText));

            var missing = ProjectSetupInspector.InspectMissingGitDependencies(manifest);
            if (missing.Count == 0)
            {
                _dependenciesBody.Add(WrappingLabel("✓ UniTask / R3 / org.nuget.r3(scoped registry) はすべて manifest.json にあります。"));
            }

            foreach (var dependency in missing)
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center, marginBottom = 2 } };
                var messageLabel = WrappingLabel($"⚠ {dependency.Message}");
                messageLabel.style.flexGrow = 1;
                row.Add(messageLabel);
                var button = new Button(() => ApplyMissingDependency(dependency)) { text = "追加" };
                row.Add(button);
                _dependenciesBody.Add(row);
            }

            var ngoPresent = ProjectSetupInspector.IsNgoPresent(manifest);
            _dependenciesBody.Add(WrappingLabel(
                ngoPresent
                    ? "NGO(com.unity.netcode.gameobjects)は導入済みです(任意依存)。"
                    : "NGO(com.unity.netcode.gameobjects)は未導入です(任意依存。マルチプレイを使う場合のみ導入してください)。"));

            _dependenciesBody.Add(new Button(RefreshDependenciesSection) { text = "再検査" });
        }

        private void ApplyMissingDependency(MissingDependency dependency)
        {
            if (dependency.Kind == MissingDependencyKind.ScopedRegistry)
            {
                ProjectSetupActions.AddScopedRegistryToProjectManifest(
                    ProjectSetupInspector.NuGetScopedRegistryName,
                    ProjectSetupInspector.NuGetScopedRegistryUrl,
                    ProjectSetupInspector.NuGetScopedRegistryScope);
                RefreshDependenciesSection();
                return;
            }

            var request = ProjectSetupActions.AddDependency(dependency);
            if (request != null)
            {
                _pendingRequests.Add(request);
            }
        }

        // ── 2. ProjectSettings ──

        private void RefreshProjectSettingsSection()
        {
            _settingsBody.Clear();
            var status = ProjectSetupInspector.InspectProjectSettings();

            _settingsBody.Add(WrappingLabel((status.UrpActive ? "✓ " : "✗ ") + "URP(Universal Render Pipeline)がアクティブなレンダーパイプラインです。"));
            _settingsBody.Add(WrappingLabel((status.InputSystemActive ? "✓ " : "✗ ") + "Active Input Handling が Input System(または Both)です。"));
            _settingsBody.Add(WrappingLabel((status.ApiCompatibilityOk ? "✓ " : "✗ ") + "API Compatibility Level が .NET Standard 2.1 相当です。"));

            if (!status.AllOk)
            {
                _settingsBody.Add(WrappingLabel("これらは自動変更しません(Editor 再起動を伴う設定があるため)。下のボタンから Project Settings を開いて直してください。"));
                _settingsBody.Add(new Button(() => SettingsService.OpenProjectSettings("Project/Player")) { text = "Project Settings > Player を開く" });
            }

            _settingsBody.Add(new Button(RefreshProjectSettingsSection) { text = "再検査" });
        }

        // ── 3. 置き場所 ──

        private void RefreshFolderSection()
        {
            _folderBody.Clear();
            var settings = DDriveProjectSettings.instance;

            _folderBody.Add(WrappingLabel($"現在: GameData={settings.GameDataRoot} / Generated={settings.GeneratedRoot} / SourceAssets={settings.SourceAssetsRoot} / Specs={settings.SpecsRoot}"));

            var hasExistingData = AssetDatabase.IsValidFolder(settings.GameDataRoot)
                && AssetSearch.FindAssets("t:" + nameof(DDrive.Foundation.Data.AssetDataBase), new[] { settings.GameDataRoot }).Length > 0;
            if (hasExistingData)
            {
                var warningLabel = WrappingLabel("⚠ 既に GameData に Data があります。置き場所を変えても既存データは移動しません(AssetReorganizer で個別に移動してください)。");
                warningLabel.style.color = new Color(0.85f, 0.55f, 0.1f);
                _folderBody.Add(warningLabel);
            }

            var presetField = new EnumField("プリセット", _preset);
            presetField.RegisterValueChangedCallback(evt =>
            {
                _preset = (FolderLayoutPreset)evt.newValue;
                RefreshFolderSection();
            });
            _folderBody.Add(presetField);

            if (_preset == FolderLayoutPreset.UnderParentFolder)
            {
                var parentField = new TextField("親フォルダ") { value = _parentFolder };
                parentField.RegisterValueChangedCallback(evt => _parentFolder = evt.newValue);
                _folderBody.Add(parentField);
                _folderBody.Add(WrappingLabel($"→ GameData={_parentFolder}/GameData 等になります。"));
            }
            else if (_preset == FolderLayoutPreset.Custom)
            {
                _folderBody.Add(BuildTextField("GameData", _customGameDataRoot, v => _customGameDataRoot = v));
                _folderBody.Add(BuildTextField("Generated", _customGeneratedRoot, v => _customGeneratedRoot = v));
                _folderBody.Add(BuildTextField("SourceAssets", _customSourceAssetsRoot, v => _customSourceAssetsRoot = v));
                _folderBody.Add(BuildTextField("Specs", _customSpecsRoot, v => _customSpecsRoot = v));
            }

            _folderBody.Add(new Button(ApplyFolderLayout) { text = "適用" });
        }

        private static TextField BuildTextField(string label, string value, System.Action<string> onChange)
        {
            var field = new TextField(label) { value = value };
            field.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            return field;
        }

        private void ApplyFolderLayout()
        {
            var custom = new FolderLayoutPaths(_customGameDataRoot, _customGeneratedRoot, _customSourceAssetsRoot, _customSpecsRoot);
            var paths = ProjectSetupInspector.ComputeFolderLayout(_preset, _parentFolder, custom);
            ProjectSetupActions.ApplyFolderLayout(paths);
            RefreshFolderSection();
            RefreshDefaultsSection();
            RefreshSummarySection();
        }

        // ── 4. 既定フォルダ・設定の生成 ──

        private void RefreshDefaultsSection()
        {
            _defaultsBody.Clear();
            var settings = DDriveProjectSettings.instance;
            var folders = ProjectSetupInspector.InspectFolderLayout(settings);

            _defaultsBody.Add(WrappingLabel((folders.GameDataRootExists ? "✓ " : "✗ ") + $"GameData ルート({settings.GameDataRoot})"));
            _defaultsBody.Add(WrappingLabel((folders.UiLayerSettingsExists ? "✓ " : "✗ ") + "UiLayerSettings(UI_LayerSettings.asset)"));
            _defaultsBody.Add(WrappingLabel((folders.SpecSettingsExists ? "✓ " : "✗ ") + "DDriveSpecSettings / DDriveTuningTable"));

            var asmdefToggle = new Toggle("DDrive.Generated.asmdef を出力する") { value = settings.EmitGeneratedAsmdef };
            asmdefToggle.RegisterValueChangedCallback(evt => settings.EmitGeneratedAsmdef = evt.newValue);
            _defaultsBody.Add(asmdefToggle);

            _defaultsBody.Add(new Button(ApplyDefaults) { text = "生成(SourceAssets/UiLayerSettings/DDriveSpecSettings/カタログ + ID/Tuning 再生成)" });
        }

        private void ApplyDefaults()
        {
            var settings = DDriveProjectSettings.instance;
            var report = ProjectSetupActions.EnsureDefaultFoldersAndSettings(settings);
            ProjectSetupActions.RegenerateGeneratedCode(settings.EmitGeneratedAsmdef);
            Debug.Log($"[DDrive] セットアップウィザード: 既定フォルダ・設定を生成しました。\n{report}");
            RefreshDefaultsSection();
            RefreshSummarySection();
        }

        // ── 5. Addressables 同期 ──

        // [48_p11_install_test_2026-09-20.md] フォローアップ — 「4. 既定フォルダ・設定の生成」は
        // 種別ごとの空カタログ(.asset)を作るだけで Addressables への登録まではしない
        // (`AssetCreationService.Create` は「そのとき作った Data が属するカタログ」だけを登録するため、
        // まだ Data が 1 件も無い残り十数種別のカタログは未登録のまま残る)。P-11 では
        // 消費側の glue コードから `AddressablesSync.SyncAll` を直接呼んで解消していたが、
        // ウィザード自体にはその手段が無かった。`UpdateStepsFactory.SyncAddressablesStep`
        // (Tools > D-Drive > Update)と同じ `AddressablesSync.SyncAll(log: true)` を呼ぶボタンを
        // ここに追加し、初回セットアップでも更新と同じ手段でまとめて同期できるようにする。
        private void RefreshAddressablesSection()
        {
            _addressablesBody.Clear();
            var initialized = ProjectSetupInspector.IsAddressablesInitialized();
            _addressablesBody.Add(WrappingLabel((initialized ? "✓ " : "✗ ") + "Addressables が初期化されています(AddressableAssetSettings)。"));

            if (!initialized)
            {
                _addressablesBody.Add(new Button(() =>
                {
                    ProjectSetupActions.EnsureAddressablesInitialized();
                    RefreshAddressablesSection();
                    RefreshSummarySection();
                })
                { text = "初期化" });
            }

            _addressablesBody.Add(WrappingLabel(
                "Data の Address/カタログ登録は各アセット作成時、または Validation > Run All の「修正」ボタンから個別に同期されますが、"
                + "「4. 既定フォルダ・設定の生成」で作った(まだ Data が無い)空カタログは、その種別の Data を 1 件も作らない限り自動では登録されません。"
                + "下のボタンでまとめて同期できます(AddressablesSync.SyncAll。Tools > D-Drive > Update の「Addressables 登録を同期」と同じ処理)。"));

            using (new EditorGUI.DisabledScope(!initialized))
            {
                _addressablesBody.Add(new Button(SyncAllAddressables) { text = "全カタログ・Data を今すぐ同期する" });
            }

            _addressablesBody.Add(new Button(RefreshAddressablesSection) { text = "再検査" });
        }

        private void SyncAllAddressables()
        {
            if (!ProjectSetupInspector.IsAddressablesInitialized())
            {
                Debug.LogWarning("[DDrive] Addressables が未初期化のため同期をスキップしました。先に「初期化」を実行してください。");
                return;
            }

            var (fixedAssets, catalogs, missingCatalog) = AddressablesSync.SyncAll(log: true);
            Debug.Log($"[DDrive] セットアップウィザード: Addressables 同期(修正 {fixedAssets} 件・カタログ {catalogs} 件・カタログ未登録 {missingCatalog} 件)。");
            RefreshAddressablesSection();
            RefreshSummarySection();
        }

        // ── 6. 起動オブジェクト ──

        private void RefreshBootstrapSection()
        {
            _bootstrapBody.Clear();
            var existing = Object.FindFirstObjectByType<DDrive.Runtime.Loop.DDriveRuntimeBootstrap>(FindObjectsInactive.Include);
            _bootstrapBody.Add(WrappingLabel(existing != null
                ? $"✓ 現在のシーンに起動オブジェクト('{existing.name}')があります。"
                : "✗ 現在のシーンに起動オブジェクト(DDriveRuntimeBootstrap)がありません。"));
            _bootstrapBody.Add(new Button(() =>
            {
                BootstrapSceneSetup.PlaceInScene();
                RefreshBootstrapSection();
                RefreshSummarySection();
            })
            { text = "配置(またはカタログを再収集)" });
        }

        // ── 7. テストを有効化する ──

        private void RefreshTestablesSection()
        {
            _testablesBody.Clear();
            var enabled = ProjectSetupActions.IsTestablesEnabled();
            var toggle = new Toggle("D-Drive のテストを有効化する(Test Runner に表示)") { value = enabled };
            toggle.RegisterValueChangedCallback(evt =>
            {
                ProjectSetupActions.SetTestablesEnabled(evt.newValue);
                RefreshTestablesSection();
                RefreshSummarySection();
            });
            _testablesBody.Add(toggle);
            _testablesBody.Add(WrappingLabel("既定 OFF。ON にすると manifest.json の testables に com.ddrive.core が追加され、Test Runner にテストが表示されます。"));
        }

        // ── 8. エージェント向けスキル ──

        private void RefreshSkillSection()
        {
            _skillBody.Clear();
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DDriveProjectSettings).Assembly);
            var bundled = packageInfo != null && System.IO.Directory.Exists(
                System.IO.Path.Combine(packageInfo.resolvedPath, "Documentation~", "skills", "ddrive-consumer"));

            _skillBody.Add(WrappingLabel(bundled
                ? "パッケージに消費側スキル(ddrive-consumer)が同梱されています。"
                : "未同梱(P-10 で追加予定)。現時点では何も行いません。"));

            if (bundled)
            {
                _skillBody.Add(new Button(() =>
                {
                    var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
                    var version = packageInfo.version;
                    var result = ProjectSetupActions.CopyConsumerSkillIfBundled(projectRoot, version);
                    Debug.Log($"[DDrive] 消費側スキルのコピー: {result}");
                    RefreshSkillSection();
                })
                { text = ".claude/skills/ddrive-consumer へコピー" });
            }
        }

        // ── 9. 完了チェック ──

        private void RefreshSummarySection()
        {
            _summaryBody.Clear();
            var settings = DDriveProjectSettings.instance;
            var manifest = ManifestJson.LoadProjectManifest();

            AddSummaryRow(_summaryBody, "依存パッケージ", ProjectSetupInspector.InspectMissingGitDependencies(manifest).Count == 0);
            var projectSettingsStatus = ProjectSetupInspector.InspectProjectSettings();
            AddSummaryRow(_summaryBody, "ProjectSettings(URP/Input/API Level)", projectSettingsStatus.AllOk);
            AddSummaryRow(_summaryBody, "Addressables 初期化", ProjectSetupInspector.IsAddressablesInitialized());
            var folders = ProjectSetupInspector.InspectFolderLayout(settings);
            AddSummaryRow(_summaryBody, "既定フォルダ・設定", folders.AllOk);
            AddSummaryRow(_summaryBody, "起動オブジェクト", Object.FindFirstObjectByType<DDrive.Runtime.Loop.DDriveRuntimeBootstrap>(FindObjectsInactive.Include) != null);

            _summaryBody.Add(new Button(() =>
            {
                RefreshDependenciesSection();
                RefreshProjectSettingsSection();
                RefreshFolderSection();
                RefreshDefaultsSection();
                RefreshAddressablesSection();
                RefreshBootstrapSection();
                RefreshTestablesSection();
                RefreshSkillSection();
                RefreshSummarySection();
            })
            { text = "すべて再検査" });

            _summaryBody.Add(new Button(DDrive.Editor.CI.RunAllMenuItem) { text = "Validation > Run All を実行" });
        }

        private static void AddSummaryRow(VisualElement parent, string label, bool ok)
        {
            parent.Add(new Label((ok ? "✓ " : "⚠ ") + label));
        }
    }
}
