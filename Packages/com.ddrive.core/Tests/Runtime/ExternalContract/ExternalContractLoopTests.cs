using System.Collections;
using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Loop;
using DDrive.Runtime.Model;
using ExternalPackage.Fake;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ExternalContract.Tests
{
    // [docs/42 §5.14] 外部拡張の契約(実行順 / ループ)。E-8(A-8)+ E-16(f27702e 由来)・E-9(A-9)。
    public class ExternalContractLoopTests
    {
        private const string FcShape = "FC_ext_Neutral_R0_C0";
        private const string SmileShape = "Smile";
        private const string OtherShape = "Other";

        private readonly List<Object> _cleanup = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _cleanup)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _cleanup.Clear();
        }

        private T Own<T>(T o) where T : Object
        {
            _cleanup.Add(o);
            return o;
        }

        // E-8 / E-16: D-Drive の AnimManager(GameLoop 経由 = Update)が AnimData の指定シェイプだけを書き、
        //   外部コンポーネント(実行順 10000 の LateUpdate)が書いた重みを次フレームの Update で上書きしない。
        //   同名のシェイプを外部が LateUpdate で書けば、そのフレームの最終値は外部の値になる。
        //   (名前が変わらないこと・プール往復・Despawn 後の復元は ExternalBlendShapeOwnershipTests / ModelsManagerReturnNotifyTests が固定済み)
        [UnityTest]
        public IEnumerator E8_AnimUpdateAndExternalLateUpdate_DoNotClobberEachOther_OverRealFrames()
        {
            var loader = new ExternalContractLoader();
            var registry = new AssetRegistry(loader);
            var pool = new PoolService();
            var anim = new AnimManager(registry);
            var models = new ModelsManager(pool, registry, anim);

            var mesh = Own(new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.up, Vector3.right },
                triangles = new[] { 0, 1, 2 },
            });
            var delta = new Vector3[3];
            delta[1] = Vector3.one;
            mesh.AddBlendShapeFrame(FcShape, 100f, delta, null, null);
            mesh.AddBlendShapeFrame(SmileShape, 100f, delta, null, null);
            mesh.AddBlendShapeFrame(OtherShape, 100f, delta, null, null);

            var prefab = Own(new GameObject("ExternalContractAnimPrefab"));
            prefab.AddComponent<Animator>();
            var body = new GameObject("Body");
            body.transform.SetParent(prefab.transform);
            body.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;

            var modelData = Own(ScriptableObject.CreateInstance<ModelData>());
            modelData.Id = 5;
            modelData.Prefab = prefab;
            modelData.Flags.Pool = PoolPolicy.Pooled(0, 4);

            var animData = Own(ScriptableObject.CreateInstance<AnimData>());
            animData.Id = 1;
            var clip = Own(new AnimationClip { legacy = true, frameRate = 30f });
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 30f, 1f));
            animData.Clip = clip;
            animData.BlendShapes = new[]
            {
                new BlendShapeTrack { ShapeName = SmileShape, Weight = AnimationCurve.Constant(0f, 30f, 20f) },
            };

            var driverGo = Own(new GameObject("ExternalContractDriver"));
            var driver = driverGo.AddComponent<GameLoopDriver>();
            driver.GameLoop.Register(anim);
            driver.GameLoop.Register(models);

            var handle = models.SpawnData(modelData, Vector3.zero, Quaternion.identity);
            var root = models.GetGameObject(handle);
            var smr = root.GetComponentInChildren<SkinnedMeshRenderer>();

            // 外部 Runner を模したコンポーネント 2 つ: FC_* を 100 に(D-Drive は触らない)、Smile を 77 に(同名 = LateUpdate 側が最終)。
            var fcWriter = root.AddComponent<ExternalLateBlendShapeWriter>();
            fcWriter.Smr = smr;
            fcWriter.WriteIndex = 0;
            fcWriter.WriteWeight = 100f;
            var smileWriter = root.AddComponent<ExternalLateBlendShapeWriter>();
            smileWriter.Smr = smr;
            smileWriter.WriteIndex = 1;
            smileWriter.WriteWeight = 77f;

            anim.PlayData(animData, root.GetComponent<Animator>());

            for (var i = 0; i < 8; i++)
            {
                yield return null;
            }

            Assert.GreaterOrEqual(fcWriter.LateCount, 6);

            // 最初の LateUpdate は D-Drive の Tick より前かもしれないため、2 回目以降を見る。
            for (var i = 1; i < fcWriter.SeenAtLateStart.Count; i++)
            {
                Assert.AreEqual(100f, fcWriter.SeenAtLateStart[i], "外部が LateUpdate で書いた FC_* を、次フレームの D-Drive の Update が上書きしない");
                Assert.AreEqual(20f, smileWriter.SeenAtLateStart[i], 0.01f, "AnimData が指定したシェイプは D-Drive が毎フレーム Update で書き直す(外部の前回の値を上書きする)");
            }

            Assert.AreEqual(0f, smr.GetBlendShapeWeight(2), "指定していないシェイプには触れない");
            Assert.AreEqual(FcShape, smr.sharedMesh.GetBlendShapeName(0));
            Assert.AreEqual(SmileShape, smr.sharedMesh.GetBlendShapeName(1));
            Assert.AreEqual(OtherShape, smr.sharedMesh.GetBlendShapeName(2));

            anim.StopAll(StopReason.Manual);
            models.Despawn(handle);
            pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(driverGo);
        }

        // E-9: 外部の IAssetManager を GameLoop.Register すると Tick / OnPause / StopAll / OnSceneUnload が届く(Unregister で止まる)。
        [UnityTest]
        public IEnumerator E9_ExternalAssetManager_RegisteredOnGameLoop_ReceivesTickPauseStopAndUnload()
        {
            var driverGo = new GameObject("ExternalContractLoopDriver");
            var driver = driverGo.AddComponent<GameLoopDriver>();
            var external = new ExternalCountingManager();
            driver.GameLoop.Register(external);
            driver.GameLoop.Register(external); // 二重登録は 1 回扱い

            yield return null;
            yield return null;
            Assert.GreaterOrEqual(external.TickCount, 1, "Update で Tick が届く");
            Assert.GreaterOrEqual(external.LastDt, 0f);

            driver.PauseService.Push(PauseChannel.Gameplay);
            Assert.AreEqual(1, external.PauseCount);
            Assert.AreEqual(PauseChannel.Gameplay, external.LastPauseChannel);
            Assert.IsTrue(external.LastPaused);
            driver.PauseService.Pop(PauseChannel.Gameplay);
            Assert.AreEqual(2, external.PauseCount);
            Assert.IsFalse(external.LastPaused);

            driver.GameLoop.StopAll(StopReason.GameOver);
            Assert.AreEqual(1, external.StopAllCount);
            Assert.AreEqual(StopReason.GameOver, external.LastStopReason);

            var ticksBefore = external.TickCount;
            driver.GameLoop.Unregister(external);
            yield return null;
            Assert.AreEqual(ticksBefore, external.TickCount, "Unregister 後は Tick が来ない");

            driver.GameLoop.Register(external);
            Object.DestroyImmediate(driverGo);
            Assert.AreEqual(1, external.SceneUnloadCount, "GameLoopDriver の破棄で OnSceneUnload が届く");
        }

        // E-9b(GA-R-01): 持ち込み先ガイドの案内どおり(OnEnable で Register・OnDisable で Unregister・Instance が null なら何もしない)に
        // 書いた外部 Manager に、Tick で dt が届き、HitStop(TimeService.TimeScale)が dt に反映され、破棄後は Tick されない。
        [UnityTest]
        public IEnumerator E9b_ConsumerGuidePattern_RegisterOnEnable_ReceivesScaledDt_AndUnregisterOnDisable()
        {
            // Bootstrap が無いときは何もしない(例外なし・登録なし)。
            var orphanGo = new GameObject("ExternalGameTimeOrphan");
            var orphan = orphanGo.AddComponent<ExternalGameTimeBehaviour>();
            Assert.IsFalse(orphan.Registered, "Instance が null のときは登録しない");
            Object.DestroyImmediate(orphanGo);

            var bootGo = new GameObject("ExternalGameTimeBootstrap");
            bootGo.SetActive(false);
            var bootstrap = bootGo.AddComponent<DDriveRuntimeBootstrap>();
            bootstrap.CatalogLabel = string.Empty;
            bootstrap.KeepAcrossScenes = false;
            bootGo.SetActive(true);
            Assert.AreSame(bootstrap, DDriveRuntimeBootstrap.Instance);

            var gameGo = new GameObject("ExternalGameTimeBehaviour");
            var game = gameGo.AddComponent<ExternalGameTimeBehaviour>();
            Assert.IsTrue(game.Registered, "Bootstrap があれば OnEnable で登録される(IsReady は不要)");

            yield return null;
            yield return null;
            Assert.GreaterOrEqual(game.TickCount, 1, "Tick が届く");
            Assert.Greater(game.LastDt, 0f, "通常時の dt は 0 より大きい");

            // HitStop(静止): dt が 0 になる。
            bootstrap.Loop.TimeService.HitStop(30f, 0f);
            yield return null;
            var ticksDuringStop = game.TickCount;
            yield return null;
            Assert.Greater(game.TickCount, ticksDuringStop, "HitStop 中も Tick は呼ばれる");
            Assert.AreEqual(0f, game.LastDt, "HitStop(静止)中の dt は 0");

            // HitStop(スロー): dt が unscaledDeltaTime * TimeScale になる。
            bootstrap.Loop.TimeService.HitStop(30f, 0.5f);
            yield return null;
            Assert.AreEqual(Time.unscaledDeltaTime * 0.5f, game.LastDt, 1e-4f, "スロー中の dt は unscaledDeltaTime × TimeScale");

            // 破棄(OnDisable)で解除され、以降は Tick されない。GameLoop 側の他の Tick は動き続ける。
            var loop = bootstrap.Loop;
            var ticksBefore = game.TickCount;
            Object.DestroyImmediate(gameGo);
            yield return null;
            yield return null;
            Assert.AreEqual(ticksBefore, game.TickCount, "OnDisable の Unregister 後は Tick が来ない");
            Assert.IsNotNull(loop, "GameLoop は動き続ける(例外で止まらない)");

            Object.DestroyImmediate(bootGo);
        }
    }
}
