using System;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Values;
using UnityEngine;

namespace DDrive.Tests.Editor.Mcp
{
    // SerializedFieldIo が対応する全プロパティ型を 1 つずつ持つ、テスト専用の ScriptableObject(アセットは作らない)。
    public enum McpProbeMode
    {
        Alpha,
        Beta,
        Gamma,
    }

    [Serializable]
    public struct McpProbeItem
    {
        public string Label;
        public int Count;
    }

    [Serializable]
    public struct McpProbeGroup
    {
        public float Gain;
        public McpProbeItem Inner;
    }

    public sealed class McpIoProbe : ScriptableObject
    {
        public bool Flag;
        public int IntValue;
        public uint UIntValue;
        public long LongValue;
        public ulong ULongValue;
        public byte ByteValue;
        public float FloatValue;
        public double DoubleValue;
        public string Text;
        public char Letter;
        public McpProbeMode Mode;
        public Color Tint;
        public Vector2 Vec2;
        public Vector3 Vec3;
        public Vector4 Vec4;
        public Quaternion Rot;
        public Vector2Int Vec2I;
        public Vector3Int Vec3I;
        public Rect Area;
        public Bounds Box;
        public AnimationCurve Curve;
        public LayerMask Layers;
        public TextAsset TextRef;
        public AudioClip ClipRef;
        public McpProbeGroup Group;
        public McpProbeItem[] Items;
        public string[] Names;
        public int[] Numbers;
        public AssetId<DDrive.Runtime.Audio.SeMarker> SeRef;
        public ValueDef Motion;
        public Gradient Grad;
    }
}
