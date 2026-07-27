using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using UnityEditor;

namespace DDrive.Editor.Inspectors
{
    // AssetIdDrawer(1-8)向けの候補一覧解決。AssetIdGenerator(0-3)と同じ発見ロジックを再利用する。
    public static class AssetIdLookup
    {
        public readonly struct Candidate
        {
            public readonly ulong Id;
            public readonly AssetType Type;
            public readonly string DisplayName;
            public readonly string AssetPath;

            public Candidate(ulong id, AssetType type, string displayName, string assetPath)
            {
                Id = id;
                Type = type;
                DisplayName = displayName;
                AssetPath = assetPath;
            }
        }

        // 新規作成ダイアログ(1-5)の種別一覧などに使う「作成可能な全 Data 型」の列挙。
        public static List<(Type dataType, AssetType assetType)> GetAllDefinitions()
        {
            var list = new List<(Type, AssetType)>();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }

                if (types == null)
                {
                    continue;
                }

                foreach (var t in types)
                {
                    if (t == null || t.IsAbstract || !typeof(AssetDataBase).IsAssignableFrom(t))
                    {
                        continue;
                    }

                    var attr = t.GetCustomAttribute<AssetIdDefinitionAttribute>();
                    if (attr != null)
                    {
                        list.Add((t, attr.Type));
                    }
                }
            }

            return list;
        }

        public static Candidate[] GetCandidates(Type markerType)
        {
            var definition = FindDefinitionForMarker(markerType);
            if (definition == null)
            {
                return Array.Empty<Candidate>();
            }

            var (dataType, assetType) = definition.Value;
            var guids = AssetDatabase.FindAssets("t:" + dataType.Name);
            var list = new List<Candidate>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath(path, dataType) as AssetDataBase;
                if (asset == null || asset.GetType() != dataType)
                {
                    continue;
                }

                var name = string.IsNullOrEmpty(asset.DisplayName)
                    ? Path.GetFileNameWithoutExtension(path)
                    : asset.DisplayName;

                list.Add(new Candidate(asset.Id, assetType, name, path));
            }

            list.Sort((a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));
            return list.ToArray();
        }

        private static (Type dataType, AssetType assetType)? FindDefinitionForMarker(Type markerType)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }

                if (types == null)
                {
                    continue;
                }

                foreach (var t in types)
                {
                    if (t == null || t.IsAbstract || !typeof(AssetDataBase).IsAssignableFrom(t))
                    {
                        continue;
                    }

                    var attr = t.GetCustomAttribute<AssetIdDefinitionAttribute>();
                    if (attr != null && attr.MarkerType == markerType)
                    {
                        return (t, attr.Type);
                    }
                }
            }

            return null;
        }
    }
}
