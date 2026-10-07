using System;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Editor.Mcp;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §4.5 MCP-3(2026-10-07) — FieldTables の完全性。
    // 「全 AssetType が表にある」「既定の欄が実在する」「Data クラスが [AssetIdDefinition] と一致する」を固定する。
    // 新しい AssetType / Data クラスを足したときにここが赤くなって、表への追記漏れに気づける。
    public class FieldTablesTests
    {
        private static IEnumerable<AssetType> AllTypes()
        {
            foreach (AssetType t in Enum.GetValues(typeof(AssetType)))
            {
                if (t != AssetType.None)
                {
                    yield return t;
                }
            }
        }

        [Test]
        public void EveryAssetType_HasAnEntry()
        {
            var count = 0;
            foreach (var type in AllTypes())
            {
                Assert.IsTrue(FieldTables.TryGet(type, out var entry), $"{type} が FieldTables に無い");
                Assert.AreEqual(type, entry.Type);
                Assert.IsNotEmpty(entry.DataClasses);
                Assert.AreEqual(entry.DataClasses.Length, entry.MainFields.Length);
                count++;
            }

            Assert.AreEqual(18, count, "AssetType は None を除いて 18 種(増えたら FieldTables と docs/1002 §4.5 を更新)");
            Assert.AreEqual(count, FieldTables.All.Count);
        }

        [Test]
        public void EveryDataClass_MatchesItsAssetIdDefinition_AndIsCovered()
        {
            var covered = new HashSet<Type>();
            foreach (var entry in FieldTables.All)
            {
                foreach (var cls in entry.DataClasses)
                {
                    Assert.IsTrue(typeof(AssetDataBase).IsAssignableFrom(cls) && !cls.IsAbstract, $"{cls.Name} は具象 AssetDataBase ではない");
                    var attr = cls.GetCustomAttribute<AssetIdDefinitionAttribute>(false);
                    Assert.IsNotNull(attr, $"{cls.Name} に [AssetIdDefinition] が無い");
                    Assert.AreEqual(entry.Type, attr.Type, $"{cls.Name} の種別が表と食い違う");
                    covered.Add(cls);
                }
            }

            // Runtime アセンブリの [AssetIdDefinition] 付き具象クラスが、全部どれかの種別に載っていること。
            foreach (var cls in typeof(DDrive.Runtime.Audio.SeData).Assembly.GetTypes())
            {
                if (cls.IsAbstract || !typeof(AssetDataBase).IsAssignableFrom(cls)
                    || cls.GetCustomAttribute<AssetIdDefinitionAttribute>(false) == null)
                {
                    continue;
                }

                Assert.IsTrue(covered.Contains(cls), $"{cls.Name} が FieldTables の DataClasses に無い(追記してください)");
            }
        }

        [Test]
        public void DefaultFields_AreAtMostTen_UniqueAndExistOnTheClass()
        {
            foreach (var entry in FieldTables.All)
            {
                for (var i = 0; i < entry.DataClasses.Length; i++)
                {
                    var cls = entry.DataClasses[i];
                    var fields = FieldTables.DefaultFields(entry.Type, cls);
                    Assert.LessOrEqual(fields.Count, FieldTables.MaxDefaultFields, $"{cls.Name}: 既定の欄は 10 個以内");
                    Assert.AreEqual(new HashSet<string>(fields).Count, fields.Count, $"{cls.Name}: 欄名が重複している");
                    CollectionAssert.IsSubsetOf(FieldTables.CommonFields, fields, $"{cls.Name}: 共通欄を含む");
                    Assert.Greater(fields.Count, FieldTables.CommonFields.Length, $"{cls.Name}: 種別の主要欄が 1 つ以上ある");

                    var instance = ScriptableObject.CreateInstance(cls);
                    try
                    {
                        var so = new SerializedObject(instance);
                        foreach (var name in fields)
                        {
                            Assert.IsNotNull(so.FindProperty(name), $"{cls.Name} にシリアライズ欄 '{name}' が無い");
                        }

                        foreach (var name in FieldTables.CommonFields)
                        {
                            Assert.IsFalse(McpGuard.IsReadOnlyField(name), $"共通の既定欄 {name} が読み取り専用になっている");
                        }
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(instance);
                    }
                }
            }
        }

        [Test]
        public void ControlSkin_HasTwoClasses_AndNeedsDataClass()
        {
            Assert.IsTrue(FieldTables.TryGet(AssetType.ControlSkin, out var entry));
            Assert.AreEqual(2, entry.DataClasses.Length);

            var missing = Assert.Throws<McpToolError>(() => FieldTables.ResolveDataClass(entry, null));
            Assert.AreEqual(McpGuard.CodeInvalidParams, missing.Code);
            StringAssert.Contains("ButtonSkinData", missing.Message);

            Assert.AreEqual(typeof(DDrive.Runtime.Ui.ButtonSkinData), FieldTables.ResolveDataClass(entry, "buttonskindata"));
            Assert.AreEqual(typeof(DDrive.Runtime.Ui.SliderSkinData), FieldTables.ResolveDataClass(entry, "SliderSkinData"));
            Assert.Throws<McpToolError>(() => FieldTables.ResolveDataClass(entry, "SeData"));
        }

        [Test]
        public void SingleClassTypes_IgnoreDataClassArgument_ButRejectOthers()
        {
            Assert.IsTrue(FieldTables.TryGet(AssetType.Se, out var entry));
            Assert.AreEqual(typeof(DDrive.Runtime.Audio.SeData), FieldTables.ResolveDataClass(entry, null));
            Assert.AreEqual(typeof(DDrive.Runtime.Audio.SeData), FieldTables.ResolveDataClass(entry, "SeData"));
            Assert.Throws<McpToolError>(() => FieldTables.ResolveDataClass(entry, "BgmData"));
        }

        [Test]
        public void TryParseType_IsCaseInsensitive_AndRejectsNoneAndNumbers()
        {
            Assert.IsTrue(FieldTables.TryParseType("se", out var t));
            Assert.AreEqual(AssetType.Se, t);
            Assert.IsTrue(FieldTables.TryParseType(" AnchorGroup ", out t));
            Assert.AreEqual(AssetType.AnchorGroup, t);
            Assert.IsFalse(FieldTables.TryParseType("None", out _));
            Assert.IsFalse(FieldTables.TryParseType("3", out _));
            Assert.IsFalse(FieldTables.TryParseType("", out _));
            Assert.IsFalse(FieldTables.TryParseType(null, out _));
            Assert.IsFalse(FieldTables.TryParseType("Foo", out _));

            var ex = Assert.Throws<McpToolError>(() => FieldTables.RequireType("Foo"));
            Assert.AreEqual(McpGuard.CodeInvalidParams, ex.Code);
            StringAssert.Contains("Cutscene", ex.Message, "種別名の一覧を案内する");
        }
    }
}
