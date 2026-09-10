using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CanvasId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Ui.CanvasMarker>;
using EventTrigger = DDrive.Foundation.Event.EventTrigger; // UnityEngine.EventSystems.EventTrigger と衝突するため明示エイリアス

namespace DDrive.Runtime.Ui
{
    // OnSignal の購読先へ渡す引数。Value は SliderWire.Action=SendSignal のとき現在値が入る(4-16)。
    public readonly struct SignalArgs
    {
        public readonly string Key;
        public readonly Handle<CanvasMarker> Canvas;
        public readonly string ElementPath;
        public readonly float Value;

        public SignalArgs(string key, Handle<CanvasMarker> canvas, string elementPath, float value = 0f)
        {
            Key = key;
            Canvas = canvas;
            ElementPath = elementPath;
            Value = value;
        }
    }

    // [07_canvas_prefab.md] Part A-3 — Canvas(UI 画面)の Open/Close/スタック/モーダル/戻る/ナビゲーション/
    // シグナルを扱う Manager(チケット 4-1)。レイヤー(UiLayer)ごとのルート Canvas 配下に Prefab を
    // PoolService から Rent して配置し、単一のグローバルスタック(_stack)で開いている順序を管理する。
    // UiButton/ElementFx 本体(4-2/4-6/4-9)は未実装。ButtonWire/SliderWire はデータのみ持ち回す。
    public sealed class UiManager : IAssetManager
    {
        private sealed class LayerRoot
        {
            public Transform Root;
            public Canvas Canvas;
            public CanvasGroup Group;
        }

        private sealed class CanvasInstance
        {
            public CanvasData Data;
            public GameObject Root;
            public PooledObject Pooled; // Placeholder のときは null
            public bool IsPlaceholder;
            public bool IsPooled; // Flags.Pool.Kind == None は Close で Discard する(PrefabsManager と同じ規則)
            public CanvasGroup Group;
            public RectTransform Rect;
            public InstanceContext Context;
            public bool PausePushed;
            public bool IsModalBlocking;
            public bool Closing;
            public Vector2 BaseAnchoredPosition;
            public Vector3 BaseScale;
            // 4-2/4-6: ButtonWire で購読した UiButton イベントの解除アクション(Close で全て呼ぶ)。
            public List<Action> WireUnsubscribers;

            // 4-9: ElementFx(Appear/Idle/Disappear)のランタイム状態。ElementEffects が空なら null のまま。
            public List<ElementFxRuntime> ElementFx;
            public float ElementFxElapsed; // Open からの経過秒(AppearDelay の基準)
            public int PendingAppearCount; // まだ完了していない Appear の数(>0 の間は入力を受け付けない)
            public int PendingDisappearCount; // まだ完了していない Disappear の数(>0 の間は Close を確定しない)
            public bool CloseTransitionCompleted; // CloseTransition(演出)側が完了したか(TryFinalizeClose が両条件を見る)
        }

        // 1 ElementFx 行ぶんのランタイム状態(4-9)。
        private sealed class ElementFxRuntime
        {
            public RectTransform Target;
            public ElementFx Def;
            public bool HasAppear; // Appear/AppearPreset/レイヤー既定のいずれかが有効か(入力ゲートの初期カウント判定用)
            public bool AppearStarted;
            public bool AppearCompleted;
            public bool IdleStarted;
            public bool DisappearStarted;
            public bool DisappearDone;
            public Handle<UiTweenMarker> AppearHandle = Handle<UiTweenMarker>.Invalid;
            public Handle<UiTweenMarker> IdleHandle = Handle<UiTweenMarker>.Invalid;
            public Handle<UiTweenMarker> DisappearHandle = Handle<UiTweenMarker>.Invalid;
        }

        // Tick で毎フレーム進める演出。Kind=None または Duration<=0 は即完了として生成しない。
        private sealed class TransitionState
        {
            public Handle<CanvasMarker> Handle;
            public bool IsOpen;
            public UiTransition Def;
            public float Elapsed;
            public UniTaskCompletionSource Completion;
        }

        private sealed class SignalSubscription : IDisposable
        {
            private UiManager _owner;
            private string _key;
            private Action<SignalArgs> _cb;

            public SignalSubscription(UiManager owner, string key, Action<SignalArgs> cb)
            {
                _owner = owner;
                _key = key;
                _cb = cb;
            }

            public void Dispose()
            {
                if (_owner == null)
                {
                    return;
                }

                if (_owner._signalSubs.TryGetValue(_key, out var list))
                {
                    list.Remove(_cb);
                }

                _owner = null;
            }
        }

        public const string RootName = "[D-Drive] UI Root";

        private readonly IPoolService _pool;
        private readonly IAssetRegistry _registry;
        private readonly PauseService _pause;
        private readonly EventBus _events;
        private readonly InstanceStore<CanvasMarker, CanvasInstance> _instances = new();
        private readonly List<Handle<CanvasMarker>> _stack = new();
        private readonly List<TransitionState> _transitions = new();
        private readonly Dictionary<string, List<Action<SignalArgs>>> _signalSubs = new();
        private readonly HashSet<CanvasData> _placeholderWarned = new();
        private readonly HashSet<(CanvasData data, string path)> _elementPathWarned = new();

        private GameObject _root;
        private readonly LayerRoot[] _layerRoots = new LayerRoot[5];
        private bool _eventSystemWarned;
        private UiTweenManager _tweens;
        private UiLayerSettings _layerSettings;
        private OptionStore _options;

        public AssetType Type => AssetType.Canvas;

        public EventBus Events => _events;

        // OpenTransition/CloseTransition の Kind=Anim を実際に再生するためのフック。未設定なら即時完了扱い
        // (UiButton/AnimEditor 側の配線は 4-2/4-6 以降)。
        public Func<AssetId<AnimMarker>, Animator, Handle<AnimMarker>> AnimHook;

        public UiManager(IPoolService pool, IAssetRegistry registry, PauseService pause = null, EventBus events = null, UiTweenManager tweens = null)
        {
            _pool = pool;
            _registry = registry;
            _pause = pause;
            _events = events ?? new EventBus();
            _tweens = tweens;
        }

        // 4-9: ElementFx の再生に使う UiTweenManager。GameLoopDriver は UiTweenManager.Tick を別枠で回すため、
        // ここで受け取るのは「再生を頼む相手」だけで UiManager.Tick からは Tick(dt) を呼ばない。
        public void SetTweenManager(UiTweenManager tweens) => _tweens = tweens;

