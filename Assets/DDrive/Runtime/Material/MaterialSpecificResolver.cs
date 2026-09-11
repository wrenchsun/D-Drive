using System.Collections.Generic;
using DDrive.Foundation.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace DDrive.Runtime.Material
{
    // [06_material_texture.md] A-2 — シェーダーから固有パラメータ(Specific)を自動解決する(2026-09-11)。
    //   Resolve : シェーダーのプロパティを MaterialCommonNaming の規約でふるい、固有だけを既定値付き ShaderParam にする
    //   Merge   : 既存の Specific を保持したまま、足りないものだけ追加する(値は上書きしない。シェーダーに無くなったものも残す = Validator が警告)
    // 純関数(Data を書き換えない)。Data への書き込みは Editor 側(MaterialSpecificSync)が Undo 付きで行う。
    // 実行時に呼ばない(Shader.GetProperty* は毎回文字列を作る)。
    public static class MaterialSpecificResolver
    {
        public sealed class MergeReport
        {
            public readonly List<string> Added = new();   // 追加した(シェーダーにあるが Specific に無かった)
            public readonly List<string> Kept = new();    // 既に Specific にあった(値は保持)
            public readonly List<string> Stale = new();   // Specific にあるがシェーダーに無い(残す。Validator が警告)
            public bool Changed => Added.Count > 0;
        }

        // シェーダーの固有プロパティを既定値付きで列挙する。shader が null なら空。
        public static List<ShaderParam> Resolve(Shader shader)
        {
            var result = new List<ShaderParam>();
            if (shader == null)
            {
                return result;
            }

            var count = shader.GetPropertyCount();
            for (var i = 0; i < count; i++)
            {
                var name = shader.GetPropertyName(i);
                if (!MaterialCommonNaming.IsSpecific(name, shader.GetPropertyFlags(i)))
                {
                    continue;
                }

                if (TryDefaultValue(shader, i, out var value))
                {
                    result.Add(new ShaderParam { Property = name, Value = value });
                }
            }

            return result;
        }

        // シェーダーにあって existing に無い固有だけを末尾に足した配列を返す(existing の順序・値は保持)。
        public static ShaderParam[] Merge(ShaderParam[] existing, Shader shader, MergeReport report = null)
        {
            var resolved = Resolve(shader);
            var merged = new List<ShaderParam>((existing?.Length ?? 0) + resolved.Count);
            var seen = new HashSet<string>();

            if (existing != null)
            {
                for (var i = 0; i < existing.Length; i++)
                {
                    merged.Add(existing[i]);
                    var name = existing[i].Property;
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    seen.Add(name);
                    if (shader == null || shader.FindPropertyIndex(name) >= 0)
                    {
                        report?.Kept.Add(name);
                    }
                    else
                    {
                        report?.Stale.Add(name);
                    }
                }
            }

            for (var i = 0; i < resolved.Count; i++)
            {
                if (seen.Add(resolved[i].Property))
                {
                    merged.Add(resolved[i]);
                    report?.Added.Add(resolved[i].Property);
                }
            }

            return merged.ToArray();
        }

        // シェーダーにあって Specific に無い固有の名前(Validator の「未登録」表示用)。
        public static List<string> FindUnregistered(ShaderParam[] existing, Shader shader)
        {
            var missing = new List<string>();
            var resolved = Resolve(shader);
            for (var i = 0; i < resolved.Count; i++)
            {
                if (!Contains(existing, resolved[i].Property))
                {
                    missing.Add(resolved[i].Property);
                }
            }

            return missing;
        }

        private static bool Contains(ShaderParam[] list, string property)
        {
            if (list == null)
            {
                return false;
            }

            for (var i = 0; i < list.Length; i++)
            {
                if (list[i].Property == property)
                {
                    return true;
                }
            }

            return false;
        }

        // シェーダー宣言の既定値を ParamValue に写す。Texture は Object(null)= 未設定として登録する。
        // ShaderLab の旧記法 `Int` は Float として報告される(Int になるのは `Integer` 記法のみ)。
        private static bool TryDefaultValue(Shader shader, int index, out ParamValue value)
        {
            switch (shader.GetPropertyType(index))
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    value = ParamValue.Of(shader.GetPropertyDefaultFloatValue(index));
                    return true;
                case ShaderPropertyType.Int:
                    value = ParamValue.Of(shader.GetPropertyDefaultIntValue(index));
                    return true;
                case ShaderPropertyType.Color:
                    var color = shader.GetPropertyDefaultVectorValue(index);
                    value = ParamValue.Of(new Color(color.x, color.y, color.z, color.w));
                    return true;
                case ShaderPropertyType.Vector:
                    value = new ParamValue { Type = ParamValueType.Vector, VectorValue = shader.GetPropertyDefaultVectorValue(index) };
                    return true;
                case ShaderPropertyType.Texture:
                    value = new ParamValue { Type = ParamValueType.Object, ObjectValue = null };
                    return true;
                default:
                    value = default;
                    return false;
            }
        }
    }
}
