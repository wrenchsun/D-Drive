using System;
using UnityEngine;

namespace DDrive.Foundation.Identity
{
    // TMarker is an empty tag struct (one per concrete asset kind, e.g. SeMarker) so that
    // SeId = AssetId<SeMarker> and VfxId = AssetId<VfxMarker> cannot be assigned to each other.
    //
    // Deliberately NOT a `readonly struct`: Unity's Inspector/SerializedProperty system cannot
    // write into C#-`readonly` fields (the CLR enforces that at the field level, not just the
    // compiler), so a readonly struct here silently breaks custom PropertyDrawers/Inspector
    // editing for every ID field in the project. Immutability from the public API is still
    // preserved via getter-only properties; only Unity's serializer touches the backing fields.
    [Serializable]
    public struct AssetId<TMarker> : IAssetId, IEquatable<AssetId<TMarker>>
    {
        [SerializeField] private ulong value;
        [SerializeField] private AssetType type;

        public AssetId(ulong value, AssetType type)
        {
            this.value = value;
            this.type = type;
        }

        public ulong Value => value;
        public AssetType Type => type;
        public bool IsValid => value != 0;

        public static readonly AssetId<TMarker> Invalid = default;

        public bool Equals(AssetId<TMarker> other) => value == other.value && type == other.type;
        public override bool Equals(object obj) => obj is AssetId<TMarker> other && Equals(other);
        public override int GetHashCode() => value.GetHashCode();
        public override string ToString() => $"{type}:{value:X16}";

        public static bool operator ==(AssetId<TMarker> a, AssetId<TMarker> b) => a.Equals(b);
        public static bool operator !=(AssetId<TMarker> a, AssetId<TMarker> b) => !a.Equals(b);
    }
}
