using System.Collections.Generic;
using System.Reflection;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Values;
using UnityEngine;

namespace DDrive.Foundation.Validation
{
    // [17_value_definition.md] §6。種別側の Validator はこの検査を再実装せず、ここに一本化する。
    // public フィールドを再帰的に走査し、ValueDef/ValueDef3/ValueDefColor を見つけたものすべてに適用する。
    public sealed class ValueDefValidator : IUniversalValidator
    {
        private const int MaxDepth = 5;

        public AssetType Target => AssetType.None;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            foreach (var (path, value) in FindValueDefs(data, string.Empty, 0))
            {
                foreach (var result in CheckValueDef(path, value))
                {
                    yield return result;
                }
            }
        }

        private static IEnumerable<(string path, ValueDef value)> FindValueDefs(object obj, string path, int depth)
        {
            if (obj == null || depth > MaxDepth)
            {
                yield break;
            }

            var type = obj.GetType();

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var fieldPath = string.IsNullOrEmpty(path) ? field.Name : $"{path}.{field.Name}";
                var value = field.GetValue(obj);
                if (value == null)
                {
                    continue;
                }

                if (field.FieldType == typeof(ValueDef))
                {
                    yield return (fieldPath, (ValueDef)value);
                }
                else if (field.FieldType == typeof(ValueDef3))
                {
                    var v3 = (ValueDef3)value;
                    yield return ($"{fieldPath}.X", v3.X);
                    yield return ($"{fieldPath}.Y", v3.Y);
                    yield return ($"{fieldPath}.Z", v3.Z);
                }
                else if (field.FieldType == typeof(ValueDefColor))
                {
                    yield return ($"{fieldPath}.Alpha", ((ValueDefColor)value).Alpha);
                }
                else if (field.FieldType.IsArray && value is System.Array array)
                {
                    for (var i = 0; i < array.Length; i++)
                    {
                        var element = array.GetValue(i);
                        if (element == null)
                        {
                            continue;
                        }

                        foreach (var found in FindValueDefs(element, $"{fieldPath}[{i}]", depth + 1))
                        {
                            yield return found;
                        }
                    }
                }
                else if (IsTraversableDDriveStruct(field.FieldType))
                {
                    foreach (var found in FindValueDefs(value, fieldPath, depth + 1))
                    {
                        yield return found;
                    }
                }
            }
        }

        private static bool IsTraversableDDriveStruct(System.Type type)
            => type.IsValueType && !type.IsPrimitive && !type.IsEnum &&
               type.Namespace != null && type.Namespace.StartsWith("DDrive");

        private static IEnumerable<ValidationResult> CheckValueDef(string path, ValueDef def)
        {
            if (def.Mode == ValueMode.Curve && (def.Curve == null || def.Curve.length == 0))
            {
                yield return ValidationResult.Error($"{path}: Mode=Curve だがキーが 0 本 / 未設定です");
            }

            if (def.Mode == ValueMode.Parametric && def.Parametric.Kind == ParametricKind.CustomBezier &&
                def.Parametric.BezierP1 == Vector2.zero && def.Parametric.BezierP2 == Vector2.zero)
            {
                yield return ValidationResult.Error($"{path}: Mode=Parametric(CustomBezier) だが制御点が未設定です");
            }

            if (def.Time.Mode == TimeMode.Duration && def.Time.Value <= 0f)
            {
                yield return ValidationResult.Error($"{path}: TimeMode=Duration ですが Value が 0 以下です");
            }

            if ((def.Loop == LoopMode.Loop || def.Loop == LoopMode.PingPong) &&
                def.Time.Mode == TimeMode.Duration && def.Time.Value == 0f)
            {
                yield return ValidationResult.Error($"{path}: 無限ループのまま Duration=0 のためフリーズします");
            }

            var fromToMatters = def.Mode == ValueMode.Parametric || (def.Mode == ValueMode.Curve && !def.Normalized);
            if (fromToMatters && def.From == def.To)
            {
                yield return ValidationResult.Warning($"{path}: From と To が同じで値が変化しません");
            }

            if (def.Mode == ValueMode.Constant && def.Loop != LoopMode.Once)
            {
                yield return ValidationResult.Info($"{path}: Mode=Constant なのに Loop 設定は無意味です");
            }

            if (def.Time.Mode == TimeMode.Rate && def.Loop == LoopMode.Once)
            {
                yield return ValidationResult.Warning($"{path}: TimeMode=Rate なのに Loop=Once です(終端がない動きのはずです)");
            }

            if (def.Time.SpeedScale <= 0f)
            {
                yield return ValidationResult.Error($"{path}: SpeedScale が 0 以下です");
            }
        }
    }
}
