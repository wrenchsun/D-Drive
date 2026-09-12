using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Values;
using DDrive.Runtime.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DDrive.Runtime.Ui
{
    // [18_ui_controls.md] B-1 — トラック/フィルの向き。UiInteractable/StateVisual と違いスライダー固有。
    public enum SliderDirection
    {
        LeftToRight,
        RightToLeft,
        BottomToTop,
        TopToBottom,
    }

    // [18_ui_controls.md] Part B — 連続値のドラッグ・Step 操作。UiButton と同じく uGUI Slider は使わず、
    // 状態機械/Skin/Locked/ナビゲーションは UiInteractable が提供する。R3 は未導入のため素の C# event。
    // 判定は Update() が毎フレーム Advance(Time.unscaledDeltaTime) を呼ぶことで進む。テストは
    // EventSystem 無しで BeginDragAt/DragTo/EndDrag/TrackClickAt/Wheel/Move/Advance を直接呼んで駆動できる。
    public sealed class UiSlider : UiInteractable,
        IDragHandler, IBeginDragHandler, IEndDragHandler, IScrollHandler, IMoveHandler
    {
        [Header("値")]
        public float Min = 0f;
        public float Max = 1f;

        [Header("刻み")]
        [Tooltip("0=連続値")]
        public float Step = 0f;
        public bool WholeNumbers = false;
        [Tooltip(">0 でノッチ表示 + 吸着")]
        public int Notches = 0;
        [Tooltip("ノッチ吸着の効き幅(正規化)")]
        public float SnapThreshold = 0.02f;

        [Header("入力")]
        public SliderDirection Direction = SliderDirection.LeftToRight;
        [Tooltip("トラック直押しで即ジャンプ(false=ページ送り)")]
        public bool JumpOnTrackClick = true;
        [Tooltip("パッド左右キー1回の移動量(値の単位)")]
        public float PadStepAmount = 0.05f;
        public float PadRepeatDelaySec = 0.4f;
        public float PadRepeatIntervalSec = 0.06f;
        [Tooltip("微調整修飾時の倍率")]
        public float FineStepMultiplier = 0.2f;
        public bool WheelEnabled = true;
        [Tooltip("左右 NavNode 設定時、端に到達したときだけフォーカスを抜けさせる")]
        public bool EscapeOnLimit = false;

        [Header("応答・追従")]
        [Tooltip("入力位置(0..1) → 正規化値(0..1)。Mode=Constant(未設定既定)は線形")]
        public ValueDef Response;
        [Tooltip("表示値が実値に追いつく動き(Mode=Constant,Value=0=即時)")]
        public ValueDef FollowMotion;
        [Tooltip("DelayFill(2本目のフィル)専用の追従(未設定なら FollowMotion と同じ挙動)")]
        public ValueDef DelayFollowMotion;

        [Header("通知制御")]
        [Tooltip("0=毎フレーム通知")]
        public float ChangeThrottleSec = 0f;
        [Tooltip("true=ドラッグ終了時のみ OnValueChanged")]
        public bool NotifyOnlyOnCommit = false;

        [Header("ビジュアル(任意)")]
        public RectTransform TrackRect;
        public RectTransform FillRect;
        public RectTransform HandleRect;
        public RectTransform DelayFillRect;

        public event Action<float> OnValueChanged;
        public event Action<float> OnCommit;
        public event Action OnDragBegin;
        public event Action OnDragEnd;
        public event Action<int> OnNotchPassed;
        public event Action<bool> OnLimitReached; // true=Max / false=Min

        private float _value;
        private float _targetFraction;
        private float _displayedFraction;
        private float _followFrom;
        private float _followElapsed;
        private float _delayDisplayedFraction;
        private float _delayFollowFrom;
        private float _delayFollowElapsed;

        private bool _dragging;
        private bool _pendingChanged;
        private float _pendingValue;
        private float _throttleTimer;

        private bool _atMin;
        private bool _atMax;
        private int _lastNotchIndex = -1;
        private float _notchSeElapsed = 999f;

        private bool _animating;
        private float _animFrom;
        private float _animTo;
        private float _animElapsed;
        private ValueDef _animMotion;

        private bool _padActive;
        private bool _padRepeating;
        private MoveDirection _padDir;
        private bool _padFine;
        private float _padHeldSec;
        private float _padLastRepeatSec;
        private int _lastMoveFrame = -1; // Codex レビュー対応(2026-09-11): Move() を呼んだ最終フレーム(EventSystem 駆動では MoveRelease が呼ばれないため、フレームの空きで解放を検出する)
        private bool _padHeldForTests; // テスト用: Time.frameCount に依存させず「押しっぱなし」を明示できるようにする

        private SliderSkinData SliderSkin => ResolvedSkin as SliderSkinData;

        public void SetVisual(SliderSkinData skin) => SetVisual((ControlSkinData)skin);

        public float Value
        {
            get => _value;
            set => SetValueInternal(value, notify: true, commit: true);
        }

        public void SetValueSilent(float v)
        {
            var clamped = Clamp(SnapValue(Clamp(v)));
            _value = clamped;
            _targetFraction = NormalizedFromValue(_value);
            _displayedFraction = _targetFraction;
            _delayDisplayedFraction = _targetFraction;
            _followFrom = _targetFraction;
            _delayFollowFrom = _targetFraction;
            _followElapsed = 0f;
            _delayFollowElapsed = 0f;
            _atMin = _value <= Min + 1e-5f;
            _atMax = _value >= Max - 1e-5f;
            _lastNotchIndex = Notches > 0 ? NotchIndexFor(_value) : -1;
            UpdateVisuals();
        }

        public float NormalizedValue
        {
            get => NormalizedFromValue(_value);
            set => SetValueInternal(ValueFromNormalized(Mathf.Clamp01(value)), notify: true, commit: true);
        }

        protected override void Awake()
        {
            base.Awake();
            SetValueSilent(_value);
        }

        // ── 関数 API ──

        public void SetRange(float min, float max, bool keepNormalized = true)
        {
            var previousNormalized = NormalizedFromValue(_value);
            Min = min;
            Max = max;
            if (keepNormalized)
            {
                SetValueSilent(ValueFromNormalized(previousNormalized));
            }
            else
            {
                SetValueSilent(Clamp(_value));
            }
        }

        public void AnimateTo(float value, in ValueDef motion)
        {
            _animating = true;
            _animFrom = _value;
            _animTo = Clamp(value);
            _animMotion = motion;
            _animElapsed = 0f;
        }

        public async UniTask<float> WaitCommitAsync(CancellationToken ct)
        {
            var tcs = new UniTaskCompletionSource<float>();
            void Handler(float v) => tcs.TrySetResult(v);
            OnCommit += Handler;
            try
            {
                using (ct.CanBeCanceled ? ct.Register(() => tcs.TrySetCanceled()) : default)
                {
                    return await tcs.Task;
                }
            }
            finally
            {
                OnCommit -= Handler;
            }
        }

        // ── テスト/EventSystem 双方から駆動できる操作 API ──

        public void BeginDragAt(float normalizedPointer)
        {
            if (_dragging)
            {
                DragTo(normalizedPointer);
                return;
            }

            if (State == ControlState.Disabled || State == ControlState.Locked)
            {
                RaiseDenied();
                PlaySe(ResolveDeniedSe());
                return;
            }

            _dragging = true;
            OnDragBegin?.Invoke();
            PlaySe(SliderSkin?.GrabSe ?? default);
            DragTo(normalizedPointer);
        }

        public void DragTo(float normalizedPointer)
        {
            if (!_dragging)
            {
                return;
            }

            var f = EvaluateResponse(Mathf.Clamp01(normalizedPointer));
            SetValueInternal(ValueFromNormalized(f), notify: true, commit: false);
        }

        public void EndDrag()
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            SetValueInternal(_value, notify: true, commit: true);
            OnDragEnd?.Invoke();
            PlaySe(SliderSkin?.ReleaseSe ?? default);
        }

        public void TrackClickAt(float normalizedPointer)
        {
            if (State == ControlState.Disabled || State == ControlState.Locked)
            {
                RaiseDenied();
                PlaySe(ResolveDeniedSe());
                return;
            }

            if (JumpOnTrackClick)
            {
                BeginDragAt(normalizedPointer);
                return;
            }

            var targetFraction = EvaluateResponse(Mathf.Clamp01(normalizedPointer));
            var currentFraction = NormalizedFromValue(_value);
            var dir = targetFraction > currentFraction ? 1f : -1f;
            var stepAmount = Step > 0f ? Step : (Max - Min) * 0.1f;
            SetValueInternal(_value + dir * stepAmount, notify: true, commit: true);
        }

        public void Wheel(float delta)
        {
            if (!WheelEnabled || Mathf.Approximately(delta, 0f))
            {
                return;
            }

            if (State == ControlState.Disabled || State == ControlState.Locked)
            {
                RaiseDenied();
                PlaySe(ResolveDeniedSe());
                return;
            }

            var dir = delta > 0f ? 1f : -1f;
            var stepAmount = Step > 0f ? Step : PadStepAmount;
            SetValueInternal(_value + dir * stepAmount, notify: true, commit: true);
        }

        // 戻り値: false=消費した(スライダーが処理した) / true=端に到達済みでフォーカスを抜けるべき。
        public bool Move(MoveDirection dir, bool fine = false)
        {
            if (State == ControlState.Disabled || State == ControlState.Locked)
            {
                RaiseDenied();
                PlaySe(ResolveDeniedSe());
                return false;
            }

            var sign = SignFor(dir);
            if (sign == 0)
            {
                return false;
            }

            var amount = PadStepAmount * (fine ? FineStepMultiplier : 1f);
            var target = Clamp(_value + sign * amount);
            if (Mathf.Approximately(target, _value))
            {
                return EscapeOnLimit;
            }

            SetValueInternal(target, notify: true, commit: true);

            _padActive = true;
            _padDir = dir;
            _padFine = fine;
            _padHeldSec = 0f;
            _padRepeating = false;
            _lastMoveFrame = Time.frameCount;

            return false;
        }

        public void MoveRelease()
        {
            _padActive = false;
            _padRepeating = false;
            _padHeldSec = 0f;
            _padHeldForTests = false;
        }

        // テスト専用: EventSystem を介さず「パッドを押し続けている」状態を明示する(Move → Advance を
        // 複数フレームに分けて呼べないテストのためのフック。実行時コードは使わない)。
        internal void SetPadHeldForTest(bool held) => _padHeldForTests = held;

        // Update から Time.unscaledDeltaTime で呼ばれる(テストは直接呼んで時間経過を模擬する)。
        public void Advance(float unscaledDt)
        {
            // Codex レビュー対応(2026-09-11): OnMove(EventSystem 駆動)は _padActive を立てるだけで
            // MoveRelease を呼ぶ実行時経路が無いため、キーを離しても Repeat が止まらなかった。
            // Move() を呼んだフレームの「翌フレーム以降」まで Move が来ていなければ離されたとみなす。
            // 同一フレーム内の Move→Advance(テストの典型パターン)は押しっぱなし継続として扱う。
            if (_padActive && !_padHeldForTests && Time.frameCount > _lastMoveFrame + 1)
            {
                MoveRelease();
            }

            TickPadRepeat(unscaledDt);
            TickThrottle(unscaledDt);
            TickAnimate(unscaledDt);
            TickFollow(unscaledDt);
            _notchSeElapsed += unscaledDt;
        }

        private void Update() => Advance(Time.unscaledDeltaTime);

        // ── 内部: 値の適用 ──

        private void SetValueInternal(float rawValue, bool notify, bool commit)
        {
            var snapped = SnapValue(Clamp(rawValue));
            var previous = _value;
            var changed = !Mathf.Approximately(snapped, previous);
            _value = snapped;

            if (changed || commit)
            {
                var newFraction = NormalizedFromValue(_value);
                if (!Mathf.Approximately(newFraction, _targetFraction))
                {
                    _followFrom = _displayedFraction;
                    _followElapsed = 0f;
                    _delayFollowFrom = _delayDisplayedFraction;
                    _delayFollowElapsed = 0f;
                }

                _targetFraction = newFraction;
            }

            if (changed)
            {
                UpdateNotchTracking(previous, _value);
                UpdateLimitTracking();
            }

            if (!notify)
            {
                return;
            }

            if (changed)
            {
                if (NotifyOnlyOnCommit && !commit)
                {
                    _pendingChanged = true;
                    _pendingValue = _value;
                }
                else if (_throttleTimer > 0f && !commit)
                {
                    _pendingChanged = true;
                    _pendingValue = _value;
                }
                else
                {
                    OnValueChanged?.Invoke(_value);
                    _throttleTimer = ChangeThrottleSec;
                }
            }

            if (commit)
            {
                if (_pendingChanged)
                {
                    OnValueChanged?.Invoke(_value);
                    _pendingChanged = false;
                }

                OnCommit?.Invoke(_value);
                _throttleTimer = 0f;
            }
        }

        private void TickThrottle(float dt)
        {
            if (_throttleTimer <= 0f)
            {
                return;
            }

            _throttleTimer -= dt;
            if (_throttleTimer > 0f)
            {
                return;
            }

            _throttleTimer = 0f;
            if (_pendingChanged && !NotifyOnlyOnCommit)
            {
                OnValueChanged?.Invoke(_pendingValue);
                _pendingChanged = false;
            }
        }

        private void TickAnimate(float dt)
        {
            if (!_animating)
            {
                return;
            }

            _animElapsed += dt;
            var t01 = _animMotion.ResolveNormalizedT(_animElapsed);
            var shape = EvaluateShape(_animMotion, t01);
            var v = Mathf.LerpUnclamped(_animFrom, _animTo, shape);
            var finished = _animMotion.Loop == LoopMode.Once && _animElapsed >= _animMotion.Duration;

            if (finished)
            {
                _animating = false;
                SetValueInternal(_animTo, notify: true, commit: true);
            }
            else
            {
                SetValueInternal(v, notify: true, commit: false);
            }
        }

        private void TickFollow(float dt)
        {
            _followElapsed += dt;
            var t = FollowMotion.ResolveNormalizedT(_followElapsed);
            var shape = EvaluateShape(FollowMotion, t);
            _displayedFraction = Mathf.LerpUnclamped(_followFrom, _targetFraction, shape);

            var delayMotion = HasDelayFollowMotion() ? DelayFollowMotion : FollowMotion;
            _delayFollowElapsed += dt;
            var dt01 = delayMotion.ResolveNormalizedT(_delayFollowElapsed);
            var dshape = EvaluateShape(delayMotion, dt01);
            _delayDisplayedFraction = Mathf.LerpUnclamped(_delayFollowFrom, _targetFraction, dshape);

            UpdateVisuals();
        }

        private bool HasDelayFollowMotion() => DelayFollowMotion.Mode != ValueMode.Constant || DelayFollowMotion.Constant != 0f
            || DelayFollowMotion.Time.Value != 0f;

        private void TickPadRepeat(float dt)
        {
            if (!_padActive)
            {
                return;
            }

            _padHeldSec += dt;
            if (!_padRepeating)
            {
                if (PadRepeatDelaySec > 0f && _padHeldSec >= PadRepeatDelaySec)
                {
                    _padRepeating = true;
                    _padLastRepeatSec = _padHeldSec;
                    StepPad();
                }
            }
            else if (PadRepeatIntervalSec > 0f && _padHeldSec - _padLastRepeatSec >= PadRepeatIntervalSec)
            {
                _padLastRepeatSec = _padHeldSec;
                StepPad();
            }
        }

        private void StepPad()
        {
            var sign = SignFor(_padDir);
            if (sign == 0)
            {
                return;
            }

            var amount = PadStepAmount * (_padFine ? FineStepMultiplier : 1f);
            SetValueInternal(_value + sign * amount, notify: true, commit: true);
        }

        // Codex レビュー対応(2026-09-11): 方針決定 — 十字キー/パッドの Right/Up は Direction(見た目の
        // 並び)に関わらず常に「値を増やす」(Wheel も同じ規則で既に Direction を見ていない)。
        // 一方、ポインタ操作(ComputePointerFraction)は Direction 通りの空間的な向きに従う(RightToLeft
        // なら画面右へドラッグすると値は減る)。これは意図的な非対称(キー入力は「Right=増加」という
        // 操作感を優先し、ポインタは見た目の並びに追従する)であり、SignFor は変更しない。
        private static int SignFor(MoveDirection dir)
        {
            switch (dir)
            {
                case MoveDirection.Right:
                case MoveDirection.Up:
                    return 1;
                case MoveDirection.Left:
                case MoveDirection.Down:
                    return -1;
                default:
                    return 0;
            }
        }

        // ── 内部: 吸着・端到達・ノッチ ──

        private float Clamp(float v) => Mathf.Clamp(v, Mathf.Min(Min, Max), Mathf.Max(Min, Max));

        private float NormalizedFromValue(float v) => Mathf.Approximately(Max, Min) ? 0f : Mathf.Clamp01((v - Min) / (Max - Min));

        private float ValueFromNormalized(float f) => Mathf.Lerp(Min, Max, Mathf.Clamp01(f));

        private float SnapValue(float v)
        {
            if (Notches > 0)
            {
                var frac = NormalizedFromValue(v);
                var notchFrac = 1f / Notches;
                var nearestIndex = Mathf.RoundToInt(frac / notchFrac);
                var nearestFrac = Mathf.Clamp01(nearestIndex * notchFrac);
                if (Mathf.Abs(frac - nearestFrac) <= SnapThreshold)
                {
                    v = ValueFromNormalized(nearestFrac);
                }
            }

            if (Step > 0f)
            {
                var stepsFromMin = Mathf.Round((v - Min) / Step);
                v = Min + stepsFromMin * Step;
            }

            if (WholeNumbers)
            {
                v = Mathf.Round(v);
            }

            return Clamp(v);
        }

        private int NotchIndexFor(float v)
        {
            if (Notches <= 0)
            {
                return -1;
            }

            var frac = NormalizedFromValue(v);
            return Mathf.RoundToInt(frac * Notches);
        }

        private void UpdateNotchTracking(float previous, float current)
        {
            if (Notches <= 0)
            {
                return;
            }

            var index = NotchIndexFor(current);
            if (index == _lastNotchIndex)
            {
                return;
            }

            _lastNotchIndex = index;
            OnNotchPassed?.Invoke(index);

            if (_notchSeElapsed >= (SliderSkin?.NotchSeMinIntervalSec ?? 0.04f))
            {
                _notchSeElapsed = 0f;
                PlaySe(SliderSkin?.NotchSe ?? default);
            }
        }

        private void UpdateLimitTracking()
        {
            var isMax = _value >= Max - 1e-5f;
            var isMin = _value <= Min + 1e-5f;

            if (isMax && !_atMax)
            {
                _atMax = true;
                OnLimitReached?.Invoke(true);
                PlaySe(SliderSkin?.LimitSe ?? default);
            }
            else if (!isMax)
            {
                _atMax = false;
            }

            if (isMin && !_atMin)
            {
                _atMin = true;
                OnLimitReached?.Invoke(false);
                PlaySe(SliderSkin?.LimitSe ?? default);
            }
            else if (!isMin)
            {
                _atMin = false;
            }
        }

        // ── 内部: 応答曲線 ──

        // Mode=Constant(未設定既定含む)は線形として扱う([18] B-1「応答曲線」)。
        // SliderEditor(4-17)の応答曲線グラフ/フィル計算からサンプリングできるよう public にしてある。
        public float EvaluateResponse(float p)
        {
            if (Response.Mode == ValueMode.Constant)
            {
                return Mathf.Clamp01(p);
            }

            return Mathf.Clamp01(Response.Evaluate(Mathf.Clamp01(p)));
        }

        // 値→つまみ位置の逆変換(単調増加前提の 16 分探索)。SliderEditor の応答曲線グラフでも使う。
        public float InverseResponse(float f)
        {
            if (Response.Mode == ValueMode.Constant)
            {
                return Mathf.Clamp01(f);
            }

            var lo = 0f;
            var hi = 1f;
            for (var i = 0; i < 16; i++)
            {
                var mid = (lo + hi) * 0.5f;
                var val = Mathf.Clamp01(Response.Evaluate(mid));
                if (val < f)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            return (lo + hi) * 0.5f;
        }

        // UiTweenManager.EvaluateShape と同じ考え方(Constant=1=即時反映)。
        private static float EvaluateShape(in ValueDef motion, float t)
        {
            switch (motion.Mode)
            {
                case ValueMode.Parametric:
                    return motion.Parametric.Evaluate(t);
                case ValueMode.Curve:
                    return motion.Curve != null ? motion.Curve.Evaluate(t) : t;
                default:
                    return 1f;
            }
        }

        // ── 内部: ビジュアル ──

        private void UpdateVisuals()
        {
            ApplyFillAndHandle(FillRect, HandleRect, _displayedFraction);
            ApplyFillAndHandle(DelayFillRect, null, _delayDisplayedFraction);
        }

        private void ApplyFillAndHandle(RectTransform fillRect, RectTransform handleRect, float displayedFraction)
        {
            var p = Mathf.Clamp01(InverseResponse(Mathf.Clamp01(displayedFraction)));

            if (fillRect != null)
            {
                var min = fillRect.anchorMin;
                var max = fillRect.anchorMax;
                switch (Direction)
                {
                    case SliderDirection.LeftToRight:
                        min.x = 0f;
                        max.x = p;
                        break;
                    case SliderDirection.RightToLeft:
                        min.x = 1f - p;
                        max.x = 1f;
                        break;
                    case SliderDirection.BottomToTop:
                        min.y = 0f;
                        max.y = p;
                        break;
                    case SliderDirection.TopToBottom:
                        min.y = 1f - p;
                        max.y = 1f;
                        break;
                }

                fillRect.anchorMin = min;
                fillRect.anchorMax = max;
            }

            if (handleRect != null && TrackRect != null)
            {
                var trackRect = TrackRect.rect;
                var pos = handleRect.anchoredPosition;
                switch (Direction)
                {
                    case SliderDirection.LeftToRight:
                        pos.x = Mathf.Lerp(0f, trackRect.width, p);
                        break;
                    case SliderDirection.RightToLeft:
                        pos.x = Mathf.Lerp(trackRect.width, 0f, p);
                        break;
                    case SliderDirection.BottomToTop:
                        pos.y = Mathf.Lerp(0f, trackRect.height, p);
                        break;
                    case SliderDirection.TopToBottom:
                        pos.y = Mathf.Lerp(trackRect.height, 0f, p);
                        break;
                }

                handleRect.anchoredPosition = pos;
            }
        }

        // ── SE ──

        private AssetId<SeMarker> ResolveDeniedSe()
        {
            var id = SliderSkin?.DeniedSe ?? default;
            return id.IsValid ? id : DefaultDeniedSe;
        }

        private static void PlaySe(AssetId<SeMarker> id)
        {
            if (id.IsValid)
            {
                Runtime.Audio.Audio.PlaySe(id);
            }
        }

        protected override void OnSkinApplied(in StateVisual v)
        {
        }

        // ── EventSystem 連携 ──

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            TrackClickAt(ComputePointerFraction(eventData));
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            base.OnPointerUp(eventData);
        }

        public void OnBeginDrag(PointerEventData eventData) => BeginDragAt(ComputePointerFraction(eventData));

        public void OnDrag(PointerEventData eventData) => DragTo(ComputePointerFraction(eventData));

        public void OnEndDrag(PointerEventData eventData) => EndDrag();

        public void OnScroll(PointerEventData eventData)
        {
            if (WheelEnabled)
            {
                Wheel(eventData.scrollDelta.y);
            }
        }

        // パッドの 4 方向はすべて値の操作(Right/Up = 増、Left/Down = 減。SignFor)として消費し、端に到達 + EscapeOnLimit の
        // ときだけフォーカスを隣へ抜ける([18] A-3)。2026-09-12: 以前は Move の戻り値を捨てていたため EscapeOnLimit が
        // 実行時に効いていなかった。「Direction と直交する方向は値を変えずにフォーカス移動」は未対応(既存の SignFor 仕様を維持)。
        public override void OnMove(AxisEventData eventData)
        {
            if (eventData != null && Move(eventData.moveDir))
            {
                base.OnMove(eventData);
            }
        }

        private float ComputePointerFraction(PointerEventData eventData)
        {
            var rt = TrackRect != null ? TrackRect : (RectTransform)transform;
            if (eventData == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, eventData.position, eventData.pressEventCamera, out var local))
            {
                return NormalizedFromValue(_value);
            }

            var rect = rt.rect;
            switch (Direction)
            {
                case SliderDirection.LeftToRight:
                    return Mathf.InverseLerp(rect.xMin, rect.xMax, local.x);
                case SliderDirection.RightToLeft:
                    return 1f - Mathf.InverseLerp(rect.xMin, rect.xMax, local.x);
                case SliderDirection.BottomToTop:
                    return Mathf.InverseLerp(rect.yMin, rect.yMax, local.y);
                case SliderDirection.TopToBottom:
                    return 1f - Mathf.InverseLerp(rect.yMin, rect.yMax, local.y);
                default:
                    return 0f;
            }
        }
    }
}
