using System;
using System.Collections.Generic;
using DDrive.Runtime.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace DDrive.Editor.CanvasTool
{
    // [07_canvas_prefab.md] A-4 — 「Selectable を自動収集」ボタンの実体。Prefab 内の Selectable を
    // 相対パス基準に列挙して NavNode[] を作る(Up/Down/Left/Right は空 = Unity 自動のまま)。
    public static class CanvasNavigationCollector
    {
        public static NavNode[] Collect(GameObject prefab)
        {
            if (prefab == null)
            {
                return Array.Empty<NavNode>();
            }

            var selectables = prefab.GetComponentsInChildren<Selectable>(true);
            var result = new NavNode[selectables.Length];
            for (var i = 0; i < selectables.Length; i++)
            {
                result[i] = new NavNode { Element = GetPath(prefab.transform, selectables[i].transform) };
            }

            return result;
        }

        // 既存の Navigation を残したまま、まだ登録されていない Selectable だけ追加する。
        public static NavNode[] CollectMerged(GameObject prefab, NavNode[] existing)
        {
            var collected = Collect(prefab);
            var map = new Dictionary<string, NavNode>(StringComparer.Ordinal);
            var order = new List<string>();

            if (existing != null)
            {
                for (var i = 0; i < existing.Length; i++)
                {
                    if (!map.ContainsKey(existing[i].Element))
                    {
                        order.Add(existing[i].Element);
                    }

                    map[existing[i].Element] = existing[i];
                }
            }

            for (var i = 0; i < collected.Length; i++)
            {
                if (!map.ContainsKey(collected[i].Element))
                {
                    map[collected[i].Element] = collected[i];
                    order.Add(collected[i].Element);
                }
            }

            var merged = new NavNode[order.Count];
            for (var i = 0; i < order.Count; i++)
            {
                merged[i] = map[order[i]];
            }

            return merged;
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
