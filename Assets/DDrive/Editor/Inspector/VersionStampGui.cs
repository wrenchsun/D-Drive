using System;
using System.Globalization;
using DDrive.Foundation.Data;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Inspector
{
    // [11_tasks.md] 6-3 / [09_editor_tools.md] §4 — 保存フック(VersionStampProcessor)が記録した
    // Version/Author/UpdatedAt を「今の値だけ」1 行で表示する(過去履歴は持たない。履歴は git に任せる)。
    // ChangeNote(手入力のまま。自動では消さない)があれば、その下に追加で表示する。
    // AssetDataInspector.DrawOpenEditorHeader() から DataEditorHeader.Draw の直後(「エディターで開く」の近く)に呼ぶ。
    public static class VersionStampGui
    {
        public static void Draw(AssetDataBase target)
        {
            if (target == null)
            {
                return;
            }

            EditorGUILayout.LabelField(BuildLine(target), EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(target.ChangeNote))
            {
                EditorGUILayout.LabelField(target.ChangeNote, EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.Space(2f);
        }

        // UI Toolkit 製の専用エディタから使う場合用(DataEditorHeader.Build と同じ位置付け)。
        public static UnityEngine.UIElements.VisualElement Build(AssetDataBase target)
        {
            var container = new UnityEngine.UIElements.VisualElement { style = { marginBottom = 2 } };
            var line = new UnityEngine.UIElements.Label(target != null ? BuildLine(target) : string.Empty)
            {
                style = { fontSize = 10, opacity = 0.8f },
            };
            container.Add(line);

            if (target != null && !string.IsNullOrEmpty(target.ChangeNote))
            {
                container.Add(new UnityEngine.UIElements.Label(target.ChangeNote)
                {
                    style = { fontSize = 10, opacity = 0.8f, whiteSpace = UnityEngine.UIElements.WhiteSpace.Normal },
                });
            }

            return container;
        }

        private static string BuildLine(AssetDataBase target)
        {
            if (target.Version <= 0)
            {
                return "未保存(保存すると v1 になります)";
            }

            var author = string.IsNullOrEmpty(target.Author) ? "-" : target.Author;
            return $"v{target.Version} ・ {author} ・ {FormatForDisplay(target.UpdatedAt)}";
        }

        // VersionStampProcessor.FormatTimestamp(ISO 8601, 秒まで)と対応。パースできない場合は生の文字列を返す
        // (手編集や旧データ等で想定外の形式が入っていても例外にしない、[00] §0-4)。
        public static string FormatForDisplay(string iso)
        {
            if (string.IsNullOrEmpty(iso))
            {
                return "-";
            }

            return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? dt.ToString("yyyy-MM-dd HH:mm")
                : iso;
        }
    }
}