        // 4-9/4-7 残り: レイヤーごとの既定 Skin/SE/Appear/Disappear(未設定なら null のまま = フォールバック無し)。
        public void SetLayerSettings(UiLayerSettings settings) => _layerSettings = settings;

        // [18_ui_controls.md] B-4(4-16) — SliderWire.Action=SetOption の解決先。未設定なら SetOption は no-op。
        public void SetOptionStore(OptionStore options) => _options = options;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterPlaceholder()
        {
            PlaceholderProvider.Register(CreatePlaceholder);
        }

        // FR-1.4: 未登録 ID は Prefab が null のまま Placeholder になる(OpenData が空 RectTransform を生成する)。
        private static CanvasData CreatePlaceholder()
        {
            var data = ScriptableObject.CreateInstance<CanvasData>();
            data.DisplayName = "<Placeholder:CANVAS>";
            data.Prefab = null;
            return data;
        }

        // ── ルート/レイヤー ──

        private void EnsureRoot()
        {
            if (_root != null)
            {
                return;
            }

            _root = new GameObject(RootName);
            if (Application.isPlaying)
            {
                UnityEngine.Object.DontDestroyOnLoad(_root);
            }
            else
            {
                _root.hideFlags = HideFlags.DontSave;
            }

            var values = (UiLayer[])Enum.GetValues(typeof(UiLayer));
            for (var i = 0; i < values.Length; i++)
            {
                var layer = values[i];
                var go = new GameObject(layer.ToString(), typeof(RectTransform));
                go.transform.SetParent(_root.transform, false);

                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = (int)layer * 100;
                go.AddComponent<GraphicRaycaster>();
                var group = go.AddComponent<CanvasGroup>();

                _layerRoots[(int)layer] = new LayerRoot { Root = go.transform, Canvas = canvas, Group = group };
            }

            if (Application.isPlaying && !_eventSystemWarned && EventSystem.current == null)
            {
                _eventSystemWarned = true;
                Debug.LogWarning("[DDrive] UiManager: シーンに EventSystem がありません。UI の入力(選択/ナビゲーション)が動作しません。");
            }
        }

        // ── Open ──

        public Handle<CanvasMarker> Open(CanvasId id) => OpenData(_registry.ResolveOrPlaceholder<CanvasData>(id.Value));

        public Handle<CanvasMarker> Popup(CanvasId id) => OpenData(_registry.ResolveOrPlaceholder<CanvasData>(id.Value), modal: true);

        public async UniTask<Handle<CanvasMarker>> OpenAsync(CanvasId id)
        {
            var handle = Open(id);
            await AwaitOpenTransition(handle);
            return handle;
        }

        public async UniTask<Handle<CanvasMarker>> PopupAsync(CanvasId id)
        {
            var handle = Popup(id);
            await AwaitOpenTransition(handle);
            return handle;
        }

        public Handle<CanvasMarker> OpenData(CanvasData data, bool modal = false)
        {
            EnsureRoot();
            if (data == null)
            {
                return Handle<CanvasMarker>.Invalid;
            }

            var isPlaceholder = data.Prefab == null;
            GameObject root;
            PooledObject pooled = null;

            if (isPlaceholder)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (_placeholderWarned.Add(data))
                {
                    Debug.LogWarning($"[DDrive] CanvasData '{data.DisplayName}' has no Prefab assigned; opening an empty placeholder instead.");
                }
#endif
                root = new GameObject("<Placeholder:CANVAS>", typeof(RectTransform));
            }
            else
            {
                pooled = _pool.Rent(data.Prefab);
                root = pooled.GameObject;
            }

            var layerRoot = _layerRoots[(int)data.Layer];
            var rect = root.GetComponent<RectTransform>();
            if (rect == null)
            {
                rect = root.AddComponent<RectTransform>();
            }

            root.transform.SetParent(layerRoot.Root, false);

            var innerCanvas = root.GetComponent<Canvas>();
            if (innerCanvas != null)
            {
                innerCanvas.overrideSorting = true;
                innerCanvas.sortingOrder = (int)data.Layer * 100 + data.SortOffset;
            }
            else
            {
                var maxIndex = Mathf.Max(0, layerRoot.Root.childCount - 1);
                rect.SetSiblingIndex(Mathf.Clamp(data.SortOffset, 0, maxIndex));
            }

            var group = root.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = root.AddComponent<CanvasGroup>();
            }

            var instance = new CanvasInstance
            {
                Data = data,
                Root = root,
                Pooled = pooled,
                IsPlaceholder = isPlaceholder,
                IsPooled = !isPlaceholder && data.Flags.Pool.Kind == PoolPolicyKind.Pooled,
                Group = group,
                Rect = rect,
                BaseAnchoredPosition = rect.anchoredPosition,
                BaseScale = root.transform.localScale,
            };

            var handle = _instances.Add(instance);
            instance.Context = new InstanceContext(handle.Index, handle.Generation);
            _stack.Add(handle);

            ApplyNavigation(root.transform, data);
            ApplyFirstSelected(root.transform, data);
            WireButtons(root.transform, data, instance, handle);
            WireSliders(root.transform, data, instance, handle);
            ApplyLayerDefaults(root.transform, data);
            SetupElementFx(instance, data, root.transform);

            if (data.PauseGameWhileOpen && _pause != null)
            {
                _pause.Push(PauseChannel.Gameplay);
                instance.PausePushed = true;
            }

            if (modal && data.ModalBlocksInput)
            {
                instance.IsModalBlocking = true;
            }

            RecomputeBlocking();

            _events.Begin(instance.Context, data.Events);
            _events.Fire(instance.Context, EventTrigger.OnSpawn);
            _events.Fire(instance.Context, EventTrigger.OnEnable);

            StartTransition(handle, instance, data.OpenTransition, isOpen: true);

