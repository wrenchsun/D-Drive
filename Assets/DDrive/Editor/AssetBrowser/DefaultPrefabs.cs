using DDrive.Editor.Menu;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Audio;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.AssetBrowser
{
    // [01_architecture.md] §5 — D-Drive 標準プレハブの置き場と生成。
    // シーンへ配置して使う既製コンポーネント(SeEmitter 等)は、各自が AddComponent で組むのではなく
    // ここで生成した標準プレハブ(Assets/GameData/Prefabs/<ドメイン>/)を使うことを推奨する。
    // 生成は冪等(既存があればそれを返すだけで上書きしない — 手で調整した既定値を壊さない)。
    public static class DefaultPrefabs
    {
        public const string PrefabRoot = AssetCreationService.DefaultGameDataRoot + "/Prefabs";
        public const string SeEmitterPrefabPath = PrefabRoot + "/Audio/SeEmitter.prefab";

        [MenuItem(DDriveMenu.Generate + "標準プレハブを生成")]
        public static void GenerateAll()
        {
            var prefab = EnsureSeEmitterPrefab();
            Debug.Log($"[DDrive] 標準プレハブ生成完了: {AssetDatabase.GetAssetPath(prefab)}");
            EditorGUIUtility.PingObject(prefab);
        }

        public const string AnchorRigFolder = PrefabRoot + "/Anchors";

        // AnchorRig は SeEmitter と違い「用途ごとに複数作る」前提のため、クリックごとに
        // 新しいプレハブを生成する(ユニーク名)。生成後は Project ウィンドウでリネームし、
        // プレハブモードで子の AnchorPoint を追加・配置して使う([04_vfx.md] §2.5)。
        [MenuItem(DDriveMenu.Generate + "Anchor プレハブを生成")]
        public static void GenerateAnchorRig()
        {
            var prefab = CreateAnchorRigPrefab();
            Debug.Log($"[DDrive] Anchor プレハブを生成しました: {AssetDatabase.GetAssetPath(prefab)}。プレハブを開いて子の AnchorPoint を配置してください。");
            EditorGUIUtility.PingObject(prefab);
            Selection.activeObject = prefab;
        }

        public static GameObject CreateAnchorRigPrefab(string folder = AnchorRigFolder)
        {
            AssetCreationService.EnsureFolder(folder);
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/AnchorRig.prefab");

            var root = new GameObject("AnchorRig");
            root.AddComponent<AnchorRig>();

            var point = new GameObject("Anchor_Main");
            point.AddComponent<AnchorPoint>();
            point.transform.SetParent(root.transform);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        public static GameObject EnsureSeEmitterPrefab(string prefabPath = SeEmitterPrefabPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null)
            {
                return existing;
            }

            var folder = System.IO.Path.GetDirectoryName(prefabPath)?.Replace('\\', '/');
            AssetCreationService.EnsureFolder(folder);

            var go = new GameObject("SeEmitter");
            go.AddComponent<SeEmitter>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }
    }
}
