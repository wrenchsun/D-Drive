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

        public static void Register<T>(Func<T> factory) where T : AssetDataBase
        {
            Factories[typeof(T)] = () => factory();
        }

        public static void Unregister<T>() where T : AssetDataBase
        {
            Factories.Remove(typeof(T));
        }

        public static T Get<T>() where T : AssetDataBase
        {
            if (Factories.TryGetValue(typeof(T), out var factory))
            {
                return (T)factory();
            }

            var instance = ScriptableObject.CreateInstance<T>();
            instance.DisplayName = $"<Placeholder:{typeof(T).Name}>";
            return instance;
        }
    }
}
