using System;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DDrive.Runtime.Ui
{
    // [15_ui_interaction.md] A-1.5 / [18_ui_controls.md] Part A — UiButton / UiSlider(将来) が共有する
    // 状態機械・入力受付・Skin・Locked・ナビゲーションの基底。R3 は未導入のため、イベントは全て
    // 素の C# event(Action)で表現する(WaitXxxAsync だけ UniTask を使う)。
    public abstract class UiInteractable : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler,
        ISelectHandler, IDeselectHandler, IUiNavigable
    {
        // 同フレーム内での多重発火防止(全 UiInteractable 横断の静的ガード)。Time.frameCount 基準なので
        // 実行時は 1 フレーム内、PlayMode の同期テストでは「その [Test] メソッドの実行中ずっと」が
        // 「同フレーム」として扱われる(テストは UiInteractable.ResetDoubleFireGuardForTests で明示的にリセットする)。
        private static int _lastFireFrame = -1;

        public static void ResetDoubleFireGuardForTests() => _lastFireFrame = -1;

        [Header("UiInteractable")]
        [Tooltip("Tint/OverrideSprite の適用先(任意)。未設定なら見た目更新は Scale のみ")]
        public Graphic TargetGraphic;

        [Tooltip("発火後の不感時間(秒)。0=無効")]
        public float CooldownSec = 0.15f;

        [Tooltip("同フレーム内の多重発火防止(全 UiInteractable 横断)")]
        public bool BlockDoubleFire = true;

        [Tooltip("状態別ビジュアル/SE の Skin(遅延解決。SetVisual で直接差し替えも可)")]
        public AssetId<ControlSkinMarker> SkinId;

        public ControlState State { get; private set; } = ControlState.Normal;

        public string LockReasonKey { get; private set; }

        public event Action<bool> OnPress;
        public event Action<bool> OnHover;
        public event Action<bool> OnFocus;
        public event Action<ControlState> OnStateChanged;
        public event Action OnDenied;

        private bool _interactable = true;
        private bool _locked;
        private bool _pointerDown;
        private bool _pointerOver;
        private bool _focused;
        private float _cooldownRemaining;
        private ControlSkinData _skin;

        // 状態遷移時に再生した Tween(次の遷移で止める。[15_ui_interaction.md] B-4「UiButton の StateVisual.EnterTween」)。
        private Handle<UiTweenMarker> _stateTween = Handle<UiTweenMarker>.Invalid;

        public bool Interactable
        {
            get => _interactable;
            set
            {
                if (_interactable == value)
                {
                    return;
                }

                _interactable = value;
                SetState(ComputeBaseState());
            }
        }

        // IUiNavigable
        public Transform Transform => transform;
        public bool CanFocus => Interactable && !_locked;
        public void SetFocused(bool focused) => SetFocusedState(focused);

        protected ControlSkinData ResolvedSkin
        {
            get
            {
                if (_skin == null && SkinId.IsValid && UiSkins.Resolver != null)
                {
                    _skin = UiSkins.Resolver(SkinId);
                }

                return _skin;
            }
        }

        protected virtual void Awake()
        {
            if (TargetGraphic == null)
            {
                TargetGraphic = GetComponent<Graphic>();
            }
        }

        protected virtual void OnEnable() => ApplySkinForCurrentState();

        public void SetLocked(bool locked, string reasonKey = null)
        {
            _locked = locked;
            LockReasonKey = locked ? reasonKey : null;
            SetState(ComputeBaseState());
        }

        // 明示的な Skin 差し替え(SkinId の遅延解決より優先される)。
        public void SetVisual(ControlSkinData skin)
        {
            _skin = skin;
            ApplySkinForCurrentState();
        }

        protected abstract void OnSkinApplied(in StateVisual v);

        // ── 状態機械 ──

        private ControlState ComputeBaseState()
        {
            if (_pointerDown)
            {
                return ControlState.Pressed;
            }

            if (_pointerOver)
            {
                return ControlState.Hover;
            }

            if (_focused)
            {
                return ControlState.Selected;
            }

            return ControlState.Normal;
        }

        // requested を Locked > Disabled で上書きしてから確定させる(優先度: Locked > Disabled > Pressed > Hover/Selected > Normal)。
        protected void SetState(ControlState requested)
        {
            var resolved = requested;
            if (_locked)
            {
                resolved = ControlState.Locked;
            }
            else if (!_interactable)
            {
                resolved = ControlState.Disabled;
            }

            if (resolved == State)
            {
                return;
            }

            State = resolved;
            OnStateChanged?.Invoke(State);
            ApplySkinForCurrentState();
        }

        protected void SetPressed(bool down)
        {
            if (_pointerDown == down)
            {
                return;
            }

            _pointerDown = down;
            OnPress?.Invoke(down);
            SetState(ComputeBaseState());
        }

        protected void SetHovered(bool over)
        {
            if (_pointerOver == over)
            {
                return;
            }

            _pointerOver = over;
            OnHover?.Invoke(over);
            SetState(ComputeBaseState());
        }

        protected void SetFocusedState(bool focused)
        {
            if (_focused == focused)
            {
                return;
            }

            _focused = focused;
            OnFocus?.Invoke(focused);
            SetState(ComputeBaseState());
        }

        protected void RaiseDenied() => OnDenied?.Invoke();

        // Cooldown + 同フレーム多重発火防止。呼び出し側(UiButton の Click/DoubleClick/LongPress)が
        // 実際に発火する直前に呼ぶ。false のときは OnDenied を発火済み。
        protected bool TryBeginFire()
        {
            if (State == ControlState.Disabled || State == ControlState.Locked)
            {
                RaiseDenied();
                return false;
            }

            if (BlockDoubleFire && Time.frameCount == _lastFireFrame)
            {
                RaiseDenied();
                return false;
            }

            if (_cooldownRemaining > 0f)
            {
                RaiseDenied();
                return false;
            }

            _cooldownRemaining = CooldownSec;
            if (BlockDoubleFire)
            {
                _lastFireFrame = Time.frameCount;
            }

            return true;
        }

        protected void TickCooldown(float dt)
        {
            if (_cooldownRemaining > 0f)
            {
                _cooldownRemaining -= dt;
            }
        }

        // ── ビジュアル適用 ──

        private void ApplySkinForCurrentState()
        {
            var skin = ResolvedSkin;
            if (skin == null)
            {
                return;
            }

            ref readonly var v = ref skin.Get(State);
            ApplyVisual(in v);
            PlayStateTween(in v);
            OnSkinApplied(in v);
        }

        // EnterTween(あれば優先) → EnterPreset(Preset!=None) の順で再生する。UiFx 未 Bind 時は no-op。
        private void PlayStateTween(in StateVisual v)
        {
            if (!UiFx.IsBound)
            {
                return;
            }

            var rt = transform as RectTransform;
            if (rt == null)
            {
                return;
            }

            UiFx.Stop(_stateTween);
            _stateTween = Handle<UiTweenMarker>.Invalid;

            if (v.EnterTween.IsValid)
            {
                _stateTween = UiFx.Play(v.EnterTween, rt);
            }
            else if (v.EnterPreset.Preset != UiPreset.None)
            {
                _stateTween = UiFx.Play(v.EnterPreset.Preset, rt, in v.EnterPreset);
            }
        }

        private void ApplyVisual(in StateVisual v)
        {
            if (TargetGraphic != null)
            {
                TargetGraphic.color = v.Tint;
                if (v.OverrideSprite != null && TargetGraphic is Image image)
                {
                    image.sprite = v.OverrideSprite;
                }
            }

            var rt = transform as RectTransform;
            if (rt != null)
            {
                var s = v.Scale.Evaluate(1f);
                rt.localScale = new Vector3(s, s, s);
            }
        }

        // ── EventSystem ハンドラ(仮想。UiButton がクリック判定等のため拡張する) ──

        public virtual void OnPointerDown(PointerEventData eventData) => SetPressed(true);
        public virtual void OnPointerUp(PointerEventData eventData) => SetPressed(false);
        public virtual void OnPointerEnter(PointerEventData eventData) => SetHovered(true);
        public virtual void OnPointerExit(PointerEventData eventData) => SetHovered(false);
        public virtual void OnSelect(BaseEventData eventData) => SetFocusedState(true);
        public virtual void OnDeselect(BaseEventData eventData) => SetFocusedState(false);
    }
}
