using System;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Anim2D;
using DDrive.Runtime.Audio;
using DDrive.Runtime.CameraShake;
using DDrive.Runtime.Haptics;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using DDrive.Runtime.Prefab;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Ui;
using DDrive.Runtime.Vfx;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Spec
{
    // [32_spec_web.md] §10.4.2(O-6) — 各 Data 具象型のパラメータスキーマ(名前・型・説明・範囲)と、
    // インポート済アセットの現在値を SerializedObject/SerializedProperty の反射で自動生成する。
    // 各 Data クラスへの手入れ(属性の追加等)は不要(docs 引き継ぎ事項の要件どおり)。
    //
    // 対象は AssetType の enum 名一覧(16種類。ControlSkin だけ ButtonSkinData/SliderSkinData の
    // 2 つの具象型がある、[27_spec_sheet.md] §7.2 を参照)。新しい AssetType/具象 Data を追加したら
    // ここの TypeMap にも追記すること(CLAUDE.md §3-1 「grep してから書く」の対象。AssetCreationService の
    // カテゴリ判定など、他にも AssetType ごとに個別対応が必要な箇所がある)。
    //
    // AssetDataBase 自身が持つ共通の管理項目(Id/DisplayName/Category/Tags/Icon/Assignee/SpecUrl/…)は
    // Web 側の発注(assets)自体が別欄として既に持っているため、パラメータ一覧からは除外する
    // (FieldInfo.DeclaringType == typeof(AssetDataBase) のものだけをスキップする。ControlSkinData のような
    // 中間基底クラスの共通フィールドは種別固有パラメータとして含める)。
    public static class SpecParamSchemaBuilder
    {
        private static readonly (AssetType AssetType, Type[] ConcreteTypes)[] TypeMap =
        {
            (AssetType.Se, new[] { typeof(SeData) }),
            (AssetType.Bgm, new[] { typeof(BgmData) }),
            (AssetType.Vfx, new[] { typeof(VfxData) }),
            (AssetType.Anim, new[] { typeof(AnimData) }),
            (AssetType.Anim2D, new[] { typeof(Anim2DData) }),
            (AssetType.Material, new[] { typeof(MaterialData) }),
            (AssetType.Texture, new[] { typeof(TextureData) }),
            (AssetType.Canvas, new[] { typeof(CanvasData) }),
            (AssetType.Prefab, new[] { typeof(PrefabData) }),
            (AssetType.Presentation, new[] { typeof(PresentationData) }),
            (AssetType.Shake, new[] { typeof(CameraShakeData) }),
            (AssetType.Haptics, new[] { typeof(HapticsData) }),
            (AssetType.UiTween, new[] { typeof(UiTweenData) }),
            (AssetType.Model, new[] { typeof(ModelData) }),
            (AssetType.Anchor, new[] { typeof(AnchorData) }),
            (AssetType.AnchorGroup, new[] { typeof(AnchorGroupData) }),
            (AssetType.ControlSkin, new[] { typeof(ButtonSkinData), typeof(SliderSkinData) }),
        };

        // 種別ごとのパラメータスキーマ一覧(16種類分、ControlSkin は2件。§10.2.4「paramSchemas.json」)。
        public static JArray BuildSchemas()
        {
            var schemas = new JArray();
            foreach (var (assetType, concreteTypes) in TypeMap)
            {
                foreach (var concreteType in concreteTypes)
                {
                    schemas.Add(BuildSchemaFor(assetType, concreteType));
                }
            }

            return schemas;
        }

        private static JObject BuildSchemaFor(AssetType assetType, Type concreteType)
        {
            var fields = new JArray();
            ScriptableObject instance = null;
            try
            {
                instance = ScriptableObject.CreateInstance(concreteType);
                var serializedObject = new SerializedObject(instance);
                IterateTopLevelProperties(serializedObject, concreteType, (property, fieldInfo) =>
                {
                    fields.Add(BuildFieldEntry(property, fieldInfo));
                });
            }
            catch (Exception e)
            {
                // CLAUDE.md §0-4: 例外で同期全体を止めない。この型だけスキーマが空になる。
                Debug.LogWarning($"[DDrive] '{concreteType.Name}' のパラメータスキーマ生成に失敗しました: {e.Message}");
            }
            finally
            {
                if (instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }

            return new JObject
            {
                ["assetType"] = assetType.ToString(),
                ["concreteType"] = concreteType.Name,
                ["fields"] = fields,
            };
        }

        private static JObject BuildFieldEntry(SerializedProperty property, FieldInfo fieldInfo)
        {
            var entry = new JObject
            {
                ["name"] = property.name,
                ["type"] = fieldInfo != null ? FormatTypeName(fieldInfo.FieldType) : property.type,
                // SerializedProperty.tooltip は [Tooltip] の内容を Unity が解決済みのもの(無ければ空文字)。
                ["tooltip"] = property.tooltip ?? string.Empty,
            };

            if (fieldInfo != null)
            {
                var range = fieldInfo.GetCustomAttribute<RangeAttribute>();
                if (range != null)
                {
                    entry["min"] = range.min;
                    entry["max"] = range.max;
                }
                else
                {
                    var min = fieldInfo.GetCustomAttribute<MinAttribute>();
                    if (min != null)
                    {
                        entry["min"] = min.min;
                    }
                }
            }

            return entry;
        }

        // インポート済(isPlaceholder=false)アセットの現在値(§10.2.4「currentValues」)。
        // ObjectReference は実体を送らず表示名のみ、配列は件数のみ(値そのものを公開しない、§10.4.2)。
        // Vector/Color/AnimationCurve 等の複合型は現在値を送らない(スキーマ側の型名表示で足りる)。
        public static JObject BuildCurrentValues(AssetDataBase asset)
        {
            var values = new JObject();
            if (asset == null)
            {
                return values;
            }

            var serializedObject = new SerializedObject(asset);
            IterateTopLevelProperties(serializedObject, asset.GetType(), (property, _) =>
            {
                var token = BuildCurrentValueToken(property);
                if (token != null)
                {
                    values[property.name] = token;
                }
            });

            return values;
        }

        private static JToken BuildCurrentValueToken(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                // 注意: `cond ? "文字列" : null` は string 型の null になり、JToken への暗黙変換で
                // JValue(String, null) が作られて「空文字の値」として送られてしまう。null は JToken として返す。
                case SerializedPropertyType.ObjectReference:
                    if (property.objectReferenceValue == null)
                    {
                        return null;
                    }

                    return property.objectReferenceValue.name;
                case SerializedPropertyType.Boolean:
                    return property.boolValue;
                case SerializedPropertyType.Integer:
                    return property.intValue;
                case SerializedPropertyType.Float:
                    return property.floatValue;
                case SerializedPropertyType.String:
                    return property.stringValue;
                case SerializedPropertyType.Enum:
                    if (property.enumDisplayNames != null
                        && property.enumValueIndex >= 0
                        && property.enumValueIndex < property.enumDisplayNames.Length)
                    {
                        return property.enumDisplayNames[property.enumValueIndex];
                    }

                    return property.enumValueIndex.ToString();
                default:
                    // 配列(AudioClip[] 等)は件数のみ([32] §10.2.4 の例「Clips: "2 件"」)。
                    if (!property.isArray)
                    {
                        return null;
                    }

                    return $"{property.arraySize} 件";
            }
        }

        // SerializedObject のトップレベル(depth 0)の可視プロパティだけを列挙する
        // (enterChildren=false で子へ降りないため、struct/配列の内部フィールドには入らない=
        // 「ネストは表示名+型名で1行に畏める」という要件を自然に満たす)。m_Script は除外し、
        // AssetDataBase 自身が宣言したフィールド(共通の管理項目)もここで除外する。
        private static void IterateTopLevelProperties(
            SerializedObject serializedObject,
            Type concreteType,
            Action<SerializedProperty, FieldInfo> onProperty)
        {
            var iterator = serializedObject.GetIterator();
            var enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.name == "m_Script")
                {
                    continue;
                }

                var fieldInfo = FindFieldInfo(concreteType, iterator.name);
                if (fieldInfo != null && fieldInfo.DeclaringType == typeof(AssetDataBase))
                {
                    continue;
                }

                onProperty(iterator, fieldInfo);
            }
        }

        private static FieldInfo FindFieldInfo(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var t = type; t != null; t = t.BaseType)
            {
                var field = t.GetField(name, flags);
                if (field != null)
                {
                    return field;
                }
            }

            return null;
        }

        private static string FormatTypeName(Type type)
        {
            if (type.IsArray)
            {
                return FormatTypeName(type.GetElementType()) + "[]";
            }

            if (type.IsGenericType)
            {
                if (type.GetGenericTypeDefinition() == typeof(List<>))
                {
                    return FormatTypeName(type.GetGenericArguments()[0]) + "[]";
                }

                // AssetId<TMarker> 等、その他のジェネリック型は "Name<Arg1,Arg2>" の形にする
                // ("AssetId`1" のような CLR 表記のままにはしない)。
                var name = type.Name;
                var tick = name.IndexOf('`');
                if (tick >= 0)
                {
                    name = name.Substring(0, tick);
                }

                var args = Array.ConvertAll(type.GetGenericArguments(), FormatTypeName);
                return $"{name}<{string.Join(",", args)}>";
            }

            if (type == typeof(float)) return "float";
            if (type == typeof(int)) return "int";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            if (type == typeof(double)) return "double";
            if (type == typeof(long)) return "long";
            if (type == typeof(uint)) return "uint";
            if (type == typeof(ulong)) return "ulong";

            return type.Name;
        }
    }
}
