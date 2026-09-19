using System.Collections;
using System.Collections.Generic;
using DDrive.Foundation.Event;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Values;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Cutscene.Tracks;
using DDrive.Runtime.Presentation;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace DDrive.Tests.Runtime
{
    // [26_timeline.md] §4.3/§4.4/§4.6(6-10b) — D-Drive Timeline トラック群(Event/Signal/Shake/Haptic
    // マーカー、Camera クリップ)の検証。SE/VFX/AnchorGroup/UI/Presentation クリップは静的ファサード経由の
    // ため実 Manager の Bind が要り、ここでは対象外(手動確認 = docs/23 系に委ねる、6-10d)。
    public class CutsceneTimelineTracksTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _created.Clear();
        }

        private TimelineAsset CreateTimeline(double durationSeconds)
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _created.Add(timeline);
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = durationSeconds;
            return timeline;
        }

        private CutsceneData CreateData(TimelineAsset timeline)
        {
            var data = ScriptableObject.CreateInstance<CutsceneData>();
            _created.Add(data);
            data.Timeline = timeline;
            return data;
        }

        // ── D-Drive Event マーカー ──

        [Test]
        public void EventMarker_RaisesAssetEvent_WhenElapsedCrosses()
        {
            var timeline = CreateTimeline(1.0);
            var track = timeline.CreateTrack<CutsceneEventTrack>(null, "Event");
            var marker = track.CreateMarker<CutsceneEventNotification>(0.3);
            marker.Event = new AssetEvent { Action = EventAction.SendMessage, CustomKey = "hello" };

            var manager = new CutsceneManager(new AssetRegistry(new FakeAssetLoader()));
            var data = CreateData(timeline);

            AssetEvent? received = null;
            manager.Events.OnEventFired += (_, evt) => received = evt;

            var handle = manager.PlayData(data, new PlayContext());
            manager.Tick(0.2f);
            Assert.IsNull(received, "0.3 秒より前では発火しない");

            manager.Tick(0.2f); // elapsed=0.4 > 0.3
            Assert.IsNotNull(received);
            Assert.AreEqual("hello", received.Value.CustomKey);

            manager.Cancel(handle);
        }

        // ── D-Drive Signal マーカー ──

        [Test]
        public void SignalMarker_FiresOnMarker_OnceWhenElapsedCrosses()
        {
            var timeline = CreateTimeline(1.0);
            var track = timeline.CreateTrack<CutsceneSignalTrack>(null, "Signal");
            var marker = track.CreateMarker<CutsceneSignalNotification>(0.3);
            marker.Key = "cutscene/hit";

            var manager = new CutsceneManager(new AssetRegistry(new FakeAssetLoader()));
            var data = CreateData(timeline);

            var received = new List<string>();
            var handle = manager.PlayData(data, new PlayContext());
            manager.OnMarker(handle).Subscribe(k => received.Add(k));

            manager.Tick(0.5f);
            CollectionAssert.AreEqual(new[] { "cutscene/hit" }, received);

            manager.Tick(0.4f); // まだ 1.0 未満、同じマーカーを再発火しないこと
            CollectionAssert.AreEqual(new[] { "cutscene/hit" }, received);
        }

        [Test]
        public void SkipToMarker_SeeksToMarkerTime_NotDuration_AndDoesNotRefire()
        {
            var timeline = CreateTimeline(2.0);
            var track = timeline.CreateTrack<CutsceneSignalTrack>(null, "Signal");
            var marker = track.CreateMarker<CutsceneSignalNotification>(1.0);
            marker.Key = "cutscene/hit";

            var manager = new CutsceneManager(new AssetRegistry(new FakeAssetLoader()));
            var data = CreateData(timeline);
            data.Skip = CutsceneSkip.ToMarker;
            data.SkipToMarkerKey = "cutscene/hit";

            var received = new List<string>();
            var handle = manager.PlayData(data, new PlayContext());
            manager.OnMarker(handle).Subscribe(k => received.Add(k));

            manager.Skip(handle);

            Assert.IsTrue(manager.IsPlaying(handle), "マーカー(1.0秒)は尺(2.0秒)より前なので再生は継続する");
            Assert.AreEqual(0.5f, manager.GetNormalizedTime(handle), 0.0001f);
            // Skip は「跨いだ」マーカーを無音でスキップする(スクラブと同じ扱い、TL;DR「連打・残留しない」)。
            CollectionAssert.IsEmpty(received);

            manager.Cancel(handle);
        }

        [Test]
        public void SkipToMarker_MarkerNotFound_FallsBackToImmediate()
        {
            var timeline = CreateTimeline(2.0);
            var manager = new CutsceneManager(new AssetRegistry(new FakeAssetLoader()));
            var data = CreateData(timeline);
            data.Skip = CutsceneSkip.ToMarker;
            data.SkipToMarkerKey = "cutscene/does_not_exist";

            var handle = manager.PlayData(data, new PlayContext());
            manager.Skip(handle);
            manager.Tick(0.001f); // Skip() 自体は Seek するだけで、完了判定は次の Tick() で行われる

            Assert.IsFalse(manager.IsPlaying(handle), "見つからない場合は末尾(Immediate 相当)まで飛ぶ");
        }

        // ── D-Drive Shake/Haptic マーカー(未 Bind でも例外にならない、no-op で継続) ──

        [Test]
        public void ShakeAndHapticMarkers_DoNotThrow_WhenFacadesUnbound()
        {
            var timeline = CreateTimeline(1.0);
            var shakeTrack = timeline.CreateTrack<CutsceneShakeTrack>(null, "Shake");
            var shakeMarker = shakeTrack.CreateMarker<CutsceneShakeNotification>(0.2);
            shakeMarker.ShakeId = new AssetId<DDrive.Runtime.CameraShake.ShakeMarker>(123UL, AssetType.Shake);

            var hapticTrack = timeline.CreateTrack<CutsceneHapticTrack>(null, "Haptic");
            var hapticMarker = hapticTrack.CreateMarker<CutsceneHapticNotification>(0.4);
            hapticMarker.HapticId = new AssetId<DDrive.Runtime.Haptics.HapticMarker>(456UL, AssetType.Haptics);

            var manager = new CutsceneManager(new AssetRegistry(new FakeAssetLoader()));
            var data = CreateData(timeline);

            var handle = manager.PlayData(data, new PlayContext());
            Assert.DoesNotThrow(() =>
            {
                manager.Tick(0.5f);
                manager.Tick(0.6f);
            });

            Assert.IsFalse(manager.IsPlaying(handle));
        }

        // ── Camera クリップ(StepFps 量子化・カメラのみコマ落ち・終了後の書き戻し) ──

        [UnityTest]
        public IEnumerator CameraClip_StepFpsQuantizesPosition_AndRestoresFocusDistanceAfterEnd()
        {
            var camGo = new GameObject("DDriveTestMainCamera", typeof(Camera)) { tag = "MainCamera" };
            _created.Add(camGo);
            var cam = camGo.GetComponent<Camera>();
            cam.focusDistance = 10f;

            var timeline = CreateTimeline(1.0);
            var track = timeline.CreateTrack<CutsceneCameraTrack>(null, "Camera");
            var clip = track.CreateClip<CutsceneCameraClip>();
            clip.duration = 1.0;
            var camAsset = (CutsceneCameraClip)clip.asset;
            camAsset.PosX = AnimationCurve.Linear(0f, 0f, 1f, 10f);
            camAsset.StepFps = 2f; // ステップ 0 / 0.5 秒
            camAsset.BlendIn = ValueDef.Constant01(1f); // Duration=0 → 常に w=1(ブレンド無し)
            camAsset.BlendOut = ValueDef.Constant01(1f);
            camAsset.Focus = CameraFocusMode.CameraOnly; // Volume/URP セットアップ無しで検証できるように
            camAsset.FocusDistance = AnimationCurve.Constant(0f, 1f, 5f);

            var manager = new CutsceneManager(new AssetRegistry(new FakeAssetLoader()));
            var data = CreateData(timeline);
            data.Origin = CutsceneOrigin.World;

            var handle = manager.PlayData(data, new PlayContext());

            manager.Tick(0.2f); // elapsed=0.2 → quantized 0.0
            yield return new WaitForEndOfFrame();
            Assert.AreEqual(0f, cam.transform.position.x, 0.01f, "StepFps=2 のステップ前(0.2 秒)は 0 のまま");

            manager.Tick(0.35f); // elapsed=0.55 → quantized 0.5 → x=5
            yield return new WaitForEndOfFrame();
            Assert.AreEqual(5f, cam.transform.position.x, 0.01f, "0.5 秒のステップに入ったら 5 へジャンプする(なめらかに補間しない)");
            Assert.AreEqual(5f, cam.focusDistance, 0.01f, "Focus=CameraOnly は Camera.focusDistance に書く");

            manager.Tick(0.6f); // elapsed=1.15 >= duration=1.0 → Complete → Cleanup で復元
            yield return new WaitForEndOfFrame();

            Assert.IsFalse(manager.IsPlaying(handle));
            Assert.AreEqual(10f, cam.focusDistance, 0.01f, "再生終了後は再生開始時の focusDistance に書き戻る([26_timeline.md] §4.6.2)");
        }

        // ── docs/45 P1-1(2026-09-20): Cancel で返却した Director のカメラ状態が残らないこと ──

        [UnityTest]
        public IEnumerator Cancel_MidCameraClip_DoesNotLeakCameraStateToNextCameraLessCutscene()
        {
            var camGo = new GameObject("DDriveTestMainCamera3", typeof(Camera)) { tag = "MainCamera" };
            _created.Add(camGo);
            var cam = camGo.GetComponent<Camera>();

            var timelineA = CreateTimeline(2.0);
            var camTrack = timelineA.CreateTrack<CutsceneCameraTrack>(null, "Camera");
            var camClip = camTrack.CreateClip<CutsceneCameraClip>();
            camClip.duration = 2.0;
            var camAsset = (CutsceneCameraClip)camClip.asset;
            camAsset.PosX = AnimationCurve.Linear(0f, 0f, 2f, 20f);
            camAsset.BlendIn = ValueDef.Constant01(1f);
            camAsset.BlendOut = ValueDef.Constant01(1f);
            camAsset.Focus = CameraFocusMode.Off;

            var manager = new CutsceneManager(new AssetRegistry(new FakeAssetLoader()));
            var dataA = CreateData(timelineA);
            dataA.Origin = CutsceneOrigin.World;

            var handleA = manager.PlayData(dataA, new PlayContext());
            manager.Tick(0.5f); // クリップの途中(尺 2.0 秒の 0.5 秒地点)。
            yield return new WaitForEndOfFrame();
            Assert.AreNotEqual(0f, cam.transform.position.x, "前提: カメラクリップ中はカメラが動くこと");

            // 再現条件([26_timeline.md] §4.6.2 / docs/45 P1-1): クリップ区間の「途中」で Cancel する
            // (Evaluate() を挟まずに Cleanup → ReturnDirector する経路)。
            manager.Cancel(handleA);
            yield return new WaitForEndOfFrame();

            // ゲーム側が Cutscene 終了後に別の場所へカメラを動かした状況を作る(Applier.Restore() は
            // 位置・回転を戻さない仕様〔P2-3、別チケット〕なので、素の位置のままでは判定できないため)。
            cam.transform.position = new Vector3(100f, 0f, 0f);

            // カメラトラックを持たない Cutscene B。free-list は LIFO のため、A が返却した同じ
            // CutsceneRoot(と CutsceneCameraStateHolder)を借りる。
            var timelineB = CreateTimeline(1.0);
            var eventTrack = timelineB.CreateTrack<CutsceneEventTrack>(null, "Event");
            var marker = eventTrack.CreateMarker<CutsceneEventNotification>(0.9);
            marker.Event = new AssetEvent { Action = EventAction.SendMessage, CustomKey = "noop" };

            var dataB = CreateData(timelineB);
            dataB.Origin = CutsceneOrigin.World;

            var handleB = manager.PlayData(dataB, new PlayContext());

            for (var i = 0; i < 5; i++)
            {
                manager.Tick(0.1f);
                yield return new WaitForEndOfFrame();
            }

            Assert.AreEqual(100f, cam.transform.position.x, 0.01f,
                "カメラトラックを持たない Cutscene の再生中に、前回の Cutscene が残したカメラ状態(HasData)を" +
                "引き継いでカメラを動かしてはならない(docs/45 P1-1)");

            manager.Cancel(handleB);
        }

        // ── docs/45 P1-2(2026-09-20): ピント距離カーブが空なら Focus=Volume でも DoF を書かない(二重防御) ──

        [UnityTest]
        public IEnumerator CameraClip_FocusVolume_EmptyFocusDistanceCurve_DoesNotWriteDoF()
        {
            var camGo = new GameObject("DDriveTestMainCamera4", typeof(Camera)) { tag = "MainCamera" };
            _created.Add(camGo);
            var cam = camGo.GetComponent<Camera>();
            cam.focusDistance = 10f;

            var timeline = CreateTimeline(1.0);
            var track = timeline.CreateTrack<CutsceneCameraTrack>(null, "Camera");
            var clip = track.CreateClip<CutsceneCameraClip>();
            clip.duration = 1.0;
            var camAsset = (CutsceneCameraClip)clip.asset;
            camAsset.PosX = AnimationCurve.Constant(0f, 1f, 1f);
            camAsset.BlendIn = ValueDef.Constant01(1f);
            camAsset.BlendOut = ValueDef.Constant01(1f);
            // 取り込みが誤って(または旧データが)Focus=Volume のまま・ピント距離カーブが空(取れなかった)
            // というケース([26_timeline.md] §4.6.4「取れなければ書かない」の二重防御)。
            camAsset.Focus = CameraFocusMode.Volume;
            camAsset.FocusDistance = new AnimationCurve();

            var manager = new CutsceneManager(new AssetRegistry(new FakeAssetLoader()));
            var data = CreateData(timeline);
            data.Origin = CutsceneOrigin.World;

            var handle = manager.PlayData(data, new PlayContext());

            manager.Tick(0.5f);
            yield return new WaitForEndOfFrame();

            Assert.AreEqual(10f, cam.focusDistance, 0.01f,
                "ピント距離カーブが空(評価結果 0)なら Camera.focusDistance も書き換えない(docs/45 P1-2 二重防御)");
            Assert.IsNull(camGo.transform.Find("DDriveCutsceneVolume"),
                "ピント距離カーブが空なら Volume(DoF)自体を作らない(docs/45 P1-2 二重防御)");

            manager.Cancel(handle);
        }

        // ── DDriveCutsceneCameraApplier の実行順契約(検出2): 実行順 1001 の LateUpdate で上書きされたら警告 ──

        [UnityTest]
        public IEnumerator Applier_DetectsOverwrite_WhenLaterScriptWritesCameraInLateUpdate()
        {
            var camGo = new GameObject("DDriveTestMainCamera2", typeof(Camera)) { tag = "MainCamera" };
            _created.Add(camGo);
            var cam = camGo.GetComponent<Camera>();
            var overwriter = camGo.AddComponent<CutsceneCameraOverwriteProbe>();
            overwriter.OffsetX = 999f;

            var timeline = CreateTimeline(1.0);
            var track = timeline.CreateTrack<CutsceneCameraTrack>(null, "Camera");
            var clip = track.CreateClip<CutsceneCameraClip>();
            clip.duration = 1.0;
            var camAsset = (CutsceneCameraClip)clip.asset;
            camAsset.PosX = AnimationCurve.Constant(0f, 1f, 1f);
            camAsset.BlendIn = ValueDef.Constant01(1f);
            camAsset.BlendOut = ValueDef.Constant01(1f);
            camAsset.Focus = CameraFocusMode.Off;

            var manager = new CutsceneManager(new AssetRegistry(new FakeAssetLoader()));
            var data = CreateData(timeline);
            data.Origin = CutsceneOrigin.World;

            var handle = manager.PlayData(data, new PlayContext());

            // 検出2([26_timeline.md] §4.6.5)は再生 1 回につき警告 1 回だけ出す。5 回上書きされても
            // ログは 1 件のみ期待する。
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("上書きされました"));

            for (var i = 0; i < 5; i++)
            {
                manager.Tick(0.1f);
                yield return new WaitForEndOfFrame();
            }

            Assert.GreaterOrEqual(overwriter.LateUpdateCallCount, 1, "実行順 1001 のテスト用スクリプトが LateUpdate で呼ばれていること");

            manager.Cancel(handle);
            LogAssert.NoUnexpectedReceived();
        }
    }

    // Applier(実行順 1000)より後(1001)で Camera を書き込む、契約違反(G-1)を模した確認用スクリプト。
    // 検出2([26_timeline.md] §4.6.5)が「描画に使われた姿勢」と Applier の書き込みの不一致を検出できることを
    // 確認するためのテスト専用コンポーネント(本体コードではない)。
    [UnityEngine.DefaultExecutionOrder(DDriveCutsceneCameraApplier.ExecutionOrder + 1)]
    internal sealed class CutsceneCameraOverwriteProbe : MonoBehaviour
    {
        public float OffsetX;
        public int LateUpdateCallCount;

        private void LateUpdate()
        {
            LateUpdateCallCount++;
            var t = transform;
            t.position = new Vector3(OffsetX, t.position.y, t.position.z);
        }
    }
}
