using System.Collections.Generic;
using DDrive.Runtime.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace DDrive.Editor.CanvasTool
{
    // [15_ui_interaction.md] B-4 — CanvasEditorWindow の「要素を自動収集」「一括適用」ボタンの実体(4-9)。
    // CanvasNavigationCollector と同じ「既存の行は保持し、無いものだけ追加する」方針。
    public static class CanvasElementFxCollector
    {
        // Prefab 内の Graphic(Image/Text 等)と UiInteractable(UiButton 等)のパスを ElementEffects へ追加する。
        public static ElementFx[] CollectMerged(GameObject prefab, ElementFx[] existing)
        {
            var map = new Dictionary<string, ElementFx>(System.StringComparer.Ordinal);
            var order = new List<string>();
            AddExisting(existing, map, order);

            var graphics = prefab.GetComponentsInChildren<Graphic>(true);
            for (var i = 0; i < graphics.Length; i++)
            {
                AddPathIfMissing(GetPath(prefab.transform, graphics[i].transform), map, order);
            }

            var interactables = prefab.GetComponentsInChildren<UiInteractable>(true);
            for (var i = 0; i < interactables.Length; i++)
            {
                AddPathIfMissing(GetPath(prefab.transform, interactables[i].transform), map, order);
            }

            return ToArray(map, order);
        }

        // Prefab 内の全 UiButton の AppearPreset を preset に設定する(行が無ければ追加する)。
        public static ElementFx[] ApplyPresetToButtons(GameObject prefab, ElementFx[] existing, UiPreset preset)
        {
            var map = new Dictionary<string, ElementFx>(System.StringComparer.Ordinal);
            var order = new List<string>();
            AddExisting(existing, map, order);

            var buttons = prefab.GetComponentsInChildren<UiButton>(true);
            for (var i = 0; i < buttons.Length; i++)
            {
                var path = GetPath(prefab.transform, buttons[i].transform);
                if (!map.TryGetValue(path, out var fx))
                {
                    fx = new ElementFx { ElementPath = path };
                    order.Add(path);
                }

                fx.AppearPreset = new UiPresetRef { Preset = preset };
                map[path] = fx;
            }

            return ToArray(map, order);
        }

        private static void AddExisting(ElementFx[] existing, Dictionary<string, ElementFx> map, List<string> order)
        {
            if (existing == null)
            {
                return;
            }

            for (var i = 0; i < existing.Length; i++)
            {
                var path = existing[i].ElementPath ?? string.Empty;
                if (!map.ContainsKey(path))
                {
                    order.Add(path);
                }

                map[path] = existing[i];
            }
        }

        private static void AddPathIfMissing(string path, Dictionary<string, ElementFx> map, List<string> order)
        {
            if (map.ContainsKey(path))
            {
                return;
            }

            map[path] = new ElementFx { ElementPath = path };
            order.Add(path);
        }

        private static ElementFx[] ToArray(Dictionary<string, ElementFx> map, List<string> order)
        {
            var result = new ElementFx[order.Count];
            for (var i = 0; i < order.Count; i++)
            {
                result[i] = map[order[i]];
            }

            return result;
        }

        private static string GetPath(Transform root, Transform target)
        {
            if (target == root)
            {
                return string.Empty;
            }

            var names = new List<string>();
            var cur = target;
            while (cur != null && cur != root)
            {
                names.Add(cur.name);
                cur = cur.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }
    }
}
