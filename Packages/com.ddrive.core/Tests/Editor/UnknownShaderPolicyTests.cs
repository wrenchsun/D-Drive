using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Foundation.Identity;
using DDrive.Editor.Materials;
using DDrive.Editor.Model;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [51_tdrive_integration.md] §4.16 FC-15(2026-10-03、U-9 = (c)) — 知らないシェーダーを黙って DDrive/Lit に変換しない。
    // 対話的な操作(Ask)では 1 操作 1 回だけ確認し、非対話の経路は従来どおり Lit。KeepSource は確認なしで元のシェーダーを保つ。
    // 実ダイアログは出さない(UnknownShaderGuard.PromptOverrideForTests を差し替える)。実 Profile / 実 GameData は触らない。
    public class UnknownShaderPolicyTests
    {
        private const string TestRoot = TestTempFolder.Root + "/TempUnknownShaderData";
        private const string TempFolder = TestTempFolder.Root + "/TempUnknownShader";

        private Shader _unknown;
        private MayaImportProfile _profile;
        private int _prompts;
        private UnknownShaderPrompt _lastPrompt;
        private UnknownShaderChoice _answer;
        private readonly List<string> _assets = new();

        [SetUp]
        public void SetUp()
        {
            MayaModelPostprocessor.Suppress = true;
            _unknown = Shader.Find("Sprites/Default") ?? Shader.Find("Hidden/InternalErrorShader");
            Assume.That(_unknown != null && !UnityMaterialMigrator.IsSupported(_unknown) && !_unknown.name.StartsWith("DDrive/"), "知らないシェーダーが必要");
            Assume.That(Shader.Find(UnityMaterialMigrator.LitShaderName) != null, "DDrive/Lit が必要");
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                TestTempFolder.CreateFolder("TempUnknownShader");
            }

            _profile = ScriptableObject.CreateInstance<MayaImportProfile>();
            _profile.hideFlags = HideFlags.HideAndDontSave;
            MayaImportProfile.ProfileOverrideForTests = _profile; // 実プロジェクトの Profile を読まない
            _prompts = 0;
            _lastPrompt = null;
            _answer = UnknownShaderChoice.Keep;
            UnknownShaderGuard.PromptOverrideForTests = prompt =>
            {
                _prompts++;
                _lastPrompt = prompt;
                return _answer;
            };
        }

        [TearDown]
        public void TearDown()
        {
            UnknownShaderGuard.PromptOverrideForTests = null;
            MayaImportProfile.ProfileOverrideForTests = null;
            if (_profile != null)
            {
                Object.DestroyImmediate(_profile);
            }

            foreach (var path in _assets)
            {
                AssetDatabase.DeleteAsset(path);
            }

            _assets.Clear();
            AssetDatabase.DeleteAsset(TempFolder);
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }
            }

            MayaModelPostprocessor.Suppress = false;
        }

        private UnityEngine.Material CreateMaterialAsset(string name, Shader shader)
        {
            var path = TempFolder + "/" + name + ".mat";
            var m = new UnityEngine.Material(shader) { name = name };
            AssetDatabase.CreateAsset(m, path);
            _assets.Add(path);
            return m;
        }

        // シェーダー参照が欠けた .mat(パッケージ未導入・GUID 切れ)を作る。Unity は shader に Hidden/InternalErrorShader を返す(FC-R-02)。
        private UnityEngine.Material CreateMissingShaderMaterialAsset(string name)
        {
            var path = TempFolder + "/" + name + ".mat";
            WriteMissingShaderMaterial(path, name);
            _assets.Add(path);
            return AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
        }

        // 既にあるパスの .mat(= GUID はそのまま)の中身を「シェーダー参照が欠けた」状態に書き換える(FX-R-02。元のシェーダーが後から欠けた状況)。
        private static void WriteMissingShaderMaterial(string path, string name, bool withSavedProperties = false)
        {
            var yaml = string.Join("\n", new[]
            {
                "%YAML 1.1",
                "%TAG !u! tag:unity3d.com,2011:",
                "--- !u!21 &2100000",
                "Material:",
                "  serializedVersion: 8",
                "  m_ObjectHideFlags: 0",
                "  m_Name: " + name,
                "  m_Shader: {fileID: 4800000, guid: 0123456789abcdef0123456789abcdef, type: 3}",
                "  m_ValidKeywords: []",
                "  m_InvalidKeywords: []",
                "  m_LightmapFlags: 4",
                "  m_EnableInstancingVariants: 0",
                "  m_DoubleSidedGI: 0",
                "  m_CustomRenderQueue: -1",
                "  stringTagMap: {}",
                "  disabledShaderPasses: []",
                "  m_SavedProperties:",
                "    serializedVersion: 3",
                "    m_TexEnvs: []",
                "    m_Ints: []",
                withSavedProperties ? "    m_Floats:\n    - _Metallic: 0.9\n    - _Glossiness: 0.8" : "    m_Floats: []",
                withSavedProperties ? "    m_Colors:\n    - _BaseColor: {r: 1, g: 0, b: 0, a: 1}\n    - _Color: {r: 0, g: 1, b: 0, a: 1}" : "    m_Colors: []",
                "",
            });
            System.IO.File.WriteAllText(path, yaml);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        private static int CountMaterialData()
        {
            if (!AssetDatabase.IsValidFolder(TestRoot))
            {
                return 0;
            }

            return AssetDatabase.FindAssets("t:" + nameof(MaterialData), new[] { TestRoot }).Length;
        }

        // ── 契約 ──

        [Test]
        public void Policy_EnumValuesAreStable_AndDefaultIsAsk()
        {
            Assert.AreEqual(0, (int)UnknownShaderPolicy.Ask);
            Assert.AreEqual(1, (int)UnknownShaderPolicy.KeepSource);
            Assert.AreEqual(2, (int)UnknownShaderPolicy.ConvertToLit);
            // 欄が無い旧 Profile アセットは 0 で読まれる = Ask。新規作成の既定も Ask。
            Assert.AreEqual(UnknownShaderPolicy.Ask, _profile.UnknownShaderPolicy);
        }

        [Test]
        public void IsUnknown_FollowsConversionTableAndDDrivePrefix()
        {
            var urp = Shader.Find("Universal Render Pipeline/Lit");
            if (urp != null)
            {
                Assert.IsFalse(UnknownShaderGuard.IsUnknown(urp), "変換表にあるシェーダーは知らないシェーダーではない");
            }

            Assert.IsFalse(UnknownShaderGuard.IsUnknown(Shader.Find(UnityMaterialMigrator.LitShaderName)));
            Assert.IsFalse(UnknownShaderGuard.IsUnknown(null));
            Assert.IsTrue(UnknownShaderGuard.IsUnknown(_unknown));
        }

        // ── TryResolve(1 操作 1 回) ──

        [Test]
        public void TryResolve_Ask_Interactive_PromptsOnce_ForManyMaterials()
        {
            var a = CreateMaterialAsset("UnkA", _unknown);
            var b = CreateMaterialAsset("UnkB", _unknown);
            var c = CreateMaterialAsset("Known", Shader.Find(UnityMaterialMigrator.LitShaderName));

            Assert.IsTrue(UnknownShaderGuard.TryResolve(_profile, new[] { a, b, c, a }, true, out var handling));

            Assert.AreEqual(1, _prompts, "マテリアルごとに出さない");
            Assert.AreEqual(2, _lastPrompt.MaterialCount, "同じ Material の重複は数えない・知っているシェーダーは数えない");
            StringAssert.Contains(_unknown.name, _lastPrompt.Message);
            Assert.AreEqual(UnknownShaderHandling.Keep, handling);
        }

        [Test]
        public void TryResolve_Ask_Interactive_ChoiceMapsToHandlingOrCancel()
        {
            var a = CreateMaterialAsset("UnkA", _unknown);

            _answer = UnknownShaderChoice.Convert;
            Assert.IsTrue(UnknownShaderGuard.TryResolve(_profile, new[] { a }, true, out var convert));
            Assert.AreEqual(UnknownShaderHandling.Convert, convert);

            _answer = UnknownShaderChoice.Keep;
            Assert.IsTrue(UnknownShaderGuard.TryResolve(_profile, new[] { a }, true, out var keep));
            Assert.AreEqual(UnknownShaderHandling.Keep, keep);

            _answer = UnknownShaderChoice.Cancel;
            Assert.IsFalse(UnknownShaderGuard.TryResolve(_profile, new[] { a }, true, out _), "キャンセルは false");
        }

        [Test]
        public void TryResolve_Ask_Interactive_NoUnknownShader_NoDialog()
        {
            var known = CreateMaterialAsset("Known", Shader.Find(UnityMaterialMigrator.LitShaderName));

            Assert.IsTrue(UnknownShaderGuard.TryResolve(_profile, new[] { known }, true, out _));
            Assert.AreEqual(0, _prompts);
        }

        [Test]
        public void TryResolve_Ask_NonInteractive_ConvertsWithoutDialog()
        {
            var a = CreateMaterialAsset("UnkA", _unknown);

            Assert.IsTrue(UnknownShaderGuard.TryResolve(_profile, new[] { a }, false, out var handling));

            Assert.AreEqual(0, _prompts);
            Assert.AreEqual(UnknownShaderHandling.ConvertKeepingExisting, handling, "Ask の非対話既定は Convert だが既存 Data の知らないシェーダーは上書きしない(FC-R-01)");
        }

        [Test]
        public void TryResolve_KeepSourceAndConvertToLit_NeverPrompt()
        {
            var a = CreateMaterialAsset("UnkA", _unknown);

            _profile.UnknownShaderPolicy = UnknownShaderPolicy.KeepSource;
            Assert.IsTrue(UnknownShaderGuard.TryResolve(_profile, new[] { a }, true, out var keep));
            Assert.AreEqual(UnknownShaderHandling.Keep, keep);

            _profile.UnknownShaderPolicy = UnknownShaderPolicy.ConvertToLit;
            Assert.IsTrue(UnknownShaderGuard.TryResolve(_profile, new[] { a }, true, out var convert));
            Assert.AreEqual(UnknownShaderHandling.Convert, convert);

            Assert.AreEqual(0, _prompts);
        }

        // ── Migrate(単体 .mat) ──

        [Test]
        public void Migrate_AskNonInteractive_ConvertsToLit_LikeBefore()
        {
            var m = CreateMaterialAsset("MigAsk", _unknown);
            var report = new MayaMaterialImporter.Report();

            var data = UnityMaterialMigrator.Migrate(m, "Migrate", report, TestRoot);

            Assert.IsNotNull(data, report.ToString());
            Assert.AreEqual(UnityMaterialMigrator.LitShaderName, data.Shader.name);
            StringAssert.Contains("KeepSource", string.Join("\n", report.Lines), "警告ログに KeepSource の案内が出る");
            Assert.AreEqual(0, _prompts, "非対話の経路ではダイアログを出さない");
        }

        [Test]
        public void Migrate_ConvertToLitPolicy_ConvertsToLit()
        {
            _profile.UnknownShaderPolicy = UnknownShaderPolicy.ConvertToLit;
            var m = CreateMaterialAsset("MigConv", _unknown);

            var data = UnityMaterialMigrator.Migrate(m, "Migrate", null, TestRoot);

            Assert.AreEqual(UnityMaterialMigrator.LitShaderName, data.Shader.name);
            Assert.AreEqual(0, _prompts);
        }

        [Test]
        public void Migrate_KeepSourcePolicy_NonInteractive_KeepsShader_AndRegistersSpecific()
        {
            _profile.UnknownShaderPolicy = UnknownShaderPolicy.KeepSource;
            var m = CreateMaterialAsset("MigKeep", _unknown);

            var data = UnityMaterialMigrator.Migrate(m, "Migrate", null, TestRoot);

            Assert.IsNotNull(data);
            Assert.AreSame(_unknown, data.Shader, "知らないシェーダーはそのまま");
            Assert.AreEqual(MaterialSpecificResolver.Resolve(data.Shader).Count, data.Specific?.Length ?? 0, "Specific はそのシェーダーの固有が既定値で登録される");
            Assert.AreEqual(0, _prompts);
        }

        [Test]
        public void Migrate_ExplicitKeep_KeepsShader()
        {
            var m = CreateMaterialAsset("MigExplicitKeep", _unknown);

            var data = UnityMaterialMigrator.Migrate(m, "Migrate", null, TestRoot, UnknownShaderHandling.Keep);

            Assert.AreSame(_unknown, data.Shader);
        }

        [Test]
        public void Migrate_Keep_DoesNotOverwriteExistingValidShader()
        {
            var m = CreateMaterialAsset("MigExisting", _unknown);
            var first = UnityMaterialMigrator.Migrate(m, "Migrate", null, TestRoot, UnknownShaderHandling.Convert);
            Assert.AreEqual(UnityMaterialMigrator.LitShaderName, first.Shader.name);

            var again = UnityMaterialMigrator.Migrate(m, "Migrate", null, TestRoot, UnknownShaderHandling.Keep);

            Assert.AreSame(first, again);
            Assert.AreEqual(UnityMaterialMigrator.LitShaderName, again.Shader.name, "既に有効なシェーダーがある既存 Data は上書きしない");
        }

        [Test]
        public void Migrate_Convert_StillOverwritesExistingShader_LikeBefore()
        {
            var m = CreateMaterialAsset("MigOverwrite", _unknown);
            var first = UnityMaterialMigrator.Migrate(m, "Migrate", null, TestRoot, UnknownShaderHandling.Keep);
            Assert.AreSame(_unknown, first.Shader);

            var again = UnityMaterialMigrator.Migrate(m, "Migrate", null, TestRoot, UnknownShaderHandling.Convert);

            Assert.AreSame(first, again);
            Assert.AreEqual(UnityMaterialMigrator.LitShaderName, again.Shader.name, "Convert は従来どおり上書きする");
        }

        // ── FC-R-01: 非対話 + Ask では、既存 Data の知らないシェーダー(= 以前「保つ」を選んだもの)を Lit に戻さない ──

        [Test]
        public void Migrate_AskNonInteractive_DoesNotOverwriteExistingKeptUnknownShader()
        {
            var m = CreateMaterialAsset("MigKeptThenAsk", _unknown);
            var first = UnityMaterialMigrator.Migrate(m, "Migrate", null, TestRoot, UnknownShaderHandling.Keep);
            Assert.AreSame(_unknown, first.Shader);

            var again = UnityMaterialMigrator.Migrate(m, "Migrate", null, TestRoot); // 非対話・Profile は Ask

            Assert.AreSame(first, again);
            Assert.AreSame(_unknown, again.Shader, "Keep で作った Data を、後の非対話処理で Lit に戻さない");
            Assert.AreEqual(0, _prompts);
        }

        [Test]
        public void Migrate_ConvertToLitPolicy_NonInteractive_StillOverwritesExistingKeptShader()
        {
            var m = CreateMaterialAsset("MigKeptThenConvertPolicy", _unknown);
            var first = UnityMaterialMigrator.Migrate(m, "Migrate", null, TestRoot, UnknownShaderHandling.Keep);
            _profile.UnknownShaderPolicy = UnknownShaderPolicy.ConvertToLit;

            var again = UnityMaterialMigrator.Migrate(m, "Migrate", null, TestRoot);

            Assert.AreSame(first, again);
            Assert.AreEqual(UnityMaterialMigrator.LitShaderName, again.Shader.name, "ConvertToLit は明示の指定なので従来どおり Lit に寄せる");
        }

        [Test]
        public void Migrate_AskNonInteractive_ExistingKnownShader_IsStillMovedToLit_LikeBefore()
        {
            var urp = Shader.Find("Universal Render Pipeline/Lit");
            Assume.That(urp != null, "URP Lit が必要");
            var m = CreateMaterialAsset("MigKnown", urp);
            var first = UnityMaterialMigrator.Migrate(m, "Migrate", null, TestRoot);
            first.Shader = urp; // 以前 URP Lit のまま作られた Data
            EditorUtility.SetDirty(first);

            var again = UnityMaterialMigrator.Migrate(m, "Migrate", null, TestRoot);

            Assert.AreEqual(UnityMaterialMigrator.LitShaderName, again.Shader.name, "変換表にある標準シェーダーの既存 Data は従来どおり寄せる");
        }

        [Test]
        public void ContextMenuMaterialCreation_AsksOncePerOperation_AndCancelCreatesNothing()
        {
            Assume.That(!Application.isBatchMode, "バッチモードは非対話");
            var option = DDrive.Editor.Creation.SourceDataCreation.Find(typeof(MaterialData));
            Assert.IsNotNull(option);
            Assert.IsNotNull(option.BeginBatch, "右クリックの Material 作成は事前確認(BeginBatch)を持つ");
            var a = CreateMaterialAsset("CtxA", _unknown);
            var b = CreateMaterialAsset("CtxB", _unknown);
            var paths = new List<string> { AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b) };

            try
            {
                _answer = UnknownShaderChoice.Cancel;
                Assert.IsFalse(option.BeginBatch(paths), "キャンセルなら false(何も作らない)");
                Assert.AreEqual(1, _prompts, "2 件でも確認は 1 回");
                option.EndBatch();

                _answer = UnknownShaderChoice.Keep;
                Assert.IsTrue(option.BeginBatch(paths));
                Assert.AreEqual(2, _prompts);
            }
            finally
            {
                option.EndBatch();
            }
        }

        // ── FC-R-02: シェーダーが欠けた Material は「知らないシェーダー」として保たず、警告して Lit にする ──

        [Test]
        public void MissingShader_IsNotUnknown_AndIsNeverKept()
        {
            var m = CreateMissingShaderMaterialAsset("MissingShader");
            Assume.That(m != null, "欠けたシェーダーの .mat を読めること");
            Assert.AreEqual(UnknownShaderGuard.MissingShaderName, m.shader.name, "Unity は欠けたシェーダーを Hidden/InternalErrorShader で返す");
            Assert.IsTrue(UnknownShaderGuard.IsMissing(m.shader));
            Assert.IsFalse(UnknownShaderGuard.IsUnknown(m.shader), "保つ対象(知らないシェーダー)にしない");
            Assert.IsNull(UnknownShaderGuard.BuildPrompt(new[] { m }), "確認ダイアログの対象でもない");

            var report = new MayaMaterialImporter.Report();
            var data = UnityMaterialMigrator.Migrate(m, "Migrate", report, TestRoot, UnknownShaderHandling.Keep);

            Assert.IsNotNull(data, report.ToString());
            Assert.AreEqual(UnityMaterialMigrator.LitShaderName, data.Shader.name, "Keep でも欠けたシェーダーは Lit に変換する");
            StringAssert.Contains("見つかりません", string.Join("\n", report.Lines));
            Assert.AreEqual(UnityMaterialMigrator.LitShaderName, MayaMaterialImporter.ResolveTargetShader(_profile, m, UnknownShaderHandling.Keep).name);
        }

        // ── FX-R-02: 元のシェーダーが欠けている間に既存 Data を再生成しても、既存 Data のシェーダー参照を書き換えない ──

        // FY-R-01: Shader / Specific だけでなく Common(色・テクスチャ参照・Blend 等)も含めて、既存 Data を一切書き換えない
        // (Data の内容・IsDirty・.asset ファイルの中身が変わらない)。
        [TestCase(UnknownShaderHandling.Keep)]
        [TestCase(UnknownShaderHandling.Convert)]
        [TestCase(UnknownShaderHandling.ConvertKeepingExisting)]
        public void MissingSource_ExistingData_IsNotModifiedAtAll_IncludingCommon_RegardlessOfHandling(UnknownShaderHandling handling)
        {
            var m = CreateMaterialAsset("MissingAfterKept", _unknown);
            var data = UnityMaterialMigrator.Migrate(m, "Migrate", null, TestRoot, UnknownShaderHandling.Keep);
            Assert.AreSame(_unknown, data.Shader);
            var specificBefore = data.Specific == null ? 0 : data.Specific.Length;

            // 既存 Data の Common を、既定値と違う値(色・テクスチャ参照・Blend 等)にしておく。
            var common = MaterialCommon.Default;
            common.Albedo = new AssetId<TextureMarker>(0xABCD01UL, AssetType.Texture);
            common.AlbedoTint = new Color(1f, 0.25f, 0.5f, 1f);
            common.Normal = new AssetId<TextureMarker>(0xABCD02UL, AssetType.Texture);
            common.NormalScale = 2f;
            common.Mask = new AssetId<TextureMarker>(0xABCD03UL, AssetType.Texture);
            common.Metallic = 0.7f;
            common.Smoothness = 0.3f;
            common.Emission = new AssetId<TextureMarker>(0xABCD04UL, AssetType.Texture);
            common.EmissionColor = new Color(0.1f, 0.2f, 0.3f, 1f);
            common.EmissionIntensity = 1.5f;
            common.Blend = BlendType.Transparent;
            common.Cutoff = 0.4f;
            common.DoubleSided = true;
            data.Common = common;
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            var dataPath = AssetDatabase.GetAssetPath(data);
            var fileBefore = System.IO.File.ReadAllBytes(dataPath);

            // 後から元の .mat のシェーダーが欠けた(パッケージの解決失敗・GUID 切れ)状態にして、再生成する。
            var path = AssetDatabase.GetAssetPath(m);
            WriteMissingShaderMaterial(path, "MissingAfterKept", withSavedProperties: true);
            var missing = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
            Assume.That(UnknownShaderGuard.IsMissing(missing.shader), "元の .mat のシェーダーが欠けた状態になること");

            var report = new MayaMaterialImporter.Report();
            var again = UnityMaterialMigrator.Migrate(missing, "Migrate", report, TestRoot, handling);

            Assert.AreSame(data, again, "同じ MaterialData が見つかる(別の Data を作らない)");
            Assert.AreSame(_unknown, again.Shader, "欠けている間の再生成で、既存 Data のシェーダーを Lit に置き換えない");
            Assert.AreEqual(specificBefore, again.Specific == null ? 0 : again.Specific.Length, "固有の設定も触らない");
            var after = again.Common;
            Assert.AreEqual(common.Albedo, after.Albedo, "Albedo(テクスチャ参照)");
            Assert.AreEqual(common.AlbedoTint, after.AlbedoTint, "色");
            Assert.AreEqual(common.Normal, after.Normal);
            Assert.AreEqual(common.NormalScale, after.NormalScale);
            Assert.AreEqual(common.Mask, after.Mask);
            Assert.AreEqual(common.Metallic, after.Metallic);
            Assert.AreEqual(common.Smoothness, after.Smoothness);
            Assert.AreEqual(common.Emission, after.Emission);
            Assert.AreEqual(common.EmissionColor, after.EmissionColor);
            Assert.AreEqual(common.EmissionIntensity, after.EmissionIntensity);
            Assert.AreEqual(common.Blend, after.Blend, "Blend");
            Assert.AreEqual(common.Cutoff, after.Cutoff);
            Assert.AreEqual(common.DoubleSided, after.DoubleSided);
            Assert.IsFalse(EditorUtility.IsDirty(again), "Data を書き換えていない(IsDirty でない)");
            CollectionAssert.AreEqual(fileBefore, System.IO.File.ReadAllBytes(dataPath), ".asset ファイルの中身が変わらない");
            var log = string.Join("\n", report.Lines);
            StringAssert.Contains("変更しませんでした", log);
            StringAssert.Contains("共通", log);
            Assert.AreEqual(1, CountMaterialData());
        }

        // FY-R-01: FBX の取り込み経路(ImportModel が呼ぶ ImportMaterial)も、欠けたシェーダーの .mat から Common を作り直して
        // 既存 Data の Common を上書きしない。
        [Test]
        public void MissingSource_ImportMaterial_DoesNotOverwriteExistingCommon()
        {
            var m = CreateMaterialAsset("MissingViaImport", _unknown);
            var data = UnityMaterialMigrator.Migrate(m, "Migrate", null, TestRoot, UnknownShaderHandling.Keep);
            var common = MaterialCommon.Default;
            common.AlbedoTint = new Color(0.9f, 0.1f, 0.1f, 1f);
            common.Metallic = 0.6f;
            data.Common = common;
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);

            var path = AssetDatabase.GetAssetPath(m);
            WriteMissingShaderMaterial(path, "MissingViaImport", withSavedProperties: true);
            var missing = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
            Assume.That(UnknownShaderGuard.IsMissing(missing.shader));

            var report = new MayaMaterialImporter.Report();
            var again = MayaMaterialImporter.ImportMaterial(missing, "Migrate", UnityMaterialMigrator.SourceKey, _profile, report, TestRoot);

            Assert.AreSame(data, again);
            Assert.AreEqual(common.AlbedoTint, again.Common.AlbedoTint);
            Assert.AreEqual(common.Metallic, again.Common.Metallic);
            Assert.IsFalse(EditorUtility.IsDirty(again));
            Assert.AreEqual(0, report.Updated);
            Assert.AreEqual(1, report.Unchanged);
        }

        // 既存 Data が無いなら、欠けたシェーダーでも従来どおり新規に作る(Lit)。
        [Test]
        public void MissingSource_NoExistingData_StillCreatesNewLitData()
        {
            var m = CreateMissingShaderMaterialAsset("MissingFresh");
            var report = new MayaMaterialImporter.Report();

            var data = UnityMaterialMigrator.Migrate(m, "Migrate", report, TestRoot, UnknownShaderHandling.Keep);

            Assert.IsNotNull(data);
            Assert.AreEqual(UnityMaterialMigrator.LitShaderName, data.Shader.name);
            Assert.AreEqual(1, report.Created);
        }

        // FY-R-01 の推定の確認(docs/56): 欠けたシェーダーの Material は、.mat に保存されたプロパティがあっても
        // HasProperty が偽になる(プロパティはシェーダーの定義で引かれる)= 元の値から Common は作れない。
        [Test]
        public void MissingShaderMaterial_PropertiesAreNotReadable_EvenIfSavedInTheFile()
        {
            var name = "MissingProbe";
            var path = TempFolder + "/" + name + ".mat";
            WriteMissingShaderMaterial(path, name, withSavedProperties: true);
            _assets.Add(path);
            var missing = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
            Assume.That(UnknownShaderGuard.IsMissing(missing.shader));

            Assert.IsFalse(missing.HasProperty("_BaseColor"), "保存された色でも HasProperty は偽");
            Assert.IsFalse(missing.HasProperty("_Color"));
            Assert.AreEqual(0, missing.GetTexturePropertyNames().Length, "テクスチャのプロパティ名もシェーダーから取られるので空");
        }

        // 既存 Data 側のシェーダー参照が欠けている(シェーダーのファイルが無くなった)とき、Maya 取り込み経路でも埋め直さない。
        // ついでに「欠けた参照が Unity の null 比較で null になるか」(docs/55 FX-R-02 の推定)を確かめる。
        [Test]
        public void ExistingData_WithMissingShaderReference_IsDetected_AndNotRefilled()
        {
            var shaderPath = TempFolder + "/DDriveTempMissingProbe.shader";
            System.IO.File.WriteAllText(shaderPath, "Shader \"Hidden/DDriveTempMissingProbe\" { SubShader { Pass { } } }");
            AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceUpdate);
            _assets.Add(shaderPath);
            var probe = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            Assume.That(probe != null, "一時シェーダーを読めること");

            var m = CreateMaterialAsset("ProbeMat", probe);
            var data = UnityMaterialMigrator.Migrate(m, "Migrate", null, TestRoot, UnknownShaderHandling.Keep);
            Assert.AreSame(probe, data.Shader);
            Assert.IsFalse(UnknownShaderGuard.HasMissingShaderReference(data), "有効な参照は欠けていない");

            // 参照先のシェーダーを消す = Data の Shader 欄は「欠けた参照」になる(デザイナーが T-Drive を外した状況の再現)。
            AssetDatabase.DeleteAsset(shaderPath);
            _assets.Remove(shaderPath);
            AssetDatabase.Refresh();

            Assert.IsTrue(data.Shader == null, "欠けた参照は Unity の == では null と等しい(docs/55 FX-R-02 の推定の確認)");
            Assert.IsTrue(UnknownShaderGuard.HasMissingShaderReference(data), "ただし未設定とは区別できる(参照先の ID が残っている)");

            // 元の .mat を変更して Common に差分を作り、Maya の既存 Data 経路(ImportMaterial)へ流す。
            m.color = Color.red;
            EditorUtility.SetDirty(m);
            var profile = ScriptableObject.CreateInstance<MayaImportProfile>();
            try
            {
                var report = new MayaMaterialImporter.Report();
                var again = MayaMaterialImporter.ImportMaterial(m, "Migrate", UnityMaterialMigrator.SourceKey, profile, report, TestRoot);
                Assert.AreSame(data, again);
                Assert.IsTrue(UnknownShaderGuard.HasMissingShaderReference(again), "欠けた参照を Lit で埋め直さない(元の参照を残す)");
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        // ── FX-R-09: 既存 Data の「知らないシェーダー」を保つのは、元の .mat も知らないシェーダーのときだけ ──

        [Test]
        public void ExistingKeptUnknownShader_IsConverted_WhenSourceMaterialWentBackToAStandardShader()
        {
            var urp = Shader.Find("Universal Render Pipeline/Lit");
            Assume.That(urp != null, "URP Lit が必要");
            var m = CreateMaterialAsset("BackToStandard", _unknown);
            var data = UnityMaterialMigrator.Migrate(m, "Migrate", null, TestRoot, UnknownShaderHandling.Keep);
            Assert.AreSame(_unknown, data.Shader);

            m.shader = urp; // 元の .mat を標準シェーダーへ戻した
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            var again = UnityMaterialMigrator.Migrate(m, "Migrate", null, TestRoot); // 非対話・Profile は Ask = ConvertKeepingExisting

            Assert.AreSame(data, again);
            Assert.AreEqual(UnityMaterialMigrator.LitShaderName, again.Shader.name, "元が標準シェーダーに戻ったので、従来どおり D-Drive 標準へ変換する");
        }

        [Test]
        public void MissingShader_IsCountedSeparately_InPrompt()
        {
            var missing = CreateMissingShaderMaterialAsset("MissingShader2");
            Assume.That(missing != null);
            var unk = CreateMaterialAsset("UnkWithMissing", _unknown);

            var prompt = UnknownShaderGuard.BuildPrompt(new[] { unk, missing });

            Assert.IsNotNull(prompt);
            Assert.AreEqual(1, prompt.MaterialCount);
            Assert.AreEqual(1, prompt.MissingShaderMaterialCount);
            StringAssert.Contains("見つからない", prompt.Message);
        }

        // ── MayaMaterialImporter ──

        [Test]
        public void ResolveTargetShader_Keep_OnlyForUnknownShaders()
        {
            var unknownMat = new UnityEngine.Material(_unknown);
            var knownMat = new UnityEngine.Material(Shader.Find(UnityMaterialMigrator.LitShaderName));
            try
            {
                Assert.AreEqual(UnityMaterialMigrator.LitShaderName, MayaMaterialImporter.ResolveTargetShader(_profile, unknownMat).name, "Ask(非対話)は従来どおり Lit");
                Assert.AreSame(_unknown, MayaMaterialImporter.ResolveTargetShader(_profile, unknownMat, UnknownShaderHandling.Keep));
                Assert.AreEqual(UnityMaterialMigrator.LitShaderName, MayaMaterialImporter.ResolveTargetShader(_profile, knownMat, UnknownShaderHandling.Keep).name);

                _profile.UnknownShaderPolicy = UnknownShaderPolicy.KeepSource;
                Assert.AreSame(_unknown, MayaMaterialImporter.ResolveTargetShader(_profile, unknownMat), "KeepSource の Profile は自動取り込み(非対話)でも保つ");

                var target = Shader.Find(UnityMaterialMigrator.UnlitShaderName);
                if (target != null)
                {
                    _profile.TargetShader = target;
                    Assert.AreSame(target, MayaMaterialImporter.ResolveTargetShader(_profile, unknownMat), "Profile の TargetShader が最優先(従来どおり)");
                }
            }
            finally
            {
                Object.DestroyImmediate(unknownMat);
                Object.DestroyImmediate(knownMat);
            }
        }

        // ── ModelSlotBinder.Rebuild(Model エディタの「元ファイルを再読み込み」) ──

        private ModelData CreateModelWithUnknownMaterial()
        {
            var m = CreateMaterialAsset("RebuildMat", _unknown);
            var go = new GameObject("RebuildPrefab");
            var body = new GameObject("Body");
            body.transform.SetParent(go.transform);
            body.AddComponent<MeshRenderer>().sharedMaterial = m;
            var prefabPath = TempFolder + "/RebuildPrefab.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            _assets.Add(prefabPath);

            var model = ScriptableObject.CreateInstance<ModelData>();
            model.hideFlags = HideFlags.HideAndDontSave;
            model.Prefab = prefab;
            return model;
        }

        private static MaterialData FirstMaterialData()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(MaterialData), new[] { TestRoot });
            return guids.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<MaterialData>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        [Test]
        public void Rebuild_Interactive_Keep_UsesSourceShader_PromptOnce()
        {
            var model = CreateModelWithUnknownMaterial();
            try
            {
                _answer = UnknownShaderChoice.Keep;
                ModelSlotBinder.Rebuild(model, true, new ModelSlotBinder.Report(), TestRoot, interactive: true);

                Assert.AreEqual(1, _prompts);
                Assert.AreSame(_unknown, FirstMaterialData().Shader);
                Assert.IsTrue(model.Slots[0].Material.IsValid, "Slot にも結び付く");
            }
            finally
            {
                Object.DestroyImmediate(model);
            }
        }

        [Test]
        public void Rebuild_Interactive_Convert_UsesLit()
        {
            var model = CreateModelWithUnknownMaterial();
            try
            {
                _answer = UnknownShaderChoice.Convert;
                ModelSlotBinder.Rebuild(model, true, new ModelSlotBinder.Report(), TestRoot, interactive: true);

                Assert.AreEqual(1, _prompts);
                Assert.AreEqual(UnityMaterialMigrator.LitShaderName, FirstMaterialData().Shader.name);
            }
            finally
            {
                Object.DestroyImmediate(model);
            }
        }

        [Test]
        public void Rebuild_Interactive_Cancel_ChangesNothing()
        {
            var model = CreateModelWithUnknownMaterial();
            try
            {
                _answer = UnknownShaderChoice.Cancel;
                var report = new ModelSlotBinder.Report();
                var changed = ModelSlotBinder.Rebuild(model, true, report, TestRoot, interactive: true);

                Assert.IsFalse(changed);
                Assert.AreEqual(1, _prompts);
                Assert.AreEqual(0, CountMaterialData(), "MaterialData を作らない");
                Assert.IsTrue(model.Slots == null || model.Slots.Length == 0, "Slots も書き換えない");
            }
            finally
            {
                Object.DestroyImmediate(model);
            }
        }

        [Test]
        public void Rebuild_NonInteractive_Ask_ConvertsToLit_WithoutDialog()
        {
            var model = CreateModelWithUnknownMaterial();
            try
            {
                ModelSlotBinder.Rebuild(model, true, new ModelSlotBinder.Report(), TestRoot);

                Assert.AreEqual(0, _prompts);
                Assert.AreEqual(UnityMaterialMigrator.LitShaderName, FirstMaterialData().Shader.name);
            }
            finally
            {
                Object.DestroyImmediate(model);
            }
        }

        [Test]
        public void Rebuild_Interactive_KeepSourcePolicy_NoDialog_KeepsShader()
        {
            _profile.UnknownShaderPolicy = UnknownShaderPolicy.KeepSource;
            var model = CreateModelWithUnknownMaterial();
            try
            {
                ModelSlotBinder.Rebuild(model, true, new ModelSlotBinder.Report(), TestRoot, interactive: true);

                Assert.AreEqual(0, _prompts);
                Assert.AreSame(_unknown, FirstMaterialData().Shader);
            }
            finally
            {
                Object.DestroyImmediate(model);
            }
        }
    }
}
