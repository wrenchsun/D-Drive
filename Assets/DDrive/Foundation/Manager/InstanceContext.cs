using System;

namespace DDrive.Foundation.Manager
{
    public readonly struct InstanceContext : IEquatable<InstanceContext>
    {
        public readonly int Index;
        public readonly int Generation;

        public InstanceContext(int index, int generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool Equals(InstanceContext other) => Index == other.Index && Generation == other.Generation;
        public override bool Equals(object obj) => obj is InstanceContext other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Index, Generation);
    }
}
