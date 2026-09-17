using System;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Menu;
using DDrive.Foundation.Data;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DDrive.Editor.CanvasTool
{
    // [11_tasks.md] U-19(2026-09-17) / [07_canvas_prefab.md] Part A —
    // 「CanvasData を作るのに毎回 Canvas を作って Panel を足して…」を 1 操作にまとめる。
    //
    // 並行経路を作らないこと:
    //  - Data の作成は既存の NewAssetDialog(種別を CanvasData に固定、[09] §8.3 の Open(Type[], Action) オーバーロード)
    //    → AssetCreationService.Create(ファイル名・ID・カタログ・Addressables 登録)をそのまま通る。
    //  - 作成後のコールバックで Canvas + Panel の Prefab を組み立てて CanvasData.Prefab に入れ、Canvas Editor を開く。
    //  - Hierarchy から呼んだ場合だけ、続けてその Prefab を選択中のオブジェクトの子としてシーンへ配置する。
    //
    // 生成する Prefab の形([07] A-2 / DesignerManual/canvas-data.html の「最小構成」):
    //   <CANVAS_…>            RectTransform + Canvas + CanvasScaler + GraphicRaycaster
    //     └ Panel             RectTransform + Image(四辺ストレッチ)
    // ルートの Canvas は UiManager が overrideSorting + sortingOrder(= Layer*100 + SortOffset)で使う
    // (UiManager.OpenData。Canvas が無い場合は兄弟順で並べ替えるだけになる)。
    public static class CanvasSetupService
    {
        // 標準プレハブと同じ場所に置く([10_workflow.md] §3.3: Assets/GameData/Prefabs/<ドメイン>/ はツール管理)。
        // SourceAssets/Canvas/ に置くと ImportRule([09] §1.1)が 2 つ目の CanvasData を作ってしまうため使わない。
        public const string PrefabFolder = DefaultPrefabs.PrefabRoot + "/Canvas";

        public const string PanelName = "Panel";

        // Tools メニュー / Hierarchy メニューの共通入口。placeUnder に GameObject を渡すと、
        // 作成後にその子として Prefab インスタンスをシーンへ配置する(null ならアセットを作るだけ)。
        public static void CreateCanvasWithPanel(GameObject placeUnder, bool placeInScene)
        {
            // ダイアログを閉じるまでの間に親が消される可能性があるため、使うときに Unity の null 判定をする。
            var parent = placeUnder;
            NewAssetDialog.Open(
                new[] { typeof(CanvasData) },
                created => OnCanvasDataCreated(created as CanvasData, parent, placeInScene));
        }

        [MenuItem(DDriveMenu.Generate + "Canvas + Panel と CanvasData を作成")]
        private static void CreateFromToolsMenu() => CreateCanvasWithPanel(null, placeInScene: false);

        private static void OnCanvasDataCreated(CanvasData data, GameObject parent, bool placeInScene)
        {
            if (data == null)
            {
                Debug.LogWarning("[DDrive] CanvasData が作成されなかったため、Canvas + Panel の生成を中止しました。");
                return;
            }

            var prefab = CreateCanvasPrefab(data.name);
            if (prefab == null)
            {
                // Prefab が作れなくても CanvasData 自体は有効なので、エディタだけ開いて続行する([00] §0-4)。
                DDrive.Editor.Inspector.CreatedAssetOpener.Reveal(data);
                return;
            }

            // [00] §0-5: Data をエディタが書き換えるときは Undo.RecordObject + EditorUtility.SetDirty。
            Undo.RecordObject(data, "Assign Canvas Prefab");
            data.Prefab = prefab;
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);

            Debug.Log($"[DDrive] Canvas + Panel を作成しました: {AssetDatabase.GetAssetPath(prefab)}(CanvasData: {data.name})");

            // 先に Canvas Editor を開いて対象をセットし(Reveal は Data を選択状態にする)、その後でシーンに
            // 置いたインスタンスを選択状態にする。Canvas Editor は Selection が CanvasData 以外に変わっても
            // 対象を外さないため、この順序なら「エディタは開いたまま、Hierarchy では置いたものが選ばれている」になる。
            DDrive.Editor.Inspector.CreatedAssetOpener.Reveal(data);

            if (placeInScene)
            {
                PlaceInstance(prefab, parent != null ? parent : null);
            }
        }

        // Canvas + Panel のプレハブを作る(シーンには残さない)。baseName は CanvasData のファイル名(CANVAS_…)。
        public static GameObject CreateCanvasPrefab(string baseName, string folder = PrefabFolder)
        {
            if (string.IsNullOrEmpty(baseName))
            {
                baseName = "CANVAS_New";
            }

            AssetCreationService.EnsureFolder(folder);
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{baseName}.prefab");

            var root = BuildCanvasWithPanel(baseName);
            try
            {
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] Canvas プレハブの保存に失敗しました({path}): {e.Message}");
                return null;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // 「Canvas を作る → Panel を作る」の組み立て本体。シーンに置く場合もプレハブ化する場合もここを通る。
        public static GameObject BuildCanvasWithPanel(string rootName)
        {
            var root = new GameObject(
                string.IsNullOrEmpty(rootName) ? "Canvas" : rootName,
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var panel = new GameObject(PanelName, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root.transform, false);

            // 四辺ストレッチ(親いっぱい)。Unity 標準の GameObject > UI > Panel と同じ形。
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.39f);

            return root;
        }

        // 作った Prefab をシーンへ配置する(Hierarchy メニュー用)。Undo 可能・配置したものを選択状態にする。
        public static GameObject PlaceInstance(GameObject prefab, GameObject parent)
        {
            if (prefab == null)
            {
                return null;
            }

            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null)
            {
                Debug.LogWarning($"[DDrive] Canvas プレハブをシーンに配置できませんでした: {AssetDatabase.GetAssetPath(prefab)}");
                return null;
            }

            GameObjectUtility.SetParentAndAlign(instance, parent);
            Undo.RegisterCreatedObjectUndo(instance, "Create " + instance.name);
            EnsureEventSystem();
            Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(instance.scene);
            return instance;
        }

        // UI を触れるようにするには EventSystem が要る(CanvasPreviewSceneSetup と同じ手順を使い回す)。
        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) != null)
            {
                return;
            }

            var created = Preview.CanvasPreviewSceneSetup.AddEventSystem();
            if (created != null)
            {
                Undo.RegisterCreatedObjectUndo(created, "Create EventSystem");
            }
        }
    }
}
