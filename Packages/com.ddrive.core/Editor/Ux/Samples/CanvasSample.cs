using System.Collections.Generic;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace DDrive.Editor.CanvasTool
{
    // 実運用規模の確認用サンプル(docs/1010 D-10。HUD 相当 = 要素 12 + 埋め込み 3 + ボタン配線 6 + ElementFx 設定済みと未設定が混在)。
    // 指定のフォルダ(Assets/ 配下)に、親 Prefab・子 Prefab 3 つ・CanvasData 4 つを作る。呼び出し側が使い終わったらフォルダごと消す
    // (実データ Assets/GameData には作らない。Addressables にも登録しない)。スクリーンショット・テスト・見通しの確認用。
    internal static class CanvasSample
    {
        public const ulong ParentId = 0xC0DE0001;
        private static readonly string[] ChildNames = { "InteractPrompt", "GaugeView", "ResultView" };

        public sealed class Result
        {
            public CanvasData Parent;
            public CanvasData[] Children;
            public string Folder;
        }

        public static Result Build(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                var parent = System.IO.Path.GetDirectoryName(folder).Replace((char)92, (char)47);
                AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
            }

            _layoutCount = 0;
            var children = new CanvasData[ChildNames.Length];
            var childPrefabs = new GameObject[ChildNames.Length];
            for (var i = 0; i < ChildNames.Length; i++)
            {
                var go = new GameObject(ChildNames[i], typeof(RectTransform), typeof(Image));
                AddText(go.transform, "Label");
                AddButton(go.transform, "BtnOk");
                AddImage(go.transform, "Icon");
                childPrefabs[i] = PrefabUtility.SaveAsPrefabAsset(go, $"{folder}/{ChildNames[i]}.prefab");
                Object.DestroyImmediate(go);
                children[i] = CreateCanvas($"{folder}/CANVAS_{ChildNames[i]}.asset", ParentId + 1 + (ulong)i, childPrefabs[i]);
                children[i].Flags.Load = DDrive.Foundation.Data.LoadMode.Preload;
                children[i].ElementEffects = CanvasElementFxCollector.CollectMerged(childPrefabs[i], null);
                children[i].ElementEffects[0].AppearPreset = new UiPresetRef { Preset = UiPreset.FadeIn };
                EditorUtility.SetDirty(children[i]);
            }

            var root = new GameObject("HudSample", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var panel = AddImage(root.transform, "Panel");
            AddImage(root.transform, "Frame");
            AddText(panel, "Title");
            AddText(panel, "Score");
            AddText(panel, "Timer");
            AddImage(panel, "Life");
            AddImage(panel, "Icon");
            foreach (var b in new[] { "BtnPause", "BtnMap", "BtnItem", "BtnOption", "BtnSkip" })
            {
                AddButton(panel, b);
            }

            var group = new GameObject("Group", typeof(RectTransform));
            group.transform.SetParent(root.transform, false);
            for (var i = 0; i < ChildNames.Length; i++)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(childPrefabs[i], group.transform);
                inst.name = ChildNames[i];
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{folder}/HudSample.prefab");
            Object.DestroyImmediate(root);

            var data = CreateCanvas($"{folder}/CANVAS_HudSample.asset", ParentId, prefab);
            data.DisplayName = "HUD(サンプル)";
            data.Flags.Load = DDrive.Foundation.Data.LoadMode.Preload;
            data.Layer = UiLayer.HUD;
            var embeds = new EmbeddedCanvas[ChildNames.Length];
            for (var i = 0; i < ChildNames.Length; i++)
            {
                embeds[i] = new EmbeddedCanvas
                {
                    RootPath = "Group/" + ChildNames[i],
                    Canvas = new AssetId<CanvasMarker>(children[i].Id, AssetType.Canvas),
                    StartInactive = i == 2,
                };
            }

            data.EmbeddedCanvases = embeds;
            var fx = CanvasElementFxCollector.CollectMerged(prefab, null, CanvasEmbeddedEditing.RegisteredRoots(data));
            // 設定済みと未設定が混在(先頭の約 4 割だけ設定)。
            var presets = new[] { UiPreset.PopIn, UiPreset.SlideInLeft, UiPreset.FadeIn, UiPreset.ScaleIn, UiPreset.SlideInTop };
            for (var i = 0; i < fx.Length; i++)
            {
                if (i % 5 == 0 || i % 5 == 2)
                {
                    fx[i].AppearPreset = new UiPresetRef { Preset = presets[i % presets.Length] };
                    fx[i].DisappearPreset = new UiPresetRef { Preset = i % 2 == 0 ? UiPreset.FadeOut : UiPreset.PopOut };
                }

                if (i % 5 == 0)
                {
                    fx[i].IdlePreset = new UiPresetRef { Preset = UiPreset.Pulse };
                }
            }

            data.ElementEffects = fx;
            var wires = new List<ButtonWire>();
            var signals = new[] { "pause", "map", "item", "option", "skip" };
            var buttons = new[] { "Panel/BtnPause", "Panel/BtnMap", "Panel/BtnItem", "Panel/BtnOption", "Panel/BtnSkip" };
            for (var i = 0; i < buttons.Length; i++)
            {
                wires.Add(new ButtonWire { ButtonPath = buttons[i], Trigger = WireTrigger.Click, Action = UiAction.SendSignal, SignalKey = signals[i] });
            }

            wires.Add(new ButtonWire { ButtonPath = buttons[0], Trigger = WireTrigger.LongPress, Action = UiAction.CloseSelf });
            wires.Add(new ButtonWire { ButtonPath = buttons[3], Trigger = WireTrigger.DoubleClick, Action = UiAction.ToggleEmbedded, EmbeddedRootPath = "Group/ResultView" });
            data.Buttons = wires.ToArray();
            var nav = CanvasNavigationCollector.CollectMerged(prefab, null);
            for (var i = 0; i + 1 < nav.Length; i++)
            {
                nav[i].Down = nav[i + 1].Element;
                nav[i + 1].Up = nav[i].Element;
            }

            data.Navigation = nav;
            data.FirstSelected = buttons[0];
            EditorUtility.SetDirty(data);
            DDrive.Editor.Versioning.DDriveAssetSave.SaveAllSuppressed();
            return new Result { Parent = data, Children = children, Folder = folder };
        }

        private static CanvasData CreateCanvas(string path, ulong id, GameObject prefab)
        {
            var data = ScriptableObject.CreateInstance<CanvasData>();
            data.Id = id;
            data.Prefab = prefab;
            AssetDatabase.CreateAsset(data, path);
            return data;
        }

        private static int _layoutCount;

        // 要素を格子状に散らす(Navigation グラフのノードが重ならないように)。
        private static void Place(GameObject go)
        {
            ((RectTransform)go.transform).anchoredPosition = new Vector2(_layoutCount % 4 * 220, -(_layoutCount / 4) * 110);
            _layoutCount++;
        }

        private static Transform AddImage(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Place(go);
            return go.transform;
        }

        private static Transform AddText(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Place(go);
            return go.transform;
        }

        private static Transform AddButton(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(UiButton), typeof(Button));
            go.transform.SetParent(parent, false);
            Place(go);
            return go.transform;
        }
    }
}
