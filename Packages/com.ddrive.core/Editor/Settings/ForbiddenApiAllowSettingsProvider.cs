using DDrive.Editor.Validation;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Settings
{
    // [11_tasks.md] M-4(2026-10-05) — Project Settings > D-Drive > 禁止 API の除外。
    // `DDriveProjectSettings.ForbiddenApiAllowEntries` を編集する(用途は自分で書き換えられない外部コード・
    // 生成コード。自分のコードの個別の行は行単位の許可コメントを使う)。
    //
    // [57_review_round4_m4_2026-10-05.md] FZ-R-04(2026-10-05) — ScriptableSingleton を SerializedObject で編集する。
    // 実機(Unity 6000.3)で hideFlags は HideInHierarchy | DontSave(NotEditable なし)で、追加・反映・保存・
    // Undo/Redo が効くことを確認済み。念のため NotEditable が付いていたら外してから使う。SerializedObject は
    // 開いたとき(activateHandler)に 1 回だけ作り、Undo/Redo されたら保存し直す。
    public static class ForbiddenApiAllowSettingsProvider
    {
        public const string SettingsPath = "Project/D-Drive/禁止 API の除外";

        private static SerializedObject _serialized;

        // 要素ごとの問題の表示。描画のたびに走査ルートの解決・要素の検査をしない(マウスを動かすだけで再描画されるため)。
        // 開いたとき・一覧を変更したとき・Undo / Redo のときだけ作り直す(docs/58 GA-R-09)。
        private static readonly System.Collections.Generic.List<string> _problemTexts = new();

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "禁止 API の除外",
                activateHandler = (_, _) => OnActivate(),
                deactivateHandler = OnDeactivate,
                guiHandler = _ => DrawGui(),
                keywords = new[] { "D-Drive", "ForbiddenApi", "ddrive-allow", "禁止 API" },
            };
        }

        private static void OnActivate()
        {
            var settings = DDriveProjectSettings.instance;
            settings.hideFlags &= ~HideFlags.NotEditable;
            _serialized = new SerializedObject(settings);
            Undo.undoRedoPerformed -= OnUndoRedo; // 二重購読しない(OnDeactivate の解除は 1 回)
            Undo.undoRedoPerformed += OnUndoRedo;
            RefreshProblems();
        }

        private static void RefreshProblems()
        {
            _problemTexts.Clear();
            var scanRoot = CI.ResolveForbiddenApiScanRoot();
            foreach (var entry in DDriveProjectSettings.instance.ForbiddenApiAllowEntries)
            {
                var problem = ForbiddenApiScanner.DescribeEntryProblem(entry, scanRoot);
                if (problem != null)
                {
                    _problemTexts.Add($"'{entry?.Path}': {problem}");
                }
            }
        }

        private static void OnDeactivate()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            _serialized = null;
        }

        private static void OnUndoRedo()
        {
            // Undo / Redo で一覧が変わったらファイルにも反映する。
            DDriveProjectSettings.instance.SaveForbiddenApiAllowEntries();
            RefreshProblems();
        }

        private static void DrawGui()
        {
            EditorGUILayout.HelpBox(
                "自分で書き換えられない外部コード・生成コードのフォルダ/ファイルを、禁止 API の検査から外します。" +
                "自分のコードの個別の行は、行末または直前の行に `// ddrive-allow: 規則名(理由)` を書いてください。" +
                $"理由は必須です(空の要素は無効)。規則名: {string.Join(" / ", ForbiddenApiScanner.RuleNames)}(空欄 = 全規則)。" +
                "パスはプロジェクトルートからの相対パス(2 階層以上。Assets 単独・走査ルートそのもの・絶対パス・.. は無効)で、" +
                "ファイルに一致するか、フォルダの配下に一致します。",
                MessageType.Info);

            var settings = DDriveProjectSettings.instance;
            if (_serialized == null || _serialized.targetObject == null)
            {
                OnActivate();
            }

            var so = _serialized;
            so.Update();
            var list = so.FindProperty("_forbiddenApiAllowEntries");
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(list, new GUIContent("除外の一覧"), true);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(settings, "Edit Forbidden API Allow Entries");
                so.ApplyModifiedProperties();
                settings.SaveForbiddenApiAllowEntries();
                RefreshProblems();
            }

            foreach (var text in _problemTexts)
            {
                EditorGUILayout.HelpBox(text, MessageType.Warning);
            }
        }
    }
}
