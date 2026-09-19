using System;
using System.IO;
using System.Text;
using UnityEngine.Networking;

namespace DDrive.Editor.Spec
{
    // [27_spec_sheet.md] §4.1 — Editor 専用の CSV 取得。UnityWebRequestAsyncOperation.completed を使う
    // (コルーチン不要で Edit Mode でも確実に発火する)。キャッシュは Library/DDriveSpec/(コミットしない)。
    // Runtime からは絶対に呼ばない(ランタイムはシートに一切依存しない、[27] §1)。
    public sealed class SpecFetchResult
    {
        public bool Success;
        public string Csv;
        public bool FromCache;
        public string Warning;
        public string Error;

        public static SpecFetchResult Ok(string csv, bool fromCache, string warning = null)
            => new() { Success = true, Csv = csv, FromCache = fromCache, Warning = warning };

        public static SpecFetchResult Fail(string error)
            => new() { Success = false, Error = error };
    }

    public static class SpecFetcher
    {
        public const string CacheRoot = "Library/DDriveSpec";

        public static string CachePathFor(string sheetName)
        {
            var safe = string.IsNullOrEmpty(sheetName) ? "sheet" : sheetName;
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                safe = safe.Replace(c, '_');
            }

            return $"{CacheRoot}/{safe}.csv";
        }

        public static bool TryReadCache(string sheetName, out string csv)
        {
            var path = CachePathFor(sheetName);
            if (File.Exists(path))
            {
                csv = File.ReadAllText(path, Encoding.UTF8);
                return true;
            }

            csv = null;
            return false;
        }

        // 取得に成功したらキャッシュへ保存して結果を返す。失敗したらキャッシュにフォールバックする
        // (キャッシュも無ければ失敗を返す)。UI スレッドをブロックしない(ネットワーク待ちで
        // エディタを止めない、[27] §4.2)。
        public static void Fetch(string url, string sheetName, Action<SpecFetchResult> onComplete)
        {
            if (string.IsNullOrEmpty(url))
            {
                onComplete(SpecFetchResult.Fail("URL が未設定です。"));
                return;
            }

            UnityWebRequest request;
            try
            {
                request = UnityWebRequest.Get(url);
            }
            catch (Exception e)
            {
                onComplete(FallbackOrFail(sheetName, e.Message));
                return;
            }

            var op = request.SendWebRequest();
            op.completed += _ =>
            {
                try
                {
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var csv = request.downloadHandler.text;
                        WriteCache(sheetName, csv);
                        onComplete(SpecFetchResult.Ok(csv, fromCache: false));
                    }
                    else
                    {
                        onComplete(FallbackOrFail(sheetName, request.error));
                    }
                }
                finally
                {
                    request.Dispose();
                }
            };
        }

        private static SpecFetchResult FallbackOrFail(string sheetName, string error)
        {
            if (TryReadCache(sheetName, out var cached))
            {
                return SpecFetchResult.Ok(cached, fromCache: true, warning: $"仕様書の取得に失敗しました({error})。前回のキャッシュを使用します。");
            }

            return SpecFetchResult.Fail($"仕様書の取得に失敗し、キャッシュもありません({error})。");
        }

        private static void WriteCache(string sheetName, string csv)
        {
            try
            {
                Directory.CreateDirectory(CacheRoot);
                File.WriteAllText(CachePathFor(sheetName), csv, Encoding.UTF8);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[DDrive] 仕様書キャッシュの書き込みに失敗しました: {e.Message}");
            }
        }
    }
}
