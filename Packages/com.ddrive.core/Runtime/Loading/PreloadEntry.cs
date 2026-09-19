using System;
using DDrive.Foundation.Identity;

namespace DDrive.Runtime.Loading
{
    // [11_tasks.md] 5-7 — ScenePreloadList 1 件分。DisplayName は実行時には使わない
    // (目視確認 / デバッグ表示専用。Editor の集計時に DependencyAssetResolver で解決した表示名を焼き込む)。
    [Serializable]
    public struct PreloadEntry : IEquatable<PreloadEntry>
    {
        public AssetType Type;
        public ulong Id;
        public string DisplayName;

        public PreloadEntry(AssetType type, ulong id, string displayName)
        {
            Type = type;
            Id = id;
            DisplayName = displayName;
        }

        public bool Equals(PreloadEntry other) => Id == other.Id && Type == other.Type;
        public override bool Equals(object obj) => obj is PreloadEntry other && Equals(other);
        public override int GetHashCode() => Id.GetHashCode();
    }
}
