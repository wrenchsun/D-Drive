using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DDrive.Editor.CanvasTool;
using DDrive.Foundation.Loader;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace DDrive.Tests.Editor
{
    // 2026-09-29: Canvas Editor の ElementFx プレビューで「▶ を連打すると位置がずれる」不具合の対策
    // (ElementFxStateSnapshot = 再生前の状態を控えて戻す)の検証。Canvas Editor の再生手順
    // (止める → 初期状態へ戻す → 再生)を UiTweenManager と組み合わせて再現する。
    public class ElementFxStateSnapshotTests
    {
        private sealed class NoLoader : IAssetLoader
        {
            public UniTask<T> LoadAsync<T>(string address, CancellationToken ct) where T : UnityEngine.Object
                => UniTask.FromResult<T>(null);

            public void Release(string address)
            {
            }

            public UniTask PreloadAsync(IEnumerable<string> addresses, IProgress<float> progress) => UniTask.CompletedTask;
        }

        private GameObject _parent;
        private RectTransform _target;
        private UiTweenManager _tweens;
        private readonly TweenTrack[] _scratch = new TweenTrack[UiTweenManager.MaxTracksPerTween];

        [SetUp]
        public void SetUp()
        {
            _parent = new GameObject("SnapshotTestParent", typeof(RectTransform));
            ((RectTransform)_parent.transform).sizeDelta = new Vector2(800f, 600f);
            var go = new GameObject("Target", typeof(RectTransform), typeof(Image));
            _target = (RectTransform)go.transform;
            _target.SetParent(_parent.transform, false);
            _target.sizeDelta = new Vector2(100f, 40f);
            _target.anchoredPosition = new Vector2(30f, -20f);
            _tweens = new UiTweenManager(new AssetRegistry(new NoLoader()));
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_parent);

        [Test]
        public void Restore_PutsBackAllTweenedValues_AndRemovesAddedCanvasGroup()
        {
            var image = _target.GetComponent<Image>();
            image.color = new Color(0.2f, 0.4f, 0.6f, 1f);
            var snapshot = new ElementFxStateSnapshot();
            Assert.IsTrue(snapshot.Capture(_target));
            Assert.IsFalse(snapshot.Capture(_target), "2 回目は上書きしない");

            _target.anchoredPosition = new Vector2(999f, 999f);
            _target.sizeDelta = new Vector2(1f, 1f);
            _target.localScale = new Vector3(3f, 3f, 3f);
            _target.localEulerAngles = new Vector3(0f, 0f, 45f);
            image.color = Color.red;
            _target.gameObject.AddComponent<CanvasGroup>().alpha = 0.1f;

            snapshot.Restore(_target);

            Assert.AreEqual(new Vector2(30f, -20f), _target.anchoredPosition);
            Assert.AreEqual(new Vector2(100f, 40f), _target.sizeDelta);
            Assert.AreEqual(Vector3.one, _target.localScale);
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, _target.localRotation), 0.001f);
            Assert.AreEqual(new Color(0.2f, 0.4f, 0.6f, 1f), image.color);
            Assert.IsNull(_target.GetComponent<CanvasGroup>(), "再生で足された CanvasGroup は残さない");
        }

        [Test]
        public void Restore_KeepsPreExistingCanvasGroup_AndRestoresAlpha()
        {
            var group = _target.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0.5f;
            var snapshot = new ElementFxStateSnapshot();
            snapshot.Capture(_target);

            group.alpha = 0f;
            snapshot.Restore(_target);

            Assert.AreSame(group, _target.GetComponent<CanvasGroup>());
            Assert.AreEqual(0.5f, group.alpha, 0.0001f);
        }

        [Test]
        public void RestoreAllAndClear_EmptiesTheSnapshot()
        {
            var snapshot = new ElementFxStateSnapshot();
            snapshot.Capture(_target);
            _target.anchoredPosition = Vector2.zero;

            snapshot.RestoreAllAndClear();

            Assert.AreEqual(0, snapshot.Count);
            Assert.AreEqual(new Vector2(30f, -20f), _target.anchoredPosition);
        }

        [Test]
        public void Restore_DestroyedTarget_DoesNotThrow()
        {
            var snapshot = new ElementFxStateSnapshot();
            snapshot.Capture(_target);
            UnityEngine.Object.DestroyImmediate(_target.gameObject);

            Assert.DoesNotThrow(() => snapshot.RestoreAll());
            Assert.DoesNotThrow(() => snapshot.RestoreAllAndClear());
        }

        // Canvas Editor の ▶ と同じ手順(止める → 初期状態へ戻す → 組み立てて再生)。
        private Vector2 PlayOnce(ElementFxStateSnapshot snapshot, UiPreset preset, float advanceSeconds)
        {
            snapshot.Capture(_target);
            _tweens.StopAll(_target);
            snapshot.Restore(_target);
            var p = new UiPresetRef { Preset = preset };
            var count = UiPresetFactory.Build(in p, _target, _scratch);
            _tweens.PlayTracks(_scratch, count, _target);
            _tweens.Tick(advanceSeconds);
            return _target.anchoredPosition;
        }

        [Test]
        public void RepeatedSlideIn_MidFlight_EndsAtSamePositionAsSinglePlay()
        {
            var snapshot = new ElementFxStateSnapshot();
            PlayOnce(snapshot, UiPreset.SlideInLeft, 5f);
            var single = _target.anchoredPosition;
            Assert.AreEqual(new Vector2(30f, -20f), single, "1 回再生した最終位置は元の位置");

            // 再生途中(0.05 秒)で 5 回連打してから最後は最後まで再生する。
            for (var i = 0; i < 5; i++)
            {
                PlayOnce(snapshot, UiPreset.SlideInLeft, 0.05f);
            }

            PlayOnce(snapshot, UiPreset.SlideInLeft, 5f);
            Assert.AreEqual(single.x, _target.anchoredPosition.x, 0.001f);
            Assert.AreEqual(single.y, _target.anchoredPosition.y, 0.001f);
        }

        [Test]
        public void Disappear_ThenSlideIn_DoesNotInheritTheOffscreenPosition()
        {
            var snapshot = new ElementFxStateSnapshot();
            PlayOnce(snapshot, UiPreset.SlideOutLeft, 5f);
            Assert.AreNotEqual(new Vector2(30f, -20f), _target.anchoredPosition, "Disappear は画面外で終わる");

            PlayOnce(snapshot, UiPreset.SlideInLeft, 5f);
            Assert.AreEqual(30f, _target.anchoredPosition.x, 0.001f);
            Assert.AreEqual(-20f, _target.anchoredPosition.y, 0.001f);
        }
    }
}
