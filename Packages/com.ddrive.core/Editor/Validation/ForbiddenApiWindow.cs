using System.Collections.Generic;
using DDrive.Editor.Menu;
using DDrive.Editor.Settings;
using DDrive.Foundation.Validation;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Validation
{
    // [57_review_round4_m4_2026-10-05.md] FZ-R-03 — 禁止 API の検査結果を Editor の中で見る入口。
    // 「許可されていない当たり(ファイル:行・規則名・該当行。クリックでその行を開く)」「許可済み(理由つき)」
    // 「無効・未使用の許可」を一覧する。CI.ValidateAll と同じ走査ルート・同じ設定を使い、読み取りだけを行う
    // (`Validation > Run All` には混ぜない。docs/42 §5.8)。ルートは ScrollView([09_editor_tools.md] §7)。
    public sealed class ForbiddenApiWindow : EditorWindow
    {
        private ScrollView _body;
        private Label _status;

        // メニューから開いたときだけ走査する(ドメインリロード後の CreateGUI では走査しない。static はリロードで false に戻る)。
        // ウィンドウを開いたままスクリプトを保存するたびに、Assets 配下の全 .cs を読み直さないため(docs/58 GA-R-08)。
        private static bool s_scanOnCreate;

        [MenuItem(DDriveMenu.Validation + "禁止 API の検査")]
        public static void Open()
        {
            // 2026-10-06(docs/59 GB-R-08): 初めて開くときは CreateGUI が 1 回だけ走査する(CreateGUI が GetWindow の中で同期的に呼ばれても、
            // 後から呼ばれても 2 回走査しない)。既に開いていたときだけ、開き直しの操作として再走査する。
            var alreadyOpen = HasOpenInstances<ForbiddenApiWindow>();
            s_scanOnCreate = true;
            var window = GetWindow<ForbiddenApiWindow>("禁止 API の検査");
            window.minSize = new Vector2(560, 320);
            if (alreadyOpen && window._body != null)
            {
                s_scanOnCreate = false;
                window.Rescan();
            }
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;

            var toolbar = new UnityEditor.UIElements.Toolbar();
            toolbar.Add(new UnityEditor.UIElements.ToolbarButton(Rescan) { text = "再走査" });
            toolbar.Add(new UnityEditor.UIElements.ToolbarButton(() => SettingsService.OpenProjectSettings(ForbiddenApiAllowSettingsProvider.SettingsPath))
            {
                text = "除外の設定を開く",
            });
            root.Add(toolbar);

            _status = new Label { style = { whiteSpace = WhiteSpace.Normal, marginLeft = 6, marginTop = 4, marginBottom = 4 } };
            root.Add(_status);

            _body = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            root.Add(_body);

            if (s_scanOnCreate)
            {
                s_scanOnCreate = false;
                Rescan();
            }
            else
            {
                _status.text = "スクリプトの再コンパイル後は自動では走査しません。「再走査」を押すと検査します。";
            }
        }

        private void Rescan()
        {
            var root = CI.ResolveForbiddenApiScanRoot();
            var report = ForbiddenApiScanner.ScanDetailed(root, DDriveProjectSettings.instance.ForbiddenApiAllowEntries);
            Render(root, report);
        }

        private void Render(string scanRoot, ForbiddenApiScanner.ScanReport report)
        {
            _body.Clear();

            var problems = new List<ForbiddenApiScanner.Notice>();
            foreach (var n in report.Notices)
            {
                if (n.Code != ForbiddenApiScanner.CodeAllowSummary)
                {
                    problems.Add(n);
                }
            }

            _status.text = $"走査ルート: {scanRoot}  /  許可されていない当たり {report.Violations.Count} 件・" +
                           $"許可済み {report.Allowed.Count} 件(コメント {report.CommentAllowedCount}、設定 {report.SettingsAllowedCount})・" +
                           $"無効 / 未使用の許可 {problems.Count} 件。CI(CI.ValidateAll)では「許可されていない当たり」が Error になります。";

            var violations = AddSection($"許可されていない当たり({report.Violations.Count})", report.Violations.Count > 0);
            if (report.Violations.Count == 0)
            {
                violations.Add(new Label("なし"));
            }

            foreach (var v in report.Violations)
            {
                var head = string.IsNullOrEmpty(v.RuleName) ? string.Empty : $"[{v.RuleName}] ";
                AddRow(violations, v.FilePath, v.Line, head + (v.Excerpt ?? v.Message), v.Message);
            }

            var allowed = AddSection($"許可済み({report.Allowed.Count})", false);
            if (report.Allowed.Count == 0)
            {
                allowed.Add(new Label("なし"));
            }

            foreach (var a in report.Allowed)
            {
                var src = a.Source == ForbiddenApiScanner.AllowSource.Comment ? "コメント" : "設定";
                AddRow(allowed, a.FilePath, a.Line, $"[{a.RuleName}] ({src}) {a.Reason}", null);
            }

            var invalid = AddSection($"無効・未使用の許可({problems.Count})", problems.Count > 0);
            if (problems.Count == 0)
            {
                invalid.Add(new Label("なし"));
            }

            foreach (var n in problems)
            {
                var sev = n.Severity == ValidationSeverity.Warning ? "Warning" : "Info";
                AddRow(invalid, n.FilePath, n.Line, $"[{sev}] {n.Message}", null);
            }
        }

        private VisualElement AddSection(string title, bool open)
        {
            var foldout = new Foldout { text = title, value = open };
            _body.Add(foldout);
            return foldout;
        }

        private static void AddRow(VisualElement parent, string path, int line, string text, string tooltip)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2 } };
            var label = line > 0 ? $"{path}:{line}" : path;
            var button = new Button(() => OpenAt(path, line)) { text = ShortPath(path) + (line > 0 ? ":" + line : string.Empty), tooltip = label };
            button.style.flexShrink = 0;
            button.style.maxWidth = 300;
            row.Add(button);
            var body = new Label(text) { tooltip = tooltip ?? text, style = { whiteSpace = WhiteSpace.Normal, flexGrow = 1, flexShrink = 1, marginLeft = 6 } };
            row.Add(body);
            parent.Add(row);
        }

        private static string ShortPath(string path)
        {
            var normalized = ForbiddenApiScanner.ToProjectRelative((path ?? string.Empty).Replace('\\', '/'));
            return normalized.Length <= 60 ? normalized : "…" + normalized.Substring(normalized.Length - 59);
        }

        // クリックで該当ファイルの該当行を外部エディタで開く。MonoScript として読めれば Unity の標準の開き方、
        // 読めなければ(Packages 外など)OS 側の関連付けで開く。
        private static void OpenAt(string path, int line)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var normalized = path.Replace('\\', '/');
            var relative = ForbiddenApiScanner.ToProjectRelative(normalized);
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(relative);
            if (script != null)
            {
                AssetDatabase.OpenAsset(script, line > 0 ? line : 1);
                return;
            }

            InternalEditorUtility.OpenFileAtLineExternal(System.IO.Path.GetFullPath(normalized), line > 0 ? line : 1);
        }
    }
}
