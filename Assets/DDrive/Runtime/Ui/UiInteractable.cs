using System;
using System.Collections.Generic;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
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
        ISelectHandler, IDeselectHandler, IMoveHandler, IUiNavigable
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
        private bool _alphaHitWarned;

        // 状態ごとのスプライトアニメ / スクロール(2026-09-14)。
        private Sprite[] _animFrames;
        private float _animFps;
        private bool _animLoop;
        private float _animTime;
        private int _animFrameIndex = -1;
        private bool _spriteOverridden;
        private Sprite _spriteBeforeOverride;
        private bool _materialOverridden;
        private UnityEngine.Material _materialBeforeScroll; // 名前空間 DDrive.Runtime.Material と衝突するため完全修飾
        private bool _scrollWarned;

        // 同じ(元マテリアル, 速度)のボタン同士はマテリアルを共有する(描画をまとめられるように)。
        private static readonly Dictionary<(UnityEngine.Material template, Vector2 speed), UnityEngine.Material> ScrollMaterials = new();
        private static readonly int ScrollSpeedId = Shader.PropertyToID("_ScrollSpeed");

        // 今の状態でスプライトアニメ / スクロールが動いているか(エディタのプレビューが描き直し続ける判定に使う)。
        public bool HasVisualAnimation => _animFrames != null || _materialOverridden;

        // 4-7 残り: SetVisual を明示的に呼んだか(true なら UiManager の Open 時レイヤー既定 Skin 適用の対象外)。
        public bool HasExplicitSkin { get; private set; }

        // 4-7 残り: ButtonSkin 側の Se が無効(未設定)のときに使う UiLayerSettings のフォールバック。
        // UiManager.ApplyLayerDefaults が Open 時に SetDefaultSe で配る(未設定なら default=Invalid のまま)。
        protected AssetId<SeMarker> DefaultClickSe { get; private set; }
        protected AssetId<SeMarker> DefaultHoverSe { get; private set; }
        protected AssetId<SeMarker> DefaultDeniedSe { get; private set; }

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
        public virtual bool CanFocus => Interactable && !_locked;
        public void SetFocused(bool focused) => SetFocusedState(focused);

        // [07_canvas_prefab.md] NavNode / [18_ui_controls.md] A-1 — EventSystem の Navigate(十字キー / スティック)。
        // Selectable でない UiInteractable は Unity の自動ナビゲーションの対象外なので、ここで UiManager.MoveFocus に
        // 回す(NavNode の明示リンク → 開いている Canvas 内で方向の最寄り、の順。2026-09-12。以前はゲームコードが
        // MoveFocus を呼ばない限り UiButton 間をパッドで移動できなかった)。Disabled / Locked でもフォーカスは抜けられる。
        public virtual void OnMove(AxisEventData eventData)
        {
            if (eventData == null)
            {
                return;
            }

            if (Ui.MoveFocus(ToVector(eventData.moveDir)))
            {
                eventData.Use();
            }
        }

        protected static Vector2 ToVector(MoveDirection dir)
        {
            switch (dir)
            {
                case MoveDirection.Up: return Vector2.up;
                case MoveDirection.Down: return Vector2.down;
                case MoveDirection.Left: return Vector2.left;
                case MoveDirection.Right: return Vector2.right;
                default: return Vector2.zero;
            }
        }

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

        // Codex レビュー対応(2026-09-11): Pool から Return されても pointer/focus/cooldown/StateTween が
        // 前回の状態のまま残り、次に Rent された瞬間に「まだ押されている/ホバー中」扱いになっていた。
        // OnDisable(Pool.Return は非アクティブ化を伴う)で必ず中立状態に戻す。
        protected virtual void OnDisable() => ResetInteractionState();

        // 入力に関する内部状態を Normal へ戻す。OnDisable から自動的に呼ばれるほか、テストからも直接呼べる。
        public void ResetInteractionState()
        {
            _pointerDown = false;
            _pointerOver = false;
            _focused = false;
            _cooldownRemaining = 0f;

            // UiFx.Stop は無効/既に完了済みの Handle に対しても安全な no-op(UiManager の同種コメント参照)。
            if (UiFx.IsBound)
            {
                UiFx.Stop(_stateTween);
            }

            _stateTween = Handle<UiTweenMarker>.Invalid;

            // SetState は resolved==State のとき何もしないため、Skin/Tween の再適用まで含めて明示的に行う。
            State = ComputeResetState();
            ApplySkinForCurrentState();
        }

        // ResetInteractionState 用: Locked/Disabled はフラグが残っていれば維持し、それ以外は Normal にする
        // (SetState と同じ優先度規則。Locked > Disabled > Normal)。
        private ControlState ComputeResetState()
        {
            if (_locked)
            {
                return ControlState.Locked;
            }

            if (!_interactable)
            {
                return ControlState.Disabled;
            }

            return ControlState.Normal;
        }

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
            HasExplicitSkin = true;
            ApplySkinForCurrentState();
        }

        // 4-7 残り: UiManager が Open 時に「SkinId 未設定 かつ 明示 SetVisual 未実行」の UiInteractable へだけ
        // レイヤー既定 Skin を当てる(HasExplicitSkin は立てない。デザイナーが後から SkinId/SetVisual を
        // 設定すればそちらが優先される)。
        public void ApplyDefaultSkin(ControlSkinData skin)
        {
            if (skin == null || SkinId.IsValid || HasExplicitSkin)
            {
                return;
            }

            _skin = skin;
            ApplySkinForCurrentState();
        }

        // 4-7 残り: ButtonSkin(または個別 Skin)の Se が未設定(Invalid)のときのフォールバック先。
        // UiManager.Open がレイヤー既定(UiLayerSettings)から配る。
        public void SetDefaultSe(AssetId<SeMarker> click, AssetId<SeMarker> hover, AssetId<SeMarker> denied)
        {
            DefaultClickSe = click;
            DefaultHoverSe = hover;
            DefaultDeniedSe = denied;
        }

        protected abstract void OnSkinApplied(in StateVisual v);

        // [18_ui_controls.md] B-6(4-17) — SliderEditor「全状態を並べる」専用。Interactable/Locked の実フラグは
        // 変えず、見た目(State + Skin適用)だけを指定状態へ強制する。エディタのプレビュー配置(DontSave)
        // にのみ使う想定で、通常のランタイム状態遷移フロー(SetState 経由の優先度解決)は経由しない。
        public void ForceStateForPreview(ControlState state)
        {
            State = state;
            ApplySkinForCurrentState();
        }

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
            ApplyVisual(skin, in v);
            ApplyHitArea(skin);
            PlayStateTween(in v);
            OnSkinApplied(in v);
        }

        // 状態の画像: コマ(AnimFrames)があれば 1 コマ目、無ければ Override Sprite。どちらも無い状態に入ったら、差し替える前の
        // 画像に戻す(2026-09-14。以前は前の状態の画像が残っていた)。
        private void ApplySprite(Image image, in StateVisual v)
        {
            var frames = v.AnimFrames != null && v.AnimFrames.Length > 0 ? v.AnimFrames : null;
            _animFrames = frames;
            _animFps = v.AnimFps > 0f ? v.AnimFps : 12f;
            _animLoop = v.AnimLoop;
            _animTime = 0f;
            _animFrameIndex = frames != null ? 0 : -1;

            var sprite = frames != null ? frames[0] : v.OverrideSprite;
            if (sprite != null)
            {
                if (!_spriteOverridden)
                {
                    _spriteBeforeOverride = image.sprite;
                    _spriteOverridden = true;
                }

                image.sprite = sprite;
            }
            else if (_spriteOverridden)
            {
                image.sprite = _spriteBeforeOverride;
                _spriteOverridden = false;
            }
        }

        // 状態のスプライトアニメを進める(UiButton / UiSlider の Advance から毎フレーム。エディタのプレビューからも呼べる)。
        // 定常経路なので割り当て無し(コマ番号の計算と、変わったときだけ sprite を差し替える)。
        public void TickVisuals(float dt)
        {
            if (_animFrames == null || !(TargetGraphic is Image image))
            {
                return;
            }

            _animTime += dt;
            var count = _animFrames.Length;
            var index = (int)(_animTime * _animFps);
            index = _animLoop ? index % count : Mathf.Min(index, count - 1);
            if (index == _animFrameIndex)
            {
                return;
            }

            _animFrameIndex = index;
            var sprite = _animFrames[index];
            if (sprite != null)
            {
                image.sprite = sprite;
            }
        }

        // スクロール: Skin の ScrollMaterial(DDrive/UI/Scroll)を元に、速度ごとに 1 つ作ったマテリアルを使う。
        // 動かすのはシェーダー(_Time)なので毎フレームの処理は無い。スクロールしない状態に入ったら元のマテリアルに戻す。
        private void ApplyScroll(ControlSkinData skin, Graphic graphic, in StateVisual v)
        {
            var speed = v.ScrollSpeed;
            var template = skin.ScrollMaterial;
            if (speed == Vector2.zero || template == null)
            {
                if (speed != Vector2.zero && !_scrollWarned)
                {
                    _scrollWarned = true;
                    Debug.LogWarning($"[DDrive] '{name}': Scroll Speed が設定されていますが、Skin の Scroll Material が空のためスクロールしません。", this);
                }

                if (_materialOverridden)
                {
                    graphic.material = _materialBeforeScroll;
                    _materialOverridden = false;
                    _materialBeforeScroll = null;
                }

                return;
            }

            if (!_materialOverridden)
            {
                _materialBeforeScroll = graphic.material == graphic.defaultMaterial ? null : graphic.material;
                _materialOverridden = true;
            }

            graphic.material = GetScrollMaterial(template, speed);
        }

        private static UnityEngine.Material GetScrollMaterial(UnityEngine.Material template, Vector2 speed)
        {
            var key = (template, speed);
            if (!ScrollMaterials.TryGetValue(key, out var material) || material == null)
            {
                material = new UnityEngine.Material(template)
                {
                    name = $"{template.name} (Scroll {speed.x}, {speed.y})",
                    hideFlags = HideFlags.DontSave,
                };
                material.SetVector(ScrollSpeedId, new Vector4(speed.x, speed.y, 0f, 0f));
                ScrollMaterials[key] = material;
            }

            return material;
        }

        // 当たり判定(2026-09-14)。Graphic.raycastPadding は「内側へ縮める量」が正なので、広げ幅の符号を反転して渡す。
        // 透明判定は Sprite の Texture が読めないと Image が毎回エラーを出すため、読めないときは警告 1 回 + 無効で続行する。
        private void ApplyHitArea(ControlSkinData skin)
        {
            var graphic = TargetGraphic;
            if (graphic == null)
            {
                return;
            }

            graphic.raycastPadding = -skin.EffectiveHitAreaExpand;

            if (graphic is Image image)
            {
                var threshold = skin.AlphaHitThreshold;
                if (threshold > 0f)
                {
                    var sprite = image.overrideSprite;
                    var texture = sprite != null ? sprite.texture : null;
                    if (texture == null || !texture.isReadable)
                    {
                        if (!_alphaHitWarned)
                        {
                            _alphaHitWarned = true;
                            Debug.LogWarning($"[DDrive] '{name}': AlphaHitThreshold が有効ですが、画像の Read/Write が無効(または画像が無い)ため透明部分の判定を行いません。画像のインポート設定で Read/Write を ON にしてください。", this);
                        }

                        threshold = 0f;
                    }
                }

                SetAlphaHitThreshold(image, threshold);
            }
        }

        // Image.alphaHitTestMinimumThreshold の setter は、画像が読めない(Read/Write 無効)と値に関係なく
        // InvalidOperationException を投げる(0 を書いても投げる)。多くのボタンの画像は読めないため、値が変わる
        // ときだけ書き、読めない画像で戻せない場合は諦めて続行する(TL;DR #4。警告は呼び出し側で 1 回出し済み)。
        private static void SetAlphaHitThreshold(Image image, float value)
        {
            if (Mathf.Approximately(image.alphaHitTestMinimumThreshold, value))
            {
                return;
            }

            try
            {
                image.alphaHitTestMinimumThreshold = value;
            }
            catch (InvalidOperationException)
            {
            }
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

        private void ApplyVisual(ControlSkinData skin, in StateVisual v)
        {
            if (TargetGraphic != null)
            {
                TargetGraphic.color = v.Tint;
                if (TargetGraphic is Image image)
                {
                    ApplySprite(image, in v);
                }

                ApplyScroll(skin, TargetGraphic, in v);
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
