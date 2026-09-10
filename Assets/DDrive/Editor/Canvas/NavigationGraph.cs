using System;
using System.Collections.Generic;
using DDrive.Runtime.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace DDrive.Editor.CanvasTool
{
    // [07_canvas_prefab.md] A-4(4-3) — Navigation を「ノードグラフ」として扱うための純粋モデル(UI 非依存)。
    // NavigationGraphView(表示/編集) と CanvasEditorWindow(Undo 込みの書き戻し) の両方から使う。
    public enum NavDirection
    {
        Up,
        Down,
        Left,
        Right,
    }

    // 1 ノード(1 Selectable/UiInteractable、または Navigation にだけ登録されていて Prefab 側で見つからない要素)。
    public sealed class NavNodeInfo
    {
        public string Path;       // Prefab ルートからの相対パス(ルート自身は空文字)
        public string DisplayName;
        public Vector2 Position;  // レイアウト座標(RectTransform 由来。無ければグリッド配置)
        public bool IsListed;     // CanvasData.Navigation に明示登録されているか
        public bool HasComponent; // Prefab 内で Selectable/UiInteractable が実際に見つかったか
    }

    public struct NavEdge
    {
        public string From;
        public NavDirection Direction;
        public string To;
    }

    public sealed class NavigationGraph
    {
        public readonly List<NavNodeInfo> Nodes = new();
        public readonly List<NavEdge> Edges = new();

        public NavNodeInfo FindNode(string path)
        {
            var key = path ?? string.Empty;
            for (var i = 0; i < Nodes.Count; i++)
            {
                if (string.Equals(Nodes[i].Path, key, StringComparison.Ordinal))
                {
                    return Nodes[i];
                }
            }

            return null;
        }

        // Prefab 内の Selectable/UiInteractable(Navigation に登録が無いものも含む。IsListed で区別する)を集め、
        // CanvasData.Navigation の Up/Down/Left/Right をエッジとして展開する。
        public static NavigationGraph Build(CanvasData data, GameObject prefab)
        {
            var graph = new NavigationGraph();
            if (prefab == null)
            {
                return graph;
            }

            var root = prefab.transform;
            var listed = new HashSet<string>(StringComparer.Ordinal);
            if (data?.Navigation != null)
            {
                for (var i = 0; i < data.Navigation.Length; i++)
                {
                    listed.Add(data.Navigation[i].Element ?? string.Empty);
                }
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var s in prefab.GetComponentsInChildren<Selectable>(true))
            {
                AddNode(graph, seen, root, s.transform, listed);
            }

            foreach (var ui in prefab.GetComponentsInChildren<UiInteractable>(true))
            {
                AddNode(graph, seen, root, ui.transform, listed);
            }

            // Navigation に書かれているが Prefab 側で見つからない要素も(パス不整合を可視化するため)ノード化する。
            if (data?.Navigation != null)
            {
                for (var i = 0; i < data.Navigation.Length; i++)
                {
                    var path = data.Navigation[i].Element ?? string.Empty;
                    if (seen.Add(path))
                    {
                        graph.Nodes.Add(new NavNodeInfo
                        {
                            Path = path,
                            DisplayName = string.IsNullOrEmpty(path) ? "(不明)" : path,
                            Position = Vector2.zero,
                            IsListed = true,
                            HasComponent = false,
                        });
                    }
                }
            }

            AssignGridFallback(graph.Nodes);

            if (data?.Navigation != null)
            {
                for (var i = 0; i < data.Navigation.Length; i++)
                {
                    var node = data.Navigation[i];
                    AddEdge(graph, node.Element, NavDirection.Up, node.Up);
                    AddEdge(graph, node.Element, NavDirection.Down, node.Down);
                    AddEdge(graph, node.Element, NavDirection.Left, node.Left);
                    AddEdge(graph, node.Element, NavDirection.Right, node.Right);
                }
            }

            return graph;
        }

        private static void AddNode(NavigationGraph graph, HashSet<string> seen, Transform root, Transform target, HashSet<string> listed)
        {
            var path = GetPath(root, target);
            if (!seen.Add(path))
            {
                return;
            }

            graph.Nodes.Add(new NavNodeInfo
            {
                Path = path,
                DisplayName = string.IsNullOrEmpty(path) ? target.name : path,
                Position = ComputePosition(root, target),
                IsListed = listed.Contains(path),
                HasComponent = true,
            });
        }

        private static void AddEdge(NavigationGraph graph, string from, NavDirection dir, string to)
        {
            if (string.IsNullOrEmpty(to))
            {
                return;
            }

            graph.Edges.Add(new NavEdge { From = from ?? string.Empty, Direction = dir, To = to });
        }

        // RectTransform の world corners の中心を、Prefab ルートを基準にした 2D レイアウト座標へ投影する
        // (uGUI は Y が上向きだが、画面表示として見慣れた「上が小さい Y」にするため上下反転する)。
        private static Vector2 ComputePosition(Transform root, Transform target)
        {
            var rt = target as RectTransform;
            var rootRt = root as RectTransform;
            if (rt == null || rootRt == null)
            {
                return new Vector2(float.NaN, float.NaN); // AssignGridFallback が拾う
            }

            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var center = (corners[0] + corners[2]) * 0.5f;

            var rootCorners = new Vector3[4];
            rootRt.GetWorldCorners(rootCorners);
            var height = rootCorners[1].y - rootCorners[0].y; // top - bottom
            var localX = center.x - rootCorners[0].x;
            var localY = center.y - rootCorners[0].y;

            return new Vector2(localX, height - localY);
        }

        // RectTransform が無い/計算できないノードをグリッドに並べる(fallback)。
        private static void AssignGridFallback(List<NavNodeInfo> nodes)
        {
            const float cellW = 160f;
            const float cellH = 80f;
            const int columns = 4;
            var fallbackIndex = 0;

            for (var i = 0; i < nodes.Count; i++)
            {
                var pos = nodes[i].Position;
                if (float.IsNaN(pos.x) || float.IsNaN(pos.y))
                {
                    var col = fallbackIndex % columns;
                    var rowIdx = fallbackIndex / columns;
                    nodes[i].Position = new Vector2(col * cellW, rowIdx * cellH);
                    fallbackIndex++;
                }
            }
        }

        // CanvasDataValidator.ValidateNavigation と同じ規則: いずれかの方向から参照されている要素(または
        // FirstSelected)は到達済みとみなす。BFS ではなく参照集合の一致を優先し、Validator と結果を一致させる。
        public List<string> Unreachable(string firstSelected)
        {
            var reached = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < Edges.Count; i++)
            {
                reached.Add(Edges[i].To);
            }

            var firstKey = firstSelected ?? string.Empty;
            var result = new List<string>();
            for (var i = 0; i < Nodes.Count; i++)
            {
                var n = Nodes[i];
                if (!n.HasComponent)
                {
                    continue;
                }

                if (string.Equals(n.Path, firstKey, StringComparison.Ordinal))
                {
                    continue;
                }

                if (reached.Contains(n.Path))
                {
                    continue;
                }

                result.Add(n.Path);
            }

            return result;
        }

        // 呼び出し側(CanvasEditorWindow)が Undo.RecordObject + SetDirty で包む前提のミューテーションヘルパー。
        public static void SetLink(CanvasData data, string fromPath, NavDirection dir, string toPath)
        {
            var key = fromPath ?? string.Empty;
            var list = data.Navigation != null ? new List<NavNode>(data.Navigation) : new List<NavNode>();
            var idx = list.FindIndex(n => string.Equals(n.Element ?? string.Empty, key, StringComparison.Ordinal));
            NavNode node = idx >= 0 ? list[idx] : new NavNode { Element = key };

            switch (dir)
            {
                case NavDirection.Up: node.Up = toPath; break;
                case NavDirection.Down: node.Down = toPath; break;
                case NavDirection.Left: node.Left = toPath; break;
                case NavDirection.Right: node.Right = toPath; break;
            }

            if (idx >= 0)
            {
                list[idx] = node;
            }
            else
            {
                list.Add(node);
            }

            data.Navigation = list.ToArray();
        }

        public static void ClearLink(CanvasData data, string fromPath, NavDirection dir)
            => SetLink(data, fromPath, dir, string.Empty);

        // ノードの全リンク(4 方向)を削除する。Navigation の行自体は残す(Element の登録は保持)。
        public static void ClearAllLinks(CanvasData data, string fromPath)
        {
            SetLink(data, fromPath, NavDirection.Up, string.Empty);
            SetLink(data, fromPath, NavDirection.Down, string.Empty);
            SetLink(data, fromPath, NavDirection.Left, string.Empty);
            SetLink(data, fromPath, NavDirection.Right, string.Empty);
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
