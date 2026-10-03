using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Loader;
using DDrive.Foundation.Net;
using DDrive.Foundation.Registry;
using UnityEngine;

namespace ExternalContract.Tests
{
    // このアセンブリは D-Drive の internal(DDrive.Tests.Runtime の FakeAssetLoader / FakeNetBridge)を見られないため、
    // 契約テストに必要な最小のテストダブルをここに持つ。

    internal sealed class ExternalContractLoader : IAssetLoader
    {
        public readonly Dictionary<string, UnityEngine.Object> Assets = new();

        public UniTask<T> LoadAsync<T>(string address, CancellationToken ct) where T : UnityEngine.Object
        {
            Assets.TryGetValue(address, out var obj);
            return UniTask.FromResult(obj as T);
        }

        public void Release(string address)
        {
        }

        public UniTask PreloadAsync(IEnumerable<string> addresses, IProgress<float> progress)
        {
            progress?.Report(1f);
            return UniTask.CompletedTask;
        }
    }

    internal static class ExternalContractRegistry
    {
        // data を種別 type でカタログ登録して解決済みにする(AssetRegistry の公開 API だけを使う)。
        public static void Register(ExternalContractLoader loader, AssetRegistry registry, AssetType type, params AssetDataBase[] assets)
        {
            var entries = new List<CatalogEntry>();
            foreach (var a in assets)
            {
                var address = $"externalcontract/{a.Id}";
                loader.Assets[address] = a;
                entries.Add(new CatalogEntry { Id = a.Id, Type = type, Address = address });
            }

            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(entries);
            registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            foreach (var a in assets)
            {
                if (type == AssetType.Material)
                {
                    registry.ResolveAsync<DDrive.Runtime.Material.MaterialData>(a.Id).GetAwaiter().GetResult();
                }
                else if (type == AssetType.Cutscene)
                {
                    registry.ResolveAsync<DDrive.Runtime.Cutscene.CutsceneData>(a.Id).GetAwaiter().GetResult();
                }
            }

            UnityEngine.Object.DestroyImmediate(catalog);
        }
    }

    // 1 つのクライアント役(IsServer=false)が受け取るだけの最小 INetBridge。InjectReceive で受信を模擬する。
    internal sealed class ExternalContractBridge : INetBridge
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers = new();

        public bool IsServer { get; set; }
        public bool IsClient { get; set; } = true;
        public double NetworkTime { get; set; }
        public ulong LocalClientId { get; set; } = 1UL;

        public event Action<ulong> ClientConnected
        {
            add { }
            remove { }
        }

        public event Action<ulong, string> ClientDisconnected
        {
            add { }
            remove { }
        }

        public void InjectReceive<T>(ulong senderClientId, in T msg) where T : INetMessage
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
            {
                return;
            }

            foreach (var d in list.ToArray())
            {
                ((Action<ulong, T>)d)(senderClientId, msg);
            }
        }

        public void Broadcast<T>(in T msg, NetChannel channel) where T : INetMessage
        {
        }

        public void SendTo<T>(ulong clientId, in T msg, NetChannel channel) where T : INetMessage
        {
        }

        public IDisposable Subscribe<T>(Action<ulong, T> handler) where T : INetMessage
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
            {
                list = new List<Delegate>();
                _handlers[typeof(T)] = list;
            }

            list.Add(handler);
            return new Unsub(() => list.Remove(handler));
        }

        public Transform ResolveNetObject(ulong netId) => null;

        public ulong ResolveNetId(Transform transform) => 0UL;

        public bool IsLocalPlayerObject(Transform transform) => false;

        public ulong SpawnNetworked(GameObject root) => 0UL;

        public void DespawnNetworked(ulong netId, bool destroy)
        {
        }

        public void DisconnectClient(ulong clientId, string reason)
        {
        }

        private sealed class Unsub : IDisposable
        {
            private readonly Action _onDispose;

            public Unsub(Action onDispose) => _onDispose = onDispose;

            public void Dispose() => _onDispose();
        }
    }
}
