using System;

namespace DDrive.Foundation.Identity
{
    // AssetDataBase 派生クラスに付与し、ID 定数ジェネレータ(Editor/Codegen)へ
    // 「この Data 型は種別 Type・マーカー MarkerType・生成先クラス名 ConstantsClassName で
    // ID 定数を生成してよい」と宣言する。新しいアセット種別の追加はこの属性を足すだけでよく、
    // ジェネレータ本体(Foundation/Editor)の改修を要しない(NFR-7)。
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class AssetIdDefinitionAttribute : Attribute
    {
        public AssetType Type { get; }
        public Type MarkerType { get; }
        public string ConstantsClassName { get; }

        public AssetIdDefinitionAttribute(AssetType type, Type markerType, string constantsClassName)
        {
            Type = type;
            MarkerType = markerType;
            ConstantsClassName = constantsClassName;
        }
    }
}
