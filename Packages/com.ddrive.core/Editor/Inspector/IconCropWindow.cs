using DDrive.Foundation.Data;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Inspector
{
    // [09_editor_tools.md] §8.1 — 「シーンから作成」の切り出し画面。
    // 撮影したシーン全体を表示し、ドラッグで正方形の範囲を決めて「この範囲でアイコンを作成」。
    //  - ドラッグ(空き場所から): 正方形を新しく描く(長い辺に合わせる)
    //  - ドラッグ(枠の中): 枠を移動  / ホイール: 枠の大きさを変える / ダブルクリック: 全体の中央に最大の正方形
    //  - 再撮影: SceneView を動かしてから撮り直す / 画面からスクショ: 表示そのまま(ギズモ込み)を OS の画面から読む
    public sealed class IconCropWindow : EditorWindow
    {
        private const float Margin = 8f;
        private const float Toolbar = 26f;
        private const float Footer = 30f;

        private AssetDataBase _asset;
        private string _iconRoot;
        private Texture2D _shot;
        private string _sourceName;
        private Rect _crop; // _shot のピクセル座標(左上原点)、正方形
        private bool _dragging;
        private bool _moving;
        private Vector2 _dragStart;
        private Vector2 _moveOffset;
        private string _message;

        public static void Open(AssetDataBase asset, string iconRoot)
        {
            if (asset == null)
            {
                return;
            }

            var window = GetWindow<IconCropWindow>(true, "アイコンを切り出す", true);
            window.minSize = new Vector2(480, 400);
            window._asset = asset;
            window._iconRoot = iconRoot;
            window.Shoot();
            window.Show();
        }

        private void OnDisable() => ReleaseShot();

        private void ReleaseShot()
        {
            if (_shot != null)
            {
                DestroyImmediate(_shot);
                _shot = null;
            }
        }

        private void Shoot()
        {
            ReleaseShot();
            var camera = AssetIconService.ResolveSourceCamera(out _sourceName);
            if (camera == null)
            {
                _message = "SceneView も Main Camera も無いため撮影できません。SceneView を開いて対象を映してください。";
                return;
            }

            _shot = AssetIconService.RenderView(camera);
            _message = _shot != null ? $"{_sourceName} を撮影しました({_shot.width}×{_shot.height})。ドラッグで範囲を決めてください。" : "撮影に失敗しました。";
            ResetCrop();
            Repaint();
        }

        // OS の画面から SceneView の表示領域をそのまま読む(ギズモ・グリッド込み。SceneView が他のウィンドウに隠れていないこと)。
        private void ShootFromScreen()
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                _message = "SceneView がありません。";
                return;
            }

            ReleaseShot();
            var pos = sceneView.position;
            var scale = EditorGUIUtility.pixelsPerPoint;
            var width = Mathf.RoundToInt(pos.width * scale);
            var height = Mathf.RoundToInt((pos.height - 21f) * scale); // ツールバー分を除く
            var origin = new Vector2(pos.x * scale, (pos.y + 21f) * scale);
            var pixels = UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(origin, width, height);
            if (pixels == null || pixels.Length == 0)
            {
                _message = "画面から読み取れませんでした。";
                return;
            }

            _shot = new Texture2D(width, height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            _shot.SetPixels(pixels);
            _shot.Apply();
            _sourceName = "画面(SceneView の表示)";
            _message = $"画面から取り込みました({width}×{height})。SceneView が隠れていた場合はその部分も写ります。";
            ResetCrop();
            Repaint();
        }

        private void ResetCrop()
        {
            if (_shot == null)
            {
                return;
            }

            var side = Mathf.Min(_shot.width, _shot.height) * 0.6f;
            _crop = new Rect((_shot.width - side) * 0.5f, (_shot.height - side) * 0.5f, side, side);
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(Toolbar)))
            {
                if (GUILayout.Button("再撮影", EditorStyles.toolbarButton, GUILayout.Width(70)))
                {
                    Shoot();
                }

                if (GUILayout.Button(new GUIContent("画面からスクショ", "表示そのまま(ギズモ込み)を OS の画面から読む"), EditorStyles.toolbarButton, GUILayout.Width(110)))
                {
                    ShootFromScreen();
                }

                GUILayout.FlexibleSpace();
                var labels = new string[AssetIconService.SizeChoices.Length];
                var index = 0;
                for (var i = 0; i < labels.Length; i++)
                {
                    labels[i] = AssetIconService.SizeChoices[i] + " px";
                    if (AssetIconService.SizeChoices[i] == AssetIconService.PreferredSize)
                    {
                        index = i;
                    }
                }

                GUILayout.Label("アイコンサイズ", EditorStyles.miniLabel);
                var next = EditorGUILayout.Popup(index, labels, EditorStyles.toolbarPopup, GUILayout.Width(70));
                if (next != index)
                {
                    AssetIconService.PreferredSize = AssetIconService.SizeChoices[next];
                }
            }

            var area = new Rect(Margin, Toolbar + Margin, position.width - Margin * 2f, position.height - Toolbar - Footer - Margin * 2f);
            if (_shot == null)
            {
                EditorGUI.HelpBox(area, _message ?? "撮影していません。", MessageType.Warning);
            }
            else
            {
                DrawShot(area);
            }

            var footer = new Rect(Margin, position.height - Footer, position.width - Margin * 2f, Footer - 4f);
            using (new GUILayout.AreaScope(footer))
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(_message ?? string.Empty, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(_shot == null || _asset == null))
                {
                    if (GUILayout.Button($"この範囲でアイコンを作成({AssetIconService.PreferredSize} px)", GUILayout.Height(22), GUILayout.Width(220)))
                    {
                        Commit();
                    }
                }
            }
        }

        private void DrawShot(Rect area)
        {
            // 縦横比を保って収める
            var scale = Mathf.Min(area.width / _shot.width, area.height / _shot.height);
            var drawW = _shot.width * scale;
            var drawH = _shot.height * scale;
            var draw = new Rect(area.x + (area.width - drawW) * 0.5f, area.y + (area.height - drawH) * 0.5f, drawW, drawH);
            EditorGUI.DrawRect(area, new Color(0.1f, 0.1f, 0.1f));
            GUI.DrawTexture(draw, _shot, ScaleMode.StretchToFill, false);

            // 枠(画面座標)
            var frame = new Rect(draw.x + _crop.x * scale, draw.y + _crop.y * scale, _crop.width * scale, _crop.height * scale);
            var dim = new Color(0f, 0f, 0f, 0.45f);
            EditorGUI.DrawRect(new Rect(draw.x, draw.y, draw.width, frame.y - draw.y), dim);
            EditorGUI.DrawRect(new Rect(draw.x, frame.yMax, draw.width, draw.yMax - frame.yMax), dim);
            EditorGUI.DrawRect(new Rect(draw.x, frame.y, frame.x - draw.x, frame.height), dim);
            EditorGUI.DrawRect(new Rect(frame.xMax, frame.y, draw.xMax - frame.xMax, frame.height), dim);
            var line = new Color(1f, 0.8f, 0.3f);
            EditorGUI.DrawRect(new Rect(frame.x, frame.y, frame.width, 2f), line);
            EditorGUI.DrawRect(new Rect(frame.x, frame.yMax - 2f, frame.width, 2f), line);
            EditorGUI.DrawRect(new Rect(frame.x, frame.y, 2f, frame.height), line);
            EditorGUI.DrawRect(new Rect(frame.xMax - 2f, frame.y, 2f, frame.height), line);
            GUI.Label(new Rect(frame.x + 4f, frame.y + 2f, 200f, 16f), $"{Mathf.RoundToInt(_crop.width)} px", EditorStyles.whiteMiniLabel);

            var evt = Event.current;
            var mouse = evt.mousePosition;
            Vector2 ToShot(Vector2 p) => new((p.x - draw.x) / scale, (p.y - draw.y) / scale);

            switch (evt.type)
            {
                case EventType.MouseDown when evt.button == 0 && draw.Contains(mouse):
                    if (evt.clickCount == 2)
                    {
                        var side = Mathf.Min(_shot.width, _shot.height);
                        _crop = new Rect((_shot.width - side) * 0.5f, (_shot.height - side) * 0.5f, side, side);
                    }
                    else if (frame.Contains(mouse))
                    {
                        _moving = true;
                        _moveOffset = ToShot(mouse) - _crop.position;
                    }
                    else
                    {
                        _dragging = true;
                        _dragStart = ToShot(mouse);
                        _crop = new Rect(_dragStart, Vector2.zero);
                    }

                    evt.Use();
                    break;
                case EventType.MouseDrag when _dragging:
                {
                    var current = ToShot(mouse);
                    var side = Mathf.Max(Mathf.Abs(current.x - _dragStart.x), Mathf.Abs(current.y - _dragStart.y));
                    var x = current.x < _dragStart.x ? _dragStart.x - side : _dragStart.x;
                    var y = current.y < _dragStart.y ? _dragStart.y - side : _dragStart.y;
                    _crop = ClampToShot(new Rect(x, y, side, side));
                    evt.Use();
                    break;
                }
                case EventType.MouseDrag when _moving:
                    _crop = ClampToShot(new Rect(ToShot(mouse) - _moveOffset, _crop.size));
                    evt.Use();
                    break;
                case EventType.MouseUp:
                    _dragging = false;
                    _moving = false;
                    break;
                case EventType.ScrollWheel when draw.Contains(mouse):
                {
                    var factor = evt.delta.y > 0f ? 0.9f : 1.1f;
                    var center = _crop.center;
                    var side = Mathf.Clamp(_crop.width * factor, 16f, Mathf.Min(_shot.width, _shot.height));
                    _crop = ClampToShot(new Rect(center.x - side * 0.5f, center.y - side * 0.5f, side, side));
                    evt.Use();
                    break;
                }
            }

            if (evt.type == EventType.MouseDrag || evt.type == EventType.ScrollWheel)
            {
                Repaint();
            }
        }

        private Rect ClampToShot(Rect r)
        {
            var side = Mathf.Clamp(r.width, 1f, Mathf.Min(_shot.width, _shot.height));
            r.width = side;
            r.height = side;
            r.x = Mathf.Clamp(r.x, 0f, _shot.width - side);
            r.y = Mathf.Clamp(r.y, 0f, _shot.height - side);
            return r;
        }

        private void Commit()
        {
            if (_shot == null || _asset == null || _crop.width < 1f)
            {
                return;
            }

            var crop = new RectInt(Mathf.RoundToInt(_crop.x), Mathf.RoundToInt(_crop.y), Mathf.RoundToInt(_crop.width), Mathf.RoundToInt(_crop.height));
            var icon = AssetIconService.CropAndSave(_asset, _shot, crop, AssetIconService.PreferredSize, _iconRoot);
            if (icon != null)
            {
                _message = $"作成しました: {AssetDatabase.GetAssetPath(icon)}";
                EditorGUIUtility.PingObject(icon);
                Close();
            }
            else
            {
                _message = "保存に失敗しました(Console を確認)。";
            }
        }
    }
}
