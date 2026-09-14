using System;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Foundation.Data;
using UnityEditor;
using UnityEngine;

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
            private readonly MethodInfo _open;

            internal Entry(Type windowType, DataEditorAttribute attribute, MethodInfo open)
            {
                WindowType = windowType;
                DataType = attribute.DataType;
                Label = attribute.Label;
                Order = attribute.Order;
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
                    var sorted = new List<Entry>(entries);
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

    // Inspector 最上部に描く「エディターで開く」ボタン列(IMGUI)。UI Toolkit 製 Inspector からは Build を使う。
    public static class DataEditorHeader
    {
        private const float ButtonHeight = 26f;

        public static void Draw(AssetDataBase target)
        {
            if (target == null)
            {
                return;
            }

            var entries = DataEditorRegistry.GetEntries(target.GetType());
            if (entries.Count == 0)
            {
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var entry in entries)
                {
                    var content = new GUIContent("▶ " + entry.Label, $"{entry.WindowType.Name} でこのアセットを開く");
                    if (GUILayout.Button(content, GUILayout.Height(ButtonHeight)))
                    {
                        entry.Open(target);
                        GUIUtility.ExitGUI();
                    }
                }
            }

            EditorGUILayout.Space(4f);
        }

        public static UnityEngine.UIElements.VisualElement Build(AssetDataBase target)
        {
            var row = new UnityEngine.UIElements.VisualElement { style = { flexDirection = UnityEngine.UIElements.FlexDirection.Row, marginBottom = 4 } };
            if (target == null)
            {
                return row;
            }

            foreach (var entry in DataEditorRegistry.GetEntries(target.GetType()))
            {
                var captured = entry;
                row.Add(new UnityEngine.UIElements.Button(() => captured.Open(target))
                {
                    text = "▶ " + entry.Label,
                    tooltip = $"{entry.WindowType.Name} でこのアセットを開く",
                    style = { height = ButtonHeight, flexGrow = 1f },
                });
            }

            return row;
        }
    }
}
