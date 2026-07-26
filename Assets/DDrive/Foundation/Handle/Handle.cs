using System;

namespace DDrive.Foundation.Handle
{
    // 種別ごとの Handle(VfxHandle 等)は Handle<TMarker> のエイリアスとして定義する。
    // GC alloc 0(struct)。有効性は生成元の InstanceStore<TMarker,_> に対して問い合わせる。
    public readonly struct Handle<TMarker> : IEquatable<Handle<TMarker>>
    {
        public readonly int Index;
        public readonly int Generation;

        internal Handle(int index, int generation)
        {
            Index = index;
            Generation = generation;
        }

        public static readonly Handle<TMarker> Invalid = new(-1, 0);

        public bool Equals(Handle<TMarker> other) => Index == other.Index && Generation == other.Generation;
        public override bool Equals(object obj) => obj is Handle<TMarker> other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Index, Generation);
        public override string ToString() => $"Handle({Index}#{Generation})";

        public static bool operator ==(Handle<TMarker> a, Handle<TMarker> b) => a.Equals(b);
        public static bool operator !=(Handle<TMarker> a, Handle<TMarker> b) => !a.Equals(b);
    }
}
