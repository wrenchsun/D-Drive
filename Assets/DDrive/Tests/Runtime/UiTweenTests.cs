using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Data;
using DDrive.Foundation.Easing;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Validation;
using DDrive.Foundation.Values;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace DDrive.Tests.Runtime
{
    // [15_ui_interaction.md] B-3/B-5 — チケット 4-8(UiTween エンジン)+ 4-11 前半(UiPreset)。
    // 全て manager.Tick(dt) を直接叩く同期テスト([12_review.md] §3: Tick は決定的な dt を受け取るだけで
    // UnityEngine.Time を読まない設計にしてある。実装メモは UiTweenManager.Tick 参照)。
    public class UiTweenTests
    {
        private AssetRegistry _registry;
        private UiTweenManager _manager;

        [SetUp]
        public void SetUp()
        {
            _registry = new AssetRegistry(new FakeAssetLoader());
            _manager = new UiTweenManager(_registry);
        }

        private static RectTransform CreateRect(string name = "Target")
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(100f, 40f);

            var parentGo = new GameObject("Parent", typeof(RectTransform));
            var parentRt = (RectTransform)parentGo.transform;
            parentRt.sizeDelta = new Vector2(800f, 600f);
            rt.SetParent(parentRt, false);

            return rt;
        }

        private static UiTweenData CreateData(params TweenTrack[] tracks)
        {
            var data = ScriptableObject.CreateInstance<UiTweenData>();
            data.Tracks = tracks;
            return data;
        }

        private static TweenTrack MoveTrack(float duration, Vector2 from, Vector2 to, Ease ease = Ease.Linear, float delay = 0f, LoopMode loop = LoopMode.Once)
        {
            return new TweenTrack
            {
                Property = TweenProperty.AnchoredPosition,
                Motion = new ValueDef { Mode = ValueMode.Parametric, Parametric = EaseDef.Named(ease), Time = TimeDef.Duration(duration), Loop = loop },
                Delay = delay,
                From = TweenFromMode.Absolute,
                FromValue = new ParamValue { Type = ParamValueType.Vector, VectorValue = new Vector4(from.x, from.y, 0f, 0f) },
                ToValue = new ParamValue { Type = ParamValueType.Vector, VectorValue = new Vector4(to.x, to.y, 0f, 0f) },
            };
        }

        [Test]
        public void MoveTo_ReachesTarget_AndCompletes()
        {
            var rt = CreateRect();
            var handle = _manager.MoveTo(rt, new Vector2(100f, 0f), 1f, Ease.Linear);

            _manager.Tick(1f);

            Assert.AreEqual(100f, rt.anchoredPosition.x, 0.01f);
            Assert.IsFalse(_manager.IsPlaying(handle));
        }

        [Test]
        public void Fade_ReachesTargetAlpha()
        {
            var rt = CreateRect();
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            var fadeHandle = _manager.Fade(group, 1f, 0.5f, Ease.Linear);
            _manager.Tick(0.5f);

            Assert.AreEqual(1f, group.alpha, 0.01f);
            Assert.IsFalse(_manager.IsPlaying(fadeHandle));
        }

        [Test]
        public void PlayData_AlphaTrack_AddsCanvasGroup_WhenMissing()
        {
            var rt = CreateRect();
            Assert.IsNull(rt.GetComponent<CanvasGroup>());

            var data = CreateData(new TweenTrack
            {
                Property = TweenProperty.Alpha,
                Motion = new ValueDef { Mode = ValueMode.Parametric, Parametric = EaseDef.Named(Ease.Linear), Time = TimeDef.Duration(0.5f), Loop = LoopMode.Once },
                From = TweenFromMode.Absolute,
                FromValue = ParamValue.Of(0f),
                ToValue = ParamValue.Of(1f),
            });

            var handle = _manager.PlayData(data, rt);
            var group = rt.GetComponent<CanvasGroup>();
            Assert.IsNotNull(group, "Alpha Track は CanvasGroup を自動追加するはず");

            _manager.Tick(0.5f);
            Assert.AreEqual(1f, group.alpha, 0.01f);
            Assert.IsFalse(_manager.IsPlaying(handle));
        }

        [Test]
        public void Scale_WithOutBack_OvershootsThenLandsAtTarget()
        {
            var rt = CreateRect();
            rt.localScale = Vector3.zero;
            var handle = _manager.Scale(rt, 1f, 1f, Ease.OutBack);

            var maxScale = 0f;
            for (var i = 0; i < 20; i++)
            {
                _manager.Tick(0.05f);
                maxScale = Mathf.Max(maxScale, rt.localScale.x);
            }

            Assert.Greater(maxScale, 1f, "OutBack はオーバーシュートするはず");
            Assert.AreEqual(1f, rt.localScale.x, 0.01f);
            Assert.IsFalse(_manager.IsPlaying(handle));
        }

        [Test]
        public void PathMove_FollowsSplinePathEndpoints()
        {
            var rt = CreateRect();
            var points = new[] { new Vector3(0f, 0f, 0f), new Vector3(50f, 0f, 0f), new Vector3(100f, 50f, 0f) };
            var path = new SplinePath(points, SplineType.CatmullRom);

            var handle = _manager.MoveAlong(rt, path, 1f, Ease.Linear);
            _manager.Tick(0f);
            Assert.AreEqual(points[0].x, rt.anchoredPosition.x, 0.5f);

            _manager.Tick(1f);
            Assert.AreEqual(points[2].x, rt.anchoredPosition.x, 0.5f);
            Assert.AreEqual(points[2].y, rt.anchoredPosition.y, 0.5f);
            Assert.IsFalse(_manager.IsPlaying(handle));
        }

        [Test]
        public void Delay_HoldsStartValue()
        {
            var rt = CreateRect();
            var data = CreateData(MoveTrack(1f, Vector2.zero, new Vector2(100f, 0f), Ease.Linear, delay: 0.5f));

            var handle = _manager.PlayData(data, rt);
            _manager.Tick(0.3f);

            Assert.AreEqual(0f, rt.anchoredPosition.x, 0.01f);
            Assert.IsTrue(_manager.IsPlaying(handle));
        }

        [Test]
        public void LoopTween_KeepsPlayingUntilStop()
        {
            var rt = CreateRect();
            var data = CreateData(MoveTrack(0.2f, Vector2.zero, new Vector2(10f, 0f), Ease.Linear, loop: LoopMode.PingPong));

            var handle = _manager.PlayData(data, rt);
            for (var i = 0; i < 50; i++)
            {
                _manager.Tick(0.1f);
            }

            Assert.IsTrue(_manager.IsPlaying(handle), "無限ループは Stop するまで完了しない");

            _manager.Stop(handle);
            Assert.IsFalse(_manager.IsPlaying(handle));
        }

        [Test]
        public void Stop_WithComplete_JumpsToEndState()
        {
            var rt = CreateRect();
            var handle = _manager.MoveTo(rt, new Vector2(100f, 0f), 10f, Ease.Linear);

            _manager.Tick(0.1f); // ごく僅かにしか進んでいない状態
            Assert.Less(rt.anchoredPosition.x, 99f);

            _manager.Stop(handle, complete: true);

            Assert.AreEqual(100f, rt.anchoredPosition.x, 0.01f);
            Assert.IsFalse(_manager.IsPlaying(handle));
        }

        [Test]
        public void PlayData_WithTwoTracks_RunsInParallel()
        {
            var rt = CreateRect();
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            var data = CreateData(
                MoveTrack(1f, Vector2.zero, new Vector2(100f, 0f), Ease.Linear),
                new TweenTrack
                {
                    Property = TweenProperty.Alpha,
                    Motion = new ValueDef { Mode = ValueMode.Parametric, Parametric = EaseDef.Named(Ease.Linear), Time = TimeDef.Duration(1f), Loop = LoopMode.Once },
                    From = TweenFromMode.Absolute,
                    FromValue = ParamValue.Of(0f),
                    ToValue = ParamValue.Of(1f),
                });

            var handle = _manager.PlayData(data, rt);
            _manager.Tick(0.5f);

            Assert.AreEqual(50f, rt.anchoredPosition.x, 0.5f);
            Assert.AreEqual(0.5f, group.alpha, 0.01f);

            _manager.Tick(0.5f);
            Assert.IsFalse(_manager.IsPlaying(handle));
        }

        [Test]
        public void WaitAsync_CompletesAfterTickingToEnd()
        {
            var rt = CreateRect();
            var handle = _manager.MoveTo(rt, new Vector2(10f, 0f), 0.5f, Ease.Linear);

            var task = _manager.WaitAsync(handle);
            Assert.IsFalse(task.Status == UniTaskStatus.Succeeded);

            _manager.Tick(0.5f);

            Assert.IsTrue(task.Status == UniTaskStatus.Succeeded);
        }

        [Test]
        public void OnPause_RespectsDataFlags()
        {
            var rt = CreateRect();
            var data = CreateData(MoveTrack(1f, Vector2.zero, new Vector2(100f, 0f), Ease.Linear));
            data.Flags.Pause = PauseMode.PauseWithGame;

            var handle = _manager.PlayData(data, rt);
            _manager.OnPause(PauseChannel.Gameplay, true);
            _manager.Tick(1f);

            Assert.AreEqual(0f, rt.anchoredPosition.x, 0.01f, "PauseWithGame の Tween はポーズ中に進んではいけない");

            _manager.OnPause(PauseChannel.Gameplay, false);
            _manager.Tick(1f);
            Assert.IsFalse(_manager.IsPlaying(handle));
        }

        [Test]
        public void OnPause_DoesNotAffectAdHocTweens()
        {
            var rt = CreateRect();
            var handle = _manager.MoveTo(rt, new Vector2(100f, 0f), 1f, Ease.Linear);

            _manager.OnPause(PauseChannel.Gameplay, true);
            _manager.Tick(1f);

            Assert.IsFalse(_manager.IsPlaying(handle), "アドホック Tween はポーズに追従しない");
        }

        [Test]
        public void UnregisteredId_UsesPlaceholder_WithoutThrowing()
        {
            var rt = CreateRect();
            var id = new DDrive.Foundation.Identity.AssetId<UiTweenMarker>(999999UL, DDrive.Foundation.Identity.AssetType.UiTween);

            Handle<UiTweenMarker> handle = default;
            Assert.DoesNotThrow(() => handle = _manager.Play(id, rt));
            Assert.IsFalse(_manager.IsPlaying(handle), "空トラックのプレースホルダは即完了する");
        }

        // ── プリセット ──

        [Test]
        public void Preset_FadeIn_EndsAtAlphaOne()
        {
            var rt = CreateRect();
            var buffer = new TweenTrack[UiTweenManager.MaxTracksPerTween];
            var p = new UiPresetRef { Preset = UiPreset.FadeIn, Duration = 0.2f };
            var count = UiPresetFactory.Build(in p, rt, buffer);

            Assert.AreEqual(1, count);
            var handle = _manager.PlayTracks(buffer, count, rt);
            var group = rt.GetComponent<CanvasGroup>();
            Assert.IsNotNull(group);

            _manager.Tick(0.2f);
            Assert.AreEqual(1f, group.alpha, 0.01f);
            Assert.IsFalse(_manager.IsPlaying(handle));
        }

        [Test]
        public void Preset_SlideInLeft_StartsOffScreen()
        {
            var rt = CreateRect();
            var startX = rt.anchoredPosition.x;

            var buffer = new TweenTrack[UiTweenManager.MaxTracksPerTween];
            var p = new UiPresetRef { Preset = UiPreset.SlideInLeft, Duration = 1f };
            var count = UiPresetFactory.Build(in p, rt, buffer);
            Assert.GreaterOrEqual(count, 1);

            _manager.PlayTracks(buffer, count, rt);
            _manager.Tick(0f); // t=0 は開始値そのもの

            Assert.Less(rt.anchoredPosition.x, startX - 100f, "SlideInLeft は画面外(左)から始まるはず");
        }

        [Test]
        public void IsApproximation_ReportsMappedPresets()
        {
            Assert.IsTrue(UiPresetFactory.IsApproximation(UiPreset.FlipInX));
            Assert.IsFalse(UiPresetFactory.IsApproximation(UiPreset.FadeIn));
        }

        // ── Validator ──

        [Test]
        public void Validator_EmptyTracks_Warns()
        {
            var data = CreateData();
            var results = new List<ValidationResult>(new UiTweenDataValidator().Validate(data, new ValidationContext(new List<AssetDataBase>())));
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning));
        }

        [Test]
        public void Validator_PathMoveWithoutPoints_Errors()
        {
            var data = CreateData(new TweenTrack
            {
                Property = TweenProperty.PathMove,
                Motion = new ValueDef { Mode = ValueMode.Parametric, Parametric = EaseDef.Named(Ease.Linear), Time = TimeDef.Duration(1f) },
            });

            var results = new List<ValidationResult>(new UiTweenDataValidator().Validate(data, new ValidationContext(new List<AssetDataBase>())));
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error));
        }

        [Test]
        public void Validator_ZeroDurationLoop_Warns()
        {
            var data = CreateData(new TweenTrack
            {
                Property = TweenProperty.Alpha,
                Motion = new ValueDef { Mode = ValueMode.Parametric, Parametric = EaseDef.Named(Ease.Linear), Time = TimeDef.Duration(0f), Loop = LoopMode.Loop },
            });

            var results = new List<ValidationResult>(new UiTweenDataValidator().Validate(data, new ValidationContext(new List<AssetDataBase>())));
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning));
        }

        // ── UiButton 統合 ──

        [Test]
        public void UiButton_PunchScalePreset_StartsTweenWhenBound()
        {
            UiFx.Bind(_manager);
            try
            {
                var go = new GameObject("Btn", typeof(RectTransform), typeof(Image), typeof(UiButton));
                var button = go.GetComponent<UiButton>();
                button.TargetGraphic = go.GetComponent<Image>();

                var skin = ScriptableObject.CreateInstance<ButtonSkinData>();
                skin.Pressed = StateVisual.Default;
                skin.Pressed.EnterPreset = new UiPresetRef { Preset = UiPreset.PunchScale, Duration = 0.3f };
                button.SetVisual(skin);

                button.Press();

                Assert.Greater(_manager.ActiveCount, 0, "Pressed 状態の EnterPreset が Tween を開始するはず");
            }
            finally
            {
                UiFx.Bind(null);
            }
        }

        // ── 0 alloc ──

        [Test]
        public void Tick_EightRunningTweens_AllocatesNothing()
        {
            var targets = new RectTransform[8];
            for (var i = 0; i < targets.Length; i++)
            {
                targets[i] = CreateRect("ZeroAllocTarget" + i);
                _manager.MoveTo(targets[i], new Vector2(500f, 0f), 100f, Ease.Linear); // 100 Tick では終わらない長さ
            }

            // ウォームアップ(JIT/初回配列拡張を先に済ませる)。
            for (var i = 0; i < 5; i++)
            {
                _manager.Tick(0.016f);
            }

            var before = System.GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++)
            {
                _manager.Tick(0.016f);
            }

            var after = System.GC.GetAllocatedBytesForCurrentThread();
            var delta = after - before;

            // Mono ランタイムでは稀に計測ノイズが乗ることがあるため、0 を理想としつつ 1KB 未満を許容する。
            Assert.Less(delta, 1024, $"Tick は 0 alloc であるべき(delta={delta} bytes)");
        }
    }
}
