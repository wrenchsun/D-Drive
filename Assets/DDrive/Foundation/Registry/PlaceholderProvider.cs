using System;
using System.Collections.Generic;
using DDrive.Foundation.Data;
using UnityEngine;

namespace DDrive.Foundation.Registry
{
    // 種別ごとの Placeholder 生成を登録制にする(FR-1.4)。各アセット種別の Runtime モジュールが
    // 自分の Data 型向けに Register する。未登録の型は空インスタンスにフォールバックする。
    public static class PlaceholderProvider
    {
        private static readonly Dictionary<Type, Func<AssetDataBase>> Factories = new();
        private static readonly Dictionary<Type, AssetDataBase> Cache = new();

        public static void Register<T>(Func<T> factory) where T : AssetDataBase
        {
            Factories[typeof(T)] = () => factory();
            Cache.Remove(typeof(T));
        }

        public static void Unregister<T>() where T : AssetDataBase
        {
            Factories.Remove(typeof(T));
            Cache.Remove(typeof(T));
        }

        // Placeholder は型ごとに 1 インスタンスをキャッシュして使い回す。
        // 毎フレーム鳴らされる未登録 SE 等で呼ばれるため、毎回 CreateInstance すると
        // ScriptableObject(ネイティブ資源)が無制限にリークする。
        public static T Get<T>() where T : AssetDataBase
        {
            if (Cache.TryGetValue(typeof(T), out var cached) && cached != null)
            {
                return (T)cached;
            }

            T instance;
            if (Factories.TryGetValue(typeof(T), out var factory))
            {
                instance = (T)factory();
            }
            else
            {
                instance = ScriptableObject.CreateInstance<T>();
                instance.DisplayName = $"<Placeholder:{typeof(T).Name}>";
            }

            Cache[typeof(T)] = instance;
            return instance;
        }
    }
}
