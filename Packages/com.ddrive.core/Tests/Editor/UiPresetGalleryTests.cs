using System.Collections.Generic;
using DDrive.Editor.Ui;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [15_ui_interaction.md] B-3.5「エディタ: プリセットギャラリー」(4-12) — 純ロジック部分。
    // EditorWindow の UI Toolkit 組み立て自体は UiTweenEditorTests / SliderEditorTests と同じ方針で対象外。
    public class UiPresetGalleryTests
    {
        // フェイク永続化ストア(EditorPrefs を直接叩かずに往復検証する)。
        private sealed class FakePrefsStore : IGalleryPrefsStore
        {
            private readonly Dictionary<string, string> _values = new();

            public string GetString(string key, string defaultValue) => _values.TryGetValue(key, out var v) ? v : defaultValue;
            public void SetString(string key, string value) => _values[key] = value;
        }

        // ── UiPresetGalleryFilter.Filter ──

        [Test]
        public void Filter_ByTab_ReturnsOnlyMatchingTabCards()
        {
            var cards = new List<GalleryCard>
            {
                new("FadeIn", GalleryTab.出現, UiPreset.FadeIn, null),
                new("FadeOut", GalleryTab.消滅, UiPreset.FadeOut, null),
                new("Pulse", GalleryTab.常時, UiPreset.Pulse, null),
            };

            var result = UiPresetGalleryFilter.Filter(cards, GalleryTab.出現, null, null);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("FadeIn", result[0].Name);
        }

        [Test]
        public void Filter_ByQuery_IsCaseInsensitiveSubstringOnName()
        {
            var cards = new List<GalleryCard>
            {
                new("SlideInLeft", GalleryTab.出現, UiPreset.SlideInLeft, null),
                new("SlideInRight", GalleryTab.出現, UiPreset.SlideInRight, null),
                new("FadeIn", GalleryTab.出現, UiPreset.FadeIn, null),
            };

            var result = UiPresetGalleryFilter.Filter(cards, GalleryTab.出現, "slidein", null);

            Assert.AreEqual(2, result.Count);
            CollectionAssert.AreEquivalent(new[] { "SlideInLeft", "SlideInRight" }, result.ConvertAll(c => c.Name));
        }

        [Test]
        public void Filter_EmptyQuery_ReturnsAllInTab()
        {
            var cards = new List<GalleryCard>
            {
                new("FadeIn", GalleryTab.出現, UiPreset.FadeIn, null),
                new("ScaleIn", GalleryTab.出現, UiPreset.ScaleIn, null),
            };

            var result = UiPresetGalleryFilter.Filter(cards, GalleryTab.出現, string.Empty, null);

            Assert.AreEqual(2, result.Count);
        }

        [Test]
        public void Filter_Favourites_AreMovedToFront_ButNotExcluded()
        {
            var cards = new List<GalleryCard>
            {
                new("A", GalleryTab.強調, UiPreset.Shake, null),
                new("B", GalleryTab.強調, UiPreset.Flash, null),
                new("C", GalleryTab.強調, UiPreset.Tada, null),
            };

            var favourites = new HashSet<string> { "C" };
            var result = UiPresetGalleryFilter.Filter(cards, GalleryTab.強調, null, favourites);

            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("C", result[0].Name);
            CollectionAssert.AreEquivalent(new[] { "A", "B", "C" }, result.ConvertAll(c => c.Name));
        }

        [Test]
        public void BuildBuiltinCards_ExcludesNone_And_ClassifiesKnownPresets()
        {
            var cards = UiPresetGalleryFilter.BuildBuiltinCards();

            Assert.IsFalse(cards.Exists(c => c.Preset == UiPreset.None));
            Assert.AreEqual(GalleryTab.出現, cards.Find(c => c.Preset == UiPreset.FadeIn).Tab);
            Assert.AreEqual(GalleryTab.消滅, cards.Find(c => c.Preset == UiPreset.FadeOut).Tab);
            Assert.AreEqual(GalleryTab.常時, cards.Find(c => c.Preset == UiPreset.Pulse).Tab);
            Assert.AreEqual(GalleryTab.強調, cards.Find(c => c.Preset == UiPreset.Shake).Tab);
        }

        // ── UiPresetGalleryFavorites(fake store 往復) ──

        [Test]
        public void Favorites_SaveThenLoad_RoundTripsThroughFakeStore()
        {
            var store = new FakePrefsStore();
            var favourites = new HashSet<string> { "FadeIn", "Pulse", "Shake" };

            UiPresetGalleryFavorites.Save(store, favourites);
            var loaded = UiPresetGalleryFavorites.Load(store);

            CollectionAssert.AreEquivalent(favourites, loaded);
        }

        [Test]
        public void Favorites_Load_EmptyStore_ReturnsEmptySet()
        {
            var store = new FakePrefsStore();
            var loaded = UiPresetGalleryFavorites.Load(store);
            Assert.AreEqual(0, loaded.Count);
        }

        [Test]
        public void Favorites_Parse_TrimsWhitespace_And_IgnoresEmptyTokens()
        {
            var parsed = UiPresetGalleryFavorites.Parse(" A , B ,, C");
            CollectionAssert.AreEquivalent(new[] { "A", "B", "C" }, parsed);
        }

        // ── UiPresetCatalogEditing.Register ──

        [Test]
        public void Register_NewName_AppendsEntry()
        {
            var catalog = ScriptableObject.CreateInstance<UiPresetCatalog>();
            var tween = ScriptableObject.CreateInstance<UiTweenData>();
            tween.Id = 42;

            UiPresetCatalogEditing.Register(catalog, "MyPreset", tween, "MyCategory");

            Assert.AreEqual(1, catalog.Entries.Length);
            Assert.AreEqual("MyPreset", catalog.Entries[0].Name);
            Assert.AreEqual(tween, catalog.Entries[0].Tween);
            Assert.AreEqual("MyCategory", catalog.Entries[0].Category);

            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(tween);
        }

        [Test]
        public void Register_ExistingName_UpdatesInPlace_WithoutDuplicating()
        {
            var catalog = ScriptableObject.CreateInstance<UiPresetCatalog>();
            var tweenA = ScriptableObject.CreateInstance<UiTweenData>();
            tweenA.Id = 1;
            var tweenB = ScriptableObject.CreateInstance<UiTweenData>();
            tweenB.Id = 2;

            UiPresetCatalogEditing.Register(catalog, "MyPreset", tweenA, "CatA");
            UiPresetCatalogEditing.Register(catalog, "MyPreset", tweenB, "CatB");

            Assert.AreEqual(1, catalog.Entries.Length);
            Assert.AreEqual(tweenB, catalog.Entries[0].Tween);
            Assert.AreEqual("CatB", catalog.Entries[0].Category);

            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(tweenA);
            Object.DestroyImmediate(tweenB);
        }

        [Test]
        public void Register_NullTweenOrEmptyName_DoesNothing()
        {
            var catalog = ScriptableObject.CreateInstance<UiPresetCatalog>();
            var tween = ScriptableObject.CreateInstance<UiTweenData>();

            UiPresetCatalogEditing.Register(catalog, string.Empty, tween, null);
            UiPresetCatalogEditing.Register(catalog, "Name", null, null);

            Assert.IsTrue(catalog.Entries == null || catalog.Entries.Length == 0);

            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(tween);
        }
    }
}
