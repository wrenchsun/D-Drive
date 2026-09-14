using System.Collections;
using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Values;
using DDrive.Runtime.CameraShake;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ShakeId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.CameraShake.ShakeMarker>;

namespace DDrive.Tests.Runtime
{
    // [16_camera_haptics.md] Part A / [11_tasks.md] 5-2 — Trauma 合成の上限・GlobalScale・Stop の減衰・
    // Pause を検証する。実カタログ・実 GameData には触れず、Id はテスト専用のダミー値を使う。
    public class CameraFxManagerTests
    {
        private FakeAssetLoader _loader;
        private AssetRegistry _registry;
        private CameraFxManager _manager;
        private GameObject _cameraGo;
        private ulong _nextId = 950001;

        // P5 レビュー対応(2026-09-14): カメラ差し替えテスト用に追加生成した GameObject を
        // まとめて破棄するためのリスト(_cameraGo の階層とは別ルートになるため個別に管理する)。
        private readonly List<GameObject> _extraGameObjects = new();

        [SetUp]
        public void SetUp()
        {
            _loader = new FakeAssetLoader();
            _registry = new AssetRegistry(_loader);
            _manager = new CameraFxManager(_registry);

            _cameraGo = new GameObject("MainCamera_Test");
            _cameraGo.AddComponent<UnityEngine.Camera>();
            _cameraGo.tag = "MainCamera";
        }

