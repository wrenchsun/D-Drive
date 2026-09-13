using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DDrive.Editor.Dependencies
{
    // [11_tasks.md] 5-5 — Library/DDriveDeps/ への保存(ファイル単位、GUID をファイル名にした .json)。
    // Library/ は .gitignore 済み(CLAUDE.md §2: Library/ は生成物、コミット不可)なので、ここへの
    // 読み書きはテストの「git status を変えない」制約に影響しない。
    internal static class DependencyGraphCache
    {
        private const string CacheDir = "Library/DDriveDeps";

        public static void ClearAll()
        {
            try
            {
                if (Directory.Exists(CacheDir))
                {
                    Directory.Delete(CacheDir, recursive: true);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] DependencyGraph: キャッシュの削除に失敗しました: {e.Message}");
            }
        }

        public static void Save(DependencyFileRecord record)
        {
            try
            {
                EnsureDir();
                File.WriteAllText(PathFor(record.Guid), JsonUtility.ToJson(record));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] DependencyGraph: キャッシュ保存に失敗しました({record.Path}): {e.Message}");
            }
        }

        public static void Delete(string guid)
        {
            try
            {
                var path = PathFor(guid);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] DependencyGraph: キャッシュ削除に失敗しました(guid={guid}): {e.Message}");
            }
        }

        public static List<DependencyFileRecord> LoadAll()
        {
            var result = new List<DependencyFileRecord>();
            if (!Directory.Exists(CacheDir))
            {
                return result;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(CacheDir, "*.json");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] DependencyGraph: キャッシュ一覧の取得に失敗しました: {e.Message}");
                return result;
            }

            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var record = JsonUtility.FromJson<DependencyFileRecord>(json);
                    if (record != null && !string.IsNullOrEmpty(record.Guid))
                    {
                        result.Add(record);
                    }
                }
                catch (Exception e)
                {
                    // 壊れたキャッシュファイル 1 件で全体を止めない([00] §0-4: 例外で止めない)。
                    Debug.LogWarning($"[DDrive] DependencyGraph: キャッシュ読み込みに失敗しました({file}): {e.Message}");
                }
            }

            return result;
        }

        private static void EnsureDir()
        {
            if (!Directory.Exists(CacheDir))
            {
                Directory.CreateDirectory(CacheDir);
            }
        }

        private static string PathFor(string guid) => $"{CacheDir}/{guid}.json";
    }
}
