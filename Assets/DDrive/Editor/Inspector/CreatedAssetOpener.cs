using System;
using DDrive.Foundation.Data;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Inspector
{
    // [11_tasks.md] U-16(2026-09-17) — 「新規作成した直後の Data を、その種別の専用エディタで開く」共通処理。
    // 経路は増やさず、既存の [DataEditor] 属性 → DataEditorRegistry.OpenDefault([09_editor_tools.md] §8)を
    // そのまま使う(Inspector の「エディターで開く」列の先頭ボタン / AssetBrowser のダブルクリックと同じ)。
    //
    // 専用エディタが無い種別(将来増えた場合)は Ping + Selection だけで終わる。例外で止めない([00] §0-4)。
    public static class CreatedAssetOpener
    {
        // 作成直後の Data を選択・Ping したうえで専用エディタを開く。エディタを開けたら true。
        public static bool Reveal(AssetDataBase created)
        {
            if (created == null)
            {
                return false;
            }

            EditorGUIUtility.PingObject(created);
            Selection.activeObject = created;

            try
            {
                return DataEditorRegistry.OpenDefault(created);
            }
            catch (Exception e)
            {
                // ウィンドウ側の CreateGUI 等で落ちても、作成そのものは成功しているので警告に留める。
                Debug.LogWarning($"[DDrive] 作成した {created.GetType().Name} の専用エディタを開けませんでした: {e.Message}");
                return false;
            }
        }
    }
}
