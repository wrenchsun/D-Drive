using System;
using UnityEngine;

namespace DDrive.Foundation.Data
{
    public enum ParamValueType
    {
        Float,
        Int,
        Bool,
        Color,
        Vector,
        Curve,
        Gradient,
        String,
        Object,
    }

    [Serializable]
    public struct ParamValue
    {
        public ParamValueType Type;
        public float FloatValue;
        public int IntValue;
        public bool BoolValue;
        public Color ColorValue;
        public Vector4 VectorValue;
        public AnimationCurve CurveValue;
        public Gradient GradientValue;
        public string StringValue;
        public UnityEngine.Object ObjectValue;

        public static ParamValue Of(float value) => new ParamValue { Type = ParamValueType.Float, FloatValue = value };
        public static ParamValue Of(int value) => new ParamValue { Type = ParamValueType.Int, IntValue = value };
        public static ParamValue Of(bool value) => new ParamValue { Type = ParamValueType.Bool, BoolValue = value };
        public static ParamValue Of(Color value) => new ParamValue { Type = ParamValueType.Color, ColorValue = value };
        public static ParamValue Of(string value) => new ParamValue { Type = ParamValueType.String, StringValue = value };
    }
}
