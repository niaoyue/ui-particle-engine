using UnityEngine;

namespace UIParticleEngine
{
    /// <summary>Serializable [min, max] range. Assigning a single float makes a constant range.</summary>
    [System.Serializable]
    public struct RangeF
    {
        public float Min;
        public float Max;

        public RangeF(float min, float max)
        {
            Min = min;
            Max = max;
        }

        public float Evaluate(float t) => Mathf.LerpUnclamped(Min, Max, t);

        public static implicit operator RangeF(float value) => new RangeF(value, value);
    }

    /// <summary>Tiny xorshift RNG — deterministic when seeded, no allocations.</summary>
    public struct XorShift32
    {
        private uint _state;

        public XorShift32(uint seed)
        {
            _state = seed == 0 ? 2463534242u : seed;
        }

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return _state = x;
        }

        /// <summary>Random float in [0, 1).</summary>
        public float Next01() => (NextUInt() & 0xFFFFFF) / 16777216f;

        public float Range(in RangeF range) => range.Evaluate(Next01());

        public float Range(float min, float max) => Mathf.LerpUnclamped(min, max, Next01());
    }
}
