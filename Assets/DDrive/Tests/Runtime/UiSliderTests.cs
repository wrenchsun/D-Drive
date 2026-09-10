using System.Collections.Generic;
using DDrive.Foundation.Easing;
using DDrive.Foundation.Validation;
using DDrive.Foundation.Values;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DDrive.Tests.Runtime
{
    // [18_ui_controls.md] Part B — UiSlider(4-14)。EventSystem 無しで BeginDragAt/DragTo/EndDrag/
    // TrackClickAt/Wheel/Move/Advance を直接呼んで駆動する(UiButtonTests と同じ方針)。
    public class UiSliderTests
    {
        private static GameObject CreateSlider(out UiSlider slider)
        {
            var go = new GameObject("Slider", typeof(RectTransform), typeof(Image), typeof(UiSlider));
            slider = go.GetComponent<UiSlider>();
            slider.TargetGraphic = go.GetComponent<Image>();
            slider.CooldownSec = 0f;
            slider.Min = 0f;
            slider.Max = 1f;
            slider.SetValueSilent(0f);
            return go;
        }

        [SetUp]
        public void SetUp() => UiInteractable.ResetDoubleFireGuardForTests();

        [Test]
        public void Drag_MapsPointerToValue_Linearly()
        {
            var go = CreateSlider(out var slider);

            slider.BeginDragAt(0.5f);
            Assert.AreEqual(0.5f, slider.Value, 0.001f);

            slider.DragTo(0.25f);
            Assert.AreEqual(0.25f, slider.Value, 0.001f);

            slider.EndDrag();
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Drag_WithInQuadResponse_MapsNonLinearly()
        {
            var go = CreateSlider(out var slider);
            slider.Response = new ValueDef { Mode = ValueMode.Parametric, Parametric = EaseDef.Named(Ease.InQuad), From = 0f, To = 1f };

            slider.BeginDragAt(0.5f);
            // InQuad(0.5) = 0.25
            Assert.AreEqual(0.25f, slider.Value, 0.01f);

            slider.EndDrag();
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Step_SnapsToNearestMultiple()
        {
            var go = CreateSlider(out var slider);
            slider.SetRange(0f, 10f);
            slider.Step = 2f;

            slider.BeginDragAt(0.53f); // -> raw value 5.3 -> nearest multiple of 2 = 6
            Assert.AreEqual(6f, slider.Value, 0.001f);

            slider.EndDrag();
            Object.DestroyImmediate(go);
        }

        [Test]
        public void WholeNumbers_RoundsToInteger()
        {
            var go = CreateSlider(out var slider);
            slider.SetRange(0f, 10f);
            slider.WholeNumbers = true;

            slider.BeginDragAt(0.37f); // 3.7 -> 4
            Assert.AreEqual(4f, slider.Value, 0.001f);

            slider.EndDrag();
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Notches_SnapWithinThreshold_AndFireOnNotchPassed()
        {
            var go = CreateSlider(out var slider);
            slider.SetRange(0f, 1f);
            slider.Notches = 4; // 0, 0.25, 0.5, 0.75, 1
            slider.SnapThreshold = 0.05f;

            var passed = new List<int>();
            slider.OnNotchPassed += idx => passed.Add(idx);

            slider.BeginDragAt(0.24f); // 近傍 0.25(index 1)へ吸着
            Assert.AreEqual(0.25f, slider.Value, 0.001f);
            Assert.Contains(1, passed);

            slider.EndDrag();
            Object.DestroyImmediate(go);
        }

        [Test]
        public void OnLimitReached_FiresOnce_AtMax()
        {
            var go = CreateSlider(out var slider);
            var maxHits = 0;
            slider.OnLimitReached += isMax => { if (isMax) maxHits++; };

            slider.BeginDragAt(1f);
            slider.DragTo(1f);
            slider.DragTo(1f);
            slider.EndDrag();

            Assert.AreEqual(1, maxHits);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void TrackClickAt_JumpOnTrackClick_True_JumpsImmediately()
        {
            var go = CreateSlider(out var slider);
            slider.JumpOnTrackClick = true;

            slider.TrackClickAt(0.8f);

            Assert.AreEqual(0.8f, slider.Value, 0.001f);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void TrackClickAt_JumpOnTrackClick_False_PagesByStep()
        {
            var go = CreateSlider(out var slider);
            slider.SetRange(0f, 10f);
            slider.Step = 2f;
            slider.JumpOnTrackClick = false;
            slider.SetValueSilent(4f);

            slider.TrackClickAt(1f); // クリック位置(value=10)は現在値より大きい → +Step

            Assert.AreEqual(6f, slider.Value, 0.001f);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Wheel_MovesByStep_WhenEnabled()
        {
            var go = CreateSlider(out var slider);
            slider.SetRange(0f, 10f);
            slider.Step = 1f;
            slider.WheelEnabled = true;
            slider.SetValueSilent(5f);

            slider.Wheel(1f);
            Assert.AreEqual(6f, slider.Value, 0.001f);

            slider.Wheel(-1f);
            Assert.AreEqual(5f, slider.Value, 0.001f);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void Wheel_Disabled_DoesNothing()
        {
            var go = CreateSlider(out var slider);
            slider.SetRange(0f, 10f);
            slider.Step = 1f;
            slider.WheelEnabled = false;
            slider.SetValueSilent(5f);

            slider.Wheel(1f);
            Assert.AreEqual(5f, slider.Value, 0.001f);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void PadMove_RepeatsAfterDelay_AtInterval_AndFineMultiplierApplies()
        {
            var go = CreateSlider(out var slider);
            slider.SetRange(0f, 100f);
            slider.PadStepAmount = 1f;
            slider.PadRepeatDelaySec = 0.4f;
            slider.PadRepeatIntervalSec = 0.1f;
            slider.FineStepMultiplier = 0.5f;
            slider.SetValueSilent(0f);

            slider.Move(MoveDirection.Right); // +1 = 1
            Assert.AreEqual(1f, slider.Value, 0.001f);

            slider.Advance(0.41f); // delay 経過 → repeat 1 回
            Assert.AreEqual(2f, slider.Value, 0.001f);

            slider.Advance(0.11f); // interval 経過 → repeat もう 1 回
            Assert.AreEqual(3f, slider.Value, 0.001f);

            slider.MoveRelease();
            slider.Advance(1f);
            Assert.AreEqual(3f, slider.Value, 0.001f, "MoveRelease 後はリピートしない");

            slider.SetValueSilent(0f);
            var beforeFine = slider.Value;
            slider.Move(MoveDirection.Right, fine: true);
            Assert.AreEqual(beforeFine + 0.5f, slider.Value, 0.001f);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void Move_ReturnsTrue_OnlyAtLimit_WhenEscapeOnLimit()
        {
            var go = CreateSlider(out var slider);
            slider.SetRange(0f, 1f);
            slider.PadStepAmount = 1f;
            slider.EscapeOnLimit = true;
            slider.SetValueSilent(0f);

            var escaped = slider.Move(MoveDirection.Left); // 既に Min → 抜ける
            Assert.IsTrue(escaped);

            var consumed = slider.Move(MoveDirection.Right); // Min→Max へ移動、消費
            Assert.IsFalse(consumed);
            Assert.AreEqual(1f, slider.Value, 0.001f);

            var escapedAtMax = slider.Move(MoveDirection.Right); // 既に Max → 抜ける
            Assert.IsTrue(escapedAtMax);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void NotifyOnlyOnCommit_DefersOnValueChanged_ToEndDrag()
        {
            var go = CreateSlider(out var slider);
            slider.NotifyOnlyOnCommit = true;
            var changes = new List<float>();
            slider.OnValueChanged += v => changes.Add(v);

            slider.BeginDragAt(0.3f);
            slider.DragTo(0.6f);
            Assert.AreEqual(0, changes.Count, "ドラッグ中は通知されない");

            slider.EndDrag();
            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(0.6f, changes[0], 0.001f);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void ChangeThrottleSec_LimitsNotifications()
        {
            var go = CreateSlider(out var slider);
            slider.ChangeThrottleSec = 0.2f;
            var changes = new List<float>();
            slider.OnValueChanged += v => changes.Add(v);

            slider.BeginDragAt(0.1f);
            Assert.AreEqual(1, changes.Count);

            slider.DragTo(0.2f); // スロットル中なので即時発火しない
            slider.DragTo(0.3f);
            Assert.AreEqual(1, changes.Count);

            slider.Advance(0.25f); // スロットル解除 → 保留分がまとめて 1 回発火
            Assert.AreEqual(2, changes.Count);
            Assert.AreEqual(0.3f, changes[1], 0.001f);

            slider.EndDrag();
            Object.DestroyImmediate(go);
        }

        [Test]
        public void FollowMotion_DisplayedValueLagsThenConverges()
        {
            var go = CreateSlider(out var slider);
            slider.FollowMotion = new ValueDef { Mode = ValueMode.Parametric, Parametric = EaseDef.Named(Ease.Linear), Time = TimeDef.Duration(1f), Loop = LoopMode.Once };

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(go.transform, false);
            slider.FillRect = (RectTransform)fillGo.transform;
            slider.FillRect.anchorMin = Vector2.zero;
            slider.FillRect.anchorMax = Vector2.one;

            slider.SetValueSilent(0f);
            slider.Value = 1f; // commit なので即座に実値は 1

            Assert.AreEqual(1f, slider.Value, 0.001f);

            slider.Advance(0.5f); // まだ追従中
            var midFillMax = slider.FillRect.anchorMax.x;
            Assert.Less(midFillMax, 1f);
            Assert.Greater(midFillMax, 0f);

            slider.Advance(0.6f); // 追従完了
            Assert.AreEqual(1f, slider.FillRect.anchorMax.x, 0.01f);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void AnimateTo_AnimatesValue_OverTime()
        {
            var go = CreateSlider(out var slider);
            slider.SetValueSilent(0f);

            var motion = new ValueDef { Mode = ValueMode.Parametric, Parametric = EaseDef.Named(Ease.Linear), Time = TimeDef.Duration(1f), Loop = LoopMode.Once };
            slider.AnimateTo(1f, in motion);

            slider.Advance(0.5f);
            Assert.AreEqual(0.5f, slider.Value, 0.05f);

            slider.Advance(0.6f);
            Assert.AreEqual(1f, slider.Value, 0.001f);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void LockedDrag_RaisesOnDenied_AndDoesNotChangeValue()
        {
            var go = CreateSlider(out var slider);
            slider.SetLocked(true, "reason/test");
            var denied = 0;
            slider.OnDenied += () => denied++;

            slider.BeginDragAt(0.9f);

            Assert.AreEqual(1, denied);
            Assert.AreEqual(0f, slider.Value, 0.001f);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void WaitCommitAsync_CompletesOnEndDrag()
        {
            var go = CreateSlider(out var slider);
            var task = slider.WaitCommitAsync(System.Threading.CancellationToken.None);
            var awaiter = task.GetAwaiter();
            Assert.IsFalse(awaiter.IsCompleted);

            slider.BeginDragAt(0.4f);
            slider.EndDrag();

            Assert.IsTrue(awaiter.IsCompleted);
            Assert.AreEqual(0.4f, awaiter.GetResult(), 0.001f);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void SetRange_KeepsNormalizedPosition()
        {
            var go = CreateSlider(out var slider);
            slider.SetRange(0f, 10f);
            slider.SetValueSilent(5f); // normalized 0.5

            slider.SetRange(0f, 100f, keepNormalized: true);

            Assert.AreEqual(50f, slider.Value, 0.001f);
            Object.DestroyImmediate(go);
        }
    }

    public class OptionStoreTests
    {
        private sealed class InMemoryStorage : IOptionStorage
        {
            public readonly System.Collections.Generic.Dictionary<string, float> Values = new();
            public void Write(string key, float v) => Values[key] = v;
            public bool TryRead(string key, out float v) => Values.TryGetValue(key, out v);
        }

        [TearDown]
        public void TearDown() => AudioListener.volume = 1f;

        [Test]
        public void SetGet_RoundTrips_AndFiresOnChanged()
        {
            var store = new OptionStore();
            OptionKey firedKey = default;
            var firedValue = -1f;
            store.OnChanged += (k, v) => { firedKey = k; firedValue = v; };

            store.Set(OptionKey.SeVolume, 0.6f);

            Assert.AreEqual(0.6f, store.Get(OptionKey.SeVolume), 0.001f);
            Assert.AreEqual(OptionKey.SeVolume, firedKey);
            Assert.AreEqual(0.6f, firedValue, 0.001f);
        }

        [Test]
        public void Set_ClampsToUnitRange()
        {
            var store = new OptionStore();
            store.Set(OptionKey.MasterVolume, 5f);
            Assert.AreEqual(1f, store.Get(OptionKey.MasterVolume), 0.001f);

            store.Set(OptionKey.MasterVolume, -5f);
            Assert.AreEqual(0f, store.Get(OptionKey.MasterVolume), 0.001f);
        }

        [Test]
        public void MasterVolume_AppliesAudioListenerVolume()
        {
            var store = new OptionStore();
            store.Set(OptionKey.MasterVolume, 0.3f);
            Assert.AreEqual(0.3f, AudioListener.volume, 0.001f);
        }

        [Test]
        public void SaveLoad_RoundTrips_WithInMemoryStorage()
        {
            var storage = new InMemoryStorage();
            var store = new OptionStore();
            store.Set(OptionKey.BgmVolume, 0.42f);
            store.Save(storage);

            var loaded = new OptionStore();
            loaded.Load(storage);

            Assert.AreEqual(0.42f, loaded.Get(OptionKey.BgmVolume), 0.001f);
        }
    }

    public class SliderSkinDataValidatorTests
    {
        private static List<ValidationResult> Run(SliderSkinData data)
        {
            var results = new List<ValidationResult>();
            foreach (var r in new SliderSkinDataValidator().Validate(data, new ValidationContext(new List<DDrive.Foundation.Data.AssetDataBase> { data })))
            {
                results.Add(r);
            }

            return results;
        }

        [Test]
        public void NegativeNotchSeMinInterval_ReportsError()
        {
            var data = ScriptableObject.CreateInstance<SliderSkinData>();
            data.NotchSeMinIntervalSec = -0.1f;

            var results = Run(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("NotchSeMinIntervalSec")));
            Object.DestroyImmediate(data);
        }

        [Test]
        public void AllTintAlphaZero_ReportsWarning()
        {
            var data = ScriptableObject.CreateInstance<SliderSkinData>();
            data.Normal.Tint = new Color(1, 1, 1, 0);
            data.Hover.Tint = new Color(1, 1, 1, 0);
            data.Pressed.Tint = new Color(1, 1, 1, 0);
            data.Selected.Tint = new Color(1, 1, 1, 0);
            data.Disabled.Tint = new Color(1, 1, 1, 0);
            data.Locked.Tint = new Color(1, 1, 1, 0);

            var results = Run(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning));
            Object.DestroyImmediate(data);
        }
    }

    public class UiSliderValidationTests
    {
        private static GameObject CreateSlider(out UiSlider slider)
        {
            var go = new GameObject("Slider", typeof(RectTransform), typeof(UiSlider));
            slider = go.GetComponent<UiSlider>();
            return go;
        }

        [Test]
        public void MinGreaterEqualMax_ReportsError()
        {
            var go = CreateSlider(out var slider);
            slider.Min = 1f;
            slider.Max = 1f;

            var results = new List<ValidationResult>();
            UiSliderValidation.Validate(slider, "Slider", results);

            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("Min")));
            Object.DestroyImmediate(go);
        }

        [Test]
        public void NonMonotonicResponse_ReportsError()
        {
            var go = CreateSlider(out var slider);
            var curve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.5f, 1f),
                new Keyframe(1f, 0.2f));
            slider.Response = new ValueDef { Mode = ValueMode.Curve, Curve = curve, Normalized = true };

            var results = new List<ValidationResult>();
            UiSliderValidation.Validate(slider, "Slider", results);

            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("Response")));
            Object.DestroyImmediate(go);
        }

        [Test]
        public void StepDoesNotDivideRange_ReportsWarning()
        {
            var go = CreateSlider(out var slider);
            slider.Min = 0f;
            slider.Max = 10f;
            slider.Step = 3f;

            var results = new List<ValidationResult>();
            UiSliderValidation.Validate(slider, "Slider", results);

            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("Step")));
            Object.DestroyImmediate(go);
        }

        [Test]
        public void NotchesStepMismatch_ReportsError()
        {
            var go = CreateSlider(out var slider);
            slider.Min = 0f;
            slider.Max = 10f;
            slider.Step = 2f; // 5 分割
            slider.Notches = 3; // 不一致

            var results = new List<ValidationResult>();
            UiSliderValidation.Validate(slider, "Slider", results);

            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("Notches")));
            Object.DestroyImmediate(go);
        }

        [Test]
        public void WholeNumbersWithNonIntegerStep_ReportsError()
        {
            var go = CreateSlider(out var slider);
            slider.WholeNumbers = true;
            slider.Step = 0.5f;

            var results = new List<ValidationResult>();
            UiSliderValidation.Validate(slider, "Slider", results);

            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("WholeNumbers")));
            Object.DestroyImmediate(go);
        }

        [Test]
        public void FollowMotionLoop_ReportsError()
        {
            var go = CreateSlider(out var slider);
            slider.FollowMotion = new ValueDef { Mode = ValueMode.Parametric, Loop = LoopMode.Loop, Time = TimeDef.Duration(1f) };

            var results = new List<ValidationResult>();
            UiSliderValidation.Validate(slider, "Slider", results);

            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("FollowMotion")));
            Object.DestroyImmediate(go);
        }
    }
}
