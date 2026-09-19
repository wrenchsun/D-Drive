using System.Collections.Generic;
using System.Text.RegularExpressions;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anchoring;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using AnchorId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anchoring.AnchorMarker>;

namespace DDrive.Tests.Runtime
{
    // テスト用: AnchorData 群を Registry に登録して同期解決できる状態にする。
    internal static class AnchorChainTestRegistry
    {
        public static AssetRegistry Build(params AnchorData[] anchors)
        {
            var loader = new FakeAssetLoader();
            var entries = new List<CatalogEntry>();
            foreach (var a in anchors)
            {
                var address = $"anchor/{a.Id}";
                loader.Assets[address] = a;
                entries.Add(new CatalogEntry { Id = a.Id, Type = AssetType.Anchor, Address = address });
            }

            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(entries);
            var registry = new AssetRegistry(loader);
            registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            foreach (var a in anchors)
            {
                registry.ResolveAsync<AnchorData>(a.Id).GetAwaiter().GetResult();
            }

            return registry;
        }

        public static AnchorData Anchor(ulong id, Vector3 offset = default, Vector3 euler = default, ulong parent = 0,
            AnchorSpace space = AnchorSpace.World, string path = null)
        {
            var a = ScriptableObject.CreateInstance<AnchorData>();
            a.Id = id;
            a.name = $"Anchor_{id}";
            a.LocalOffset = offset;
            a.LocalEuler = euler;
            a.Space = space;
            a.Path = path;
            if (parent != 0)
            {
                a.Parent = new AnchorId(parent, AssetType.Anchor);
            }

            return a;
        }

        public static AnchorId Id(ulong id) => new(id, AssetType.Anchor);
    }

    // [21_anchor_spec.md] §3.2 — 連鎖の合成・ランダム・ディレイ/確率の合算・循環の扱い。
    public class AnchorChainTests
    {
        [Test]
        public void Resolve_UnknownId_FallsBackToWorldDefault()
        {
            var registry = AnchorChainTestRegistry.Build();
            LogAssert.Expect(LogType.Warning, new Regex("Unregistered AssetId"));

            var spec = AnchorChain.Resolve(registry, AnchorChainTestRegistry.Id(999), sampleRandom: true);

            Assert.AreEqual(AnchorSpace.World, spec.Def.Space);
            Assert.AreEqual(Vector3.zero, spec.Def.LocalOffset);
            Assert.AreEqual(Vector3.one, spec.Def.LocalScale);
            Assert.AreEqual(1f, spec.SpawnChance);
            Assert.AreEqual(0f, spec.DelaySec);
        }

        [Test]
        public void Resolve_Child_AddsOffsetInParentFrame()
        {
            var root = AnchorChainTestRegistry.Anchor(1, offset: new Vector3(1f, 0f, 0f), euler: new Vector3(0f, 90f, 0f));
            var child = AnchorChainTestRegistry.Anchor(2, offset: new Vector3(0f, 0f, 1f), parent: 1);
            var registry = AnchorChainTestRegistry.Build(root, child);

            var spec = AnchorChain.Resolve(registry, AnchorChainTestRegistry.Id(2), sampleRandom: false);

            // 親の向き(Y+90°)で子の前方(+Z)は +X になる: (1,0,0) + (1,0,0) = (2,0,0)
            Assert.Less(Vector3.Distance(new Vector3(2f, 0f, 0f), spec.Def.LocalOffset), 1e-4f);
            Assert.Less(Mathf.DeltaAngle(90f, spec.Def.LocalEuler.y), 1e-3f);
        }

        [Test]
        public void Resolve_Child_InheritsRootSpaceAndPath()
        {
            var root = AnchorChainTestRegistry.Anchor(1, space: AnchorSpace.NamedObject, path: "Anchor_Root");
            root.FollowRotation = true;
            var child = AnchorChainTestRegistry.Anchor(2, parent: 1, space: AnchorSpace.BoneName, path: "Ignored");
            var registry = AnchorChainTestRegistry.Build(root, child);

            var spec = AnchorChain.Resolve(registry, AnchorChainTestRegistry.Id(2), sampleRandom: false);

            Assert.AreEqual(AnchorSpace.NamedObject, spec.Def.Space);
            Assert.AreEqual("Anchor_Root", spec.Def.Path);
            Assert.IsTrue(spec.Def.FollowRotation);
        }

        [Test]
        public void Resolve_Chain_SumsDelayAndMultipliesChance()
        {
            var root = AnchorChainTestRegistry.Anchor(1);
            root.DelaySec = 0.5f;
            root.SpawnChance = 0.5f;
            var child = AnchorChainTestRegistry.Anchor(2, parent: 1);
            child.DelaySec = 0.25f;
            child.SpawnChance = 0.5f;
            var registry = AnchorChainTestRegistry.Build(root, child);

            var spec = AnchorChain.Resolve(registry, AnchorChainTestRegistry.Id(2), sampleRandom: false);

            Assert.AreEqual(0.75f, spec.DelaySec, 1e-5f);
            Assert.AreEqual(0.25f, spec.SpawnChance, 1e-5f);
        }

        [Test]
        public void Resolve_SampleRandom_StaysWithinConfiguredRanges()
        {
            var root = AnchorChainTestRegistry.Anchor(1);
            root.PositionJitterRadius = 0.5f;
            root.ScaleRange = new Vector2(2f, 2f);
            root.DelayJitterSec = 1f;
            var registry = AnchorChainTestRegistry.Build(root);

            for (var i = 0; i < 20; i++)
            {
                var spec = AnchorChain.Resolve(registry, AnchorChainTestRegistry.Id(1), sampleRandom: true);
                Assert.LessOrEqual(spec.ExtraOffset.magnitude, 0.5f + 1e-4f);
                Assert.AreEqual(2f, spec.ScaleMultiplier, 1e-5f);
                Assert.That(spec.DelaySec, Is.InRange(0f, 1f));
            }

            var staticSpec = AnchorChain.Resolve(registry, AnchorChainTestRegistry.Id(1), sampleRandom: false);
            Assert.AreEqual(Vector3.zero, staticSpec.ExtraOffset);
            Assert.AreEqual(1f, staticSpec.ScaleMultiplier);
            Assert.AreEqual(0f, staticSpec.DelaySec);
        }

        [Test]
        public void Resolve_Cycle_WarnsAndTreatsReachedNodeAsRoot()
        {
            var a = AnchorChainTestRegistry.Anchor(1, offset: new Vector3(1f, 0f, 0f), parent: 2);
            var b = AnchorChainTestRegistry.Anchor(2, offset: new Vector3(0f, 1f, 0f), parent: 1);
            var registry = AnchorChainTestRegistry.Build(a, b);
            LogAssert.Expect(LogType.Warning, new Regex("cycle"));

            var spec = AnchorChain.Resolve(registry, AnchorChainTestRegistry.Id(1), sampleRandom: false);

            // 1 → 2 → (1 は既出) で止まり、2 がルート扱い: (0,1,0) + (1,0,0)
            Assert.Less(Vector3.Distance(new Vector3(1f, 1f, 0f), spec.Def.LocalOffset), 1e-4f);
        }
    }
}
