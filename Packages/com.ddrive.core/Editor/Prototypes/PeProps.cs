using System;
using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Runtime.Vfx;
using UnityEngine;
using UnityEngine.Rendering;

namespace DDrive.EditorPrototypes
{
    // E: 「＋ つまみを追加」の候補。VfxDataValidator の TargetProperty 判定(Prefab の全 Renderer の sharedMaterial.HasProperty)と
    // 同じ Renderer / Material の列挙から、実在するシェーダープロパティだけを返す。
    // (Validator の AnyRendererHasProperty は DDrive.Runtime の private で、Editor アセンブリからは見えないため同じ列挙を持つ。判定は変えない。)
    internal static class PeProps
    {
        internal struct Candidate
        {
            public string Name;
            public ShaderPropertyType Type;
            public ParamValue Value;
        }

        // つまみの種類(パレットの項目)。
        internal enum Kind { Color, Size, Speed, Strength, Opacity, Lifetime, Free }

        public static readonly string[] KindNames = { "色", "サイズ", "速度", "強さ", "不透明度", "寿命", "自由入力" };
        public static readonly string[] KindLabels = { "Color", "Size", "Speed", "Strength", "Opacity", "Lifetime", "Param" };

        private static readonly string[][] Words =
        {
            new string[0],
            new[] { "size", "scale", "radius", "width", "thick" },
            new[] { "speed", "rate", "velocity", "freq", "scroll" },
            new[] { "intensity", "strength", "power", "emission", "glow", "bright", "amount" },
            new[] { "alpha", "opacity", "transparen", "fade" },
            new[] { "life", "age", "duration" },
        };

        public static List<Candidate> Enumerate(GameObject prefab)
        {
            var all = new List<Candidate>();
            var hidden = new List<Candidate>();
            if (prefab == null)
            {
                return all;
            }

            var seen = new HashSet<string>();
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r.sharedMaterial == null || r.sharedMaterial.shader == null)
                {
                    continue;
                }

                var mat = r.sharedMaterial;
                var sh = mat.shader;
                for (var i = 0; i < sh.GetPropertyCount(); i++)
                {
                    var name = sh.GetPropertyName(i);
                    var t = sh.GetPropertyType(i);
                    if (!mat.HasProperty(name) || !seen.Add(name))
                    {
                        continue;
                    }

                    var c = new Candidate { Name = name, Type = t, Value = ReadValue(mat, name, t) };
                    if ((sh.GetPropertyFlags(i) & ShaderPropertyFlags.HideInInspector) != 0)
                    {
                        hidden.Add(c);
                    }
                    else
                    {
                        all.Add(c);
                    }
                }
            }

            return all.Count > 0 ? all : hidden;
        }

        private static ParamValue ReadValue(Material mat, string name, ShaderPropertyType t)
        {
            switch (t)
            {
                case ShaderPropertyType.Color: return ParamValue.Of(mat.GetColor(name));
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: return ParamValue.Of(mat.GetFloat(name));
                case ShaderPropertyType.Vector: return new ParamValue { Type = ParamValueType.Vector, VectorValue = mat.GetVector(name) };
                default: return ParamValue.Of(0f);
            }
        }

        public static VfxParamType ParamTypeOf(ShaderPropertyType t)
        {
            switch (t)
            {
                case ShaderPropertyType.Color: return VfxParamType.Color;
                case ShaderPropertyType.Vector: return VfxParamType.Vector;
                case ShaderPropertyType.Texture: return VfxParamType.Texture;
                default: return VfxParamType.Float;
            }
        }

        // 種類に合う候補(色 = Color 型、それ以外 = Float / Range 型で名前に語を含むもの)。
        public static List<Candidate> ForKind(List<Candidate> all, Kind kind, HashSet<string> used)
        {
            var list = new List<Candidate>();
            foreach (var c in all)
            {
                if (used != null && used.Contains(c.Name))
                {
                    continue;
                }

                if (kind == Kind.Color)
                {
                    if (c.Type == ShaderPropertyType.Color)
                    {
                        list.Add(c);
                    }

                    continue;
                }

                if ((c.Type == ShaderPropertyType.Float || c.Type == ShaderPropertyType.Range) && HasWord(c.Name, Words[(int)kind]))
                {
                    list.Add(c);
                }
            }

            return list;
        }

        public static bool HasWord(string text, string[] words)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            for (var i = 0; i < words.Length; i++)
            {
                if (text.IndexOf(words[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        // カテゴリ(0 色 / 1 サイズ / 2 速度 / 3 強さ / 4 不透明度 / 5 その他)を Type と Label / TargetProperty の語から推定する。
        public static readonly string[] CategoryNames = { "色", "サイズ", "速度", "強さ", "不透明度", "その他" };

        public static int Infer(VfxParam p)
        {
            if (p.Type == VfxParamType.Color)
            {
                return 0;
            }

            var text = (p.Label ?? string.Empty) + " " + (p.TargetProperty ?? string.Empty);
            if (HasWord(text, new[] { "color", "colour", "tint", "色" }))
            {
                return 0;
            }

            if (HasWord(text, Words[(int)Kind.Opacity]) || HasWord(text, new[] { "不透明", "透明" }))
            {
                return 4;
            }

            if (HasWord(text, Words[(int)Kind.Size]) || HasWord(text, new[] { "サイズ", "大きさ" }))
            {
                return 1;
            }

            if (HasWord(text, Words[(int)Kind.Speed]) || HasWord(text, new[] { "速" }))
            {
                return 2;
            }

            if (HasWord(text, Words[(int)Kind.Strength]) || HasWord(text, new[] { "強" }))
            {
                return 3;
            }

            return 5;
        }
    }
}
