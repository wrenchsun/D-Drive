using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.CanvasTool
{
    // [07_canvas_prefab.md] A-4(4-3) — GraphView(UnityEditor.Experimental.GraphView、実験 API)を避けて
    // 手組みした UI Toolkit のノードグラフ表示/編集。ノードは Prefab の RectTransform 位置を投影したものを
    // 表示専用に使い、矢印(Up/Down/Left/Right)は generateVisualContent + Painter2D で描く。
    // パン: 中ドラッグ、ズーム: ホイール(_world.transform.scale)。編集はポートからのドラッグ&ドロップと右クリックメニュー。
    public sealed class NavigationGraphView : VisualElement
    {
        private const float NodeWidth = 132f;
        private const float NodeHeight = 40f;
        private const float PortSize = 10f;

        private static readonly Color ColorUp = new(0.35f, 0.75f, 1f);
        private static readonly Color ColorDown = new(1f, 0.65f, 0.3f);
        private static readonly Color ColorLeft = new(1f, 0.9f, 0.3f);
        private static readonly Color ColorRight = new(0.85f, 0.4f, 1f);

        // 呼び出し元(CanvasEditorWindow)へのコールバック。実際の書き戻しは Undo.RecordObject で包んで行う。
        public Action<string, NavDirection, string> OnSetLink;
        public Action<string, NavDirection> OnClearLink;
        public Action<string> OnClearAllLinks;
        public Action<string> OnSetFirstSelected;
        public Action<string> OnPingElement;

        private NavigationGraph _graph = new();
        private GameObject _prefab;
        private string _firstSelected = string.Empty;
        private string _focusedPath;
        private HashSet<string> _unreachable = new();

        private readonly VisualElement _world;
        private readonly VisualElement _arrowLayer;
        private readonly Dictionary<string, VisualElement> _nodeElements = new();

        private Vector2 _pan = new(20f, 20f);
        private float _zoom = 1f;

        private bool _panning;
        private Vector2 _panLastMouse;

        private bool _linking;
        private string _linkFromPath;
        private NavDirection _linkDir;
        private Vector2 _linkCurrentPos;

        public NavigationGraphView()
        {
            style.overflow = Overflow.Hidden;
            style.backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f));
            focusable = true;
            pickingMode = PickingMode.Position;

            _world = new VisualElement { style = { position = Position.Absolute, left = 0, top = 0, width = 1, height = 1 } };
            Add(_world);

            // ノード座標(ComputePosition/グリッド fallback)は常に非負なので、レイヤーは (0,0) 起点で十分。
            _arrowLayer = new VisualElement { style = { position = Position.Absolute, left = 0, top = 0, width = 6000, height = 4000 } };
            _arrowLayer.pickingMode = PickingMode.Ignore;
            _arrowLayer.generateVisualContent += DrawArrows;
            _world.Add(_arrowLayer);

            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<PointerDownEvent>(OnBackgroundPointerDown);
            RegisterCallback<PointerMoveEvent>(OnBackgroundPointerMove);
            RegisterCallback<PointerUpEvent>(OnBackgroundPointerUp);
        }

        public void SetGraph(NavigationGraph graph, string firstSelected, GameObject prefab)
        {
            _graph = graph ?? new NavigationGraph();
            _prefab = prefab;
            _firstSelected = firstSelected ?? string.Empty;
            _unreachable = new HashSet<string>(_graph.Unreachable(_firstSelected));
            Rebuild();
        }

        public void SetFocusedPath(string path)
        {
            _focusedPath = path;
            UpdateNodeVisuals();
            _arrowLayer.MarkDirtyRepaint();
        }

        public IReadOnlyList<string> Unreachable => _graph.Unreachable(_firstSelected);

        private void Rebuild()
        {
            for (var i = _world.childCount - 1; i >= 0; i--)
            {
                if (_world[i] != _arrowLayer)
                {
                    _world.RemoveAt(i);
                }
            }

            _nodeElements.Clear();

            foreach (var node in _graph.Nodes)
            {
                var box = BuildNodeElement(node);
                _nodeElements[node.Path] = box;
                _world.Add(box);
            }

            UpdateNodeVisuals();
            ApplyTransform();
            _arrowLayer.MarkDirtyRepaint();
        }

        private VisualElement BuildNodeElement(NavNodeInfo node)
        {
            var box = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = node.Position.x,
                    top = node.Position.y,
                    width = NodeWidth,
                    height = NodeHeight,
                    backgroundColor = new StyleColor(new Color(0.24f, 0.24f, 0.24f)),
                    borderTopWidth = 2, borderBottomWidth = 2, borderLeftWidth = 2, borderRightWidth = 2,
                    borderTopLeftRadius = 4, borderTopRightRadius = 4, borderBottomLeftRadius = 4, borderBottomRightRadius = 4,
                    justifyContent = Justify.Center,
                    alignItems = Align.Center,
                },
                userData = node.Path,
            };

            var label = new Label(string.IsNullOrEmpty(node.DisplayName) ? "(ルート)" : node.DisplayName)
            {
                style = { fontSize = 10, unityTextAlign = TextAnchor.MiddleCenter, whiteSpace = WhiteSpace.Normal },
                pickingMode = PickingMode.Ignore,
            };
            box.Add(label);

            box.Add(BuildPort(node.Path, NavDirection.Up, new StyleLength(Length.Percent(50)), 0, true));
            box.Add(BuildPort(node.Path, NavDirection.Down, new StyleLength(Length.Percent(50)), 0, false));
            box.Add(BuildLeftRightPort(node.Path, NavDirection.Left, true));
            box.Add(BuildLeftRightPort(node.Path, NavDirection.Right, false));

            box.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                if (evt.clickCount >= 2)
                {
                    PingElement(node.Path);
                    evt.StopPropagation();
                }
            });

            var menu = new ContextualMenuManipulator(evt => BuildContextMenu(evt, node.Path));
            box.AddManipulator(menu);

            return box;
        }

        private VisualElement BuildPort(string path, NavDirection dir, StyleLength left, float top, bool isTop)
        {
            var port = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    width = PortSize, height = PortSize,
                    left = left, marginLeft = -PortSize / 2f,
                    top = isTop ? -PortSize / 2f : new StyleLength(new Length(100, LengthUnit.Percent)),
                    marginTop = isTop ? 0 : -PortSize / 2f,
                    backgroundColor = new StyleColor(DirectionColor(dir)),
                    borderTopLeftRadius = PortSize / 2f, borderTopRightRadius = PortSize / 2f,
                    borderBottomLeftRadius = PortSize / 2f, borderBottomRightRadius = PortSize / 2f,
                },
                tooltip = $"{dir} へドラッグしてリンクを作成",
            };

            RegisterPortDrag(port, path, dir);
            return port;
        }

        private VisualElement BuildLeftRightPort(string path, NavDirection dir, bool isLeft)
        {
            var port = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    width = PortSize, height = PortSize,
                    top = new StyleLength(Length.Percent(50)), marginTop = -PortSize / 2f,
                    left = isLeft ? -PortSize / 2f : new StyleLength(new Length(100, LengthUnit.Percent)),
                    marginLeft = isLeft ? 0 : -PortSize / 2f,
                    backgroundColor = new StyleColor(DirectionColor(dir)),
                    borderTopLeftRadius = PortSize / 2f, borderTopRightRadius = PortSize / 2f,
                    borderBottomLeftRadius = PortSize / 2f, borderBottomRightRadius = PortSize / 2f,
                },
                tooltip = $"{dir} へドラッグしてリンクを作成",
            };

            RegisterPortDrag(port, path, dir);
            return port;
        }

        private static Color DirectionColor(NavDirection dir) => dir switch
        {
            NavDirection.Up => ColorUp,
            NavDirection.Down => ColorDown,
            NavDirection.Left => ColorLeft,
            _ => ColorRight,
        };

        private void RegisterPortDrag(VisualElement port, string path, NavDirection dir)
        {
            port.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                _linking = true;
                _linkFromPath = path;
                _linkDir = dir;
                _linkCurrentPos = ScreenToWorld(this.WorldToLocal(evt.position));
                this.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
        }

        private void OnBackgroundPointerDown(PointerDownEvent evt)
        {
            if (_linking)
            {
                return;
            }

            if (evt.button == 2 || (evt.button == 0 && evt.altKey))
            {
                _panning = true;
                _panLastMouse = evt.position;
                this.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            }
        }

        private void OnBackgroundPointerMove(PointerMoveEvent evt)
        {
            if (_linking)
            {
                _linkCurrentPos = ScreenToWorld(this.WorldToLocal(evt.position));
                _arrowLayer.MarkDirtyRepaint();
                return;
            }

            if (_panning)
            {
                var delta = (Vector2)evt.position - _panLastMouse;
                _panLastMouse = evt.position;
                _pan += delta;
                ApplyTransform();
            }
        }

        private void OnBackgroundPointerUp(PointerUpEvent evt)
        {
            if (_panning)
            {
                _panning = false;
                this.ReleasePointer(evt.pointerId);
                return;
            }

            if (_linking)
            {
                _linking = false;
                this.ReleasePointer(evt.pointerId);
                var worldPos = ScreenToWorld(this.WorldToLocal(evt.position));
                var target = FindNodeAt(worldPos);
                if (target != null && !string.Equals(target.Path, _linkFromPath, StringComparison.Ordinal))
                {
                    OnSetLink?.Invoke(_linkFromPath, _linkDir, target.Path);
                }

                _arrowLayer.MarkDirtyRepaint();
            }
        }

        private NavNodeInfo FindNodeAt(Vector2 worldPos)
        {
            foreach (var node in _graph.Nodes)
            {
                var rect = new Rect(node.Position.x, node.Position.y, NodeWidth, NodeHeight);
                if (rect.Contains(worldPos))
                {
                    return node;
                }
            }

            return null;
        }

        private void OnWheel(WheelEvent evt)
        {
            var prevZoom = _zoom;
            _zoom = Mathf.Clamp(_zoom - evt.delta.y * 0.05f, 0.25f, 2.5f);
            if (!Mathf.Approximately(prevZoom, _zoom))
            {
                ApplyTransform();
                evt.StopPropagation();
            }
        }

        private void ApplyTransform()
        {
            _world.transform.position = new Vector3(_pan.x, _pan.y, 0f);
            _world.transform.scale = new Vector3(_zoom, _zoom, 1f);
        }

        private Vector2 ScreenToWorld(Vector2 localToThis) => (localToThis - _pan) / Mathf.Max(0.0001f, _zoom);

        private void UpdateNodeVisuals()
        {
            foreach (var kv in _nodeElements)
            {
                var node = _graph.FindNode(kv.Key);
                var box = kv.Value;
                var borderColor = new Color(0.05f, 0.05f, 0.05f);
                var bg = node != null && node.IsListed ? new Color(0.24f, 0.24f, 0.24f) : new Color(0.19f, 0.19f, 0.19f);

                if (node != null && !node.HasComponent)
                {
                    borderColor = new Color(0.6f, 0.6f, 0.1f); // Navigation にあるが Prefab に無い(パス不整合)
                }

                if (string.Equals(kv.Key, _firstSelected, StringComparison.Ordinal))
                {
                    borderColor = new Color(0.3f, 0.9f, 0.3f);
                }

                if (_unreachable.Contains(kv.Key))
                {
                    borderColor = new Color(0.9f, 0.25f, 0.25f);
                }

                box.style.backgroundColor = new StyleColor(bg);
                var bw = string.Equals(kv.Key, _focusedPath, StringComparison.Ordinal) ? 3f : 2f;
                box.style.borderTopColor = borderColor;
                box.style.borderBottomColor = borderColor;
                box.style.borderLeftColor = borderColor;
                box.style.borderRightColor = borderColor;
                box.style.borderTopWidth = bw;
                box.style.borderBottomWidth = bw;
                box.style.borderLeftWidth = bw;
                box.style.borderRightWidth = bw;

                if (string.Equals(kv.Key, _focusedPath, StringComparison.Ordinal))
                {
                    box.style.borderTopColor = new Color(1f, 1f, 1f);
                    box.style.borderBottomColor = new Color(1f, 1f, 1f);
                    box.style.borderLeftColor = new Color(1f, 1f, 1f);
                    box.style.borderRightColor = new Color(1f, 1f, 1f);
                }
            }
        }

        private void BuildContextMenu(ContextualMenuPopulateEvent evt, string path)
        {
            evt.menu.AppendAction("FirstSelected にする", _ => OnSetFirstSelected?.Invoke(path));
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Up を削除", _ => OnClearLink?.Invoke(path, NavDirection.Up), HasEdge(path, NavDirection.Up));
            evt.menu.AppendAction("Down を削除", _ => OnClearLink?.Invoke(path, NavDirection.Down), HasEdge(path, NavDirection.Down));
            evt.menu.AppendAction("Left を削除", _ => OnClearLink?.Invoke(path, NavDirection.Left), HasEdge(path, NavDirection.Left));
            evt.menu.AppendAction("Right を削除", _ => OnClearLink?.Invoke(path, NavDirection.Right), HasEdge(path, NavDirection.Right));
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("リンクを全て削除", _ => OnClearAllLinks?.Invoke(path));
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("要素を Hierarchy で Ping", _ => PingElement(path));
        }

        private DropdownMenuAction.Status HasEdge(string path, NavDirection dir)
        {
            foreach (var e in _graph.Edges)
            {
                if (string.Equals(e.From, path, StringComparison.Ordinal) && e.Direction == dir)
                {
                    return DropdownMenuAction.Status.Normal;
                }
            }

            return DropdownMenuAction.Status.Disabled;
        }

        private void PingElement(string path)
        {
            if (_prefab == null)
            {
                return;
            }

            var t = string.IsNullOrEmpty(path) ? _prefab.transform : _prefab.transform.Find(path);
            if (t != null)
            {
                EditorGUIUtility.PingObject(t.gameObject);
            }

            OnPingElement?.Invoke(path);
        }

        // 矢印(Up/Down/Left/Right の明示リンク)+ ドラッグ中の仮リンクを描画する。
        private void DrawArrows(MeshGenerationContext mgc)
        {
            var painter = mgc.painter2D;

            foreach (var edge in _graph.Edges)
            {
                var from = _graph.FindNode(edge.From);
                var to = _graph.FindNode(edge.To);
                if (from == null || to == null)
                {
                    continue;
                }

                var a = new Vector2(from.Position.x + NodeWidth / 2f, from.Position.y + NodeHeight / 2f);
                var b = new Vector2(to.Position.x + NodeWidth / 2f, to.Position.y + NodeHeight / 2f);
                DrawArrow(painter, a, b, DirectionColor(edge.Direction));
            }

            if (_linking)
            {
                var from = _graph.FindNode(_linkFromPath);
                if (from != null)
                {
                    var a = new Vector2(from.Position.x + NodeWidth / 2f, from.Position.y + NodeHeight / 2f);
                    DrawArrow(painter, a, _linkCurrentPos, Color.white);
                }
            }
        }

        private static void DrawArrow(Painter2D painter, Vector2 a, Vector2 b, Color color)
        {
            painter.strokeColor = color;
            painter.lineWidth = 2f;
            painter.BeginPath();
            painter.MoveTo(a);
            painter.LineTo(b);
            painter.Stroke();

            var dir = (b - a);
            if (dir.sqrMagnitude < 1f)
            {
                return;
            }

            dir.Normalize();
            var normal = new Vector2(-dir.y, dir.x);
            const float headLength = 9f;
            const float headWidth = 5f;
            var tip = b;
            var baseCenter = b - dir * headLength;

            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(tip);
            painter.LineTo(baseCenter + normal * headWidth);
            painter.LineTo(baseCenter - normal * headWidth);
            painter.ClosePath();
            painter.Fill();
        }
    }
}