        [TearDown]
        public void TearDown()
        {
            // P5 レビュー対応(2026-09-14) tests P2-3: Facade_UnboundCameraFx_... が CameraFx.Bind(null)
            // をテスト本体でしか呼んでいなかった(ScenePreloadTests/TuningTests の流儀に揃える)。
            CameraFx.Bind(null);

            if (_cameraGo != null)
            {
                // カメラを消す前にシェイクノードごと消えるよう、親子関係を辿って破棄する。
                var root = _cameraGo.transform.root;
                Object.DestroyImmediate(root.gameObject);
            }

            foreach (var go in _extraGameObjects)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go.transform.root.gameObject);
                }
            }

            _extraGameObjects.Clear();
        }

        private CameraShakeData CreateData(ulong id, Vector3 posAmplitude, int maxStack = 3, float traumaWeight = 1f, float durationSec = 0.3f)
        {
            var data = ScriptableObject.CreateInstance<CameraShakeData>();
            data.Id = id;
            data.Pattern = ShakePattern.Impulse; // 波形をなくして振幅の大小だけを検証しやすくする
            data.PosAmplitude = posAmplitude;
            data.RotAmplitude = Vector3.zero;
            data.MaxStack = maxStack;
            data.TraumaWeight = traumaWeight;
            var envelope = data.Envelope;
            envelope.Time = new TimeDef { Mode = TimeMode.Duration, Value = durationSec, SpeedScale = 1f };
            data.Envelope = envelope;
            return data;
        }

        // CameraFxManager はカメラ本体でなく、その直上に挿入した専用ノード(親)を揺らす([16] Part A)。
        // Tick() 実行後はカメラの親がそのノードになるため、オフセットはそちらの localPosition で見る。
        private Vector3 CurrentOffset() => _cameraGo.transform.parent != null ? _cameraGo.transform.parent.localPosition : Vector3.zero;

        [Test]
        public void ShakeData_SingleInstance_OffsetBoundedByAmplitude()
        {
            var data = CreateData(_nextId++, new Vector3(1f, 0f, 0f));
            _manager.ShakeData(data);

            _manager.Tick(0f); // Envelope(t=0) = From(1) なので shakeAmount はほぼ最大

            Assert.LessOrEqual(CurrentOffset().magnitude, 1.01f, "単一 Instance のオフセットは PosAmplitude を超えない");
        }

        [Test]
        public void ShakeData_ManyOverlappingInstances_DoesNotExceedMaxAmplitude()
        {
            // 多重発火で破綻しない(AC): 同じ振幅の Shake を MaxStack いっぱいまで積んでも、
            // 重み付き平均 × shakeAmount(<=1) の性質上、合成後のオフセットが単体の振幅を超えないことを確認する。
            var data = CreateData(_nextId++, new Vector3(1f, 0f, 0f), maxStack: 10, traumaWeight: 1f);

            for (var i = 0; i < 10; i++)
            {
                _manager.ShakeData(data);
            }

            Assert.AreEqual(10, _manager.ActiveCount);

            _manager.Tick(0f);

            Assert.LessOrEqual(CurrentOffset().magnitude, 1.01f, "10 重発火でも合成後オフセットは PosAmplitude(1)を大きく超えない");
        }

        [Test]
        public void ShakeData_MaxStackExceeded_IsIgnored()
        {
            var data = CreateData(_nextId++, Vector3.one, maxStack: 2);

            var h1 = _manager.ShakeData(data);
            var h2 = _manager.ShakeData(data);
            var h3 = _manager.ShakeData(data);

            Assert.IsTrue(_manager.IsPlaying(h1));
            Assert.IsTrue(_manager.IsPlaying(h2));
            Assert.AreEqual(Handle<ShakeMarker>.Invalid, h3, "MaxStack 超過は Invalid Handle を返す");
            Assert.AreEqual(2, _manager.ActiveCount, "MaxStack を超えた分は無視される");
        }

        [Test]
        public void SetGlobalScale_Zero_ProducesZeroOffset()
        {
            var data = CreateData(_nextId++, new Vector3(1f, 1f, 1f));
            _manager.ShakeData(data);
            _manager.SetGlobalScale(0f);

            _manager.Tick(0f);

            Assert.AreEqual(Vector3.zero, CurrentOffset(), "GlobalScale=0 は完全に無揺れ");
        }

        [Test]
        public void Stop_DecaysToZero_ThenRemovesInstance()
        {
            var data = CreateData(_nextId++, new Vector3(1f, 0f, 0f), durationSec: 10f); // 自然減衰では消えない尺
            var handle = _manager.ShakeData(data);
            _manager.Tick(0f);

            var beforeStop = CurrentOffset().magnitude;
            Assert.Greater(beforeStop, 0.5f);

            _manager.Stop(handle, 1f);
            _manager.Tick(0.5f); // フェード半分

            var mid = CurrentOffset().magnitude;
            Assert.Less(mid, beforeStop, "Stop 後は減衰していく");

            _manager.Tick(0.6f); // フェード完了

            Assert.IsFalse(_manager.IsPlaying(handle));
            Assert.AreEqual(Vector3.zero, CurrentOffset(), "フェード完了後はオフセット 0 に戻る");
        }

        [Test]
        public void OnPause_FreezesInstance_DoesNotAdvanceOrExpire()
        {
            var data = CreateData(_nextId++, Vector3.one, durationSec: 0.1f);
            data.Flags.Pause = PauseMode.PauseWithGame;
            var handle = _manager.ShakeData(data);

            _manager.OnPause(PauseChannel.Gameplay, true);
            _manager.Tick(1f); // Pause 中なので進行しないはず

            Assert.IsTrue(_manager.IsPlaying(handle), "Pause 中は Envelope の尺を超えても消えない");
        }

        // P5 レビュー第 1 弾 追加テスト(review1_tests.md「追加すべきテスト」⑧)— Pause 復帰で
        // 複数 Instance が正しく再合成されること(1 個だけの OnPause_FreezesInstance_... とは別に、
        // 2 個以上でも両方が合成に残ることを軸ごとに確認する)。
        [Test]
        public void OnPause_MultipleInstances_AllRecomposeAfterResume()
        {
            var dataA = CreateData(_nextId++, new Vector3(1f, 0f, 0f), durationSec: 5f, traumaWeight: 0.5f);
            var dataB = CreateData(_nextId++, new Vector3(0f, 1f, 0f), durationSec: 5f, traumaWeight: 0.5f);
            dataA.Flags.Pause = PauseMode.PauseWithGame;
            dataB.Flags.Pause = PauseMode.PauseWithGame;

            var handleA = _manager.ShakeData(dataA);
            var handleB = _manager.ShakeData(dataB);
            _manager.Tick(0f);
            Assert.AreEqual(2, _manager.ActiveCount);

            _manager.OnPause(PauseChannel.Gameplay, true);
            _manager.Tick(1f); // Pause 中は進行しない

            Assert.IsTrue(_manager.IsPlaying(handleA));
            Assert.IsTrue(_manager.IsPlaying(handleB));

            _manager.OnPause(PauseChannel.Gameplay, false);
            _manager.Tick(0f);

            Assert.AreEqual(2, _manager.ActiveCount, "Pause 復帰後も両方の Instance が残っている");
            var offset = CurrentOffset();
            Assert.Greater(offset.x, 0f, "A(X 軸)の寄与が復帰後の合成に含まれる");
            Assert.Greater(offset.y, 0f, "B(Y 軸)の寄与が復帰後の合成に含まれる");
        }

        [Test]
        public void StopAll_StopReason_ImmediatelyClearsAllInstances()
        {
            var data = CreateData(_nextId++, Vector3.one);
            _manager.ShakeData(data);
            _manager.ShakeData(data);

            _manager.StopAll(StopReason.SceneUnload);

            Assert.AreEqual(0, _manager.ActiveCount);
        }

        [Test]
        public void Facade_UnboundCameraFx_ShakeReturnsInvalidHandle_AndIsNoOp()
        {
            CameraFx.Bind(null);

            var handle = CameraFx.Shake(default(ShakeId));

            Assert.IsFalse(handle.IsPlaying());
            Assert.DoesNotThrow(() =>
            {
                handle.Stop();
                CameraFx.SetGlobalScale(0.5f);
                CameraFx.StopAll();
            });
        }

        // P5 レビュー対応(2026-09-14) P1-2 回帰テスト — Camera.main 差し替え検知時に旧ノードを破棄せず、
        // カメラを元の親に戻さない問題(review1_runtime.md #2)。A→B→A と切り替えても孤児ノードが残らず、
        // 両カメラとも元の親子構造(親 + Sibling Index)へ戻ることを確認する。
        // [UnityTest]にする理由: このテストは PlayMode(Tests/Runtime)で走るため、CameraFxManager が
        // Play Mode 用に呼ぶ `Object.Destroy`(Edit Mode の `DestroyImmediate` とは違い、実際の破棄は
        // フレーム末まで遅延される)の完了を観測するには最低 1 フレームの `yield return null` が必要。
        [UnityTest]
        public IEnumerator EnsureCameraNode_SwapAtoBtoA_RestoresBothCameras_AndLeavesNoOrphanNode()
        {
            var parentA = new GameObject("ParentA");
            _extraGameObjects.Add(parentA);
            _cameraGo.transform.SetParent(parentA.transform, false);
            var siblingUnderA = new GameObject("SiblingUnderA"); // Sibling Index を意味のある形で検証するための同居オブジェクト
            siblingUnderA.transform.SetParent(parentA.transform, false);
            _cameraGo.transform.SetSiblingIndex(0);
            siblingUnderA.transform.SetSiblingIndex(1);

            var cameraBGo = new GameObject("MainCamera_TestB");
            _extraGameObjects.Add(cameraBGo);
            var cameraB = cameraBGo.AddComponent<UnityEngine.Camera>();
            cameraBGo.tag = "MainCamera";
            var parentB = new GameObject("ParentB");
            _extraGameObjects.Add(parentB);
            cameraBGo.transform.SetParent(parentB.transform, false);
            cameraB.enabled = false; // まだ Camera.main には出さない(A を有効にしておく)

            var data = CreateData(_nextId++, Vector3.one);
            _manager.ShakeData(data);
            _manager.Tick(0f); // A にノードを挿入

            var nodeA1 = _cameraGo.transform.parent;
            Assert.IsNotNull(nodeA1);
            Assert.AreEqual("DDriveCameraShakeNode", nodeA1.name);
            Assert.AreEqual(parentA.transform, nodeA1.parent, "ノードは A の元の親の下に作られる");

            // A → B
            _cameraGo.GetComponent<UnityEngine.Camera>().enabled = false;
            cameraB.enabled = true;
            _manager.Tick(0f);
            yield return null; // Play Mode の Object.Destroy はフレーム末まで実際の破棄が遅延されるため 1 フレーム待つ

            Assert.AreEqual(parentA.transform, _cameraGo.transform.parent, "A は本来の親へ戻る");
            Assert.AreEqual(0, _cameraGo.transform.GetSiblingIndex(), "A の Sibling Index も復元される");
            Assert.IsTrue(nodeA1 == null, "A 用の旧ノードは破棄されている(Unity の破棄済み判定で null 相当)");

            var nodeB1 = cameraBGo.transform.parent;
            Assert.IsNotNull(nodeB1);
            Assert.AreEqual("DDriveCameraShakeNode", nodeB1.name);
            Assert.AreEqual(parentB.transform, nodeB1.parent);

            // B → A
            cameraB.enabled = false;
            _cameraGo.GetComponent<UnityEngine.Camera>().enabled = true;
            _manager.Tick(0f);
            yield return null; // 同上(B 用ノードの破棄を観測するために 1 フレーム待つ)

            Assert.AreEqual(parentB.transform, cameraBGo.transform.parent, "B も本来の親へ戻る");
            Assert.IsTrue(nodeB1 == null, "B 用の旧ノードも破棄されている");

            var nodeA2 = _cameraGo.transform.parent;
            Assert.IsNotNull(nodeA2);
            Assert.AreEqual("DDriveCameraShakeNode", nodeA2.name);
            Assert.AreEqual(parentA.transform, nodeA2.parent);

            // シーン内に "DDriveCameraShakeNode" が現在アタッチ中の 1 個だけであること(孤児ノードが残っていない)。
            var allTransforms = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
            var nodeCount = 0;
            foreach (var t in allTransforms)
            {
                if (t != null && t.name == "DDriveCameraShakeNode")
                {
                    nodeCount++;
                }
            }

            Assert.AreEqual(1, nodeCount, "孤児ノードが残っていない");
        }

        // P5 レビュー対応(2026-09-14) P2-3 — MaxStack<=0 は Validator の警告文言(「常に無視される」)に
        // Manager の挙動を揃える(以前は「無制限」と解釈していた)。
        [Test]
        public void ShakeData_MaxStackZeroOrNegative_AlwaysIgnored()
        {
            var zeroStackData = CreateData(_nextId++, Vector3.one, maxStack: 0);
            var negativeStackData = CreateData(_nextId++, Vector3.one, maxStack: -1);

            var h1 = _manager.ShakeData(zeroStackData);
            var h2 = _manager.ShakeData(negativeStackData);

            Assert.AreEqual(Handle<ShakeMarker>.Invalid, h1, "MaxStack=0 は常に無視される(Validator の警告文言と一致させる)");
            Assert.AreEqual(Handle<ShakeMarker>.Invalid, h2, "MaxStack<0 も同様に常に無視される");
            Assert.AreEqual(0, _manager.ActiveCount);
        }

        // P5 レビュー第 1 弾 追加テスト — CameraFx 再生中にカメラ(GameObject)自体が破棄された場合、
        // 次の Tick で例外にならず、Camera.main が見つからない no-op 経路へフォールバックすることを確認する。
        [Test]
        public void Tick_CameraDestroyedWhilePlaying_DoesNotThrow_AndBecomesNoOp()
        {
            var data = CreateData(_nextId++, Vector3.one, durationSec: 10f);
            _manager.ShakeData(data);
            _manager.Tick(0f); // ノードを挿入

            Object.DestroyImmediate(_cameraGo.transform.root.gameObject); // カメラ・ノードを一括破棄
            _cameraGo = null; // TearDown での二重破棄を避ける

            Assert.DoesNotThrow(() => _manager.Tick(0.1f));
            Assert.DoesNotThrow(() => _manager.Tick(0.1f)); // 続けて呼んでも安定して no-op
        }
    }
}
