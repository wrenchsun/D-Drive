using System;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Net;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Loading;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace DDrive.Runtime.Loop
{
    // [01_architecture.md] §11 / [02_core_framework.md] §14 — ランタイム唯一の Composition Root(2026-09-09)。
    // シーンに 1 つ置く(Tools > D-Drive > Generate > 起動オブジェクトをシーンに配置)。Awake で
    //   Registry(Addressables ローダー) → Pool → 各 Manager → AnchorGroupPlayer → AssetEventDispatcher
    // を生成し、GameLoop(GameLoopDriver)へ登録、静的ファサード(Audio / Vfx / Anim / Models / Anchors)を Bind する。
    // Start でカタログ(Inspector の直参照 + Addressables ラベル)を Registry に登録し、IsReady になる。
    // 破棄時は StopAll → ファサード Unbind → GameLoop から解除。
    //
    // 例外で止めない: Addressables が未設定でも Manager 群は組み上がり、未登録 ID は Placeholder で動く。
    [DefaultExecutionOrder(-1000)]
    [RequireComponent(typeof(GameLoopDriver))]
    [DisallowMultipleComponent]
    public sealed class DDriveRuntimeBootstrap : MonoBehaviour
    {
        public const string DefaultCatalogLabel = "DDriveCatalog";

        [Tooltip("起動時に Registry へ登録するカタログ(GameData/Catalogs)。Generate メニュー / Inspector の「カタログを再収集」で自動設定される")]
        public AssetCatalog[] Catalogs;

        [Tooltip("このラベルを持つ Addressables のカタログも起動時に集めて登録する(ビルド後にカタログを増やす運用向け)。空なら無効")]
        public string CatalogLabel = DefaultCatalogLabel;

        [Tooltip("シーンをまたいで生かす(通常 ON)。OFF ならシーン破棄と同時に全 Manager を停止する")]
        public bool KeepAcrossScenes = true;

        [Tooltip("静的ファサード(Audio.PlaySe 等)をこのインスタンスへ Bind する。テストや多重起動の検証で OFF にする")]
        public bool BindFacades = true;

        public static DDriveRuntimeBootstrap Instance { get; private set; }

        public GameLoopDriver Loop { get; private set; }
        public AssetRegistry Registry { get; private set; }
        public PoolService Pool { get; private set; }
        public INetBridge NetBridge { get; private set; }
        public AudioManager Audio { get; private set; }
        public BgmManager Bgm { get; private set; }
        public VfxManager Vfx { get; private set; }
        public AnimManager Anim { get; private set; }
        public ModelsManager Models { get; private set; }
        public MaterialManager Materials { get; private set; }
        public AnchorGroupPlayer Groups { get; private set; }
        public AssetEventDispatcher Dispatcher { get; private set; }

        // カタログ登録が終わったか(IsReady 前の Play は未登録 ID として Placeholder になる)。
        public bool IsReady { get; private set; }
        public int RegisteredCatalogCount { get; private set; }
        public event Action OnReady;

        private readonly UniTaskCompletionSource _ready = new();
        private AnchorGroupLoopAdapter _groupAdapter;
        private bool _built;

        public UniTask WhenReady => _ready.Task;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[DDrive] DDriveRuntimeBootstrap は既に '{Instance.name}' にあります。'{name}' の分は破棄します。");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (KeepAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
            }

            Loop = GetComponent<GameLoopDriver>();
            if (Loop == null)
            {
                Loop = gameObject.AddComponent<GameLoopDriver>();
            }

            Build();
        }

        private void Start()
        {
            RegisterCatalogsAsync().Forget();
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            Teardown();
            Instance = null;
        }

        // ── 組み立て ──

        private void Build()
        {
            if (_built)
            {
                return;
            }

            var instances = new GameObject("Instances");
            instances.transform.SetParent(transform, false);

            Pool = new PoolService();
            Pool.SetInstanceParent(instances.transform);
            Registry = new AssetRegistry(new AddressablesAssetLoader());
            NetBridge = new LocalLoopbackBridge(); // NGO 統合([14])時に差し替える注入点

            var seTemplate = new GameObject("SeSourceTemplate");
            seTemplate.transform.SetParent(transform, false);
            seTemplate.AddComponent<AudioSource>();
            seTemplate.SetActive(false);

            Audio = new AudioManager(Pool, Registry, seTemplate, NetBridge);
            Bgm = new BgmManager(Registry, CreateAudioChannel("BgmChannelA"), CreateAudioChannel("BgmChannelB"));
            Vfx = new VfxManager(Pool, Registry, NetBridge);
            Anim = new AnimManager(Registry);
            Materials = new MaterialManager(Registry);
            Models = new ModelsManager(Pool, Registry, Anim, Materials);
            Groups = new AnchorGroupPlayer(Registry, Vfx, Audio);
            Dispatcher = new AssetEventDispatcher(Anim.Events, Registry, Audio, Vfx, Anim.GetContextTransform, Groups);

            var loop = Loop.GameLoop;
            loop.Register(Audio);
            loop.Register(Bgm);
            loop.Register(Vfx);
            loop.Register(Anim);
            loop.Register(Materials);
            loop.Register(Models);
            _groupAdapter = new AnchorGroupLoopAdapter(Groups);
            loop.Register(_groupAdapter);

            if (BindFacades)
            {
                Runtime.Audio.Audio.Bind(Audio);
                Runtime.Audio.Audio.Bind(Bgm);
                Runtime.Vfx.Vfx.Bind(Vfx);
                Runtime.Anim.Anim.Bind(Anim);
                Mats.Bind(Materials);
                Runtime.Model.Models.Bind(Models);
                Anchors.Bind(Groups);
            }

            _built = true;
        }

        private AudioSource CreateAudioChannel(string channelName)
        {
            var go = new GameObject(channelName);
            go.transform.SetParent(transform, false);
            return go.AddComponent<AudioSource>();
        }

        private void Teardown()
        {
            if (!_built)
            {
                return;
            }

            var loop = Loop != null ? Loop.GameLoop : null;
            if (loop != null)
            {
                loop.StopAll(StopReason.SceneUnload);
                loop.Unregister(Audio);
                loop.Unregister(Bgm);
                loop.Unregister(Vfx);
                loop.Unregister(Anim);
                loop.Unregister(Materials);
                loop.Unregister(Models);
                loop.Unregister(_groupAdapter);
            }

            Dispatcher?.Dispose();
            Dispatcher = null;

            if (BindFacades)
            {
                Runtime.Audio.Audio.Bind((AudioManager)null);
                Runtime.Audio.Audio.Bind((BgmManager)null);
                Runtime.Vfx.Vfx.Bind(null);
                Runtime.Anim.Anim.Bind(null);
                Mats.Bind(null);
                Runtime.Model.Models.Bind(null);
                Anchors.Bind(null);
            }

            Pool?.Clear(PoolScope.Global);
            _built = false;
        }

        // ── カタログ ──

        public async UniTask RegisterCatalogsAsync()
        {
            if (IsReady)
            {
                return;
            }

            var count = 0;
            if (Catalogs != null)
            {
                foreach (var catalog in Catalogs)
                {
                    if (catalog == null)
                    {
                        continue;
                    }

                    await Registry.RegisterCatalogAsync(catalog);
                    count++;
                }
            }

            count += await RegisterLabeledCatalogsAsync();

            RegisteredCatalogCount = count;
            IsReady = true;
            _ready.TrySetResult();
            OnReady?.Invoke();
            if (count == 0)
            {
                Debug.LogWarning("[DDrive] カタログが 1 つも登録されていません。Inspector の Catalogs か Addressables のラベルを設定してください(全 ID が Placeholder になります)。");
            }
        }

        private async UniTask<int> RegisterLabeledCatalogsAsync()
        {
            if (string.IsNullOrEmpty(CatalogLabel))
            {
                return 0;
            }

            try
            {
                var locations = Addressables.LoadResourceLocationsAsync(CatalogLabel, typeof(AssetCatalog));
                await UniTask.WaitUntil(() => locations.IsDone);
                if (locations.Status != UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded || locations.Result == null || locations.Result.Count == 0)
                {
                    Addressables.Release(locations);
                    return 0;
                }

                var load = Addressables.LoadAssetsAsync<AssetCatalog>(locations.Result, null);
                await UniTask.WaitUntil(() => load.IsDone);
                var count = 0;
                if (load.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded && load.Result != null)
                {
                    foreach (var catalog in load.Result)
                    {
                        if (catalog == null || Contains(Catalogs, catalog))
                        {
                            continue;
                        }

                        await Registry.RegisterCatalogAsync(catalog);
                        count++;
                    }
                }

                Addressables.Release(locations);
                return count;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] Addressables のラベル '{CatalogLabel}' からカタログを集められませんでした: {e.Message}");
                return 0;
            }
        }

        private static bool Contains(AssetCatalog[] list, AssetCatalog catalog)
        {
            if (list == null)
            {
                return false;
            }

            for (var i = 0; i < list.Length; i++)
            {
                if (list[i] == catalog)
                {
                    return true;
                }
            }

            return false;
        }

        // AnchorGroupPlayer は IAssetManager ではない(Tick() のみ)ため、GameLoop に載せる薄い橋渡し。
        private sealed class AnchorGroupLoopAdapter : IAssetManager
        {
            private readonly AnchorGroupPlayer _player;

            public AnchorGroupLoopAdapter(AnchorGroupPlayer player) => _player = player;

            public Foundation.Identity.AssetType Type => Foundation.Identity.AssetType.AnchorGroup;

            public void Tick(float dt) => _player.Tick();

            public void OnPause(PauseChannel channel, bool paused)
            {
            }

            public void StopAll(StopReason reason) => _player.StopAll();

            public void OnSceneUnload() => _player.StopAll();
        }
    }
}
