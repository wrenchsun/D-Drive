using System;

namespace DDrive.Editor.Inspector
{
    // [09_editor_tools.md] §8 — 「この Data 種別は、この EditorWindow で編集する」を宣言する属性。
    // EditorWindow に付けると、その Data の Inspector 最上部に「〜で開く」ボタンが自動で出る(AssetDataInspector)。
    // 既存・新規を問わず専用エディタを持つ Data は必ずこれを付ける(DataEditorRegistryTests が未登録を検出する)。
    //
    // Open メソッド: public static で引数 1 つ(DataType を受け取れる型)のもの。既定名は "Open"。
    // 1 つのウィンドウが複数の Data 種別を扱う場合(AudioEditor = SE / BGM)は属性を複数付ける。
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class DataEditorAttribute : Attribute
    {
        public Type DataType { get; }
        public string Label { get; }
        public string OpenMethod { get; }

        // 同じ Data に複数のボタンが付くときの並び順(小さい順)。
        public int Order { get; set; }

        public DataEditorAttribute(Type dataType, string label, string openMethod = "Open")
        {
            DataType = dataType;
            Label = label;
            OpenMethod = openMethod;
        }
    }
}