            return handle;
        }

        // ── Close ──

        public void Close(Handle<CanvasMarker> handle)
        {
            if (!_instances.TryGet(handle, out var instance) || instance.Closing)
            {
                return;
            }

            instance.Closing = true;
            instance.CloseTransitionCompleted = false;
            StartAllDisappearFx(instance); // PendingDisappearCount を確定させてから CloseTransition を始める
            StartTransition(handle, instance, instance.Data.CloseTransition, isOpen: false);
            TryFinalizeClose(handle, instance); // Duration<=0/Kind=None で上の StartTransition が同期完了した場合の後始末
        }

        public async UniTask CloseAsync(Handle<CanvasMarker> handle)
        {
            if (!_instances.TryGet(handle, out var instance) || instance.Closing)
            {
                return;
            }

            Close(handle);
            await AwaitCloseTransition(handle);
        }

        // 戻る操作。スタック上位から CloseOnBack==true かつ閉じ処理中でないものを 1 つ閉じる。
        public bool CloseTop()
        {
            for (var i = _stack.Count - 1; i >= 0; i--)
            {
                var handle = _stack[i];
                if (!_instances.TryGet(handle, out var instance) || instance.Closing)
                {
                    continue;
                }

                if (!instance.Data.CloseOnBack)
                {
                    continue;
                }

                Close(handle);
                return true;
            }

            return false;
        }

        public async UniTask<bool> CloseTopAsync()
        {
            for (var i = _stack.Count - 1; i >= 0; i--)
            {
                var handle = _stack[i];
                if (!_instances.TryGet(handle, out var instance) || instance.Closing || !instance.Data.CloseOnBack)
                {
                    continue;
                }

                await CloseAsync(handle);
                return true;
            }

            return false;
        }

        // ── 演出 ──

        private void StartTransition(Handle<CanvasMarker> handle, CanvasInstance instance, UiTransition def, bool isOpen)
        {
            ApplyTransitionFrame(instance, def, isOpen, isOpen ? 0f : 0f);

            if (def.Kind == UiTransitionKind.Anim && instance.Root != null)
            {
                var animator = instance.Root.GetComponentInChildren<Animator>(true);
                if (AnimHook != null && animator != null && def.Anim.IsValid)
                {
                    AnimHook(def.Anim, animator);
                }
            }

            if (def.Kind == UiTransitionKind.None || def.Kind == UiTransitionKind.Anim || def.Duration <= 0f)
            {
                CompleteTransition(handle, instance, def, isOpen);
                return;
            }

            var state = new TransitionState
            {
                Handle = handle,
                IsOpen = isOpen,
                Def = def,
                Elapsed = 0f,
                Completion = new UniTaskCompletionSource(),
            };
            _transitions.Add(state);
        }

        private static void ApplyTransitionFrame(CanvasInstance instance, UiTransition def, bool isOpen, float normalized)
        {
            var eased = def.Ease.Evaluate(Mathf.Clamp01(normalized));
            switch (def.Kind)
            {
                case UiTransitionKind.Fade:
                    if (instance.Group != null)
                    {
                        instance.Group.alpha = isOpen ? eased : 1f - eased;
                    }

                    break;
                case UiTransitionKind.Slide:
                    if (instance.Rect != null)
                    {
                        var progress = isOpen ? eased : 1f - eased; // 1=画面内, 0=オフセット位置
                        instance.Rect.anchoredPosition = instance.BaseAnchoredPosition + Vector2.Lerp(def.SlideOffset, Vector2.zero, progress);
                    }

                    break;
                case UiTransitionKind.Scale:
                    if (instance.Root != null)
                    {
                        var progress = isOpen ? eased : 1f - eased;
                        instance.Root.transform.localScale = Vector3.Lerp(Vector3.zero, instance.BaseScale, progress);
                    }

                    break;
            }
        }

        private void CompleteTransition(Handle<CanvasMarker> handle, CanvasInstance instance, UiTransition def, bool isOpen)
        {
            ApplyTransitionFrame(instance, def, isOpen, 1f);
            if (!isOpen)
            {
                // 4-9: CloseTransition(見た目の演出)自体はここで終わるが、実際に閉じる(FinalizeClose)のは
                // ElementFx の Disappear も全て終わってから(TryFinalizeClose が両方を見る)。
                instance.CloseTransitionCompleted = true;
                TryFinalizeClose(handle, instance);
            }
        }

        // CloseTransition と全 ElementFx.Disappear の両方が終わって初めて実際に閉じる([07] A-3 実装メモ)。
        private void TryFinalizeClose(Handle<CanvasMarker> handle, CanvasInstance instance)
        {
            if (!instance.CloseTransitionCompleted || instance.PendingDisappearCount > 0)
            {
                return;
            }

            if (!_instances.TryGet(handle, out _))
            {
                return; // 既に FinalizeClose 済み(二重呼び出しガード)
            }

            FinalizeClose(handle, instance);
        }

        private async UniTask AwaitOpenTransition(Handle<CanvasMarker> handle)
        {
            for (var i = 0; i < _transitions.Count; i++)
            {
                if (_transitions[i].Handle.Equals(handle) && _transitions[i].IsOpen)
                {
                    await _transitions[i].Completion.Task;
                    return;
                }
            }
        }

        private async UniTask AwaitCloseTransition(Handle<CanvasMarker> handle)
        {
            for (var i = 0; i < _transitions.Count; i++)
            {
                if (_transitions[i].Handle.Equals(handle) && !_transitions[i].IsOpen)
                {
                    await _transitions[i].Completion.Task;
                    return;
                }
            }
        }

        private void FinalizeClose(Handle<CanvasMarker> handle, CanvasInstance instance)
        {
            if (instance.WireUnsubscribers != null)
            {
                for (var i = 0; i < instance.WireUnsubscribers.Count; i++)
                {
                    instance.WireUnsubscribers[i]?.Invoke();
                }

                instance.WireUnsubscribers.Clear();
            }

            _events.Fire(instance.Context, EventTrigger.OnDisable);
            _events.Fire(instance.Context, EventTrigger.OnDestroy);
            _events.End(instance.Context);

            _stack.Remove(handle);
            _instances.Remove(handle);
            RecomputeBlocking();

            if (instance.PausePushed && _pause != null)
            {
                _pause.Pop(PauseChannel.Gameplay);
            }

            if (instance.IsPlaceholder)
            {
                if (instance.Root != null)
                {
                    UnityEngine.Object.Destroy(instance.Root);
                }
            }
            else if (instance.IsPooled)
            {
                _pool.Return(instance.Pooled);
            }
            else
            {
                _pool.Discard(instance.Pooled);
            }
        }

        // ── モーダルブロッキング ──

        // 一番上の「閉じ処理中でないモーダル」より下のスタックを全て非対話化する。
        private void RecomputeBlocking()
        {
            var blockIndex = -1;
            for (var i = _stack.Count - 1; i >= 0; i--)
            {
                if (_instances.TryGet(_stack[i], out var inst) && inst.IsModalBlocking && !inst.Closing)
                {
                    blockIndex = i;
                    break;
                }
            }

            for (var i = 0; i < _stack.Count; i++)
            {
                if (!_instances.TryGet(_stack[i], out var inst) || inst.Group == null)
                {
                    continue;
                }

                // 4-9: ElementFx の Appear が完了するまでは(モーダルブロックとは無関係に)入力を受け付けない。
                var blocked = (blockIndex >= 0 && i < blockIndex) || inst.PendingAppearCount > 0;
                inst.Group.interactable = !blocked;
                inst.Group.blocksRaycasts = !blocked;
            }
        }

        // ── ナビゲーション ──

        // uGUI Selectable(既存ボタン等)と UiInteractable(4-6, Selectable ではない)の両方に対応する。
        // 対象要素が Selectable なら従来通り Navigation を explicit 設定、UiInteractable なら
        // UiNavigation コンポーネントに Up/Down/Left/Right の Transform を持たせる(MoveFocus が辿る)。
        private static void ApplyNavigation(Transform root, CanvasData data)
        {
            if (data.Navigation == null)
            {
                return;
            }

            for (var i = 0; i < data.Navigation.Length; i++)
            {
                var node = data.Navigation[i];
                var self = FindTransform(root, node.Element);
                if (self == null)
                {
                    continue;
                }

                var selectable = self.GetComponent<Selectable>();
                if (selectable != null)
                {
                    ApplySelectableNavigation(selectable, root, node);
                    continue;
                }

                if (self.GetComponent<UiInteractable>() != null)
                {
                    ApplyUiInteractableNavigation(self, root, node);
                }
            }
        }

        // 4 方向すべて空の NavNode(「Selectable を自動収集」が作る初期状態)は Unity の自動ナビゲーションのままにする。
        // Explicit にして null リンクを入れると一切移動できなくなる(Codex レビュー 201285b P1)。
        // プールから再利用した実体に前回の明示リンクが残らないよう、指定が無い方向は null で上書きする。
        private static void ApplySelectableNavigation(Selectable self, Transform root, NavNode node)
        {
            var nav = self.navigation;
            var up = ResolveSelectable(root, node.Up);
            var down = ResolveSelectable(root, node.Down);
            var left = ResolveSelectable(root, node.Left);
            var right = ResolveSelectable(root, node.Right);
            var anyExplicit = up != null || down != null || left != null || right != null;

            nav.mode = anyExplicit ? Navigation.Mode.Explicit : Navigation.Mode.Automatic;
            nav.selectOnUp = up;
            nav.selectOnDown = down;
            nav.selectOnLeft = left;
            nav.selectOnRight = right;
            self.navigation = nav;
        }

        private static void ApplyUiInteractableNavigation(Transform self, Transform root, NavNode node)
        {
            var nav = self.GetComponent<UiNavigation>();
            if (nav == null)
            {
                nav = self.gameObject.AddComponent<UiNavigation>();
            }

            nav.Up = FindTransform(root, node.Up); // 指定なしは null(再利用時の古いリンクを消す)

            nav.Down = FindTransform(root, node.Down); // 指定なしは null(再利用時の古いリンクを消す)

            nav.Left = FindTransform(root, node.Left); // 指定なしは null(再利用時の古いリンクを消す)

            nav.Right = FindTransform(root, node.Right); // 指定なしは null(再利用時の古いリンクを消す)
        }

        // [15]/[18] 十字キー/スティックでのフォーカス移動。EventSystem.currentSelectedGameObject が
        // Selectable なら navigation を、UiNavigation を持つ UiInteractable ならそちらを辿る(最小実装)。
        public bool MoveFocus(Vector2 dir)
        {
            var current = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (current == null)
            {
                return false;
            }

            var t = current.transform;
            Transform target = null;

            var nav = t.GetComponent<UiNavigation>();
            if (nav != null)
            {
                target = ResolveDirection(nav, dir);
            }
            else
            {
                var selectable = t.GetComponent<Selectable>();
                if (selectable != null)
                {
                    var s = ResolveSelectableDirection(selectable, dir);
                    target = s != null ? s.transform : null;
                }
            }

            if (target == null)
            {
                return false;
            }

            EventSystem.current.SetSelectedGameObject(target.gameObject);
            return true;
        }

        private static Transform ResolveDirection(UiNavigation nav, Vector2 dir)
        {
            if (dir.y > 0.5f)
            {
                return nav.Up;
            }

            if (dir.y < -0.5f)
            {
                return nav.Down;
            }

            if (dir.x < -0.5f)
            {
                return nav.Left;
            }

            if (dir.x > 0.5f)
            {
                return nav.Right;
            }

            return null;
        }

        private static Selectable ResolveSelectableDirection(Selectable s, Vector2 dir)
        {
            if (dir.y > 0.5f)
            {
                return s.navigation.selectOnUp;
            }

            if (dir.y < -0.5f)
            {
                return s.navigation.selectOnDown;
            }

            if (dir.x < -0.5f)
            {
                return s.navigation.selectOnLeft;
            }

            if (dir.x > 0.5f)
            {
                return s.navigation.selectOnRight;
            }

            return null;
        }

        // 4-3: CanvasEditorWindow のパッド操作シミュレーション向け。EventSystem に頼らずデータだけから
        // 次のフォーカス先を解決する(1: NavNode の明示リンク、2: Selectable/UiInteractable の中から
        // 指定方向にある最も近いものを Unity の自動ナビゲーションに近い基準で選ぶ)。
        // EventSystem.current があれば実際に選択も反映する(無ければ呼び出し元が nextPath を保持する)。
        public bool MoveFocusFrom(Handle<CanvasMarker> handle, string currentPath, Vector2 dir, out string nextPath)
        {
            nextPath = currentPath;
            if (!_instances.TryGet(handle, out var instance) || instance.Root == null)
            {
                return false;
            }

            var root = instance.Root.transform;
            var data = instance.Data;

            if (data.Navigation != null)
            {
                for (var i = 0; i < data.Navigation.Length; i++)
                {
                    var node = data.Navigation[i];
                    if (!string.Equals(node.Element ?? string.Empty, currentPath ?? string.Empty, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var explicitPath = ResolveDirectionPath(node, dir);
                    if (!string.IsNullOrEmpty(explicitPath))
                    {
                        nextPath = explicitPath;
                        ApplyFocusPath(root, nextPath);
                        return true;
                    }

                    break;
                }
            }

            var currentT = FindTransform(root, currentPath);
            var currentRect = currentT as RectTransform;
            if (currentRect == null)
            {
                return false;
            }

            var currentCenter = RectCenter(currentRect);
            Transform best = null;
            var bestScore = float.MaxValue;

            foreach (var candidate in CollectFocusableTransforms(root))
            {
                if (candidate == currentT || candidate is not RectTransform rt)
                {
                    continue;
                }

                var delta = RectCenter(rt) - currentCenter;
                if (!IsInDirection(delta, dir))
                {
                    continue;
                }

                var primary = Mathf.Abs(Vector2.Dot(delta, dir));
                var lateral = Mathf.Abs(Vector2.Dot(delta, new Vector2(-dir.y, dir.x)));
                var score = primary + lateral * 2f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (best == null)
            {
                return false;
            }

            nextPath = GetRelativePath(root, best);
            ApplyFocusPath(root, nextPath);
            return true;
        }

        private static string ResolveDirectionPath(NavNode node, Vector2 dir)
        {
            if (dir.y > 0.5f)
            {
                return node.Up;
            }

            if (dir.y < -0.5f)
            {
                return node.Down;
            }

            if (dir.x < -0.5f)
            {
                return node.Left;
            }

            if (dir.x > 0.5f)
            {
                return node.Right;
            }

            return null;
        }

        private static bool IsInDirection(Vector2 delta, Vector2 dir)
        {
            if (delta.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            return Vector2.Dot(delta.normalized, dir.normalized) > 0.2f;
        }

        private static Vector2 RectCenter(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            return (corners[0] + corners[2]) * 0.5f;
        }

        private static IEnumerable<Transform> CollectFocusableTransforms(Transform root)
        {
            foreach (var s in root.GetComponentsInChildren<Selectable>(true))
            {
                if (s.interactable)
                {
                    yield return s.transform;
                }
            }

            foreach (var ui in root.GetComponentsInChildren<UiInteractable>(true))
            {
                if (ui.CanFocus)
                {
                    yield return ui.transform;
                }
            }
        }

        // EventSystem があるときだけ実際に選択を反映する(無ければ呼び出し元が nextPath を保持するだけでよい)。
        private static void ApplyFocusPath(Transform root, string path)
        {
            var t = FindTransform(root, path);
            if (t == null || EventSystem.current == null)
            {
                return;
            }

            var selectable = t.GetComponent<Selectable>();
            if (selectable != null)
            {
                selectable.Select();
                return;
            }

            if (t.GetComponent<UiInteractable>() != null)
            {
                EventSystem.current.SetSelectedGameObject(t.gameObject);
            }
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root)
            {
                return string.Empty;
            }

            var names = new List<string>();
            var cur = target;
            while (cur != null && cur != root)
            {
                names.Add(cur.name);
                cur = cur.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        // ── ボタン配線(ButtonWire, 4-2/4-6) ──

        private void WireButtons(Transform root, CanvasData data, CanvasInstance instance, Handle<CanvasMarker> handle)
        {
            if (data.Buttons == null || data.Buttons.Length == 0)
            {
                return;
            }

            instance.WireUnsubscribers = new List<Action>();
            for (var i = 0; i < data.Buttons.Length; i++)
            {
                var wire = data.Buttons[i];
                var target = FindTransform(root, wire.ButtonPath);
                var button = target != null ? target.GetComponent<UiButton>() : null;
                if (button == null)
                {
                    continue;
                }

                // クロージャは Open 時に配線の数だけ生成される(Tick 経路ではないため許容。[12]§3)。
                void Handler() => ExecuteButtonWire(wire, handle);
                SubscribeWire(button, wire.Trigger, Handler, instance.WireUnsubscribers);
            }
        }

        private static void SubscribeWire(UiButton button, WireTrigger trigger, Action handler, List<Action> unsubscribers)
        {
            switch (trigger)
            {
                case WireTrigger.Click:
                    button.OnClick += handler;
                    unsubscribers.Add(() => button.OnClick -= handler);
                    break;
                case WireTrigger.DoubleClick:
                    button.OnDoubleClick += handler;
                    unsubscribers.Add(() => button.OnDoubleClick -= handler);
                    break;
                case WireTrigger.LongPress:
                    button.OnLongPress += handler;
                    unsubscribers.Add(() => button.OnLongPress -= handler);
                    break;
                case WireTrigger.Repeat:
                    button.OnRepeat += handler;
                    unsubscribers.Add(() => button.OnRepeat -= handler);
                    break;
            }
        }

        private void ExecuteButtonWire(ButtonWire wire, Handle<CanvasMarker> from)
        {
            if (wire.ClickSe.IsValid)
            {
                Runtime.Audio.Audio.PlaySe(wire.ClickSe);
            }

            switch (wire.Action)
            {
                case UiAction.OpenCanvas:
                    if (wire.Target.IsAssigned)
                    {
                        OpenAsync(new CanvasId(wire.Target.Id, AssetType.Canvas)).Forget();
                    }

                    break;

                case UiAction.CloseSelf:
                    Close(from);
                    break;

                case UiAction.CloseTop:
                    CloseTop();
                    break;

                case UiAction.SendSignal:
                    SendSignal(wire.SignalKey, from, wire.ButtonPath);
                    break;

                case UiAction.PlayPresentation:
                    Debug.LogWarning("[DDrive] ButtonWire.Action=PlayPresentation は Phase 5 で実装予定です");
                    break;
            }
        }

        // ── スライダー配線(SliderWire, 4-16) ──

        private void WireSliders(Transform root, CanvasData data, CanvasInstance instance, Handle<CanvasMarker> handle)
        {
            if (data.Sliders == null || data.Sliders.Length == 0)
            {
                return;
            }

            instance.WireUnsubscribers ??= new List<Action>();
            for (var i = 0; i < data.Sliders.Length; i++)
            {
                var wire = data.Sliders[i];
                var target = FindTransform(root, wire.ElementPath);
                var slider = target != null ? target.GetComponent<UiSlider>() : null;
                if (slider == null)
                {
                    continue;
                }

                if (wire.Action == UiAction.SetOption && wire.Option != OptionKey.None && _options != null)
                {
                    var normalized = _options.Get(wire.Option);
                    slider.SetValueSilent(Mathf.Lerp(slider.Min, slider.Max, normalized));
                }

                if (wire.Trigger == SliderTrigger.Changed && wire.ThrottleSec > slider.ChangeThrottleSec)
                {
                    slider.ChangeThrottleSec = wire.ThrottleSec;
                }

                SubscribeSliderWire(slider, wire, handle, instance.WireUnsubscribers);
            }
        }

        private void SubscribeSliderWire(UiSlider slider, SliderWire wire, Handle<CanvasMarker> handle, List<Action> unsubscribers)
        {
            switch (wire.Trigger)
            {
                case SliderTrigger.Changed:
                {
                    void OnChanged(float v) => ExecuteSliderWire(wire, handle, slider, v);
                    slider.OnValueChanged += OnChanged;
                    unsubscribers.Add(() => slider.OnValueChanged -= OnChanged);
                    break;
                }

                case SliderTrigger.Commit:
                {
                    void OnCommit(float v) => ExecuteSliderWire(wire, handle, slider, v);
                    slider.OnCommit += OnCommit;
                    unsubscribers.Add(() => slider.OnCommit -= OnCommit);
                    break;
                }

                case SliderTrigger.NotchPassed:
                {
                    void OnNotch(int idx) => ExecuteSliderWire(wire, handle, slider, idx);
                    slider.OnNotchPassed += OnNotch;
                    unsubscribers.Add(() => slider.OnNotchPassed -= OnNotch);
                    break;
                }

                case SliderTrigger.LimitReached:
                {
                    void OnLimit(bool isMax) => ExecuteSliderWire(wire, handle, slider, isMax ? 1f : 0f);
                    slider.OnLimitReached += OnLimit;
                    unsubscribers.Add(() => slider.OnLimitReached -= OnLimit);
                    break;
                }
            }
        }

        private void ExecuteSliderWire(SliderWire wire, Handle<CanvasMarker> from, UiSlider slider, float value)
        {
            switch (wire.Action)
            {
                case UiAction.SetOption:
                    if (wire.Option != OptionKey.None)
                    {
                        _options?.Set(wire.Option, slider.NormalizedValue);
                    }

                    break;

                case UiAction.SendSignal:
                    SendSignal(wire.SignalKey, from, wire.ElementPath, value);
                    break;

                case UiAction.PlayPresentation:
                    Debug.LogWarning("[DDrive] SliderWire.Action=PlayPresentation は Phase 5 で実装予定です");
                    break;
            }
        }

        private static void ApplyFirstSelected(Transform root, CanvasData data)
        {
            if (string.IsNullOrEmpty(data.FirstSelected))
            {
                return;
            }

            var target = FindTransform(root, data.FirstSelected);
            if (target != null && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(target.gameObject);
            }
        }

        private static Selectable ResolveSelectable(Transform root, string path)
        {
            var t = FindTransform(root, path);
            return t != null ? t.GetComponent<Selectable>() : null;
        }

        private static Transform FindTransform(Transform root, string path)
            => string.IsNullOrEmpty(path) ? null : root.Find(path);

        // ── レイヤー既定(4-7 残り: UiLayerSettings) ──

        // Open 時、SkinId 未設定 かつ 明示 SetVisual 未実行の UiInteractable にだけレイヤー既定 Skin/SE を配る。
        private void ApplyLayerDefaults(Transform root, CanvasData data)
        {
            if (_layerSettings == null || !_layerSettings.TryGet(data.Layer, out var defaults))
            {
                return;
            }

            ControlSkinData skin = null;
            if (defaults.DefaultButtonSkin.IsValid && _registry != null)
            {
                _registry.TryResolveSync(defaults.DefaultButtonSkin.Value, out skin);
            }

            var interactables = root.GetComponentsInChildren<UiInteractable>(true);
            for (var i = 0; i < interactables.Length; i++)
            {
                var ui = interactables[i];
                if (skin != null)
                {
                    ui.ApplyDefaultSkin(skin);
                }

                ui.SetDefaultSe(defaults.DefaultClickSe, defaults.DefaultHoverSe, defaults.DefaultDeniedSe);
            }
        }

        // ── ElementFx(4-9: Appear / Idle / Disappear) ──

        // Open 直後に 1 回だけ呼ぶ。要素を解決してランタイム一覧を作り、Appear を持つ要素の数だけ
        // PendingAppearCount を積む(実際の再生開始は Tick の Delay 待ちを経て行う)。
        private void SetupElementFx(CanvasInstance instance, CanvasData data, Transform root)
        {
            if (data.ElementEffects == null || data.ElementEffects.Length == 0)
            {
                return;
            }

            var layerDefaults = default(UiLayerDefaultEntry);
            _layerSettings?.TryGet(data.Layer, out layerDefaults);
            var hasLayerAppear = _layerSettings != null && layerDefaults.DefaultAppear.Preset != UiPreset.None;

            var list = new List<ElementFxRuntime>(data.ElementEffects.Length);
            for (var i = 0; i < data.ElementEffects.Length; i++)
            {
                var def = data.ElementEffects[i];
                var target = FindTransform(root, def.ElementPath) as RectTransform;
                if (target == null)
                {
                    if (_elementPathWarned.Add((data, def.ElementPath)))
                    {
                        Debug.LogWarning($"[DDrive] CanvasData '{data.DisplayName}' の ElementFx[{i}] '{def.ElementPath}' が Prefab 内(RectTransform)で見つかりません。この行はスキップします。");
                    }

                    continue;
                }

                var hasAppear = def.Appear.IsValid || def.AppearPreset.Preset != UiPreset.None || hasLayerAppear;
                var runtime = new ElementFxRuntime { Target = target, Def = def, HasAppear = hasAppear };
                list.Add(runtime);
                if (hasAppear)
                {
                    instance.PendingAppearCount++;
                }
            }

            instance.ElementFx = list;
        }

        // 戻り値: PendingAppearCount(入力ゲート)が変化したか(呼び出し元 Tick が RecomputeBlocking を呼ぶかの判断に使う)。
        private bool TickElementFx(Handle<CanvasMarker> handle, CanvasInstance instance, float dt)
        {
            var list = instance.ElementFx;
            var gateChanged = false;

            if (!instance.Closing)
            {
                instance.ElementFxElapsed += dt;
                for (var i = 0; i < list.Count; i++)
                {
                    var r = list[i];
                    if (!r.AppearStarted)
                    {
                        if (instance.ElementFxElapsed < r.Def.AppearDelay)
                        {
                            continue;
                        }

                        PlayAppear(instance, r);
                    }

                    if (!r.AppearCompleted && !IsTweenPlaying(r.AppearHandle))
                    {
                        gateChanged |= OnAppearCompleted(instance, r);
                    }
                }
            }
            else
            {
                for (var i = 0; i < list.Count; i++)
                {
                    var r = list[i];
                    if (r.DisappearStarted && !r.DisappearDone && !IsTweenPlaying(r.DisappearHandle))
                    {
                        r.DisappearDone = true;
                        instance.PendingDisappearCount = Mathf.Max(0, instance.PendingDisappearCount - 1);
                    }
                }

                if (instance.PendingDisappearCount <= 0)
                {
                    TryFinalizeClose(handle, instance);
                }
            }

            return gateChanged;
        }

        private void PlayAppear(CanvasInstance instance, ElementFxRuntime r)
        {
            r.AppearStarted = true;
            if (r.Def.Appear.IsValid)
            {
                r.AppearHandle = _tweens?.Play(r.Def.Appear, r.Target) ?? Handle<UiTweenMarker>.Invalid;
            }
            else if (r.Def.AppearPreset.Preset != UiPreset.None)
            {
                r.AppearHandle = PlayPreset(r.Def.AppearPreset, r.Target);
            }
            else if (_layerSettings != null && _layerSettings.TryGet(instance.Data.Layer, out var defaults) && defaults.DefaultAppear.Preset != UiPreset.None)
            {
                r.AppearHandle = PlayPreset(defaults.DefaultAppear, r.Target);
            }
            else
            {
                r.AppearHandle = Handle<UiTweenMarker>.Invalid;
            }

            if (r.Def.AppearSe.IsValid)
            {
                Runtime.Audio.Audio.PlaySe(r.Def.AppearSe);
            }
        }

        // Appear が完了したら Idle を開始する。戻り値は PendingAppearCount が変化したか。
        private bool OnAppearCompleted(CanvasInstance instance, ElementFxRuntime r)
        {
            r.AppearCompleted = true;
            var changed = false;
            if (r.HasAppear && instance.PendingAppearCount > 0)
            {
                instance.PendingAppearCount--;
                changed = true;
            }

            StartIdle(r);
            return changed;
        }

        private void StartIdle(ElementFxRuntime r)
        {
            if (r.IdleStarted)
            {
                return;
            }

            r.IdleStarted = true;
            if (r.Def.Idle.IsValid)
            {
                r.IdleHandle = _tweens?.Play(r.Def.Idle, r.Target) ?? Handle<UiTweenMarker>.Invalid;
            }
            else if (r.Def.IdlePreset.Preset != UiPreset.None)
            {
                r.IdleHandle = PlayPreset(r.Def.IdlePreset, r.Target);
            }
        }

        // Close 開始時に全 ElementFx の Disappear を一斉に始める(Appear のような Delay スタッガーは無い)。
        private void StartAllDisappearFx(CanvasInstance instance)
        {
            instance.PendingDisappearCount = 0;
            var list = instance.ElementFx;
            if (list == null)
            {
                return;
            }

            for (var i = 0; i < list.Count; i++)
            {
                StartDisappear(instance, list[i]);
            }
        }

        private void StartDisappear(CanvasInstance instance, ElementFxRuntime r)
        {
            if (r.DisappearStarted)
            {
                return;
            }

            r.DisappearStarted = true;

            // Stop は無効/既に完了済みの Handle に対しても安全な no-op(UiTweenManager.Stop 参照)なので、
            // Idle/Appear が実際に生きているかを確認せずそのまま呼んでよい。
            _tweens?.Stop(r.IdleHandle);
            r.IdleHandle = Handle<UiTweenMarker>.Invalid;
            _tweens?.Stop(r.AppearHandle);

            Handle<UiTweenMarker> handle;
            if (r.Def.Disappear.IsValid)
            {
                handle = _tweens?.Play(r.Def.Disappear, r.Target) ?? Handle<UiTweenMarker>.Invalid;
            }
            else if (r.Def.DisappearPreset.Preset != UiPreset.None)
            {
                handle = PlayPreset(r.Def.DisappearPreset, r.Target);
            }
            else
            {
                handle = PlayLayerDefaultDisappear(instance, r);
            }

            r.DisappearHandle = handle;
            if (r.Def.DisappearSe.IsValid)
            {
                Runtime.Audio.Audio.PlaySe(r.Def.DisappearSe);
            }

            if (!IsTweenPlaying(handle))
            {
                r.DisappearDone = true;
            }
            else
            {
                instance.PendingDisappearCount++;
            }
        }

        private Handle<UiTweenMarker> PlayLayerDefaultDisappear(CanvasInstance instance, ElementFxRuntime r)
        {
            if (_layerSettings != null && _layerSettings.TryGet(instance.Data.Layer, out var defaults) && defaults.DefaultDisappear.Preset != UiPreset.None)
            {
                return PlayPreset(defaults.DefaultDisappear, r.Target);
            }

            return Handle<UiTweenMarker>.Invalid;
        }

        // 演出を待たず即完了させる(StopAll 用)。実体を巻き戻さず終端値へジャンプする(complete:true)。
        private void StopAllElementFx(CanvasInstance instance)
        {
            var list = instance.ElementFx;
            if (list == null)
            {
                return;
            }

            for (var i = 0; i < list.Count; i++)
            {
                var r = list[i];
                // 無効/終了済み Handle への Stop は no-op(UiTweenManager.Stop 参照)なので判定せずまとめて呼ぶ。
                _tweens?.Stop(r.AppearHandle, complete: true);
                _tweens?.Stop(r.IdleHandle);
                _tweens?.Stop(r.DisappearHandle, complete: true);
            }

            instance.PendingAppearCount = 0;
            instance.PendingDisappearCount = 0;
        }

        // UiPresetRef → TweenTrack[] へ展開して再生する(UiFx.Play と同じ手順。静的ファサードに依存せず
        // このインスタンスの _tweens を直接使う)。
        private Handle<UiTweenMarker> PlayPreset(in UiPresetRef p, RectTransform target)
        {
            if (_tweens == null || target == null || p.Preset == UiPreset.None)
            {
                return Handle<UiTweenMarker>.Invalid;
            }

            var buffer = new TweenTrack[UiTweenManager.MaxTracksPerTween];
            var count = UiPresetFactory.Build(in p, target, buffer);
            if (count <= 0)
            {
                return Handle<UiTweenMarker>.Invalid;
            }

            if (p.Se.IsValid)
            {
                Runtime.Audio.Audio.PlaySe(p.Se);
            }

            return _tweens.PlayTracks(buffer, count, target);
        }

        private bool IsTweenPlaying(Handle<UiTweenMarker> handle) => _tweens != null && _tweens.IsPlaying(handle);

        // ── Handle 操作 ──

        public bool IsOpen(Handle<CanvasMarker> handle) => _instances.IsValidSilent(handle);

        // 4-9: テスト/デザイナーツール向け。Appear 待ち(入力ブロック中)/ Close の Disappear+CloseTransition 待ち。
        public bool IsOpening(Handle<CanvasMarker> handle)
            => _instances.TryGet(handle, out var instance) && !instance.Closing && instance.PendingAppearCount > 0;

        public bool IsClosing(Handle<CanvasMarker> handle)
            => _instances.TryGet(handle, out var instance) && instance.Closing;

        public int StackCount => _stack.Count;

        public Handle<CanvasMarker> Top => _stack.Count > 0 ? _stack[_stack.Count - 1] : Handle<CanvasMarker>.Invalid;

        public GameObject GetGameObject(Handle<CanvasMarker> handle)
            => _instances.TryGet(handle, out var instance) ? instance.Root : null;

        // 発火元 Instance(InstanceContext)の Transform。AssetEventDispatcher が SE/VFX の contextRoot に使う([05] AnimManager と同じ配線)。
        public Transform GetContextTransform(InstanceContext ctx)
        {
            for (var i = 0; i < _stack.Count; i++)
            {
                if (_instances.TryGet(_stack[i], out var instance) && instance.Context.Equals(ctx))
                {
                    return instance.Root != null ? instance.Root.transform : null;
                }
            }

            return null;
        }

        public T GetComponent<T>(Handle<CanvasMarker> handle, string relativePath = null) where T : Component
        {
            if (!_instances.TryGet(handle, out var instance) || instance.Root == null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(relativePath))
            {
                return instance.Root.GetComponent<T>();
            }

            var t = instance.Root.transform.Find(relativePath);
            return t != null ? t.GetComponent<T>() : null;
        }

        public void SetLayerVisible(UiLayer layer, bool visible)
        {
            EnsureRoot();
            var lr = _layerRoots[(int)layer];
            lr.Group.alpha = visible ? 1f : 0f;
            lr.Group.interactable = visible;
            lr.Group.blocksRaycasts = visible;
        }

        // ── シグナル ──

        public IDisposable OnSignal(string key, Action<SignalArgs> cb)
        {
            if (string.IsNullOrEmpty(key) || cb == null)
            {
                return NullDisposable.Instance;
            }

            if (!_signalSubs.TryGetValue(key, out var list))
            {
                list = new List<Action<SignalArgs>>();
                _signalSubs[key] = list;
            }

            list.Add(cb);
            return new SignalSubscription(this, key, cb);
        }

        public void SendSignal(string key, Handle<CanvasMarker> from, string elementPath = null, float value = 0f)
        {
            if (string.IsNullOrEmpty(key) || !_signalSubs.TryGetValue(key, out var list))
            {
                return;
            }

            var args = new SignalArgs(key, from, elementPath, value);
            for (var i = 0; i < list.Count; i++)
            {
                list[i]?.Invoke(args);
            }
        }

        // ── Tick / Pause / StopAll ──

        public void Tick(float dt)
        {
            for (var i = _transitions.Count - 1; i >= 0; i--)
            {
                var t = _transitions[i];
                if (!_instances.TryGet(t.Handle, out var instance))
                {
                    _transitions.RemoveAt(i);
                    t.Completion.TrySetResult();
                    continue;
                }

                t.Elapsed += dt;
                var normalized = t.Def.Duration > 0f ? t.Elapsed / t.Def.Duration : 1f;
                if (normalized >= 1f)
                {
                    _transitions.RemoveAt(i);
                    CompleteTransition(t.Handle, instance, t.Def, t.IsOpen);
                    t.Completion.TrySetResult();
                }
                else
                {
                    ApplyTransitionFrame(instance, t.Def, t.IsOpen, normalized);
                }
            }

            // 4-9: ElementFx(スタッガー再生の Delay 進行 / Appear-Idle-Disappear の進行監視)。
            // FinalizeClose が _stack.Remove を呼ぶことがあるため後ろから走査する(既存の _transitions ループと同じ規則)。
            var gateChanged = false;
            for (var i = _stack.Count - 1; i >= 0; i--)
            {
                var handle = _stack[i];
                if (!_instances.TryGet(handle, out var instance) || instance.ElementFx == null)
                {
                    continue;
                }

                gateChanged |= TickElementFx(handle, instance, dt);
            }

            if (gateChanged)
            {
                RecomputeBlocking();
            }
        }

        public void OnPause(PauseChannel channel, bool paused)
        {
            // Canvas 自体はポーズで特別な処理をしない(PauseGameWhileOpen は Push/Pop する側)。
        }

        // 演出を待たず即座に全て閉じる(シーン破棄等)。
        public void StopAll(StopReason reason)
        {
            for (var i = _transitions.Count - 1; i >= 0; i--)
            {
                _transitions[i].Completion.TrySetResult();
            }

            _transitions.Clear();

            for (var i = _stack.Count - 1; i >= 0; i--)
            {
                if (_instances.TryGet(_stack[i], out var instance))
                {
                    StopAllElementFx(instance); // 演出を待たず即完了(Stop(complete:true))させてから閉じる
                    FinalizeClose(_stack[i], instance);
                }
            }

            _stack.Clear();
        }

        public void OnSceneUnload() => StopAll(StopReason.SceneUnload);
    }

    // OnSignal の戻り値の既定実装(key/cb が無効なときの no-op)。
    internal sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose()
        {
        }
    }
}
