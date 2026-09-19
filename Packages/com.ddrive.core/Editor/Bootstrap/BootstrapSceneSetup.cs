using System.Collections.Generic;
using DDrive.Editor.Menu;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Loop;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DDrive.Editor.Bootstrap
{
    // [02_core_framework.md] §14 — 起動オブジェクト(DDriveRuntimeBootstrap)をシーンに置き、プロジェクト内の
    // カタログを直参照で割り当てる。カタログを追加したら Inspector の「カタログを再収集」で更新する。
    public static class BootstrapSceneSetup
    {
        public const string ObjectName = "[D-Drive] Runtime";

        [MenuItem(DDriveMenu.Generate + "起動オブジェクト(DDriveRuntimeBootstrap)をシーンに配置")]
        public static void PlaceInScene()
        {
            var existing = Object.FindFirstObjectByType<DDriveRuntimeBootstrap>(FindObjectsInactive.Include);
            if (existing != null)
            {
                RefreshCatalogs(existing);
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing.gameObject);
                Debug.Log($"[DDrive] 起動オブジェクトは既に '{existing.name}' にあります。カタログを再収集しました({existing.Catalogs?.Length ?? 0} 件)。");
                return;
            }

            var go = new GameObject(ObjectName);
            Undo.RegisterCreatedObjectUndo(go, "Place D-Drive Runtime");
            var bootstrap = Undo.AddComponent<DDriveRuntimeBootstrap>(go);
            RefreshCatalogs(bootstrap);
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log($"[DDrive] 起動オブジェクト '{ObjectName}' を配置しました(カタログ {bootstrap.Catalogs.Length} 件)。シーンを保存してください。");
        }

        // プロジェクト内の全 AssetCatalog(テスト用フォルダ配下は除く)を割り当てる。
        public static void RefreshCatalogs(DDriveRuntimeBootstrap bootstrap)
        {
            if (bootstrap == null)
            {
                return;
            }

            var catalogs = FindProjectCatalogs();
            Undo.RecordObject(bootstrap, "Refresh D-Drive Catalogs");
            bootstrap.Catalogs = catalogs.ToArray();
            EditorUtility.SetDirty(bootstrap);
        }

        public static List<AssetCatalog> FindProjectCatalogs()
        {
            var result = new List<AssetCatalog>();
            foreach (var guid in AssetSearch.FindAssets("t:" + nameof(AssetCatalog)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Tests/"))
                {
                    continue;
                }

                var catalog = AssetDatabase.LoadAssetAtPath<AssetCatalog>(path);
                if (catalog != null)
                {
                    result.Add(catalog);
                }
            }

            result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return result;
        }
    }

    [CustomEditor(typeof(DDriveRuntimeBootstrap))]
    public sealed class DDriveRuntimeBootstrapEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var bootstrap = (DDriveRuntimeBootstrap)target;
            EditorGUILayout.HelpBox("D-Drive の起動オブジェクト(唯一の Composition Root)。Registry / Pool / 各 Manager を組み立て、GameLoop へ登録し、静的ファサードを Bind します。シーンに 1 つだけ置いてください。", MessageType.Info);
            if (GUILayout.Button("カタログを再収集(GameData/Catalogs)", GUILayout.Height(24)))
            {
                BootstrapSceneSetup.RefreshCatalogs(bootstrap);
            }

            DrawDefaultInspector();

            if (Application.isPlaying && bootstrap == DDriveRuntimeBootstrap.Instance)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("実行中", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(bootstrap.IsReady ? $"● Ready(カタログ {bootstrap.RegisteredCatalogCount} 件)" : "… カタログ登録中");
                if (bootstrap.Anim != null)
                {
                    EditorGUILayout.LabelField($"Anim {bootstrap.Anim.ActiveCount} / VFX {bootstrap.Vfx.ActiveCount} / Groups {bootstrap.Groups.ActiveCount}");
                }
            }
        }
    }
}
