using System;
using System.Collections.Generic;
using DDrive.Foundation.Identity;

namespace DDrive.Editor.Dependencies
{
    // [11_tasks.md] 5-5 — 依存関係グラフのキャッシュ形式と公開クエリ結果の型。
    // キャッシュは Library/DDriveDeps/<guid>.json にファイル単位で保存する(コミットしない。CLAUDE.md §2)。
    // JsonUtility でそのままシリアライズできるよう、キャッシュ用の型は Dictionary を持たない素朴なクラスにする。

    [Serializable]
    internal sealed class DependencyEdgeRecord
    {
        // シーン/Prefab: ルートから対象 GameObject までの相対パス("Root/Child")。Data(.asset)自身の参照は空文字。
        public string ObjectPath;

        // コンポーネント型名(MonoBehaviour 等)、または Data 型名(AssetDataBase の具象型)。
        public string ComponentType;

        // SerializedProperty.propertyPath(配列内なら "Events.Array.data[0].Target" 等)。
        public string PropertyPath;

        // DDrive.Foundation.Identity.AssetType を int にしたもの(JsonUtility は enum を int としてそのまま扱える)。
        public int TargetType;

        public ulong TargetId;
    }

    [Serializable]
    internal sealed class DependencyFileRecord
    {
        public string Guid;
        public string Path;

        // 変更検知用(現状は差分更新を AssetPostprocessor に任せているため未使用だが、
        // キャッシュの健全性確認・デバッグ用に残す。要判断は docs/28 参照)。
        public string ContentHash;

        public List<DependencyEdgeRecord> Edges = new();
    }

    // 「この ID を使っている場所」の 1 件(5-6 の使用箇所検索 UI がそのまま表示に使える形)。
    public readonly struct DependencyReference
    {
        public readonly string SourcePath;
        public readonly string ObjectPath;
        public readonly string ComponentType;
        public readonly string PropertyPath;
        public readonly AssetType TargetType;
        public readonly ulong TargetId;

        public DependencyReference(string sourcePath, string objectPath, string componentType, string propertyPath, AssetType targetType, ulong targetId)
        {
            SourcePath = sourcePath;
            ObjectPath = objectPath;
            ComponentType = componentType;
            PropertyPath = propertyPath;
            TargetType = targetType;
            TargetId = targetId;
        }
    }

    // 「どこからも参照されない ID」の 1 件(5-6 の未使用検出 UI 向け)。
    public readonly struct UnusedAssetId
    {
        public readonly AssetType Type;
        public readonly ulong Id;
        public readonly string AssetPath;
        public readonly string DisplayName;

        public UnusedAssetId(AssetType type, ulong id, string assetPath, string displayName)
        {
            Type = type;
            Id = id;
            AssetPath = assetPath;
            DisplayName = displayName;
        }
    }
}
