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

        // [1014_extension_platform.md] §2.1(X-1、2026-10-16): 種別キー。Type が AssetType.Extension のときは必須
        // (空だと AssetIdGenerator が Error を出して飛ばす)。組み込み種別では空のまま。
        // 名前付き引数で指定する: [AssetIdDefinition(AssetType.Extension, typeof(XMarker), "XID", KindKey = "X")]
        public string KindKey { get; set; }

        public AssetIdDefinitionAttribute(AssetType type, Type markerType, string constantsClassName)
        {
            Type = type;
            MarkerType = markerType;
            ConstantsClassName = constantsClassName;
        }
    }
}
