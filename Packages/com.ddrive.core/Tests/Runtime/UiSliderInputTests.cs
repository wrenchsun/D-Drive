using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DDrive.Tests.Runtime
{
    // 2026-09-14 — UiSlider の入力の許可(PointerInput / NavigationInput)と、つまみの当たり判定(SliderSkinData.HandleHitAreaExpand)。
    public class UiSliderInputTests
    {
        private GameObject _go;
        private SliderSkinData _skin;

        [SetUp]
        public void SetUp() => UiInteractable.ResetDoubleFireGuardForTests();

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
            }

            if (_skin != null)
            {
                Object.DestroyImmediate(_skin);
            }
        }

        private UiSlider CreateSlider(out Image handleImage)
        {
            _go = new GameObject("Slider", typeof(RectTransform), typeof(Image), typeof(UiSlider));
            var slider = _go.GetComponent<UiSlider>();
            slider.TargetGraphic = _go.GetComponent<Image>();
            slider.CooldownSec = 0f;
            slider.Min = 0f;
            slider.Max = 1f;
            slider.SetValueSilent(0f);

            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(_go.transform, false);
            slider.HandleRect = (RectTransform)handle.transform;
            handleImage = handle.GetComponent<Image>();
            return slider;
        }

        [Test]
        public void PointerInput_DefaultOn_HoverChangesState()
        {
            var slider = CreateSlider(out _);

            slider.OnPointerEnter(new PointerEventData(null));

            Assert.AreEqual(ControlState.Hover, slider.State);
        }

        [Test]
        public void PointerInputOff_IgnoresHoverAndPress()
        {
            var slider = CreateSlider(out _);
            slider.PointerInput = false;
            var e = new PointerEventData(null);

            slider.OnPointerEnter(e);
            slider.OnPointerDown(e);
            slider.OnBeginDrag(e);

            Assert.AreEqual(ControlState.Normal, slider.State);
            Assert.AreEqual(0f, slider.Value, 0.0001f);
        }

        [Test]
        public void NavigationInputOff_CannotFocus_AndMoveEventKeepsValue()
        {
            var slider = CreateSlider(out _);
            Assert.IsTrue(slider.CanFocus);

            slider.NavigationInput = false;
            slider.OnMove(new AxisEventData(null) { moveDir = MoveDirection.Right });

            Assert.IsFalse(slider.CanFocus);
            Assert.AreEqual(0f, slider.Value, 0.0001f);
        }

        [Test]
        public void InputOff_DoesNotBlockProgrammaticValue()
        {
            var slider = CreateSlider(out _);
            slider.PointerInput = false;
            slider.NavigationInput = false;

            slider.Value = 0.6f; // HP の増減などゲームコードからの変更は効く

            Assert.AreEqual(0.6f, slider.Value, 0.0001f);
        }

        // 回帰(2026-09-14): 既定(中央アンカー)のつまみでも、つまみの中心が値の位置(溝の左端〜右端)に乗る。
        // 以前は値 0 で溝の中央、値 1 で右端より幅の半分はみ出していた。
        [Test]
        public void Handle_FollowsValue_EvenWithCenterAnchor()
        {
            var slider = CreateSlider(out _);
            var track = (RectTransform)_go.transform;
            track.sizeDelta = new Vector2(300f, 24f);
            slider.TrackRect = track;
            var handle = slider.HandleRect;

            slider.Value = 1f;
            slider.Advance(0.01f);
            Assert.AreEqual(150f, handle.localPosition.x, 0.01f, "値 1 でつまみの中心が溝の右端");

            slider.Value = 0f;
            slider.Advance(0.01f);
            Assert.AreEqual(-150f, handle.localPosition.x, 0.01f, "値 0 でつまみの中心が溝の左端");

            slider.Value = 0.5f;
            slider.Advance(0.01f);
            Assert.AreEqual(0f, handle.localPosition.x, 0.01f, "値 0.5 で溝の中央");
        }

        // 回帰(2026-09-14): スタミナ設定(0〜100 / Step 1 / Notches 4 / Snap 0.03)で、パッドの移動量(既定 0.05)が
        // 刻みと目盛りの吸い付きで元に戻り、何度押しても「Commit: 100」のまま動かなかった。隣の目盛りへ進む。
        [Test]
        public void PadMove_WithStepAndNotches_MovesToNeighbourNotch()
        {
            var slider = CreateSlider(out _);
            slider.SetRange(0f, 100f);
            slider.Step = 1f;
            slider.Notches = 4;
            slider.SnapThreshold = 0.03f;
            slider.SetValueSilent(100f);

            slider.Move(MoveDirection.Left);
            Assert.AreEqual(75f, slider.Value, 0.001f);
            slider.MoveRelease();

            slider.Move(MoveDirection.Left);
            Assert.AreEqual(50f, slider.Value, 0.001f);
            slider.MoveRelease();

            slider.Move(MoveDirection.Right);
            Assert.AreEqual(75f, slider.Value, 0.001f);
        }

        [Test]
        public void Wheel_WithNotchSnap_MovesToNeighbourNotch()
        {
            var slider = CreateSlider(out _);
            slider.SetRange(0f, 100f);
            slider.Step = 1f;
            slider.Notches = 4;
            slider.SnapThreshold = 0.03f;
            slider.SetValueSilent(100f);

            slider.Wheel(-1f);

            Assert.AreEqual(75f, slider.Value, 0.001f);
        }

        [Test]
        public void PadMove_SmallerThanStep_StillMovesOneStep()
        {
            var slider = CreateSlider(out _);
            slider.SetRange(0f, 100f);
            slider.Step = 5f;
            slider.PadStepAmount = 0.05f;
            slider.SetValueSilent(50f);

            slider.Move(MoveDirection.Right);

            Assert.AreEqual(55f, slider.Value, 0.001f);
        }

        // レビュー対応(2026-09-14): 整数のみ(Step=0)でも、移動量 0.05 が四捨五入で元に戻らず 1 ずつ動く。
        [Test]
        public void PadMove_WholeNumbersWithoutStep_MovesByOne()
        {
            var slider = CreateSlider(out _);
            slider.SetRange(0f, 10f);
            slider.WholeNumbers = true;
            slider.SetValueSilent(5f);

            Assert.IsFalse(slider.Move(MoveDirection.Right), "範囲の途中で端に着いた扱いにしない");
            Assert.AreEqual(6f, slider.Value, 0.001f);
        }

        // レビュー対応(2026-09-14): Min > Max でも、▶ で隣の目盛りへ進むとき値が増える方向へ進む(以前は逆に減っていた)。
        [Test]
        public void PadMove_ReversedRangeWithNotches_MovesValueUp()
        {
            var slider = CreateSlider(out _);
            slider.Min = 100f;
            slider.Max = 0f;
            slider.Step = 1f;
            slider.Notches = 4;
            slider.SnapThreshold = 0.03f;
            slider.SetValueSilent(50f);

            slider.Move(MoveDirection.Right);

            Assert.AreEqual(75f, slider.Value, 0.001f);
        }

        // レビュー対応(2026-09-14): ドラッグ中・ホバー中に PointerInput を OFF にしても、ドラッグ終了・ホバー解除は届く。
        [Test]
        public void PointerInputTurnedOffMidway_EndDragAndExitStillProcessed()
        {
            var slider = CreateSlider(out _);
            var ended = 0;
            slider.OnDragEnd += () => ended++;
            var e = new PointerEventData(null);

            slider.OnPointerEnter(e);
            slider.BeginDragAt(0.3f);
            slider.PointerInput = false;
            slider.OnEndDrag(e);
            slider.OnPointerExit(e);

            Assert.AreEqual(1, ended);
            Assert.AreEqual(ControlState.Normal, slider.State);
        }

        // レビュー対応(2026-09-14): つまみに手設定した padding を Skin が消さない(広げ幅は足し算)。
        [Test]
        public void HandleHitAreaExpand_AddsToHandSetPadding()
        {
            var slider = CreateSlider(out var handleImage);
            handleImage.raycastPadding = new Vector4(1f, 1f, 1f, 1f);
            _skin = ScriptableObject.CreateInstance<SliderSkinData>();
            _skin.HandleHitAreaExpand = new Vector4(1f, 2f, 3f, 4f);

            slider.SetVisual(_skin);

            Assert.AreEqual(new Vector4(0f, -1f, -2f, -3f), handleImage.raycastPadding);
        }

        [Test]
        public void HandleHitAreaExpand_AppliedToHandleGraphic()
        {
            var slider = CreateSlider(out var handleImage);
            _skin = ScriptableObject.CreateInstance<SliderSkinData>();
            _skin.HandleHitAreaExpand = new Vector4(1f, 2f, 3f, 4f);

            slider.SetVisual(_skin);

            Assert.AreEqual(new Vector4(-1f, -2f, -3f, -4f), handleImage.raycastPadding);
        }
    }
}
