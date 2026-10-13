using System;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Editor.Ux;
using DDrive.Foundation.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Inspector
{
    // [09_editor_tools.md] §8 — [DataEditor] 属性を TypeCache で集め、Data 型 → 「開く」操作の一覧を返す。
    // 基底型も遡って引くので、既存 Data を継承した新しい Data も自動で同じエディタが付く。
    public static class DataEditorRegistry
    {
        public sealed class Entry
        {
            public Type WindowType { get; }
            public Type DataType { get; }
            public string Label { get; }
            public int Order { get; }
            internal Type ExceptFor { get; }
            private readonly MethodInfo _open;

            internal Entry(Type windowType, DataEditorAttribute attribute, MethodInfo open)
            {
                WindowType = windowType;
                DataType = attribute.DataType;
                Label = attribute.Label;
                Order = attribute.Order;
                ExceptFor = attribute.ExceptFor;
                _open = open;
            }

            public void Open(AssetDataBase data)
            {
                if (data == null)
                {
                    return;
                }

                try
                {
                    _open.Invoke(null, new object[] { data });
                }
                catch (TargetInvocationException e)
                {
                    Debug.LogWarning($"[DDrive] {WindowType.Name}.{_open.Name} の呼び出しに失敗しました: {e.InnerException?.Message ?? e.Message}");
                }
            }
        }

        private static Dictionary<Type, List<Entry>> _declared;
        private static readonly Dictionary<Type, IReadOnlyList<Entry>> _resolved = new();
        private static readonly Entry[] Empty = Array.Empty<Entry>();

        // 属性を付けた Data 型そのもの(継承で引ける型は含まない)。
        public static IEnumerable<Type> RegisteredDataTypes
        {
            get
            {
                EnsureBuilt();
                return _declared.Keys;
            }
        }

        public static bool HasEditor(Type dataType) => GetEntries(dataType).Count > 0;

        // AssetBrowser のダブルクリック([09_editor_tools.md] §1)用 — Data 型に対応する専用エディタが
        // 複数ある場合(MaterialData / SliderSkinData 等)は Order が最小のもの(既定 Order=0 の「主エディタ」、
        // 変換・プレビュー等の副次ツールは明示的に大きい Order を付ける既存の運用)を主エディタとする。
        // GetEntries は既に Order 昇順で返すため、先頭を返すだけでよい。
        public static bool TryGetPrimary(Type dataType, out Entry primary)
        {
            var entries = GetEntries(dataType);
            if (entries.Count == 0)
            {
                primary = null;
                return false;
            }

            primary = entries[0];
            return true;
        }

        // dataType の主エディタ(Order 最小)を開く。Inspector の「エディターで開く」ボタン列の先頭を
        // 押したのと同じ効果。対応するエディタが無い場合は何もせず false を返す(呼び出し側は
        // Selection/Ping 等の従来動作へフォールバックする)。
        public static bool OpenDefault(AssetDataBase data)
        {
            if (data == null || !TryGetPrimary(data.GetType(), out var primary))
            {
                return false;
            }

            primary.Open(data);
            return true;
        }

        // dataType とその基底型(AssetDataBase 手前まで)に宣言されたエントリを、派生側優先・Order 昇順で返す。
        public static IReadOnlyList<Entry> GetEntries(Type dataType)
        {
            if (dataType == null)
            {
                return Empty;
            }

            EnsureBuilt();
            if (_resolved.TryGetValue(dataType, out var cached))
            {
                return cached;
            }

            var list = new List<Entry>();
            for (var t = dataType; t != null && t != typeof(AssetDataBase) && typeof(AssetDataBase).IsAssignableFrom(t); t = t.BaseType)
            {
                if (_declared.TryGetValue(t, out var entries))
                {
                    var sorted = new List<Entry>(entries.Count);
                    foreach (var entry in entries)
                    {
                        // 基底型の登録で、この型(以下)を除外しているものは引き継がない。
                        if (t != dataType && entry.ExceptFor != null && entry.ExceptFor.IsAssignableFrom(dataType))
                        {
                            continue;
                        }

                        sorted.Add(entry);
                    }

                    sorted.Sort((a, b) => a.Order.CompareTo(b.Order));
                    list.AddRange(sorted);
                }
            }

            IReadOnlyList<Entry> result = list.Count > 0 ? list : Empty;
            _resolved[dataType] = result;
            return result;
        }

        // テスト・ドメインリロード無しでの再収集用。
        public static void Invalidate()
        {
            _declared = null;
            _resolved.Clear();
        }

        private static void EnsureBuilt()
        {
            if (_declared != null)
            {
                return;
            }

            _declared = new Dictionary<Type, List<Entry>>();
            foreach (var windowType in TypeCache.GetTypesWithAttribute<DataEditorAttribute>())
            {
                foreach (var attribute in windowType.GetCustomAttributes<DataEditorAttribute>(false))
                {
                    if (attribute.DataType == null || !typeof(AssetDataBase).IsAssignableFrom(attribute.DataType))
                    {
                        Debug.LogWarning($"[DDrive] {windowType.Name} の [DataEditor] は AssetDataBase 派生型を指定してください: {attribute.DataType?.Name ?? "null"}");
                        continue;
                    }

                    var open = FindOpenMethod(windowType, attribute);
                    if (open == null)
                    {
                        Debug.LogWarning($"[DDrive] {windowType.Name} に public static {attribute.OpenMethod}({attribute.DataType.Name}) がありません。[DataEditor] を無視します。");
                        continue;
                    }

                    if (!_declared.TryGetValue(attribute.DataType, out var list))
                    {
                        list = new List<Entry>();
                        _declared[attribute.DataType] = list;
                    }

                    list.Add(new Entry(windowType, attribute, open));
                }
            }
        }

        private static MethodInfo FindOpenMethod(Type windowType, DataEditorAttribute attribute)
        {
            MethodInfo best = null;
            foreach (var method in windowType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (method.Name != attribute.OpenMethod)
                {
                    continue;
                }

                var parameters = method.GetParameters();
                if (parameters.Length != 1 || !parameters[0].ParameterType.IsAssignableFrom(attribute.DataType))
                {
                    continue;
                }

                // 引数型が Data 型に近いもの(VfxData > AssetDataBase)を優先する。
                if (best == null || best.GetParameters()[0].ParameterType.IsAssignableFrom(parameters[0].ParameterType))
                {
                    best = method;
                }
            }

            return best;
        }
    }

    // Inspector 最上部の「エディターで開く」ボタン列(docs/1010 UX-17)。
    // 新エディター(Order 0)が主ボタン(塗り)、Order 1〜99 の補助ツール(Material の変換など)は副ボタン(枠)、
    // 旧エディター(Order 100 以上、ラベルは「(旧) 〜」)は折りたたみ「旧エディター」の中(既定は閉じる)に入れる。
    // 新エディターが 1 つも無い種別は、旧を折りたたまず副ボタンのまま出す(開く手段が隠れないように)。
    // UI Toolkit 製の Inspector からは Build、IMGUI の Inspector からは Draw を使う(見た目の部品は同じ並び)。
    public static class DataEditorHeader
    {
        private const float ButtonHeight = 26f;

        // これ以上の Order は旧エディターとして折りたたみに入れる。
        public const int LegacyOrder = 100;

        private const string LegacyFoldKey = "DDrive.Inspector.LegacyEditorsFold";

        // 旧エディターの折りたたみが開いているか(セッション内のみ記憶。既定は閉じる)。
        internal static bool LegacyFoldOpen
        {
            get => SessionState.GetBool(LegacyFoldKey, false);
            set => SessionState.SetBool(LegacyFoldKey, value);
        }

        // 並びを「主(Order 最小の新)/ 副(その他の新)/ 旧」に振り分ける。新が無ければ旧を副として扱う。
        internal static void Split(Type dataType, List<DataEditorRegistry.Entry> primary, List<DataEditorRegistry.Entry> secondary, List<DataEditorRegistry.Entry> legacy)
        {
            var entries = DataEditorRegistry.GetEntries(dataType);
            var primaryOrder = int.MaxValue;
            foreach (var entry in entries)
            {
                if (entry.Order < LegacyOrder && entry.Order < primaryOrder)
                {
                    primaryOrder = entry.Order;
                }
            }

            var hasNew = primaryOrder != int.MaxValue;
            foreach (var entry in entries)
            {
                if (entry.Order >= LegacyOrder)
                {
                    (hasNew ? legacy : secondary).Add(entry);
                }
                else if (entry.Order == primaryOrder)
                {
                    primary.Add(entry);
                }
                else
                {
                    secondary.Add(entry);
                }
            }
        }

        private static GUIContent Content(DataEditorRegistry.Entry entry) => new GUIContent("▶ " + entry.Label, $"{entry.WindowType.Name} でこのアセットを開く");

        public static void Draw(AssetDataBase target)
        {
            if (target == null)
            {
                return;
            }

            var primary = new List<DataEditorRegistry.Entry>();
            var secondary = new List<DataEditorRegistry.Entry>();
            var legacy = new List<DataEditorRegistry.Entry>();
            Split(target.GetType(), primary, secondary, legacy);
            if (primary.Count + secondary.Count + legacy.Count == 0)
            {
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                var primaryStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
                foreach (var entry in primary)
                {
                    var oldBg = GUI.backgroundColor;
                    var oldContent = GUI.contentColor;
                    // IMGUI は USS が使えないので、DDriveUx.uss の --ux-accent と同じ色で塗る(Dark #4a8cff / Light #2f6fe4)。
                    GUI.backgroundColor = EditorGUIUtility.isProSkin ? new Color(0.29f, 0.55f, 1f) : new Color(0.18f, 0.44f, 0.89f);
                    GUI.contentColor = Color.white;
                    var clicked = GUILayout.Button(Content(entry), primaryStyle, GUILayout.Height(ButtonHeight));
                    GUI.backgroundColor = oldBg;
                    GUI.contentColor = oldContent;
                    if (clicked)
                    {
                        entry.Open(target);
                        GUIUtility.ExitGUI();
                    }
                }

                foreach (var entry in secondary)
                {
                    if (GUILayout.Button(Content(entry), GUILayout.Height(ButtonHeight)))
                    {
                        entry.Open(target);
                        GUIUtility.ExitGUI();
                    }
                }
            }

            if (legacy.Count > 0)
            {
                var open = EditorGUILayout.Foldout(LegacyFoldOpen, $"旧エディター（{legacy.Count}）", true);
                LegacyFoldOpen = open;
                if (open)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        foreach (var entry in legacy)
                        {
                            if (GUILayout.Button(Content(entry), GUILayout.Height(ButtonHeight)))
                            {
                                entry.Open(target);
                                GUIUtility.ExitGUI();
                            }
                        }
                    }
                }
            }

            EditorGUILayout.Space(4f);
        }

        // UI Toolkit 版。ux-root + ux-insp(背景なし)にテーマ(DDriveUx.uss)を当てる。
        // 構造: .ux-open > [.ux-open__row "open-main"(主 = .ux-btn--primary / 副 = .ux-btn)] + [.ux-fold "legacy-fold"(見出し + 本体。旧エディターのボタン)]。
        public static VisualElement Build(AssetDataBase target)
        {
            var root = new VisualElement { name = "open-editors" };
            UxTheme.Apply(root);
            root.AddToClassList("ux-insp");
            root.AddToClassList("ux-open");
            if (target == null)
            {
                return root;
            }

            var primary = new List<DataEditorRegistry.Entry>();
            var secondary = new List<DataEditorRegistry.Entry>();
            var legacy = new List<DataEditorRegistry.Entry>();
            Split(target.GetType(), primary, secondary, legacy);

            if (primary.Count + secondary.Count > 0)
            {
                var row = new VisualElement { name = "open-main" };
                row.AddToClassList("ux-open__row");
                foreach (var entry in primary)
                {
                    row.Add(MakeButton(entry, target, true));
                }

                foreach (var entry in secondary)
                {
                    row.Add(MakeButton(entry, target, false));
                }

                root.Add(row);
            }

            if (legacy.Count > 0)
            {
                var fold = new VisualElement { name = "legacy-fold" };
                fold.AddToClassList("ux-fold");
                var body = new VisualElement { name = "legacy-fold-body" };
                body.AddToClassList("ux-fold__body");
                body.AddToClassList("ux-open__row");
                foreach (var entry in legacy)
                {
                    body.Add(MakeButton(entry, target, false));
                }

                var head = new Button { name = "legacy-fold-head" };
                head.AddToClassList("ux-fold__head");
                void Refresh()
                {
                    var open = LegacyFoldOpen;
                    head.text = (open ? "▾ " : "▸ ") + $"旧エディター（{legacy.Count}）";
                    head.tooltip = "以前のエディターです。通常は上の新しいエディターを使います";
                    body.Show(open);
                }

                head.clicked += () =>
                {
                    LegacyFoldOpen = !LegacyFoldOpen;
                    Refresh();
                };
                fold.Add(head);
                fold.Add(body);
                Refresh();
                root.Add(fold);
            }

            return root;
        }

        private static Button MakeButton(DataEditorRegistry.Entry entry, AssetDataBase target, bool primary)
        {
            var button = new Button(() => entry.Open(target))
            {
                text = "▶ " + entry.Label,
                tooltip = $"{entry.WindowType.Name} でこのアセットを開く",
            };
            button.AddToClassList("ux-btn");
            if (primary)
            {
                button.AddToClassList("ux-btn--primary");
            }

            return button;
        }
    }
}
