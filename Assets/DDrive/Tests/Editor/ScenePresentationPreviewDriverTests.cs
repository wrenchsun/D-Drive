using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DDrive.Editor.Presentation;
using DDrive.Foundation.Loader;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Model;
using DDrive.Runtime.Presentation;
using NUnit.Framework;
using R3;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [08_presentation.md] §4(5-4) — PresentationEditor の統合プレビュー駆動(ScenePresentationPreviewDriver)。
    // 実 PresentationManager を Editor から動かす(ADR-4)ため、Manager/Registry は差し替えて実プロジェクトの
    // Assets/GameData や実カタログには一切触れない([11_tasks.md] 5-4 のテスト要件)。
    public class ScenePresentationPreviewDriverTests
    {
        private sealed class EmptyLoader : IAssetLoader
        {
            public UniTask<T> LoadAsync<T>(string address, CancellationToken ct) where T : UnityEngine.Object => UniTask.FromResult<T>(null);
            public void Release(string address)
            {
            }

            public UniTask PreloadAsync(IEnumerable<string> addresses, IProgress<float> progress) => UniTask.CompletedTask;
        }

        private ScenePresentationPreviewDriver _driver;
        private AssetRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _registry = new AssetRegistry(new EmptyLoader());
            _driver = new ScenePresentationPreviewDriver(_registry);
        }

        [TearDown]
        public void TearDown()
        {
            _driver.Dispose();
        }

        private static PresentationData CreateData(float totalDuration, params PresentationTrack[] tracks)
        {
            var data = ScriptableObject.CreateInstance<PresentationData>();
            data.Tracks = tracks;
            data.TotalDuration = totalDuration;
            data.Interruptible = true;
            return data;
        }

        [Test]
        public void Play_ReturnsValidHandle_AndIsPlaying()
        {
            var data = CreateData(1f, new PresentationTrack { Kind = TrackKind.Marker, Trigger = TrackTrigger.AtTime, Time = 0f, SignalKey = "go" });

            var handle = _driver.Play(data);

            Assert.IsTrue(_driver.IsPlaying);
            Assert.AreNotEqual(DDrive.Foundation.Handle.Handle<PresentationMarker>.Invalid, handle);
            UnityEngine.Object.DestroyImmediate(data);
        }

        [Test]
        public void Signal_AfterPlay_FiresOnSignalTrack_ReachesOnMarker()
        {
            var data = CreateData(5f, new PresentationTrack { Kind = TrackKind.Marker, Trigger = TrackTrigger.OnSignal, SignalKey = "hit" });
            _driver.Play(data);

            var received = new List<string>();
            using var sub = _driver.OnMarker.Subscribe(name => received.Add(name));

            _driver.Signal("hit");

            Assert.Contains("hit", received, "手動発火した Signal が Handle(OnMarker)へ届く");
            UnityEngine.Object.DestroyImmediate(data);
        }

        [Test]
        public void PlayContextSignalTrack_InvokesOnDataSignal()
        {
            // Kind=Signal はデータ→コード方向(PlayContext.OnSignal)。ドライバの OnDataSignal イベントに中継される。
            var data = CreateData(5f, new PresentationTrack { Kind = TrackKind.Signal, Trigger = TrackTrigger.OnSignal, SignalKey = "notify" });
            _driver.Play(data);

            string received = null;
            _driver.OnDataSignal += key => received = key;

            _driver.Signal("notify");

            Assert.AreEqual("notify", received);
            UnityEngine.Object.DestroyImmediate(data);
        }

        [Test]
        public void Tick_PastDuration_CompletesAndStopsPlaying()
        {
            var data = CreateData(0.05f, new PresentationTrack { Kind = TrackKind.Marker, Trigger = TrackTrigger.AtTime, Time = 0f, SignalKey = "start" });
            _driver.Play(data);

            var completed = false;
            using var sub = _driver.OnCompleted.Subscribe(_ => completed = true);

            _driver.Tick(0.1f);

            Assert.IsTrue(completed);
            Assert.IsFalse(_driver.IsPlaying);
            UnityEngine.Object.DestroyImmediate(data);
        }

        [Test]
        public void Cancel_StopsPlaying_AndFiresOnCancelled()
        {
            var data = CreateData(5f, new PresentationTrack { Kind = TrackKind.Marker, Trigger = TrackTrigger.OnSignal, SignalKey = "never" });
            _driver.Play(data);

            var cancelled = false;
            using var sub = _driver.OnCancelled.Subscribe(_ => cancelled = true);

            _driver.Cancel();

            Assert.IsTrue(cancelled);
            Assert.IsFalse(_driver.IsPlaying);
            UnityEngine.Object.DestroyImmediate(data);
        }

        [Test]
        public void SpawnModel_SetsHasSelf_ReleaseModel_ClearsIt()
        {
            var prefab = new GameObject("TestModelPrefab");
            var model = ScriptableObject.CreateInstance<ModelData>();
            model.Prefab = prefab;

            Assert.IsFalse(_driver.HasSelf);

            _driver.SpawnModel(model, Vector3.zero, Quaternion.identity);
            Assert.IsTrue(_driver.HasSelf, "SpawnModel 後は SelfRoot が非 null になる(ctx.Self として使える)");

            _driver.ReleaseModel();
            Assert.IsFalse(_driver.HasSelf, "ReleaseModel で配置物が撤去され Self が無くなる");

            UnityEngine.Object.DestroyImmediate(model);
            UnityEngine.Object.DestroyImmediate(prefab);
        }

        [Test]
        public void Play_WithSelfSpawned_UsesModelTransformAsSelf()
        {
            var prefab = new GameObject("TestModelPrefab2");
            var model = ScriptableObject.CreateInstance<ModelData>();
            model.Prefab = prefab;
            _driver.SpawnModel(model, new Vector3(1f, 2f, 3f), Quaternion.identity);

            var data = CreateData(1f, new PresentationTrack { Kind = TrackKind.Marker, Trigger = TrackTrigger.AtTime, Time = 0f, SignalKey = "go" });
            _driver.Play(data);

            Assert.IsTrue(_driver.IsPlaying);

            UnityEngine.Object.DestroyImmediate(data);
            UnityEngine.Object.DestroyImmediate(model);
            UnityEngine.Object.DestroyImmediate(prefab);
        }

        [Test]
        public void Dispose_StopsTicking_NoExceptionOnDoubleDispose()
        {
            var data = CreateData(5f, new PresentationTrack { Kind = TrackKind.Marker, Trigger = TrackTrigger.OnSignal, SignalKey = "x" });
            _driver.Play(data);

            _driver.Dispose();
            Assert.IsFalse(_driver.IsPlaying);

            // TearDown が再度 Dispose を呼ぶため、ここでの重複呼び出しでも例外にならないことを確認する。
            Assert.DoesNotThrow(() => _driver.Dispose());

            UnityEngine.Object.DestroyImmediate(data);
        }

        [Test]
        public void Play_Null_ReturnsInvalidHandle_NoThrow()
        {
            var handle = _driver.Play(null);
            Assert.AreEqual(DDrive.Foundation.Handle.Handle<PresentationMarker>.Invalid, handle);
        }
    }
}
