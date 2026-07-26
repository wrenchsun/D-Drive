namespace DDrive.Foundation.Net
{
    // SE のランダム選択・PitchRange 等を Seed から決定的に引くための xorshift32。
    // 呼び出し側が struct を保持して呼ぶたびに状態を進める(0 alloc・純関数的)。
    public struct SeedRandom
    {
        private uint _state;

        public SeedRandom(ushort seed)
        {
            _state = seed == 0 ? 0x9E3779B9u : seed;
        }

        public uint NextUInt()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state;
        }

        public float NextFloat01() => (NextUInt() & 0xFFFFFF) / (float)0x1000000;

        public int NextInt(int maxExclusive) => (int)(NextFloat01() * maxExclusive);
    }
}
