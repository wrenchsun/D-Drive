using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DDrive.Runtime.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DDrive.Runtime.Ui
{
    // [15_ui_interaction.md] Part A — uGUI Button を使わない独自実装。状態機械/Skin/Locked/ナビゲーションは
    // 基底 UiInteractable が提供し、ここでは Click/DoubleClick/LongPress/Repeat の判定だけを持つ。
    // R3 は未導入のため Observable ではなく素の C# event(Action)、待ち合わせは WaitClickAsync(UniTask)のみ。
    //
    // 判定は Update() が毎フレーム Advance(Time.unscaledDeltaTime) を呼ぶことで進む。テストは
    // EventSystem 無しで Press()/Release()/Hover()/Focus()/Advance() を直接呼んで駆動できる。
    public sealed class UiButton : UiInteractable, ISubmitHandler
    {
        [Header("UiButton")]
        [Tooltip("この秒数押し続けると LongPress が発火する(0=無効)")]
        public float LongPressSec = 0.5f;
        [Tooltip("LongPress 発火後、押しっぱなしでこの間隔ごとに Repeat が発火する(0=無効)")]
        public float RepeatIntervalSec = 0.1f;
        [Tooltip("この秒数以内の 2 回目クリックを DoubleClick として扱う(0=無効。単発 Click が即時発火する)")]
        public float DoubleClickSec = 0.3f;

        public event Action OnClick;
        public event Action OnDoubleClick;
        public event Action OnLongPress;
        public event Action OnRepeat;

        private bool _isHeld;
        private float _heldSec;
        private bool _longPressFired;
        private float _lastRepeatSec;
        private bool _pendingClickActive;
        private float _pendingClickTimer;

        private ButtonSkinData ButtonSkin => ResolvedSkin as ButtonSkinData;

        public void SetVisual(ButtonSkinData skin) => SetVisual((ControlSkinData)skin);

        // デバッグ・チュートリアル誘導用。Press/Release の状態機械を経由せず、Cooldown/多重発火防止だけを通して即発火する。
        public void SimulateClick()
        {
            if (TryBeginFire())
            {
                OnClick?.Invoke();
                PlaySe(ButtonSkin?.ClickSe ?? default);
            }
        }

        public async UniTask WaitClickAsync(CancellationToken ct)
        {
            var tcs = new UniTaskCompletionSource();
            void Handler() => tcs.TrySetResult();
            OnClick += Handler;
            try
            {
                using (ct.CanBeCanceled ? ct.Register(() => tcs.TrySetCanceled()) : default)
                {
                    await tcs.Task;
                }
            }
            finally
            {
                OnClick -= Handler;
            }
        }

        // ── テスト/EventSystem 双方から駆動できる操作 API ──

        public void Press()
        {
            SetPressed(true);

            if (State == ControlState.Disabled || State == ControlState.Locked)
            {
                RaiseDenied();
                PlaySe(ButtonSkin?.DeniedSe ?? default);
                _isHeld = false;
                return;
            }

            _isHeld = true;
            _heldSec = 0f;
            _longPressFired = false;
        }

        public void Release(bool inside = true)
        {
            var wasHeld = _isHeld;
            _isHeld = false;
            SetPressed(false);

            if (!wasHeld)
            {
                return;
            }

            if (inside && !_longPressFired)
            {
                BeginClickOrDouble();
            }

            _longPressFired = false;
        }

        public void Hover(bool over)
        {
            var wasOver = State == ControlState.Hover;
            SetHovered(over);
            if (over && !wasOver)
            {
                PlaySe(ButtonSkin?.HoverSe ?? default);
            }
        }

        public void Focus(bool focused) => SetFocusedState(focused);

        // Update から Time.unscaledDeltaTime で呼ばれる(テストは直接呼んで時間経過を模擬する。
        // InternalsVisibleTo が未設定のため public にしている)。
        public void Advance(float unscaledDt)
        {
            TickCooldown(unscaledDt);

            if (_isHeld)
            {
                _heldSec += unscaledDt;
                if (!_longPressFired && LongPressSec > 0f && _heldSec >= LongPressSec)
                {
                    _longPressFired = true;
                    _lastRepeatSec = _heldSec;
                    if (TryBeginFire())
                    {
                        OnLongPress?.Invoke();
                        PlaySe(ButtonSkin?.LongPressSe ?? default);
                    }
                }
                else if (_longPressFired && RepeatIntervalSec > 0f && _heldSec - _lastRepeatSec >= RepeatIntervalSec)
                {
                    _lastRepeatSec = _heldSec;
                    OnRepeat?.Invoke();
                }
            }

            if (_pendingClickActive)
            {
                _pendingClickTimer -= unscaledDt;
                if (_pendingClickTimer <= 0f)
                {
                    _pendingClickActive = false;
                    FireClick();
                }
            }
        }

        private void Update() => Advance(Time.unscaledDeltaTime);

        private void BeginClickOrDouble()
        {
            if (DoubleClickSec > 0f)
            {
                if (_pendingClickActive)
                {
                    _pendingClickActive = false;
                    if (TryBeginFire())
                    {
                        OnDoubleClick?.Invoke();
                        PlaySe(ButtonSkin?.ClickSe ?? default);
                    }
                }
                else
                {
                    _pendingClickActive = true;
                    _pendingClickTimer = DoubleClickSec;
                }
            }
            else
            {
                FireClick();
            }
        }

        private void FireClick()
        {
            if (TryBeginFire())
            {
                OnClick?.Invoke();
                PlaySe(ButtonSkin?.ClickSe ?? default);
            }
        }

        private static void PlaySe(DDrive.Foundation.Identity.AssetId<SeMarker> id)
        {
            if (id.IsValid)
            {
                DDrive.Runtime.Audio.Audio.PlaySe(id);
            }
        }

        // 4-8(UiTween)実装まではビジュアル演出はビジュアル(Tint/Scale)のみ。Tween 再生フックはここに追加する。
        protected override void OnSkinApplied(in StateVisual v)
        {
        }

        // ── EventSystem 連携 ──

        public override void OnPointerDown(PointerEventData eventData) => Press();

        public override void OnPointerUp(PointerEventData eventData)
        {
            var rt = (RectTransform)transform;
            var inside = eventData == null || RectTransformUtility.RectangleContainsScreenPoint(rt, eventData.position, eventData.pressEventCamera);
            Release(inside);
        }

        public override void OnPointerEnter(PointerEventData eventData) => Hover(true);
        public override void OnPointerExit(PointerEventData eventData) => Hover(false);
        public override void OnSelect(BaseEventData eventData) => Focus(true);
        public override void OnDeselect(BaseEventData eventData) => Focus(false);

        public void OnSubmit(BaseEventData eventData) => SimulateClick();
    }
}
