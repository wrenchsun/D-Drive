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
    // OnSignal の購読先へ渡す引数。
    public readonly struct SignalArgs
    {
        public readonly string Key;
        public readonly Handle<CanvasMarker> Canvas;
        public readonly string ElementPath;

        public SignalArgs(string key, Handle<CanvasMarker> canvas, string elementPath)
        {
            Key = key;
            Canvas = canvas;
            ElementPath = elementPath;
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

        private GameObject _root;
        private readonly LayerRoot[] _layerRoots = new LayerRoot[5];
        private bool _eventSystemWarned;

        public AssetType Type => AssetType.Canvas;

        public EventBus Events => _events;

        // OpenTransition/CloseTransition の Kind=Anim を実際に再生するためのフック。未設定なら即時完了扱い
        // (UiButton/AnimEditor 側の配線は 4-2/4-6 以降)。
        public Func<AssetId<AnimMarker>, Animator, Handle<AnimMarker>> AnimHook;

        public UiManager(IPoolService pool, IAssetRegistry registry, PauseService pause = null, EventBus events = null)
        {
            _pool = pool;
            _registry = registry;
            _pause = pause;
            _events = events ?? new EventBus();
        }

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
            StartTransition(handle, instance, instance.Data.CloseTransition, isOpen: false);
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
                FinalizeClose(handle, instance);
            }
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

                var blocked = blockIndex >= 0 && i < blockIndex;
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

        private static void ApplySelectableNavigation(Selectable self, Transform root, NavNode node)
        {
            var nav = self.navigation;
            nav.mode = Navigation.Mode.Explicit;

            var up = ResolveSelectable(root, node.Up);
            if (up != null)
            {
                nav.selectOnUp = up;
            }

            var down = ResolveSelectable(root, node.Down);
            if (down != null)
            {
                nav.selectOnDown = down;
            }

            var left = ResolveSelectable(root, node.Left);
            if (left != null)
            {
                nav.selectOnLeft = left;
            }

            var right = ResolveSelectable(root, node.Right);
            if (right != null)
            {
                nav.selectOnRight = right;
            }

            self.navigation = nav;
        }

        private static void ApplyUiInteractableNavigation(Transform self, Transform root, NavNode node)
        {
            var nav = self.GetComponent<UiNavigation>();
            if (nav == null)
            {
                nav = self.gameObject.AddComponent<UiNavigation>();
            }

            var up = FindTransform(root, node.Up);
            if (up != null)
            {
                nav.Up = up;
            }

            var down = FindTransform(root, node.Down);
            if (down != null)
            {
                nav.Down = down;
            }

            var left = FindTransform(root, node.Left);
            if (left != null)
            {
                nav.Left = left;
            }

            var right = FindTransform(root, node.Right);
            if (right != null)
            {
                nav.Right = right;
            }
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

        // ── Handle 操作 ──

        public bool IsOpen(Handle<CanvasMarker> handle) => _instances.IsValidSilent(handle);

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

        public void SendSignal(string key, Handle<CanvasMarker> from, string elementPath = null)
        {
            if (string.IsNullOrEmpty(key) || !_signalSubs.TryGetValue(key, out var list))
            {
                return;
            }

            var args = new SignalArgs(key, from, elementPath);
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
