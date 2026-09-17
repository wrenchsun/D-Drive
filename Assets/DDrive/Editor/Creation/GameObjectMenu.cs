using System;
using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Bootstrap;
using DDrive.Editor.CanvasTool;
using DDrive.Editor.Menu;
using DDrive.Editor.Ui;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace DDrive.Editor.Creation
{
    // [11_tasks.md] U-18 / U-19(2026-09-17) / [09_editor_tools.md] §6.2 —
    // Hierarchy の右クリック(Unity 標準の "GameObject/" メニュー)から D-Drive の基本オブジェクトを配置する。
    //
    // 共通の約束:
    //  - 右クリックした GameObject の子として置く(MenuCommand.context。GameObjectUtility.SetParentAndAlign)
    //  - Undo.RegisterCreatedObjectUndo で Ctrl+Z で消せる
    //  - 置いたオブジェクトを選択状態にする
    //  - 標準プレハブ(Assets/GameData/Prefabs/<ドメイン>/)があるものは AddComponent で組まずプレハブを
    //    インスタンス化する([10_workflow.md] §3.3)。無ければ DefaultPrefabs が生成してから置く
    internal static class GameObjectMenu
    {
        private const int Priority = DDriveMenu.GameObjectPriority;

        // ── 標準プレハブ ──

        [MenuItem(DDriveMenu.GameObjectRoot + "SeEmitter", false, Priority)]
        private static void CreateSeEmitter(MenuCommand command)
        {
            if (!ShouldRun(command))
            {
                return;
            }

            PlacePrefab(DefaultPrefabs.EnsureSeEmitterPrefab(), command);
        }

        [MenuItem(DDriveMenu.GameObjectRoot + "AnchorRig", false, Priority)]
        private static void CreateAnchorRig(MenuCommand command)
        {
            if (!ShouldRun(command))
            {
                return;
            }

            // 既定の AnchorRig.prefab があればそれを、無ければ標準生成(DefaultPrefabs)で 1 つ作ってから置く。
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultPrefabs.AnchorRigFolder + "/AnchorRig.prefab")
                         ?? DefaultPrefabs.CreateAnchorRigPrefab();
            PlacePrefab(prefab, command);
        }

        [MenuItem(DDriveMenu.GameObjectRoot + "AnchorPoint(選択中の子に追加)", false, Priority)]
        private static void CreateAnchorPoint(MenuCommand command)
        {
            if (!ShouldRun(command))
            {
                return;
            }

            var go = new GameObject("Anchor_New", typeof(AnchorPoint));
            Register(go, command);

            if (go.GetComponentInParent<AnchorRig>() == null)
            {
                Debug.LogWarning("[DDrive] AnchorPoint は AnchorRig の下に置いてください(AnchorRig が親に見つかりませんでした)。[21_anchor_spec.md] §2");
            }
        }

        [MenuItem(DDriveMenu.GameObjectRoot + "標準プレハブ...", false, Priority)]
        private static void CreateFromStandardPrefabs(MenuCommand command)
        {
            if (!ShouldRun(command))
            {
                return;
            }

            var prefabs = FindStandardPrefabs();
            if (prefabs.Count == 0)
            {
                Debug.LogWarning($"[DDrive] {DefaultPrefabs.PrefabRoot} に標準プレハブがありません。Tools > D-Drive > Generate > 標準プレハブを生成 を実行してください。");
                return;
            }

            var menu = new GenericMenu();
            foreach (var path in prefabs)
            {
                var captured = path;
                // メニュー上は PrefabRoot からの相対パスで階層表示する(Audio/SeEmitter のように出る)。
                var label = captured.Substring(DefaultPrefabs.PrefabRoot.Length + 1).Replace(".prefab", string.Empty);
                menu.AddItem(new GUIContent(label), false,
                    () => PlacePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(captured), command));
            }

            menu.ShowAsContext();
        }

        // ── UI ──

        // U-19: Canvas + Panel + CanvasData を 1 操作で。実体は CanvasSetupService(Tools メニューと同じ経路)。
        [MenuItem(DDriveMenu.GameObjectRoot + "Canvas + Panel(CanvasData も作成)", false, Priority)]
        private static void CreateCanvasWithPanel(MenuCommand command)
        {
            if (!ShouldRun(command))
            {
                return;
            }

            CanvasSetupService.CreateCanvasWithPanel(command?.context as GameObject, placeInScene: true);
        }

        [MenuItem(DDriveMenu.GameObjectRoot + "UiButton", false, Priority)]
        private static void CreateUiButton(MenuCommand command)
        {
            if (!ShouldRun(command))
            {
                return;
            }

            var go = new GameObject("UiButton", typeof(RectTransform), typeof(Image), typeof(UiButton));
            ((RectTransform)go.transform).sizeDelta = new Vector2(200f, 60f);
            go.GetComponent<UiButton>().TargetGraphic = go.GetComponent<Image>();
            Register(go, command);
            WarnIfOutsideCanvas(go);
        }

        [MenuItem(DDriveMenu.GameObjectRoot + "UiSlider", false, Priority)]
        private static void CreateUiSlider(MenuCommand command)
        {
            if (!ShouldRun(command))
            {
                return;
            }

            // 組み立て(Track + Fill + Handle)は PreviewSliderFactory と共有する。dontSave:false で
            // シーンに残る実オブジェクトとして作る。
            var slider = PreviewSliderFactory.Create(null, "UiSlider", Vector2.zero, dontSave: false);
            Register(slider.gameObject, command);
            WarnIfOutsideCanvas(slider.gameObject);
        }

        // ── 起動オブジェクト ──

        [MenuItem(DDriveMenu.GameObjectRoot + "起動オブジェクト(DDriveRuntimeBootstrap)", false, Priority)]
        private static void CreateBootstrap()
        {
            // シーンに 1 つだけの特別なオブジェクト。Tools メニューと同じ既存処理をそのまま使う([02] §14。
            // 既にあればそれを選択してカタログを再収集するだけ = 何度押しても増えない)。
            BootstrapSceneSetup.PlaceInScene();
        }

        // ── 共通処理 ──

        // "GameObject/" のメニュー項目は、選択中の GameObject の数だけ(context を変えて)呼ばれる Unity 仕様。
        // 1 回のクリックで 1 個だけ作りたいので、右クリックした本体(Selection.activeGameObject)に対応する
        // 呼び出しだけを通す。Hierarchy の何も無い所での右クリック / メニューバーからの実行は context=null で
        // 1 回しか来ないため、そのまま通す。
        private static bool ShouldRun(MenuCommand command)
        {
            var context = command?.context as GameObject;
            return context == null || Selection.activeGameObject == null || context == Selection.activeGameObject;
        }

        private static void PlacePrefab(GameObject prefab, MenuCommand command)
        {
            if (prefab == null)
            {
                Debug.LogWarning("[DDrive] 配置するプレハブが見つかりませんでした。");
                return;
            }

            if (PrefabUtility.InstantiatePrefab(prefab) is not GameObject instance)
            {
                Debug.LogWarning($"[DDrive] プレハブをインスタンス化できませんでした: {AssetDatabase.GetAssetPath(prefab)}");
                return;
            }

            Register(instance, command);
        }

        private static void Register(GameObject go, MenuCommand command)
        {
            GameObjectUtility.SetParentAndAlign(go, command?.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            if (go.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(go.scene);
            }
        }

        private static void WarnIfOutsideCanvas(GameObject go)
        {
            if (go.GetComponentInParent<Canvas>() == null)
            {
                Debug.LogWarning($"[DDrive] '{go.name}' は Canvas の下に置いてください(親に Canvas が見つかりませんでした)。GameObject > D-Drive > Canvas + Panel で Canvas を作れます。");
            }
        }

        // Assets/GameData/Prefabs 配下の全プレハブ(パス昇順)。[09] §9: FindAssets は AssetSearch 経由。
        private static List<string> FindStandardPrefabs()
        {
            var result = new List<string>();
            if (!AssetDatabase.IsValidFolder(DefaultPrefabs.PrefabRoot))
            {
                return result;
            }

            foreach (var guid in AssetSearch.FindAssets("t:Prefab", new[] { DefaultPrefabs.PrefabRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                {
                    result.Add(path);
                }
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }
    }
}
