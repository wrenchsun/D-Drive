using DDrive.Editor.Validation;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Settings
{
    // [26_timeline.md] §4.6.5 契約 G-1(2026-10-06、P-15 確認 Q-4) — Project Settings > D-Drive > 実行順の検査の除外。
    // `DDriveProjectSettings.CameraExecutionOrderExemptions` を編集する(用途は「カメラを読むだけ」のスクリプトを
    // 実行順の検査 = Validation > Run All の Warning から外すこと。外部パッケージの型はそのパッケージの Editor 側の宣言が本筋)。
    //
    // 作りは ForbiddenApiAllowSettingsProvider と同じ(M-4 / FZ-R-04): ScriptableSingleton を SerializedObject で編集し、
    // 開いたとき(activateHandler)に 1 回だけ作り、Undo / Redo されたら保存し直す。
    public static class CameraExecutionOrderExemptionSettingsProvider
    {
        public const string SettingsPath = "Project/D-Drive/実行順の検査の除外";

        private static SerializedObject _serialized;

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "実行順の検査の除外",
                activateHandler = (_, _) => OnActivate(),
                deactivateHandler = OnDeactivate,
                guiHandler = _ => DrawGui(),
                keywords = new[] { "D-Drive", "Cutscene", "ExecutionOrder", "実行順", "カメラ" },
            };
        }

        private static void OnActivate()
        {
            var settings = DDriveProjectSettings.instance;
            settings.hideFlags &= ~HideFlags.NotEditable;
            _serialized = new SerializedObject(settings);
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private static void OnDeactivate()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            _serialized = null;
        }

        private static void OnUndoRedo()
        {
            DDriveProjectSettings.instance.SaveCameraExecutionOrderExemptions();
        }

        private static void DrawGui()
        {
            EditorGUILayout.HelpBox(
                "Cutscene のカメラ適用(実行順 1000)以降に動くスクリプトは、LateUpdate でカメラを書くと Cutscene のカメラが効かなくなるため、" +
                "Validation > Run All で Warning になります(契約 G-1)。カメラを「読むだけ」のスクリプトは、ここに型の完全修飾名と理由を書くと" +
                "検査から外れます。理由は必須、型は実在する完全修飾名(名前空間付き)です(無効な要素は Warning になり、除外されません)。" +
                "外部パッケージのスクリプトは、そのパッケージの Editor 側が自動で宣言するので、ここに書く必要はありません。",
                MessageType.Info);

            var settings = DDriveProjectSettings.instance;
            if (_serialized == null || _serialized.targetObject == null)
            {
                OnActivate();
            }

            var so = _serialized;
            so.Update();
            var list = so.FindProperty("_cameraExecutionOrderExemptions");
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(list, new GUIContent("除外の一覧"), true);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(settings, "Edit Camera Execution Order Exemptions");
                so.ApplyModifiedProperties();
                settings.SaveCameraExecutionOrderExemptions();
            }

            var resolution = CameraExecutionOrderExemptions.Resolve(
                System.Array.Empty<ICameraExecutionOrderExemptionProvider>(),
                settings.CameraExecutionOrderExemptions,
                CameraExecutionOrderExemptions.TypeExistsInLoadedAssemblies);
            foreach (var problem in resolution.Problems)
            {
                EditorGUILayout.HelpBox(problem, MessageType.Warning);
            }
        }
    }
}
