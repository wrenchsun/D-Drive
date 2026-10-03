using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Import;
using DDrive.Editor.Materials;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Material;
using ExternalPackage.Fake;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace ExternalContract.Tests
{
    // [docs/42 §5.14] 外部拡張の契約: 取り込みルールの外部登録(FC-6、E-21)と、変換表・テクスチャ規則の提供口(FC-14、E-22)。
    // 外部アセンブリ(ExternalContract.Tests.Editor)の public な実装(ExternalImportExtensions.cs)が TypeCache で発見され、
    // 組み込みの後ろに並び、競合は組み込み優先、例外は隔離され、外部が何も名乗らなければ従来どおり、を固定する。
    public class ExternalContractImportExtensionTests
    {
        private const string TempRoot = "Assets/Tests/DDriveTemp/ExternalContractImportExt";
        private const string SourceRoot = TempRoot + "/SourceAssets";
        private const string GameDataRoot = TempRoot + "/GameData";

        private readonly List<string> _logs = new();
        private readonly List<Object> _objects = new();

        private void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Warning || type == LogType.Exception)
            {
                _logs.Add(type + ":" + condition);
            }
        }

        [SetUp]
        public void SetUp()
        {
            ImportRulePostprocessor.Suppress = true;
            ExternalImportProbe.Reset();
            ImportRuleService.ResetImportHintStateForTests();
            _logs.Clear();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            ExternalImportProbe.Reset();
            ImportRulePostprocessor.Suppress = false;
            foreach (var o in _objects)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _objects.Clear();
            if (AssetDatabase.IsValidFolder(TempRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TempRoot);
                AssetDatabase.DeleteAsset(TempRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope())
                {
                    AssetDatabase.SaveAssets();
                }
            }
        }

        private int Count(string pattern) => _logs.Count(l => Regex.IsMatch(l, pattern));

        private static string WriteText(string assetPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(assetPath)));
            File.WriteAllText(Path.GetFullPath(assetPath), "note");
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            return assetPath;
        }

        private static readonly string[] BuiltInFolders = { "Se", "Bgm", "Texture", "Model", "Anim", "Anim2D", "Prefab", "Canvas", "Vfx" };

        // ── FC-6: ハンドラの外部登録 ──

        [Test]
        public void E21_NothingDeclared_HandlersAreTheBuiltInNineInOrder()
        {
            var handlers = ImportRuleService.Handlers;
            CollectionAssert.AreEqual(BuiltInFolders, handlers.Select(h => h.TypeFolder).ToArray());
            Assert.IsEmpty(ImportRuleService.ExternalOptOutFolders);
            Assert.AreEqual(0, _logs.Count, string.Join("\n", _logs));
        }

        [Test]
        public void E21_ExternalHandler_IsDiscoveredAfterBuiltIns_AndCached()
        {
            ExternalImportProbe.HandlerEnabled = true;
            ImportRuleService.ResetExtensionCacheForTests();

            var handlers = ImportRuleService.Handlers;
            Assert.AreEqual(BuiltInFolders.Length + 1, handlers.Count);
            CollectionAssert.AreEqual(BuiltInFolders, handlers.Take(BuiltInFolders.Length).Select(h => h.TypeFolder).ToArray(), "組み込みの順序は不変");
            Assert.AreEqual(ExternalImportProbe.NotesFolder, handlers[handlers.Count - 1].TypeFolder, "外部は組み込みの後ろ");
            Assert.AreSame(handlers, ImportRuleService.Handlers, "発見結果はキャッシュされる(取り込みのたびに作り直さない)");
        }

        [Test]
        public void E21_ExternalHandler_CreatesDataFromItsFolder_WithoutHint()
        {
            ExternalImportProbe.HandlerEnabled = true;
            ImportRuleService.ResetExtensionCacheForTests();
            var path = WriteText($"{SourceRoot}/{ExternalImportProbe.NotesFolder}/Cat/Hello.txt");

            var report = ImportRuleService.ProcessPaths(new[] { path }, SourceRoot, GameDataRoot);

            Assert.AreEqual(1, report.Created, report.ToString());
            Assert.AreEqual(0, Count("ImportRule"), "案内ログは出ない: " + string.Join("\n", _logs));
            var guid = AssetDatabase.FindAssets("t:SeData", new[] { GameDataRoot }).Single();
            var data = AssetDatabase.LoadAssetAtPath<SeData>(AssetDatabase.GUIDToAssetPath(guid));
            Assert.AreEqual("ext:Hello", data.DisplayName);
            Assert.AreEqual(AssetDatabase.AssetPathToGUID(path), data.ImportSourceGuid);

            var again = ImportRuleService.ProcessPaths(new[] { path }, SourceRoot, GameDataRoot);
            Assert.AreEqual(0, again.Created, "再取り込みで二重生成しない");
            Assert.AreEqual(1, again.Skipped);
        }

        [Test]
        public void E21_ExternalHandler_UnsupportedExtension_StillGetsTheExtensionHint()
        {
            ExternalImportProbe.HandlerEnabled = true;
            ImportRuleService.ResetExtensionCacheForTests();
            LogAssert.Expect(LogType.Warning, new Regex(@"\[DDrive\] ImportRule 案内:.*'ExternalNotes'.*対象拡張子"));
            var report = ImportRuleService.ProcessPaths(new[] { $"{SourceRoot}/{ExternalImportProbe.NotesFolder}/Cat/x.png" }, SourceRoot, GameDataRoot);
            Assert.AreEqual(0, report.Created);
        }

        [Test]
        public void E21_ExternalHandler_ClaimingBuiltInOrReservedFolder_IsIgnored_WarnOnce()
        {
            ExternalImportProbe.ConflictEnabled = true;
            ImportRuleService.ResetExtensionCacheForTests();

            var handlers = ImportRuleService.Handlers;
            _ = ImportRuleService.Handlers;
            CollectionAssert.AreEqual(BuiltInFolders, handlers.Select(h => h.TypeFolder).ToArray(), "組み込み優先 = 外部は採用されない");
            Assert.AreEqual(typeof(SeData), handlers[0].DataType, "Se は組み込みのまま");
            Assert.AreEqual(1, Count(@"'Se'.*組み込み"), string.Join("\n", _logs));
            Assert.AreEqual(1, Count(@"'Cutscene'.*組み込み優先"), string.Join("\n", _logs));
            Assert.AreEqual(2, _logs.Count, "警告はそれぞれ 1 回だけ(再アクセスで増えない): " + string.Join("\n", _logs));
        }

        [Test]
        public void E21_TwoExternalHandlersClaimingSameFolder_FirstByTypeNameWins_WarnOnce()
        {
            ExternalImportProbe.HandlerEnabled = true;
            ExternalImportProbe.DuplicateEnabled = true;
            ImportRuleService.ResetExtensionCacheForTests();

            Assert.AreEqual(BuiltInFolders.Length + 1, ImportRuleService.Handlers.Count);
            Assert.AreEqual(1, Count(@"'ExternalNotes'.*先に使っている"), string.Join("\n", _logs));
        }

        [Test]
        public void E21_ExternalHandler_ExceptionsAreIsolated()
        {
            ExternalImportProbe.HandlerEnabled = true;
            ExternalImportProbe.ThrowInConfigureOnName = "Bad";
            ImportRuleService.ResetExtensionCacheForTests();
            var bad = WriteText($"{SourceRoot}/{ExternalImportProbe.NotesFolder}/Cat/Bad.txt");
            var good = WriteText($"{SourceRoot}/{ExternalImportProbe.NotesFolder}/Cat/Good.txt");

            LogAssert.Expect(LogType.Exception, new Regex("external configure failure"));
            var report = ImportRuleService.ProcessPaths(new[] { bad, good }, SourceRoot, GameDataRoot);
            Assert.AreEqual(1, report.Created, "Configure が例外でも他のファイルの取り込みは続く: " + report);
            Assert.AreEqual(1, AssetDatabase.FindAssets("t:SeData", new[] { GameDataRoot }).Length, "例外のファイルの Data は作られない");

            ExternalImportProbe.ThrowInConfigureOnName = null;
            ExternalImportProbe.ThrowInLoad = true;
            var third = WriteText($"{SourceRoot}/{ExternalImportProbe.NotesFolder}/Cat/Third.txt");
            LogAssert.Expect(LogType.Exception, new Regex("external load failure"));
            var report2 = ImportRuleService.ProcessPaths(new[] { third }, SourceRoot, GameDataRoot);
            Assert.AreEqual(0, report2.Created);
            StringAssert.Contains("参照できる元データが見つかりません", report2.ToString());
        }

        // ── FC-6: 種別フォルダの宣言 ──

        [Test]
        public void E21_DeclaredFolder_GetsNoUnknownFolderHint_UndeclaredStillDoes()
        {
            ExternalImportProbe.OptOutEnabled = true;
            ImportRuleService.ResetExtensionCacheForTests();
            _logs.Clear(); // 宣言側の無視警告(組み込みの Se)は E21_DeclaredFolders_Ignore... で確認する

            var owned = $"{SourceRoot}/{ExternalImportProbe.OwnedFolder}/Hero/Hero.fcpose.json";
            var report = ImportRuleService.ProcessPaths(new[] { owned }, SourceRoot, GameDataRoot);
            Assert.AreEqual(0, report.Created);
            Assert.AreEqual(0, Count("ImportRule 案内"), "宣言したフォルダは案内しない: " + string.Join("\n", _logs));

            LogAssert.Expect(LogType.Warning, new Regex(@"\[DDrive\] ImportRule 案内:.*'NotDeclared'.*種別フォルダではありません"));
            ImportRuleService.ProcessPaths(new[] { $"{SourceRoot}/NotDeclared/Hero/Hero.fctrack" }, SourceRoot, GameDataRoot);
        }

        [Test]
        public void E21_DeclaredFolders_IgnoreEmptyDuplicateInvalidAndBuiltInNames()
        {
            ExternalImportProbe.OptOutEnabled = true;
            ImportRuleService.ResetExtensionCacheForTests();
            _ = ImportRuleService.ExternalOptOutFolders;

            CollectionAssert.AreEquivalent(new[] { ExternalImportProbe.OwnedFolder }, ImportRuleService.ExternalOptOutFolders);
            Assert.AreEqual(1, Count(@"'Se'.*組み込み優先"), "組み込みの種別フォルダ名の宣言は警告 1 回 + 無視: " + string.Join("\n", _logs));
        }

        [Test]
        public void E21_BuiltInKnownFolders_StayQuiet_WithoutAnyExtension()
        {
            var report = ImportRuleService.ProcessPaths(new[] { $"{SourceRoot}/Cutscene/Opening/a.ddrivecontract", $"{SourceRoot}/Shaders/a.shader" }, SourceRoot, GameDataRoot);
            Assert.AreEqual(0, report.Created);
            Assert.AreEqual(0, _logs.Count, string.Join("\n", _logs));
        }

        // ── FC-14: テクスチャ規則 ──

        private const string CharaDir = "Assets/SourceAssets/Chara/";

        [Test]
        public void E22_NoExternalRules_MatchingIsUnchanged()
        {
            Assert.IsEmpty(TextureImportRuleProviders.Rules);
            // 持ち込み先の実 Profile(規則を編集していることがある)に依存しないよう、既定の規則だけの Profile で確認する(FC-R-21)。
            var profile = ScriptableObject.CreateInstance<TextureImportProfile>();
            _objects.Add(profile);
            profile.Rules = TextureImportProfile.DefaultRules();
            Assert.IsFalse(profile.TryMatch(CharaDir + "Chara_ToonMask.png", out _));
            Assert.IsTrue(profile.TryMatch(CharaDir + "T_Body_N.png", out var normal));
            Assert.AreEqual("NormalMap", normal.Name);
            Assert.IsTrue(profile.TryMatch(CharaDir + "T_ToonBase.png", out var tPrefix));
            Assert.AreEqual("Model default", tPrefix.Name);
            Assert.AreEqual(0, _logs.Count, string.Join("\n", _logs));
        }

        [Test]
        public void E22_ExternalRule_IsEvaluatedBeforeProfileRules()
        {
            ExternalImportProbe.RuleProviderEnabled = true;
            TextureImportRuleProviders.ResetCacheForTests();
            var profile = TextureImportProfile.FindOrDefault();

            Assert.IsTrue(profile.TryMatch(CharaDir + "Chara_ToonMask.png", out var mask));
            Assert.AreEqual(ExternalToonMaskRuleProvider.MaskRuleName, mask.Name);
            Assert.IsFalse(mask.SRgb);

            Assert.IsTrue(profile.TryMatch(CharaDir + "T_Chara_ToonMask.png", out var tMask), "T_ 接頭辞の規則より先に効く");
            Assert.AreEqual(ExternalToonMaskRuleProvider.MaskRuleName, tMask.Name);

            Assert.IsTrue(profile.TryMatch(CharaDir + "T_ToonBase.png", out var prefix), "Model default(T_)より前");
            Assert.AreEqual(ExternalToonMaskRuleProvider.PrefixRuleName, prefix.Name);

            Assert.IsTrue(profile.TryMatch(CharaDir + "T_Body.png", out var plain));
            Assert.AreEqual("Model default", plain.Name, "外部規則に当たらないものは従来どおり");
            Assert.IsTrue(profile.TryMatch(CharaDir + "T_Body_N.png", out var normal));
            Assert.AreEqual("NormalMap", normal.Name);
        }

        [Test]
        public void E22_ProfileRuleWithSameCondition_WinsOverExternalRule()
        {
            ExternalImportProbe.RuleProviderEnabled = true;
            TextureImportRuleProviders.ResetCacheForTests();
            var profile = ScriptableObject.CreateInstance<TextureImportProfile>();
            _objects.Add(profile);
            var rules = TextureImportProfile.DefaultRules().ToList();
            rules.Add(new TextureImportProfile.Rule
            {
                Name = "ProjectToonMask", Match = TextureImportProfile.MatchKind.Suffix, Pattern = "_toonmask",
                Type = TextureImporterType.Default, SRgb = true, Mipmaps = true, Compression = TextureImporterCompression.Compressed,
            });
            profile.Rules = rules.ToArray();

            Assert.IsTrue(profile.TryMatch(CharaDir + "Chara_ToonMask.png", out var mask));
            Assert.AreEqual("ProjectToonMask", mask.Name, "同じ条件(種類 + Pattern、大文字小文字無視)は Profile 優先");
            Assert.IsTrue(mask.SRgb);

            Assert.IsTrue(profile.TryMatch(CharaDir + "T_ToonBase.png", out var prefix));
            Assert.AreEqual(ExternalToonMaskRuleProvider.PrefixRuleName, prefix.Name, "条件が違う外部規則は上書きされない");
        }

        [Test]
        public void E22_ExternalRule_AppliesToTheImporter()
        {
            ExternalImportProbe.RuleProviderEnabled = true;
            TextureImportRuleProviders.ResetCacheForTests();
            var path = $"{TempRoot}/Tex/Chara_ToonMask.png";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var png = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            File.WriteAllBytes(Path.GetFullPath(path), png.EncodeToPNG());
            Object.DestroyImmediate(png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.IsTrue(importer.sRGBTexture, "既定は sRGB オン");
            Assert.IsTrue(TextureImportProfile.FindOrDefault().TryMatch(path, out var rule));
            Assert.IsTrue(TextureImportProfile.Apply(importer, rule));
            importer.SaveAndReimport();
            importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.IsFalse(importer.sRGBTexture, "_ToonMask → sRGB オフ");
            Assert.IsEmpty(TextureImportProfile.Diff(importer, rule));
        }

        [Test]
        public void E22_ThrowingRuleProvider_IsIsolated_AndLoggedOnce()
        {
            ExternalImportProbe.RuleProviderEnabled = true;
            ExternalImportProbe.RuleProviderThrows = true;
            TextureImportRuleProviders.ResetCacheForTests();
            LogAssert.Expect(LogType.Exception, new Regex("external rule provider failure"));

            var profile = TextureImportProfile.FindOrDefault();
            Assert.IsTrue(profile.TryMatch(CharaDir + "Chara_ToonMask.png", out var mask), "他の提供口の規則は有効");
            Assert.AreEqual(ExternalToonMaskRuleProvider.MaskRuleName, mask.Name);
            profile.TryMatch(CharaDir + "Other.png", out _);
            profile.TryMatch(CharaDir + "Other2.png", out _);
            Assert.AreEqual(1, Count("external rule provider failure"), "規則はキャッシュされ、例外のログは 1 回だけ");
        }

        // ── FC-14: 変換表 ──

        private ShaderConversionTable NewTable(string tableName)
        {
            var table = ScriptableObject.CreateInstance<ShaderConversionTable>();
            table.name = tableName;
            _objects.Add(table);
            return table;
        }

        [Test]
        public void E22_ExternalTables_AreCollected_AfterProjectTables_BeforeBundled()
        {
            var a = NewTable("ExtA");
            var b = NewTable("ExtB");
            ExternalImportProbe.Tables = new[] { a, null, b, a };
            ShaderConversionTables.ResetProviderCacheForTests();

            var tables = ShaderConversionTables.Collect();
            var ia = tables.IndexOf(a);
            var ib = tables.IndexOf(b);
            Assert.GreaterOrEqual(ia, 0);
            Assert.AreEqual(ia + 1, ib, "返した順(null・重複は無視)");
            Assert.AreEqual(1, tables.Count(t => t == a), "重複させない");
            Assert.IsFalse(tables.Contains(null));

            for (var i = 0; i < tables.Count; i++)
            {
                var p = AssetDatabase.GetAssetPath(tables[i]);
                if (p.StartsWith("Assets/"))
                {
                    Assert.Less(i, ia, "Assets/ の表は外部より前");
                }
                else if (p.StartsWith("Packages/"))
                {
                    Assert.Greater(i, ib, "D-Drive 同梱の表は外部より後ろ");
                }
            }
        }

        [Test]
        public void E22_NoExternalProvider_CollectIsDeterministic_AndExcludesTests()
        {
            var first = ShaderConversionTables.Collect();
            var second = ShaderConversionTables.Collect();
            CollectionAssert.AreEqual(first, second);
            foreach (var t in first)
            {
                Assert.IsFalse(AssetDatabase.GetAssetPath(t).Contains("/Tests/"));
            }
        }

        // FY-R-07: 取り込み系の拡張点(ExtensionPointDiscovery)も同じ規則。外側が internal の入れ子型は発見されず、外側も public なら発見される。
        [Test]
        public void E22_NestedProvider_IsDiscovered_OnlyWhenTheOuterTypeIsVisible()
        {
            Assert.IsFalse(typeof(ExternalHiddenProviderHost.ExternalHiddenNestedTableProvider).IsVisible);
            Assert.IsTrue(typeof(ExternalVisibleProviderHost.ExternalVisibleNestedTableProvider).IsVisible);
            ShaderConversionTables.ResetProviderCacheForTests();

            ShaderConversionTables.Collect();

            Assert.AreEqual(0, ExternalImportProbe.HiddenNestedProviderCalls, "外側が internal の入れ子は発見されない");
            Assert.Greater(ExternalImportProbe.VisibleNestedProviderCalls, 0, "外側も public の入れ子は発見される");
        }

        [Test]
        public void E22_ThrowingTableProvider_IsIsolated()
        {
            var a = NewTable("ExtOnly");
            ExternalImportProbe.Tables = new[] { a };
            ExternalImportProbe.TableProviderThrows = true;
            ShaderConversionTables.ResetProviderCacheForTests();
            LogAssert.Expect(LogType.Exception, new Regex("external table provider failure"));

            Assert.IsTrue(ShaderConversionTables.Collect().Contains(a), "例外の提供口があっても他の提供口の表は集まる");
        }
    }
}
