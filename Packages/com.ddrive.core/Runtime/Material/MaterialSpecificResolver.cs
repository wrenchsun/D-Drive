using System.Collections.Generic;
using DDrive.Foundation.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace DDrive.Runtime.Material
{
    // [06_material_texture.md] A-2 — シェーダーから固有パラメータ(Specific)を自動解決する(2026-09-11)。
    //   Resolve : シェーダーのプロパティを MaterialCommonNaming の規約でふるい、固有だけを既定値付き ShaderParam にする
    //   Merge   : 既存の Specific を保持したまま、足りないものだけ追加する(値は上書きしない。シェーダーに無くなったものも残す = Validator が警告)
    //             共通チャンネル名等が Specific に紛れている場合は MergeReport.Conflict に列挙する(残すが警告。2026-09-11)
    // 純関数(Data を書き換えない)。Data への書き込みは Editor 側(MaterialSpecificSync)が Undo 付きで行う。
    // 実行時に呼ばない(Shader.GetProperty* は毎回文字列を作る)。
    public static class MaterialSpecificResolver
    {
        public sealed class MergeReport
        {
            public readonly List<string> Added = new();   // 追加した(シェーダーにあるが Specific に無かった)
            public readonly List<string> Kept = new();    // 既に Specific にあった(値は保持)
            public readonly List<string> Stale = new();   // Specific にあるがシェーダーに無い(残す。Validator が警告)

            // 共通チャンネル名 / 描画ステート名 / 予約名 が Specific に入っている(2026-09-11 レビュー対応)。
            // MaterialManager は Common → Specific の順に流し込むので、これらは Common の値を黙って上書きしてしまう。
            // データを失わないよう配列には残し、ここに列挙して Validator / エディタが警告する。
            public readonly List<string> Conflict = new();

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
                    var index = shader != null ? shader.FindPropertyIndex(name) : -1;
                    if (shader == null || index >= 0)
                    {
                        report?.Kept.Add(name);
                    }
                    else
                    {
                        report?.Stale.Add(name);
                    }

                    // シェーダーにあっても「固有ではない」名前(共通チャンネル / 描画ステート / 予約 / 付随、
                    // または [HideInInspector] 等のフラグ付き)は Common を上書きするので衝突として報告する。
                    if (index >= 0 && !MaterialCommonNaming.IsSpecific(name, shader.GetPropertyFlags(index)))
                    {
                        report?.Conflict.Add(name);
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

        // その名前を Specific に置くと Common(MaterialCommonBinding)の値を上書きしてしまうか(2026-09-11 レビュー対応)。
        // Manager は Common → Specific の順に流し込むため、共通チャンネル名・描画ステート名・付随名・予約名・
        // フラグ除外プロパティが Specific にあると、デザイナーが Common で設定した値が黙って消える。
        public static bool IsConflicting(Shader shader, string property)
        {
            if (shader == null || string.IsNullOrEmpty(property))
            {
                return false;
            }

            var index = shader.FindPropertyIndex(property);
            return index >= 0 && !MaterialCommonNaming.IsSpecific(property, shader.GetPropertyFlags(index));
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
