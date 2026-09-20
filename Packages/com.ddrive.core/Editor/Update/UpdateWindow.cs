using System.IO;
using DDrive.Editor.Menu;
using DDrive.Editor.Migration;
using DDrive.Editor.Setup;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.2 手順 5/§6 P-8(2026-09-20) — 持ち込み先が版を進めた直後に開く更新ウィンドウ。
    // 表示 = 現在の版 / 前回適用した版 / その間の CHANGELOG の該当節(「破壊あり」があれば警告)。
    // 「更新を適用」= マイグレーション → ID/Tuning 再生成 → Addressables 同期 → Validation → LastAppliedVersion
    // 更新の順で実行し、途中で失敗したら止まる(UpdateActions.Apply に委譲)。
    // P-7 のマイグレーション専用メニュー(MigrationMenu)はそのまま残しつつ、同じ操作をここにも置く
    // (統合してよい、の指示に沿って重複実装はしない)。
    // [09_editor_tools.md] §7 のとおり ScrollView ルート、メニューは DDriveMenu 経由。
    public sealed class UpdateWindow : EditorWindow
    {
        [MenuItem(DDriveMenu.Update + "更新ウィンドウ")]
        public static void Open()
        {
            var window = GetWindow<UpdateWindow>("D-Drive 更新");
            window.minSize = new Vector2(480, 360);
        }

        private VisualElement _versionsBody;
        private VisualElement _changelogBody;
        private VisualElement _migrationBody;
        private VisualElement _applyBody;
        private VisualElement _testablesBody;
        private VisualElement _skillBody;

        public void CreateGUI()
        {
            var scrollView = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            rootVisualElement.Add(scrollView);

            scrollView.Add(new Label("D-Drive 更新ウィンドウ") { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 14, marginBottom = 4 } });
            scrollView.Add(WrappingLabel("持ち込み先の manifest.json を新しい版に進めた直後に使います([docs/42_distribution.md] §4.2)。"));

            // CHANGELOG 表示部分は「1. 版と CHANGELOG」の再検査時に丸ごと Clear() されるため、
            // _versionsBody の子ではなく同じ Foldout の別の子(兄弟)にする(親を Clear すると
            // 子ごと消えてしまい、再検査後に _changelogBody が描画されなくなる事故を避ける)。
            var versionsSection = new Foldout { text = "1. 版と CHANGELOG", value = true };
            _versionsBody = new VisualElement();
            _changelogBody = new VisualElement();
            versionsSection.Add(_versionsBody);
            versionsSection.Add(_changelogBody);
            scrollView.Add(versionsSection);

            scrollView.Add(BuildSection("2. マイグレーション(プレビュー)", out _migrationBody));
            scrollView.Add(BuildSection("3. 更新を適用", out _applyBody));
            scrollView.Add(BuildSection("4. テストを有効化する(既定 OFF)", out _testablesBody));
            scrollView.Add(BuildSection("5. エージェント向けスキルを更新", out _skillBody));

            RefreshVersionsSection();
            RefreshMigrationSection();
            RefreshApplySection();
            RefreshTestablesSection();
            RefreshSkillSection();
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

        // ── 1. 版と CHANGELOG ──

        private static UnityEditor.PackageManager.PackageInfo ResolvePackageInfo()
            => UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DDrive.Runtime.DDriveVersion).Assembly);

        private static string ResolveCurrentVersion(UnityEditor.PackageManager.PackageInfo packageInfo)
            => !string.IsNullOrEmpty(packageInfo?.version) ? packageInfo.version : DDrive.Runtime.DDriveVersion.Value;

        private void RefreshVersionsSection()
        {
            _versionsBody.Clear();
            _changelogBody.Clear();

            var packageInfo = ResolvePackageInfo();
            var currentVersion = ResolveCurrentVersion(packageInfo);
            var settings = DDrive.Editor.Settings.DDriveProjectSettings.instance;
            var lastApplied = settings.LastAppliedVersion;

            _versionsBody.Add(WrappingLabel($"現在の版: {currentVersion}"));
            _versionsBody.Add(WrappingLabel($"前回適用した版: {(string.IsNullOrEmpty(lastApplied) ? "未適用" : lastApplied)}"));

            var changelogPath = packageInfo != null
                ? ChangelogLocator.ResolvePath(packageInfo.resolvedPath, settings.IsDevelopmentRepo)
                : null;
            if (changelogPath == null)
            {
                _changelogBody.Add(WrappingLabel("CHANGELOG.md が見つかりませんでした。"));
            }
            else
            {
                var sections = ChangelogRangeReader.ParseSections(File.ReadAllText(changelogPath));
                var range = ChangelogRangeReader.ExtractRange(sections, lastApplied, currentVersion);

                if (range.Count == 0)
                {
                    _changelogBody.Add(WrappingLabel("表示できる更新節がありません(前回適用した版が現在の版と同じか、CHANGELOG に該当節がありません)。"));
                }

                var hasBreaking = false;
                foreach (var section in range)
                {
                    if (ChangelogCompatibilityAnalyzer.HasBreakingChange(section.Body))
                    {
                        hasBreaking = true;
                    }
                }

                if (hasBreaking)
                {
                    var warning = WrappingLabel("⚠ 「破壊あり」の変更が含まれています。適用前に docs/migrations/ の移行ガイドを確認してください([docs/42_distribution.md] §5.12)。");
                    warning.style.color = new Color(0.85f, 0.2f, 0.2f);
                    _changelogBody.Add(warning);
                }

                foreach (var section in range)
                {
                    _changelogBody.Add(new Label(section.HeaderLine) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 4 } });
                    var compat = ChangelogCompatibilityAnalyzer.ExtractCompatibilityText(section.Body);
                    if (!string.IsNullOrEmpty(compat))
                    {
                        _changelogBody.Add(WrappingLabel("互換性: " + compat.Trim()));
                    }
                }
            }

            _versionsBody.Add(new Button(() => { RefreshVersionsSection(); RefreshMigrationSection(); }) { text = "再検査" });
        }

        // ── 2. マイグレーション(プレビュー) ──

        private void RefreshMigrationSection()
        {
            _migrationBody.Clear();

            var plan = DDriveMigrationRunner.PlanProject();
            _migrationBody.Add(WrappingLabel(plan.TotalCount == 0
                ? "未適用のマイグレーションはありません。"
                : $"未適用のマイグレーションが {plan.TotalCount} 件あります(Data {plan.DataMigrations.Count} 件 / プロジェクト {plan.ProjectMigrations.Count} 件)。"));
            _migrationBody.Add(WrappingLabel("実際の適用は下の「更新を適用」に含まれます。ここでは件数の確認のみできます。"));
            _migrationBody.Add(new Button(RefreshMigrationSection) { text = "再検査" });
        }

        // ── 3. 更新を適用 ──

        private void RefreshApplySection()
        {
            _applyBody.Clear();
            _applyBody.Add(WrappingLabel("順番: (1) マイグレーション → (2) ID/Tuning 再生成 → (3) Addressables 同期 → (4) Validation → (5) 前回適用した版を更新。途中で失敗したら以降は実行しません。"));

            var log = new ScrollView(ScrollViewMode.Vertical) { style = { height = 140, borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1 } };
            _applyBody.Add(new Button(() => RunApply(log)) { text = "更新を適用" });
            _applyBody.Add(log);
        }

        private void RunApply(ScrollView log)
        {
            log.Clear();

            var packageInfo = ResolvePackageInfo();
            var currentVersion = ResolveCurrentVersion(packageInfo);
            var steps = UpdateStepsFactory.CreateRealSteps(currentVersion);
            var result = UpdateActions.Apply(steps);

            foreach (var outcome in result.StepOutcomes)
            {
                var line = (outcome.Success ? "✓ " : "✗ ") + outcome.Name + ": " + outcome.Message;
                log.Add(WrappingLabel(line));

                if (outcome.Success)
                {
                    Debug.Log($"[DDrive][Update] {line}");
                }
                else
                {
                    Debug.LogError($"[DDrive][Update] {line}");
                }
            }

            if (result.MarkAppliedCalled)
            {
                log.Add(WrappingLabel($"✓ LastAppliedVersion を {currentVersion} に更新しました。"));
                Debug.Log($"[DDrive][Update] LastAppliedVersion を {currentVersion} に更新しました。");
            }
            else if (result.StepOutcomes.Count > 0)
            {
                var failWarning = WrappingLabel("途中で失敗したため、以降の手順は実行していません(LastAppliedVersion は更新されていません)。");
                failWarning.style.color = new Color(0.85f, 0.2f, 0.2f);
                log.Add(failWarning);
            }

            RefreshVersionsSection();
            RefreshMigrationSection();
        }

        // ── 4. テストを有効化する ──

        private void RefreshTestablesSection()
        {
            _testablesBody.Clear();
            var enabled = ProjectSetupActions.IsTestablesEnabled();
            var toggle = new Toggle("D-Drive のテストを有効化する(Test Runner に表示)") { value = enabled };
            toggle.RegisterValueChangedCallback(evt =>
            {
                ProjectSetupActions.SetTestablesEnabled(evt.newValue);
                RefreshTestablesSection();
            });
            _testablesBody.Add(toggle);
            _testablesBody.Add(WrappingLabel("既定 OFF。ON にすると manifest.json の testables に com.ddrive.core が追加され、Test Runner にテストが表示されます([docs/42_distribution.md] §2.1)。"));
        }

        // ── 5. エージェント向けスキルを更新 ──

        private void RefreshSkillSection()
        {
            _skillBody.Clear();
            var packageInfo = ResolvePackageInfo();
            var bundled = packageInfo != null && Directory.Exists(
                Path.Combine(packageInfo.resolvedPath, "Documentation~", "skills", "ddrive-consumer"));

            _skillBody.Add(WrappingLabel(bundled
                ? "パッケージに消費側スキル(ddrive-consumer)が同梱されています。"
                : "未同梱(P-10 で追加予定)。現時点では何も行いません。"));

            _skillBody.Add(new Button(() =>
            {
                if (packageInfo == null)
                {
                    Debug.Log("[DDrive][Update] エージェント向けスキルの更新: パッケージ情報が取得できませんでした。");
                    return;
                }

                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var result = ProjectSetupActions.CopyConsumerSkillIfBundled(projectRoot, packageInfo.version);
                Debug.Log($"[DDrive][Update] エージェント向けスキルの更新: {result}");
                RefreshSkillSection();
            })
            { text = "エージェント向けスキルを更新" });
        }
    }
}
