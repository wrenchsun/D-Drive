using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DDrive.Editor.Codegen;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor.Compat
{
    // [42_distribution.md] §5.3 / §5.11-4(P-3、2026-09-20) — ID 定数名の導出規則(`AssetIdGenerator.
    // ToConstantName` + `KnownPrefixes`)を固定する。
    //
    // `ToConstantName` は internal(同一 asmdef 限定)のため、既存の `AssetIdGeneratorTests` と同じ手段
    // (`TestAssetData` を規約どおりのファイル名で作り `AssetIdGenerator.Regenerate` の出力を読む)で
    // 間接的に検証する。20 例の入出力(発効後は固定)+ `KnownPrefixes` 自体のスナップショット(19 個。
    // 発効後は追加も禁止、[42] §5.3)。
    public class ConstantNameGoldenTests
    {
        private const string TempDir = "Assets/DDrive/Tests/Editor/Temp";
        private readonly List<string> _createdAssetPaths = new();
        private string _tempOutputPath;

        // 入力(ファイル名) → 期待される定数名。[42] §5.13/§7 A-6 で確定した KnownPrefixes(19 個)を
        // 網羅する。この対応が変わったら定数名の導出規則(§5.3)が変わったということなので MAJOR。
        private static readonly (string fileName, string expected)[] Examples =
        {
            ("SE_Category_PlayerAttack", "CategoryPlayerAttack"),
            ("BGM_MainTheme", "MainTheme"),
            ("VFX_Explosion", "Explosion"),
            ("MODEL_PlayerModel", "PlayerModel"),
            ("ANC_PlayerAnchor", "PlayerAnchor"),
            ("ANCG_1PlayerSlash", "_1PlayerSlash"),
            ("SKIN_ButtonSkin", "ButtonSkin"),
            ("CUT_OpeningCutscene", "OpeningCutscene"),
            ("PRES_PlayerAttack", "PlayerAttack"),
            ("SHAKE_BigHit", "BigHit"),
            ("HAPTIC_LightBuzz", "LightBuzz"),
            ("HAPTICS_LightBuzz2", "LightBuzz2"),
            ("UITWEEN_FadeIn", "FadeIn"),
            ("MAT_Skin", "Skin"),
            ("TEX_Diffuse", "Diffuse"),
            ("CANVAS_MainMenu", "MainMenu"),
            ("PREFAB_Chest", "Chest"),
            ("ANIM_Walk", "Walk"),
            ("ANIM2D_Walk2D", "Walk2D"),
            ("Unknown_NoPrefix_Test", "UnknownNoPrefixTest"),
        };

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
            {
                AssetDatabase.CreateFolder("Assets/DDrive/Tests/Editor", "Temp");
            }

            _tempOutputPath = Path.Combine(Path.GetTempPath(), "ddrive_test_constnames_" + Guid.NewGuid().ToString("N") + ".cs");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var path in _createdAssetPaths)
            {
                if (AssetDatabase.LoadAssetAtPath<TestAssetData>(path) != null)
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }

            _createdAssetPaths.Clear();

            if (File.Exists(_tempOutputPath))
            {
                File.Delete(_tempOutputPath);
            }
        }

        [Test]
        public void ToConstantName_MatchesFixedExamples()
        {
            foreach (var (fileName, _) in Examples)
            {
                var data = ScriptableObject.CreateInstance<TestAssetData>();
                data.DisplayName = fileName;
                var path = $"{TempDir}/{fileName}.asset";
                AssetDatabase.CreateAsset(data, path);
                _createdAssetPaths.Add(path);
            }

            using (DDrive.Editor.Versioning.VersionStampSuppression.Scope())
            {
                AssetDatabase.SaveAssets();
            }

            var result = AssetIdGenerator.Regenerate(_tempOutputPath, includeTestAssemblies: true);
            Assert.IsTrue(result.Success);

            var generated = File.ReadAllText(_tempOutputPath);
            var missing = new List<string>();
            foreach (var (fileName, expected) in Examples)
            {
                // 定数の型は TestAssetData の [AssetIdDefinition] で固定された DDrive.Tests.Editor.TestAssetMarker。
                var needle = $"AssetId<DDrive.Tests.Editor.TestAssetMarker> {expected} =";
                if (!generated.Contains(needle))
                {
                    missing.Add($"{fileName} -> {expected}(見つからない行: \"{needle}\")");
                }
            }

            Assert.IsEmpty(missing,
                "ToConstantName の入出力が固定例と一致しません([42] §5.3、発効後は MAJOR):\n" + string.Join("\n", missing) +
                "\n--- 生成結果 ---\n" + generated);
        }

        [Test]
        public void KnownPrefixes_MatchesGolden()
        {
            var field = typeof(AssetIdGenerator).GetField("KnownPrefixes", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, "AssetIdGenerator.KnownPrefixes が見つかりません(private static readonly HashSet<string> のはず)。");

            var prefixes = (HashSet<string>)field.GetValue(null);
            var sorted = new List<string>(prefixes);
            sorted.Sort(StringComparer.OrdinalIgnoreCase);

            var expected = new[]
            {
                "ANC", "ANCG", "ANIM", "ANIM2D", "BGM", "CANVAS", "CUT", "HAPTIC", "HAPTICS",
                "MAT", "MODEL", "PREFAB", "PRES", "SE", "SHAKE", "SKIN", "TEX", "UITWEEN", "VFX",
            };

            Assert.AreEqual(expected.Length, sorted.Count,
                "KnownPrefixes の個数が変わりました。発効後は追加も禁止です([42] §5.3「KnownPrefixes への追加も禁止」)。" +
                "現在の内容: " + string.Join(", ", sorted));
            CollectionAssert.AreEquivalent(expected, sorted,
                "KnownPrefixes の内容が変わりました([42] §5.3、発効後は変更禁止)。現在: " + string.Join(", ", sorted));
        }
    }
}
