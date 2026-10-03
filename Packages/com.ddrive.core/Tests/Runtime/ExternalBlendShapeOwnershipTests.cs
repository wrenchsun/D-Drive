using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Model;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    // FC-20(f27702e 由来)、[docs/51] §3.5・§4.21。外部パッケージが所有するシェイプ接頭辞(FC_ / fcs_)の判定・Validator と、
    // 「D-Drive は名前を変えず、AnimData が指定した通常シェイプだけを書く」契約(E-16 の先行分)。FBX フィクスチャが要る確認は FC-10。
    public class ExternalBlendShapeOwnershipTests
    {
        private static readonly string[] ShapeNames =
        {
            "FC_Hero_Neutral_R0_C0",
            "FC_Hero_Neutral_R0_C0_Ex",
            "FC_Hero_Persp_K0",
            "Smile",
        };

        private PoolService _pool;
        private AnimManager _anim;
        private ModelsManager _models;
        private GameObject _prefab;
        private Mesh _mesh;

        [SetUp]
        public void SetUp()
        {
            var registry = new AssetRegistry(new FakeAssetLoader());
            _pool = new PoolService();
            _anim = new AnimManager(registry);
            _models = new ModelsManager(_pool, registry, _anim);

            _mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.up, Vector3.right },
                triangles = new[] { 0, 1, 2 },
            };
            var delta = new Vector3[3];
            delta[1] = Vector3.one;
            foreach (var n in ShapeNames)
            {
                _mesh.AddBlendShapeFrame(n, 100f, delta, null, null);
            }

            _prefab = new GameObject("FcOwnershipPrefab");
            _prefab.AddComponent<Animator>();
            var body = new GameObject("Body");
            body.transform.SetParent(_prefab.transform);
            body.AddComponent<SkinnedMeshRenderer>().sharedMesh = _mesh;
        }

        [TearDown]
        public void TearDown()
        {
            _anim.StopAll(StopReason.Manual);
            _pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(_prefab);
            Object.DestroyImmediate(_mesh);
        }

        private static AnimData CreateAnim(params BlendShapeTrack[] tracks)
        {
            var data = ScriptableObject.CreateInstance<AnimData>();
            data.Id = 1;
            var clip = new AnimationClip { legacy = true, frameRate = 30f };
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            data.Clip = clip;
            data.BlendShapes = tracks;
            return data;
        }

        private static List<ValidationResult> Validate(AnimData data)
            => new(new AnimDataValidator().Validate(data, new ValidationContext(new List<AssetDataBase> { data })));

        // ── 判定(定数 1 か所) ──

        [Test]
        public void IsOwnedExternally_PrefixMatch_IsCaseSensitive()
        {
            Assert.IsTrue(ExternalBlendShapePrefixes.IsOwnedExternally("FC_x"));
            Assert.IsTrue(ExternalBlendShapePrefixes.IsOwnedExternally("fcs_x"));
            Assert.IsTrue(ExternalBlendShapePrefixes.IsOwnedExternally("FC_Hero_Neutral_R0_C0_Ex"));
            Assert.IsFalse(ExternalBlendShapePrefixes.IsOwnedExternally("fc_x"));
            Assert.IsFalse(ExternalBlendShapePrefixes.IsOwnedExternally("FCS_x"));
            Assert.IsFalse(ExternalBlendShapePrefixes.IsOwnedExternally("Smile"));
            Assert.IsFalse(ExternalBlendShapePrefixes.IsOwnedExternally("MyFC_x"));
            Assert.IsFalse(ExternalBlendShapePrefixes.IsOwnedExternally(string.Empty));
            Assert.IsFalse(ExternalBlendShapePrefixes.IsOwnedExternally(null));
            CollectionAssert.AreEquivalent(new[] { "FC_", "fcs_" }, ExternalBlendShapePrefixes.All);
        }

        // ── Validator ──

        [Test]
        public void Validator_OwnedShape_IsWarning_WithCode()
        {
            var data = CreateAnim(
                new BlendShapeTrack { ShapeName = "FC_Hero_Neutral_R0_C0", Weight = AnimationCurve.Constant(0, 1, 1) },
                new BlendShapeTrack { ShapeName = "fcs_Hero_Eye", Weight = AnimationCurve.Constant(0, 1, 1) });
            var owned = Validate(data).FindAll(r => r.Code == "DD-ANIM-BLENDSHAPE-EXTERNAL-OWNED");
            Assert.AreEqual(2, owned.Count);
            Assert.IsTrue(owned.TrueForAll(r => r.Severity == ValidationSeverity.Warning));
        }

        [Test]
        public void Validator_PlainShape_HasNoOwnedWarning()
        {
            var data = CreateAnim(
                new BlendShapeTrack { ShapeName = "Smile", Weight = AnimationCurve.Constant(0, 1, 1) },
                new BlendShapeTrack { ShapeName = "fc_lower", Weight = AnimationCurve.Constant(0, 1, 1) });
            Assert.IsFalse(Validate(data).Exists(r => r.Code == "DD-ANIM-BLENDSHAPE-EXTERNAL-OWNED"));
        }

        // ── 契約: 名前を変えず、指定した通常シェイプだけを書く ──

        private static void AssertNames(SkinnedMeshRenderer smr)
        {
            Assert.AreEqual(ShapeNames.Length, smr.sharedMesh.blendShapeCount);
            for (var i = 0; i < ShapeNames.Length; i++)
            {
                Assert.AreEqual(ShapeNames[i], smr.sharedMesh.GetBlendShapeName(i));
            }
        }

        [Test]
        public void AnimTick_WritesOnlySpecifiedShape_AndExternalLateWritesSurvive()
        {
            var data = CreateAnim(new BlendShapeTrack { ShapeName = "Smile", Weight = AnimationCurve.Linear(0f, 0f, 1f, 100f) });
            var h = _models.SpawnData(CreateModelData(), Vector3.zero, Quaternion.identity);
            var root = _models.GetGameObject(h);
            var smr = root.GetComponentInChildren<SkinnedMeshRenderer>();
            _anim.PlayData(data, root.GetComponent<Animator>());

            _anim.Tick(0.5f);
            Assert.AreEqual(50f, smr.GetBlendShapeWeight(3), 0.5f, "AnimData が指定した通常シェイプは書かれる");
            for (var i = 0; i < 3; i++)
            {
                Assert.AreEqual(0f, smr.GetBlendShapeWeight(i), "D-Drive は FC_* に触れない");
            }

            // 外部が LateUpdate 相当で FC_* に重みを書く → 次の Tick(D-Drive の Update 側)で上書きされない。
            smr.SetBlendShapeWeight(0, 80f);
            smr.SetBlendShapeWeight(1, 60f);
            smr.SetBlendShapeWeight(2, 40f);
            _anim.Tick(0.25f);
            Assert.AreEqual(80f, smr.GetBlendShapeWeight(0));
            Assert.AreEqual(60f, smr.GetBlendShapeWeight(1));
            Assert.AreEqual(40f, smr.GetBlendShapeWeight(2));
            Assert.AreEqual(75f, smr.GetBlendShapeWeight(3), 0.5f);
            AssertNames(smr);

            // プール往復でも名前は変わらない。
            _models.Despawn(h);
            var h2 = _models.SpawnData(CreateModelData(), Vector3.zero, Quaternion.identity);
            AssertNames(_models.GetGameObject(h2).GetComponentInChildren<SkinnedMeshRenderer>());
        }

        private ModelData CreateModelData()
        {
            var m = ScriptableObject.CreateInstance<ModelData>();
            m.Id = 5;
            m.Prefab = _prefab;
            m.Flags.Pool = PoolPolicy.Pooled(0, 4);
            return m;
        }
    }
}
