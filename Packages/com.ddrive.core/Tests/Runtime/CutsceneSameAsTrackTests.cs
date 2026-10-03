using System.Collections.Generic;
using System.Text.RegularExpressions;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Model;
using DDrive.Runtime.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace DDrive.Tests.Runtime
{
    // [26_timeline.md] §4.2 / [51_tdrive_integration.md] §4.2(FC-1) — CutsceneBindTarget.SameAsTrack。
    // 2 パス解決・並び順非依存・鎖・フェイルソフト(警告 + そのトラックだけ null)・SpawnModel を参照しても 1 体。
    public class CutsceneSameAsTrackTests
    {
        // Prefab に付けて「有効なインスタンス数」を数える(ModelsManager のハンドル数を外から見られないため)。
        private sealed class EnabledCounter : MonoBehaviour
        {
            public static int Enabled;

            private void OnEnable() => Enabled++;

            private void OnDisable() => Enabled--;
        }

        // OnModelSpawned の中から別のカットシーンを再生する外部リスナーの模擬(FC-R-05)。
        private sealed class ReentrantPlayListener : MonoBehaviour, IModelInstanceListener
        {
            public static System.Action OnSpawned;

            public void OnModelSpawned(in ModelInstanceContext context) => OnSpawned?.Invoke();

            public void OnModelReturning(in ModelInstanceContext context) { }
        }

        private readonly List<Object> _created = new();
        private PoolService _pool;

        [SetUp]
        public void SetUp()
        {
            _pool = new PoolService();
            EnabledCounter.Enabled = 0;
        }

        [TearDown]
        public void TearDown()
        {
            ReentrantPlayListener.OnSpawned = null;
            _pool.Clear(PoolScope.Global);
            foreach (var o in _created)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _created.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _created.Add(o);
            return o;
        }

        private TimelineAsset CreateTimeline(params string[] trackNames)
        {
            var timeline = Track(ScriptableObject.CreateInstance<TimelineAsset>());
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 10.0;
            foreach (var name in trackNames)
            {
                timeline.CreateTrack<AnimationTrack>(null, name);
            }

            return timeline;
        }

        private CutsceneData CreateData(TimelineAsset timeline, params CutsceneBinding[] bindings)
        {
            var data = Track(ScriptableObject.CreateInstance<CutsceneData>());
            data.Timeline = timeline;
            data.Origin = CutsceneOrigin.World;
            data.Bindings = bindings;
            return data;
        }

        private static CutsceneBinding Self(string track) => new() { TrackName = track, Target = CutsceneBindTarget.Self };

        private static CutsceneBinding Same(string track, string source) =>
            new() { TrackName = track, Target = CutsceneBindTarget.SameAsTrack, SourceTrackName = source };

        private GameObject CreateActor(bool withAnimator)
        {
            var go = Track(new GameObject("SameAsTrackActor"));
            if (withAnimator)
            {
                go.AddComponent<Animator>();
            }

            return go;
        }

        private static PlayableDirector FindDirector(TimelineAsset timeline)
        {
            foreach (var d in Object.FindObjectsByType<PlayableDirector>(FindObjectsSortMode.None))
            {
                if (d.playableAsset == timeline)
                {
                    return d;
                }
            }

            return null;
        }

        private static Object BindingOf(TimelineAsset timeline, string trackName)
        {
            var director = FindDirector(timeline);
            Assert.IsNotNull(director, "再生中の Director が見つからない");
            foreach (var track in timeline.GetOutputTracks())
            {
                if (track.name == trackName)
                {
                    return director.GetGenericBinding(track);
                }
            }

            Assert.Fail($"トラック {trackName} が無い");
            return null;
        }

        private static CutsceneManager NewManager() => new(new AssetRegistry(new FakeAssetLoader()));

        private static void ExpectUnresolvedWarning(string trackName)
        {
            LogAssert.Expect(LogType.Warning, new Regex($"トラック '{trackName}' が未解決です"));
        }

        [Test]
        public void SameAsTrack_BindsToSameObjectAsSource()
        {
            var actor = CreateActor(withAnimator: true);
            var timeline = CreateTimeline("Hero", "Hero_Ext");
            var data = CreateData(timeline, Self("Hero"), Same("Hero_Ext", "Hero"));
            var manager = NewManager();

            manager.PlayData(data, new PlayContext { Self = actor.transform });

            var hero = BindingOf(timeline, "Hero");
            Assert.IsInstanceOf<Animator>(hero);
            Assert.AreSame(hero, BindingOf(timeline, "Hero_Ext"));
        }

        [Test]
        public void SameAsTrack_SourceAfterReferrer_ResolvesRegardlessOfOrder()
        {
            var actor = CreateActor(withAnimator: false);
            var timeline = CreateTimeline("Hero", "Hero_Ext");
            var data = CreateData(timeline, Same("Hero_Ext", "Hero"), Self("Hero"));
            var manager = NewManager();

            manager.PlayData(data, new PlayContext { Self = actor.transform });

            Assert.AreSame(actor.transform, BindingOf(timeline, "Hero"));
            Assert.AreSame(actor.transform, BindingOf(timeline, "Hero_Ext"));
        }

        [Test]
        public void SameAsTrack_Chain_ResolvesThroughSeveralHops_InAnyOrder()
        {
            var actor = CreateActor(withAnimator: true);
            var timeline = CreateTimeline("A", "B", "C");
            // C → B → A(A が実体)。参照元を先頭側に並べる。
            var data = CreateData(timeline, Same("C", "B"), Same("B", "A"), Self("A"));
            var manager = NewManager();

            manager.PlayData(data, new PlayContext { Self = actor.transform });

            var a = BindingOf(timeline, "A");
            Assert.IsNotNull(a);
            Assert.AreSame(a, BindingOf(timeline, "B"));
            Assert.AreSame(a, BindingOf(timeline, "C"));
        }

        [Test]
        public void SameAsTrack_EmptySourceName_WarnsOnce_AndMutesOnlyThatTrack()
        {
            var actor = CreateActor(withAnimator: false);
            var timeline = CreateTimeline("Hero", "Hero_Ext");
            var data = CreateData(timeline, Self("Hero"), Same("Hero_Ext", string.Empty));
            var manager = NewManager();

            ExpectUnresolvedWarning("Hero_Ext");
            var handle = manager.PlayData(data, new PlayContext { Self = actor.transform });

            Assert.IsTrue(manager.IsPlaying(handle), "未解決でも再生は継続する");
            Assert.AreSame(actor.transform, BindingOf(timeline, "Hero"));
            Assert.IsNull(BindingOf(timeline, "Hero_Ext"));
        }

        [Test]
        public void SameAsTrack_SourceNotInBindings_WarnsAndBindsNull()
        {
            var actor = CreateActor(withAnimator: false);
            var timeline = CreateTimeline("Hero", "Hero_Ext");
            var data = CreateData(timeline, Self("Hero"), Same("Hero_Ext", "Nobody"));
            var manager = NewManager();

            ExpectUnresolvedWarning("Hero_Ext");
            manager.PlayData(data, new PlayContext { Self = actor.transform });

            Assert.IsNull(BindingOf(timeline, "Hero_Ext"));
            Assert.AreSame(actor.transform, BindingOf(timeline, "Hero"));
        }

        [Test]
        public void SameAsTrack_SelfReference_WarnsAndBindsNull()
        {
            var timeline = CreateTimeline("Hero_Ext");
            var data = CreateData(timeline, Same("Hero_Ext", "Hero_Ext"));
            var manager = NewManager();

            ExpectUnresolvedWarning("Hero_Ext");
            manager.PlayData(data, new PlayContext());

            Assert.IsNull(BindingOf(timeline, "Hero_Ext"));
        }

        [Test]
        public void SameAsTrack_Cycle_WarnsForEachMember_AndBindsNull()
        {
            var timeline = CreateTimeline("A", "B");
            var data = CreateData(timeline, Same("A", "B"), Same("B", "A"));
            var manager = NewManager();

            ExpectUnresolvedWarning("A");
            ExpectUnresolvedWarning("B");
            manager.PlayData(data, new PlayContext());

            Assert.IsNull(BindingOf(timeline, "A"));
            Assert.IsNull(BindingOf(timeline, "B"));
        }

        [Test]
        public void SameAsTrack_SourceUnresolved_WarnsAndBindsNull()
        {
            // Hero は Target=Self だが ctx.Self が無い → 未解決。Hero_Ext も連鎖して未解決。
            var timeline = CreateTimeline("Hero", "Hero_Ext");
            var data = CreateData(timeline, Self("Hero"), Same("Hero_Ext", "Hero"));
            var manager = NewManager();

            ExpectUnresolvedWarning("Hero");
            ExpectUnresolvedWarning("Hero_Ext");
            var handle = manager.PlayData(data, new PlayContext());

            Assert.IsTrue(manager.IsPlaying(handle));
            Assert.IsNull(BindingOf(timeline, "Hero_Ext"));
        }

        [Test]
        public void SameAsTrack_SourceTrackMissingFromTimeline_WarnsAndBindsNull()
        {
            var actor = CreateActor(withAnimator: false);
            var timeline = CreateTimeline("Hero_Ext"); // "Hero" トラックは Timeline に無い
            var data = CreateData(timeline, Self("Hero"), Same("Hero_Ext", "Hero"));
            var manager = NewManager();

            LogAssert.Expect(LogType.Warning, new Regex("トラック 'Hero' が未解決です"));
            ExpectUnresolvedWarning("Hero_Ext");
            manager.PlayData(data, new PlayContext { Self = actor.transform });

            Assert.IsNull(BindingOf(timeline, "Hero_Ext"));
        }

        [Test]
        public void SameAsTrack_ReferencingSpawnModel_SpawnsOnlyOneModel_AndSharesAnimator()
        {
            var prefab = Track(new GameObject("SameAsTrackModelPrefab"));
            prefab.AddComponent<Animator>();
            prefab.AddComponent<EnabledCounter>();
            prefab.SetActive(true);
            EnabledCounter.Enabled = 0; // プレハブ自体の OnEnable 分を除く

            var model = Track(ScriptableObject.CreateInstance<ModelData>());
            model.Id = 777001UL;
            model.Prefab = prefab;

            var loader = new FakeAssetLoader();
            const string address = "model/777001";
            loader.Assets[address] = model;
            var catalog = Track(ScriptableObject.CreateInstance<AssetCatalog>());
            catalog.SetEntries(new List<CatalogEntry> { new() { Id = model.Id, Type = AssetType.Model, Address = address } });
            var registry = new AssetRegistry(loader);
            registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            registry.ResolveAsync<ModelData>(model.Id).GetAwaiter().GetResult();

            var models = new ModelsManager(_pool, registry);
            var manager = new CutsceneManager(registry, models);

            var timeline = CreateTimeline("Hero", "Hero_Ext");
            var data = CreateData(
                timeline,
                // 参照元を先に置いても Spawn は 1 回。
                Same("Hero_Ext", "Hero"),
                new CutsceneBinding
                {
                    TrackName = "Hero",
                    Target = CutsceneBindTarget.SpawnModel,
                    Model = new AssetId<ModelMarker>(model.Id, AssetType.Model),
                });

            var handle = manager.PlayData(data, new PlayContext());

            Assert.AreEqual(1, EnabledCounter.Enabled, "SameAsTrack は Spawn しない(モデルは 1 体)");
            var hero = BindingOf(timeline, "Hero");
            Assert.IsInstanceOf<Animator>(hero);
            Assert.AreSame(hero, BindingOf(timeline, "Hero_Ext"));

            manager.Cancel(handle);
            Assert.AreEqual(0, EnabledCounter.Enabled, "終了時に 1 回だけ Despawn される");
        }

        // FC-R-05: SpawnModel の OnModelSpawned(外部リスナー)の中から別のカットシーンを Play しても、
        // 外側の ApplyBindings の共有バッファが壊れず、例外にもならない(長さの違う Bindings で入れ子にする)。
        [Test]
        public void ApplyBindings_ReentrantPlayFromOnModelSpawned_DoesNotCorruptOuterBindings()
        {
            var prefab = Track(new GameObject("ReentrantModelPrefab"));
            prefab.AddComponent<Animator>();
            prefab.AddComponent<ReentrantPlayListener>();

            var model = Track(ScriptableObject.CreateInstance<ModelData>());
            model.Id = 777002UL;
            model.Prefab = prefab;

            var loader = new FakeAssetLoader();
            const string address = "model/777002";
            loader.Assets[address] = model;
            var catalog = Track(ScriptableObject.CreateInstance<AssetCatalog>());
            catalog.SetEntries(new List<CatalogEntry> { new() { Id = model.Id, Type = AssetType.Model, Address = address } });
            var registry = new AssetRegistry(loader);
            registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            registry.ResolveAsync<ModelData>(model.Id).GetAwaiter().GetResult();

            var models = new ModelsManager(_pool, registry);
            var manager = new CutsceneManager(registry, models);

            var nestedActor = CreateActor(withAnimator: false);
            var nestedTimeline = CreateTimeline("Nested");
            var nestedData = CreateData(nestedTimeline, Self("Nested"));
            Handle<CutsceneMarker> nestedHandle = default;
            ReentrantPlayListener.OnSpawned = () =>
            {
                ReentrantPlayListener.OnSpawned = null; // 入れ子の Play で再び Spawn しない
                nestedHandle = manager.PlayData(nestedData, new PlayContext { Self = nestedActor.transform });
            };

            var actor = CreateActor(withAnimator: false);
            var timeline = CreateTimeline("Hero", "Hero_Ext", "Extra");
            var data = CreateData(
                timeline,
                Same("Hero_Ext", "Hero"),
                new CutsceneBinding
                {
                    TrackName = "Hero",
                    Target = CutsceneBindTarget.SpawnModel,
                    Model = new AssetId<ModelMarker>(model.Id, AssetType.Model),
                },
                Self("Extra"));

            var handle = manager.PlayData(data, new PlayContext { Self = actor.transform });

            Assert.IsTrue(manager.IsPlaying(handle), "外側の再生は例外なく始まる");
            Assert.IsTrue(manager.IsPlaying(nestedHandle), "入れ子の再生も始まる");
            var hero = BindingOf(timeline, "Hero");
            Assert.IsInstanceOf<Animator>(hero);
            Assert.AreSame(hero, BindingOf(timeline, "Hero_Ext"), "外側の SameAsTrack は外側の結果へ結ばれる");
            Assert.AreSame(actor.transform, BindingOf(timeline, "Extra"));
            Assert.AreSame(nestedActor.transform, BindingOf(nestedTimeline, "Nested"), "入れ子側のバインドも正しい");

            manager.Cancel(nestedHandle);
            manager.Cancel(handle);
        }
    }
}
