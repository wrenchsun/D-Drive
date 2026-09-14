using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace DDrive.Editor.Spec
{
    // [32_spec_web.md] §5.1/§9-4 — Editor 専用の Web API(GAS)取得。W-9 で 5-13 の SpecFetcher(CSV/gviz)
    // を置き換える。UnityWebRequest は既定で 302 リダイレクトに追従する(redirectLimit 既定 32)。
    // GAS の doGet/doPost レスポンスは script.googleusercontent.com への 302 を経由するため、
    // 実装はこの既定動作に依存するだけで追加コードは不要(SpecWebFetcherTests がローカル
    // HttpListener で「302 → 本文」を再現して確認する)。実デプロイでの確認はユーザーが
    // デプロイ URL を用意してから行う(docs/32_spec_web.md §9-4、未確認のまま引き継ぎ)。
    // キャッシュは Library/DDriveSpec/(コミットしない、SpecFetcher と同じ場所を共有)。
    // Runtime からは絶対に呼ばない(ランタイムは Web に一切依存しない、[32] §1.3)。
    public sealed class SpecWebFetchResult
    {
        public bool Success;
        public string Json;
        public bool FromCache;
        public string Warning;
        public string Error;

        public static SpecWebFetchResult Ok(string json, bool fromCache, string warning = null)
            => new() { Success = true, Json = json, FromCache = fromCache, Warning = warning };

        public static SpecWebFetchResult Fail(string error)
            => new() { Success = false, Error = error };
    }

    public static class SpecWebFetcher
    {
        public const string CacheRoot = "Library/DDriveSpec";

        public static string CachePathFor(string apiName)
        {
            var safe = string.IsNullOrEmpty(apiName) ? "api" : apiName;
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                safe = safe.Replace(c, '_');
            }

            return $"{CacheRoot}/web_{safe}.json";
        }

        public static bool TryReadCache(string apiName, out string json)
        {
            var path = CachePathFor(apiName);
            if (File.Exists(path))
            {
                json = File.ReadAllText(path, Encoding.UTF8);
                return true;
            }

            json = null;
            return false;
        }

        // 取得系(GET ?api=1&name=<apiName>&token=<token>)。失敗時はキャッシュにフォールバックする。
        public static void FetchGet(string webAppUrl, string apiName, string token, string extraQuery, Action<SpecWebFetchResult> onComplete)
        {
            var url = BuildUrl(webAppUrl, apiName, token, extraQuery);
            if (url == null)
            {
                onComplete(SpecWebFetchResult.Fail("Web App URL が未設定です。"));
                return;
            }

            UnityWebRequest request;
            try
            {
                request = UnityWebRequest.Get(url);
            }
            catch (Exception e)
            {
                onComplete(FallbackOrFail(apiName, e.Message));
                return;
            }

            SendAndHandle(request, apiName, onComplete, useCache: true);
        }

        // 送信系(POST、W-12)。payload は params.payload として JSON 文字列で送る規約
        // (Tools/SpecWeb/src/TuningCommon.js の specWebParsePayload_ と同じ形。GAS は
        // doGet/doPost を同じ関数で扱い、e.parameter は GET/POST どちらのパラメータも同じ形で運ぶ)。
        // 送信結果はキャッシュしない(取得専用のキャッシュに送信結果を混ぜない)。
        public static void FetchPost(string webAppUrl, string apiName, string token, string payloadJson, Action<SpecWebFetchResult> onComplete)
        {
            if (string.IsNullOrEmpty(webAppUrl))
            {
                onComplete(SpecWebFetchResult.Fail("Web App URL が未設定です。"));
                return;
            }

            var form = new WWWForm();
            form.AddField("api", "1");
            form.AddField("name", apiName);
            if (!string.IsNullOrEmpty(token))
            {
                form.AddField("token", token);
            }

            if (!string.IsNullOrEmpty(payloadJson))
            {
                form.AddField("payload", payloadJson);
            }

            UnityWebRequest request;
            try
            {
                request = UnityWebRequest.Post(webAppUrl, form);
            }
            catch (Exception e)
            {
                onComplete(SpecWebFetchResult.Fail(e.Message));
                return;
            }

            SendAndHandle(request, apiName, onComplete, useCache: false);
        }

        private static void SendAndHandle(UnityWebRequest request, string apiName, Action<SpecWebFetchResult> onComplete, bool useCache)
        {
            var op = request.SendWebRequest();
            op.completed += _ =>
            {
                try
                {
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var json = request.downloadHandler.text;
                        if (useCache)
                        {
                            WriteCache(apiName, json);
                        }

                        onComplete(SpecWebFetchResult.Ok(json, fromCache: false));
                    }
                    else
                    {
                        onComplete(useCache ? FallbackOrFail(apiName, request.error) : SpecWebFetchResult.Fail(request.error));
                    }
                }
                finally
                {
                    request.Dispose();
                }
            };
        }

        private static string BuildUrl(string webAppUrl, string apiName, string token, string extraQuery)
        {
            if (string.IsNullOrEmpty(webAppUrl))
            {
                return null;
            }

            var sb = new StringBuilder(webAppUrl);
            sb.Append(webAppUrl.Contains('?') ? '&' : '?');
            sb.Append("api=1&name=").Append(Uri.EscapeDataString(apiName ?? string.Empty));
            if (!string.IsNullOrEmpty(token))
            {
                sb.Append("&token=").Append(Uri.EscapeDataString(token));
            }

            if (!string.IsNullOrEmpty(extraQuery))
            {
                sb.Append('&').Append(extraQuery);
            }

            return sb.ToString();
        }

        private static SpecWebFetchResult FallbackOrFail(string apiName, string error)
        {
            if (TryReadCache(apiName, out var cached))
            {
                return SpecWebFetchResult.Ok(cached, fromCache: true, warning: $"仕様書 Web API の取得に失敗しました({error})。前回のキャッシュを使用します。");
            }

            return SpecWebFetchResult.Fail($"仕様書 Web API の取得に失敗し、キャッシュもありません({error})。");
        }

        private static void WriteCache(string apiName, string json)
        {
            try
            {
                Directory.CreateDirectory(CacheRoot);
                File.WriteAllText(CachePathFor(apiName), json, Encoding.UTF8);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] 仕様書 Web API キャッシュの書き込みに失敗しました: {e.Message}");
            }
        }
    }
}
