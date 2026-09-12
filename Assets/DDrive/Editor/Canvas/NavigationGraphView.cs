using System;
using System.Collections.Generic;
using DDrive.Runtime.Ui;
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
        private const float RerouteSize = 10f;
        private const float WireHitTolerance = 6f;

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
        // UE ブループリント風に「Ctrl+ドラッグでなぞった線と交差するエッジをまとめて切る」ジェスチャー。
        public Action<List<NavEdge>> OnCutLinks;
        // ノード位置・Reroute point がドラッグ/追加/削除で変わるたびに呼ばれる(CanvasEditorWindow が
        // ExportNodeLayout/ExportEdgeWaypoints で読み出し、CanvasData へ永続化する)。
        public Action OnLayoutChanged;

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

        // Ctrl+左ドラッグで背景をなぞって配線を切るジェスチャー(UE ブループリント風)。
        private bool _cutting;
        private List<Vector2> _cutPoints;

        // ノードが密集していると矢印/ポートが判読しづらいため、ドラッグで手動配置できるようにする。
        // 位置はパスをキーにビュー側で保持し、Rebuild(リンク編集や Undo/Redo での再構築)を跨いで維持する。
        // CanvasEditorWindow が OnLayoutChanged 経由で CanvasData.NavigationNodeLayout へ永続化する
        // (2026-09-12 追記、ユーザー要望による例外)。「自動レイアウトを更新」ボタンで ClearManualLayout する。
        private readonly Dictionary<string, Vector2> _manualPositions = new();
        private string _draggingPath;
        private Vector2 _dragPointerStartWorld;
        private Vector2 _dragNodeStartPos;

        // UE の Reroute ノード風の中継点。ワイヤー(From, Direction。方向ごとに 1 本しか無いので一意に決まる)ごとに
        // 折れ線の頂点をビュー内で保持する。CanvasData.NavigationEdgeWaypoints への永続化は _manualPositions と同じ。
        // ダブルクリックで追加、ドラッグで移動、右クリックで削除。「自動レイアウトを更新」で全消去する。
        private readonly Dictionary<(string From, NavDirection Direction), List<Vector2>> _reroutePoints = new();
        private (string From, NavDirection Direction, int Index)? _draggingReroute;
        private Vector2 _dragReroutePointerStartWorld;
        private Vector2 _dragRerouteStartPos;

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
            ApplyManualPositions();
            PruneReroutePoints();
            _unreachable = new HashSet<string>(_graph.Unreachable(_firstSelected));
            Rebuild();
        }

        // ドラッグで動かした位置・Reroute point を破棄し、次の SetGraph 以降は Prefab のレイアウトどおりに戻す。
        public void ClearManualLayout()
        {
            _manualPositions.Clear();
            _reroutePoints.Clear();
            OnLayoutChanged?.Invoke();
        }

        // CanvasData.NavigationNodeLayout/NavigationEdgeWaypoints から読み込む(SetTarget/RebuildGraph の
        // たびに呼ばれる想定。Undo/Redo で配列側が変わった場合もこれで追従する)。
        public void LoadLayout(NavNodeLayout[] nodeLayout, NavEdgeWaypoint[] edgeWaypoints)
        {
            _manualPositions.Clear();
            if (nodeLayout != null)
            {
                foreach (var entry in nodeLayout)
                {
                    _manualPositions[entry.Element ?? string.Empty] = entry.Position;
                }
            }

            _reroutePoints.Clear();
            if (edgeWaypoints != null)
            {
                foreach (var entry in edgeWaypoints)
                {
                    if (entry.Points == null || entry.Points.Length == 0 || !Enum.TryParse<NavDirection>(entry.Direction, out var dir))
                    {
                        continue;
                    }

                    _reroutePoints[(entry.Element ?? string.Empty, dir)] = new List<Vector2>(entry.Points);
                }
            }
        }

        public NavNodeLayout[] ExportNodeLayout()
        {
            var result = new NavNodeLayout[_manualPositions.Count];
            var i = 0;
            foreach (var kv in _manualPositions)
            {
                result[i++] = new NavNodeLayout { Element = kv.Key, Position = kv.Value };
            }

            return result;
        }

        public NavEdgeWaypoint[] ExportEdgeWaypoints()
        {
            var result = new List<NavEdgeWaypoint>(_reroutePoints.Count);
            foreach (var kv in _reroutePoints)
            {
                if (kv.Value.Count == 0)
                {
                    continue;
                }

                result.Add(new NavEdgeWaypoint
                {
                    Element = kv.Key.From,
                    Direction = kv.Key.Direction.ToString(),
                    Points = kv.Value.ToArray(),
                });
            }

            return result.ToArray();
        }

        private void ApplyManualPositions()
        {
            foreach (var node in _graph.Nodes)
            {
                if (_manualPositions.TryGetValue(node.Path, out var pos))
                {
                    node.Position = pos;
                }
            }
        }

        // リンクが削除されると孤立する Reroute point を掃除する(残っていても描画には使われないが、
        // 削除し忘れたリンクを再作成したときに古い中継点が復活すると分かりにくいため)。
        private void PruneReroutePoints()
        {
            if (_reroutePoints.Count == 0)
            {
                return;
            }

            var live = new HashSet<(string, NavDirection)>();
            foreach (var edge in _graph.Edges)
            {
                live.Add((edge.From ?? string.Empty, edge.Direction));
            }

            var stale = new List<(string, NavDirection)>();
            foreach (var key in _reroutePoints.Keys)
            {
                if (!live.Contains(key))
                {
                    stale.Add(key);
                }
            }

            foreach (var key in stale)
            {
                _reroutePoints.Remove(key);
            }
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

            foreach (var edge in _graph.Edges)
            {
                var key = (edge.From ?? string.Empty, edge.Direction);
                if (!_reroutePoints.TryGetValue(key, out var points))
                {
                    continue;
                }

                for (var i = 0; i < points.Count; i++)
                {
                    _world.Add(BuildRerouteElement(edge.From, edge.Direction, i, points[i]));
                }
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
                tooltip = "ドラッグで移動 / ダブルクリックで Hierarchy を Ping",
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
                    return;
                }

                // ポート(端の丸)は RegisterPortDrag 側で StopPropagation 済みなのでここには来ない。
                // ボックス本体のドラッグは配置を動かすだけで、リンク作成(ポート起点)とは独立。
                _draggingPath = node.Path;
                _dragPointerStartWorld = ScreenToWorld(this.WorldToLocal(evt.position));
                var current = _graph.FindNode(node.Path);
                _dragNodeStartPos = current != null ? current.Position : node.Position;
                box.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            box.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!string.Equals(_draggingPath, node.Path, StringComparison.Ordinal))
                {
                    return;
                }

                var worldPos = ScreenToWorld(this.WorldToLocal(evt.position));
                var newPos = _dragNodeStartPos + (worldPos - _dragPointerStartWorld);
                var current = _graph.FindNode(node.Path);
                if (current != null)
                {
                    current.Position = newPos;
                }

                box.style.left = newPos.x;
                box.style.top = newPos.y;
                _arrowLayer.MarkDirtyRepaint();
                evt.StopPropagation();
            });

            box.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!string.Equals(_draggingPath, node.Path, StringComparison.Ordinal))
                {
                    return;
                }

                box.ReleasePointer(evt.pointerId);
                var current = _graph.FindNode(node.Path);
                if (current != null)
                {
                    _manualPositions[node.Path] = current.Position;
                    OnLayoutChanged?.Invoke();
                }

                _draggingPath = null;
                evt.StopPropagation();
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

        // UE の Reroute ノードに相当する中継点(小さな丸)。ドラッグで移動、右クリックで削除できる。
        private VisualElement BuildRerouteElement(string fromPath, NavDirection dir, int index, Vector2 pos)
        {
            var key = (From: fromPath ?? string.Empty, Direction: dir);
            var knot = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = pos.x - RerouteSize / 2f,
                    top = pos.y - RerouteSize / 2f,
                    width = RerouteSize,
                    height = RerouteSize,
                    backgroundColor = new StyleColor(DirectionColor(dir)),
                    borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
                    borderTopColor = Color.black, borderBottomColor = Color.black, borderLeftColor = Color.black, borderRightColor = Color.black,
                    borderTopLeftRadius = RerouteSize / 2f, borderTopRightRadius = RerouteSize / 2f,
                    borderBottomLeftRadius = RerouteSize / 2f, borderBottomRightRadius = RerouteSize / 2f,
                },
                tooltip = "ドラッグで移動 / 右クリックで削除(Reroute point)",
            };

            knot.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                _draggingReroute = (key.From, key.Direction, index);
                _dragReroutePointerStartWorld = ScreenToWorld(this.WorldToLocal(evt.position));
                _dragRerouteStartPos = pos;
                knot.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            knot.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_draggingReroute == null)
                {
                    return;
                }

                var d = _draggingReroute.Value;
                if (!string.Equals(d.From, key.From, StringComparison.Ordinal) || d.Direction != key.Direction || d.Index != index)
                {
                    return;
                }

                var worldPos = ScreenToWorld(this.WorldToLocal(evt.position));
                var newPos = _dragRerouteStartPos + (worldPos - _dragReroutePointerStartWorld);
                if (_reroutePoints.TryGetValue(key, out var list) && index < list.Count)
                {
                    list[index] = newPos;
                }

                knot.style.left = newPos.x - RerouteSize / 2f;
                knot.style.top = newPos.y - RerouteSize / 2f;
                _arrowLayer.MarkDirtyRepaint();
                evt.StopPropagation();
            });

            knot.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (_draggingReroute == null)
                {
                    return;
                }

                knot.ReleasePointer(evt.pointerId);
                _draggingReroute = null;
                OnLayoutChanged?.Invoke();
                evt.StopPropagation();
            });

            var menu = new ContextualMenuManipulator(evt =>
                evt.menu.AppendAction("Reroute point を削除", _ => RemoveReroutePoint(key.From, key.Direction, index)));
            knot.AddManipulator(menu);

            return knot;
        }

        private void RemoveReroutePoint(string fromPath, NavDirection dir, int index)
        {
            var key = (fromPath ?? string.Empty, dir);
            if (!_reroutePoints.TryGetValue(key, out var list) || index < 0 || index >= list.Count)
            {
                return;
            }

            list.RemoveAt(index);
            if (list.Count == 0)
            {
                _reroutePoints.Remove(key);
            }

            OnLayoutChanged?.Invoke();
            Rebuild();
        }

        private void InsertReroutePoint(NavEdge edge, int segmentIndex, Vector2 worldPos)
        {
            var key = (edge.From ?? string.Empty, edge.Direction);
            if (!_reroutePoints.TryGetValue(key, out var list))
            {
                list = new List<Vector2>();
                _reroutePoints[key] = list;
            }

            list.Insert(Mathf.Clamp(segmentIndex, 0, list.Count), worldPos);
            OnLayoutChanged?.Invoke();
            Rebuild();
        }

        // エッジの折れ線(始点ノード中心 → Reroute point... → 終点ノード中心)。
        private List<Vector2> BuildEdgePolyline(NavEdge edge, NavNodeInfo from, NavNodeInfo to)
        {
            var points = new List<Vector2> { CenterOf(from) };
            if (_reroutePoints.TryGetValue((edge.From ?? string.Empty, edge.Direction), out var wps))
            {
                points.AddRange(wps);
            }

            points.Add(CenterOf(to));
            return points;
        }

        private static Vector2 CenterOf(NavNodeInfo n) => new(n.Position.x + NodeWidth / 2f, n.Position.y + NodeHeight / 2f);

        // ダブルクリック/右クリックでの Reroute point 追加のためのワイヤー当たり判定。
        private bool TryFindWireNear(Vector2 worldPos, out NavEdge hitEdge, out int hitSegmentIndex)
        {
            var tolerance = WireHitTolerance / Mathf.Max(0.0001f, _zoom);
            foreach (var edge in _graph.Edges)
            {
                var from = _graph.FindNode(edge.From);
                var to = _graph.FindNode(edge.To);
                if (from == null || to == null)
                {
                    continue;
                }

                var points = BuildEdgePolyline(edge, from, to);
                for (var i = 0; i < points.Count - 1; i++)
                {
                    if (DistancePointToSegment(worldPos, points[i], points[i + 1]) <= tolerance)
                    {
                        hitEdge = edge;
                        hitSegmentIndex = i;
                        return true;
                    }
                }
            }

            hitEdge = default;
            hitSegmentIndex = -1;
            return false;
        }

        private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = ab.sqrMagnitude > 0.0001f ? Vector2.Dot(p - a, ab) / ab.sqrMagnitude : 0f;
            var closest = a + ab * Mathf.Clamp01(t);
            return Vector2.Distance(p, closest);
        }

        private void OnBackgroundPointerDown(PointerDownEvent evt)
        {
            if (_linking || _cutting)
            {
                return;
            }

            if (evt.button == 2 || (evt.button == 0 && evt.altKey))
            {
                _panning = true;
                _panLastMouse = evt.position;
                this.CapturePointer(evt.pointerId);
                evt.StopPropagation();
                return;
            }

            if (evt.button == 0 && evt.ctrlKey)
            {
                _cutting = true;
                _cutPoints = new List<Vector2> { ScreenToWorld(this.WorldToLocal(evt.position)) };
                this.CapturePointer(evt.pointerId);
                evt.StopPropagation();
                return;
            }

            // UE ブループリントの「ワイヤーをダブルクリックして Reroute ノードを挿入」を模した操作。
            if (evt.button == 0 && evt.clickCount >= 2)
            {
                var worldPos = ScreenToWorld(this.WorldToLocal(evt.position));
                if (TryFindWireNear(worldPos, out var edge, out var segmentIndex))
                {
                    InsertReroutePoint(edge, segmentIndex, worldPos);
                    evt.StopPropagation();
                }
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

            if (_cutting)
            {
                var p = ScreenToWorld(this.WorldToLocal(evt.position));
                // 頂点を打ちすぎないよう、ある程度動いた時だけ折れ線に追加する。
                if ((p - _cutPoints[_cutPoints.Count - 1]).sqrMagnitude > 16f)
                {
                    _cutPoints.Add(p);
                    _arrowLayer.MarkDirtyRepaint();
                }

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

            if (_cutting)
            {
                _cutting = false;
                this.ReleasePointer(evt.pointerId);
                var cut = FindEdgesCrossingCutPath();
                _cutPoints = null;
                _arrowLayer.MarkDirtyRepaint();
                if (cut.Count > 0)
                {
                    OnCutLinks?.Invoke(cut);
                }

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

        // カット折れ線の各セグメントと交差するエッジ(矢印の描画線分そのもの)を集める。
        private List<NavEdge> FindEdgesCrossingCutPath()
        {
            var result = new List<NavEdge>();
            if (_cutPoints == null || _cutPoints.Count < 2)
            {
                return result;
            }

            foreach (var edge in _graph.Edges)
            {
                var from = _graph.FindNode(edge.From);
                var to = _graph.FindNode(edge.To);
                if (from == null || to == null)
                {
                    continue;
                }

                var points = BuildEdgePolyline(edge, from, to);
                var crossed = false;
                for (var w = 0; w < points.Count - 1 && !crossed; w++)
                {
                    for (var i = 0; i < _cutPoints.Count - 1; i++)
                    {
                        if (SegmentsIntersect(points[w], points[w + 1], _cutPoints[i], _cutPoints[i + 1]))
                        {
                            result.Add(edge);
                            crossed = true;
                            break;
                        }
                    }
                }
            }

            return result;
        }

        private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
        {
            var d1 = Cross(p4 - p3, p1 - p3);
            var d2 = Cross(p4 - p3, p2 - p3);
            var d3 = Cross(p2 - p1, p3 - p1);
            var d4 = Cross(p2 - p1, p4 - p1);

            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f))
                && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

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

                // Reroute point があれば、そこを経由する折れ線として描く(矢頭は最後の区間だけ)。
                var points = BuildEdgePolyline(edge, from, to);
                var color = DirectionColor(edge.Direction);
                for (var i = 0; i < points.Count - 2; i++)
                {
                    DrawLine(painter, points[i], points[i + 1], color);
                }

                DrawArrow(painter, points[points.Count - 2], points[points.Count - 1], color);
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

            if (_cutting && _cutPoints != null && _cutPoints.Count > 0)
            {
                painter.strokeColor = new Color(1f, 0.2f, 0.2f);
                painter.lineWidth = 2f;
                painter.BeginPath();
                painter.MoveTo(_cutPoints[0]);
                for (var i = 1; i < _cutPoints.Count; i++)
                {
                    painter.LineTo(_cutPoints[i]);
                }

                painter.Stroke();
            }
        }

        private static void DrawLine(Painter2D painter, Vector2 a, Vector2 b, Color color)
        {
            painter.strokeColor = color;
            painter.lineWidth = 2f;
            painter.BeginPath();
            painter.MoveTo(a);
            painter.LineTo(b);
            painter.Stroke();
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
