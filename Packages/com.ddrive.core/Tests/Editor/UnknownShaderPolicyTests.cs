using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
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
    // 実ダイアログは出さない(UnknownShaderGuard.PromptOverride を差し替える)。実 Profile / 実 GameData は触らない。
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
            MayaImportProfile.TestOverride = _profile; // 実プロジェクトの Profile を読まない
            _prompts = 0;
            _lastPrompt = null;
            _answer = UnknownShaderChoice.Keep;
            UnknownShaderGuard.PromptOverride = prompt =>
            {
                _prompts++;
                _lastPrompt = prompt;
                return _answer;
            };
        }

        [TearDown]
        public void TearDown()
        {
            UnknownShaderGuard.PromptOverride = null;
            MayaImportProfile.TestOverride = null;
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
            Assert.AreEqual(UnknownShaderHandling.Convert, handling);
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
