using System;
using DDrive.Foundation.Identity;

namespace DDrive.Foundation.Data
{
    // 任意種別の ID を型安全でない形で保持する軽量参照。AssetEvent.Target 等に使う。
    [Serializable]
    public struct AssetRef : IEquatable<AssetRef>
    {
        public AssetType Type;
        public ulong Id;

        public bool IsAssigned => Id != 0;

        public static AssetRef From<TMarker>(AssetId<TMarker> id) => new AssetRef { Type = id.Type, Id = id.Value };

        public bool Equals(AssetRef other) => Type == other.Type && Id == other.Id;
        public override bool Equals(object obj) => obj is AssetRef other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Type, Id);

        public static bool operator ==(AssetRef a, AssetRef b) => a.Equals(b);
        public static bool operator !=(AssetRef a, AssetRef b) => !a.Equals(b);
    }
}
