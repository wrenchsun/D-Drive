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
        private bool _scrollAcquired;
        private (UnityEngine.Material template, Vector2 speed) _scrollKey;
        private UnityEngine.Material _scrollMaterial;

        // Skin を当てる前の当たり判定(Prefab での手設定)。Skin の広げ幅はこれに足し、Skin が外れたらこれに戻す(レビュー対応 2026-09-14)。
        private Graphic _hitBaseOwner;
        private Vector4 _hitBasePadding;
        private float _hitBaseThreshold;

        // 同じ(元マテリアル, 速度)のボタン同士はマテリアルを共有する(描画をまとめられるように)。使っている数を数え、
        // 誰も使わなくなったら破棄する(Skin Editor で速度をドラッグ編集しても溜まり続けないように。レビュー対応 2026-09-14)。
        private sealed class ScrollEntry
        {
            public UnityEngine.Material Material;
            public int Refs;
        }

        private static readonly Dictionary<(UnityEngine.Material template, Vector2 speed), ScrollEntry> ScrollMaterials = new();
        private static readonly int ScrollSpeedId = Shader.PropertyToID("_ScrollSpeed");

        // スクロールの時計。シェーダー標準の _Time は timeScale=0(ポーズ中)で止まるため、止まらない時間を配る。
        private static readonly int ScrollTimeId = Shader.PropertyToID("_DDriveUiUnscaledTime");

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
                RestoreVisuals();
                return;
            }

            ref readonly var v = ref skin.Get(State);
            ReleaseAlphaHitBeforeSpriteChange();
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
            if (_materialOverridden)
            {
                Shader.SetGlobalFloat(ScrollTimeId, Time.realtimeSinceStartup);
            }

            // `is Image` の型判定は破棄済みを通してしまうため、as + Unity の null 判定で見る(レビュー対応 2026-09-14)。
            var image = TargetGraphic as Image;
            if (_animFrames == null || image == null)
            {
                return;
            }

            var count = _animFrames.Length;
            var cycle = count / _animFps;
            _animTime += dt;
            if (_animTime >= cycle)
            {
                // ループは 1 周ぶん巻き戻す(時間が増え続けて float の精度でコマ送りがぶれないように)。
                _animTime = _animLoop ? _animTime % cycle : cycle;
            }

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

                RestoreMaterial(graphic);
                return;
            }

            if (!_materialOverridden)
            {
                _materialBeforeScroll = graphic.material == graphic.defaultMaterial ? null : graphic.material;
                _materialOverridden = true;
            }

            var key = (template, speed);
            if (!_scrollAcquired || _scrollKey != key || _scrollMaterial == null)
            {
                ReleaseScrollMaterial();
                _scrollMaterial = AcquireScrollMaterial(key);
                _scrollKey = key;
                _scrollAcquired = true;
            }

            graphic.material = _scrollMaterial;
            Shader.SetGlobalFloat(ScrollTimeId, Time.realtimeSinceStartup);
        }

        private void RestoreMaterial(Graphic graphic)
        {
            if (_materialOverridden && graphic != null)
            {
                graphic.material = _materialBeforeScroll;
            }

            _materialOverridden = false;
            _materialBeforeScroll = null;
            ReleaseScrollMaterial();
        }

        private static UnityEngine.Material AcquireScrollMaterial((UnityEngine.Material template, Vector2 speed) key)
        {
            if (!ScrollMaterials.TryGetValue(key, out var entry))
            {
                entry = new ScrollEntry();
                ScrollMaterials[key] = entry;
            }

            if (entry.Material == null)
            {
                entry.Material = new UnityEngine.Material(key.template)
                {
                    name = $"{key.template.name} (Scroll {key.speed.x}, {key.speed.y})",
                    hideFlags = HideFlags.DontSave,
                };
                entry.Material.SetVector(ScrollSpeedId, new Vector4(key.speed.x, key.speed.y, 0f, 0f));
            }

            entry.Refs++;
            return entry.Material;
        }

        private void ReleaseScrollMaterial()
        {
            if (!_scrollAcquired)
            {
                return;
            }

            _scrollAcquired = false;
            _scrollMaterial = null;
            if (!ScrollMaterials.TryGetValue(_scrollKey, out var entry) || --entry.Refs > 0)
            {
                return;
            }

            ScrollMaterials.Remove(_scrollKey);
            if (entry.Material == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(entry.Material);
            }
            else
            {
                DestroyImmediate(entry.Material);
            }
        }

        // 今ある共有スクロールマテリアルの数(テスト用)。
        public static int ScrollMaterialCountForTests => ScrollMaterials.Count;

        protected virtual void OnDestroy() => ReleaseScrollMaterial();

        // Skin が外れた(SetVisual(null) / Resolver が null)ときに、Skin が変えた見た目と当たり判定を全部元に戻す。
        private void RestoreVisuals()
        {
            _animFrames = null;
            _animFrameIndex = -1;

            var graphic = TargetGraphic;
            if (_spriteOverridden && graphic is Image image && image != null)
            {
                image.sprite = _spriteBeforeOverride;
            }

            _spriteOverridden = false;
            RestoreMaterial(graphic);

            if (graphic != null && _hitBaseOwner == graphic)
            {
                graphic.raycastPadding = _hitBasePadding;
                if (graphic is Image hitImage)
                {
                    SetAlphaHitThreshold(hitImage, _hitBaseThreshold);
                }
            }
        }

        // 当たり判定(2026-09-14)。Graphic.raycastPadding は「内側へ縮める量」が正なので、広げ幅の符号を反転して渡す。
        // 透明判定は Sprite の Texture が読めないと Image が毎回エラーを出すため、読めないときは警告 1 回 + 無効で続行する。
        // 手設定(Prefab)の padding / 透明判定を、最初に Skin を当てる前に 1 回だけ覚える。
        private void EnsureHitBase(Graphic graphic)
        {
            if (_hitBaseOwner == graphic)
            {
                return;
            }

            _hitBaseOwner = graphic;
            _hitBasePadding = graphic.raycastPadding;
            _hitBaseThreshold = graphic is Image image ? image.alphaHitTestMinimumThreshold : 0f;
        }

        // 透明判定が有効なまま読めない画像へ差し替えると、setter が例外を投げて 0 に戻せなくなる(以後ポインタが動くたびに
        // Image がエラーを出す)。まだ読める今の画像のうちに 0 にしておき、差し替え後に ApplyHitArea で付け直す(レビュー対応 2026-09-14)。
        private void ReleaseAlphaHitBeforeSpriteChange()
        {
            var image = TargetGraphic as Image;
            if (image == null)
            {
                return;
            }

            EnsureHitBase(image);
            if (image.alphaHitTestMinimumThreshold > 0f)
            {
                SetAlphaHitThreshold(image, 0f);
            }
        }

        private void ApplyHitArea(ControlSkinData skin)
        {
            var graphic = TargetGraphic;
            if (graphic == null)
            {
                return;
            }

            EnsureHitBase(graphic);
            graphic.raycastPadding = _hitBasePadding - skin.EffectiveHitAreaExpand;

            var image = graphic as Image;
            if (image == null)
            {
                return;
            }

            var threshold = skin.AlphaHitThreshold > 0f ? skin.AlphaHitThreshold : _hitBaseThreshold;
            if (threshold > 0f && !AllSpritesReadable(image))
            {
                if (!_alphaHitWarned)
                {
                    _alphaHitWarned = true;
                    Debug.LogWarning($"[DDrive] '{name}': AlphaHitThreshold が有効ですが、画像(またはスプライトアニメのコマ)の Read/Write が無効(または画像が無い)ため透明部分の判定を行いません。画像のインポート設定で Read/Write を ON にしてください。", this);
                }

                threshold = 0f;
            }

            SetAlphaHitThreshold(image, threshold);
        }

        // 今の画像と、この状態のコマが全部読めるか(コマ送りのたびに読めない画像へ替わらないよう、コマも見る)。
        private bool AllSpritesReadable(Image image)
        {
            if (!IsReadable(image.overrideSprite))
            {
                return false;
            }

            if (_animFrames != null)
            {
                for (var i = 0; i < _animFrames.Length; i++)
                {
                    if (_animFrames[i] != null && !IsReadable(_animFrames[i]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsReadable(Sprite sprite) => sprite != null && sprite.texture != null && sprite.texture.isReadable;

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
