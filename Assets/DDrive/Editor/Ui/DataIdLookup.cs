using System;
using System.Collections.Generic;
using DDrive.Foundation.Data;
using UnityEditor;

namespace DDrive.Editor.Ui
{
    // (レビュー対応 2026-09-14) Id → Data(エディタ用)の検索。ControlSkinPreviewSection.FindData と SliderEditorWindow
    // (PlaySkinSe / GetTargetSkin)が同じ全件走査をそれぞれ持ち、スライダーの目盛り SE では 1 ノッチごとに全 SeData を
    // ロードし直していたため、ここに寄せて (型, Id) ごとに覚える。見つからなかった結果も覚える。
    // プロジェクト変更(アセットの作成・削除・インポート = EditorApplication.projectChanged)で全部捨てる。
    public static class DataIdLookup
    {
        private static readonly Dictionary<(Type type, ulong id), AssetDataBase> Cache = new();

        static DataIdLookup()
        {
            EditorApplication.projectChanged += Invalidate;
        }

        public static void Invalidate() => Cache.Clear();

        public static T Find<T>(ulong id) where T : AssetDataBase
        {
            if (id == 0)
            {
                return null;
            }

            var key = (typeof(T), id);
            if (Cache.TryGetValue(key, out var cached))
            {
                // 見つからなかった結果はそのまま返す。削除済み(Unity の null)や Id を書き換えられたものは探し直す。
                if (ReferenceEquals(cached, null))
                {
                    return null;
                }

                if (cached != null && cached.Id == id)
                {
                    return (T)cached;
                }
            }

            T found = null;
            foreach (var guid in AssetSearch.FindAssets("t:" + typeof(T).Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.Id == id)
                {
                    found = asset;
                    break;
                }
            }

            Cache[key] = found;
            return found;
        }
    }
}
