using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace DDrive.Editor.Spec
{
    // [32_spec_web.md] §5.1/§7/§9-4 — Editor 専用の Web API(GAS)取得。W-9 で 5-13 の SpecFetcher(CSV/gviz)
    // を置き換える。UnityWebRequest は既定で 302 リダイレクトに追従する(redirectLimit 既定 32)。
    // GAS の doGet/doPost レスポンスは script.googleusercontent.com への 302 を経由するため、
    // 実装はこの既定動作に依存するだけで追加コードは不要(SpecWebFetcherTests がローカル
    // HttpListener で「302 → 本文」を再現して確認する)。実デプロイでの確認はユーザーが
    // デプロイ URL を用意してから行う(docs/32_spec_web.md §9-4、未確認のまま引き継ぎ)。
    //
    // 追補(2026-09-14): 取得系(旧 FetchGet)も含め、token は必ず POST 本文(WWWForm)で送る。
    // 以前は取得系だけ `?api=1&name=<api>&token=<token>` の GET クエリで送っていたため、
    // トークンが GAS の実行ログ・中継プロキシ・Unity 側の例外メッセージ・ブラウザ履歴に残る
    // 経路があった([32] §7)。POST の場合、UnityWebRequest は 302 リダイレクト先への追従時に
    // メソッドを GET へ切り替え本文を引き継がない(HTTP の標準的な 302 挙動)が、GAS の
    // Content Service は「① 元の URL への POST で doPost を実行し結果を確定 → ② 確定済みの
    // 結果を script.googleusercontent.com の一意な URL から GET で取り出す」という 2 段構成
    // (① で e.parameter/e.postData から token を読める。② は事前に確定した内容を返すだけで
    // token を必要としない)ため、本文が失われても問題ない。この段取りは GAS 公式の解説
    // (Content Service ガイド、"Understanding Flow of Request to Web Apps" 等)と一致する
    // (docs/32_spec_web.md §7 に出典を記載)。SpecWebFetcherTests がローカル HttpListener で
    // 「POST → 302 → GET → 本文」を再現して確認する。
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

        // 取得系(POST 本文: api=1&name=<apiName>&token=<token>[&extraFields...])。token を URL に
        // 出さないため、以前の GET クエリ方式(2026-09-14 に廃止)ではなく POST 本文に統一する
        // (上記クラスコメント参照)。extraQuery は "includeArchived=1" のような
        // "key=value[&key2=value2...]" 形式の文字列(呼び出し側の既存シグネチャを変えないための
        // 互換入力)で、そのまま URL に連結せず 1 組ずつ POST フォームフィールドへ分解する。
        // 失敗時はキャッシュにフォールバックする。
        public static void FetchGet(string webAppUrl, string apiName, string token, string extraQuery, Action<SpecWebFetchResult> onComplete)
        {
            if (string.IsNullOrEmpty(webAppUrl))
            {
                onComplete(SpecWebFetchResult.Fail("Web App URL が未設定です。"));
                return;
            }

            var form = BuildForm(apiName, token);
            AddExtraFields(form, extraQuery);

            UnityWebRequest request;
            try
            {
                request = UnityWebRequest.Post(webAppUrl, form);
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

            var form = BuildForm(apiName, token);
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

        // FetchGet/FetchPost 共通の土台(api=1&name=<apiName>[&token=<token>])。
        // token は必ずここ(POST フォームフィールド)経由でだけ組み立てる = URL には一切現れない。
        private static WWWForm BuildForm(string apiName, string token)
        {
            var form = new WWWForm();
            form.AddField("api", "1");
            form.AddField("name", apiName ?? string.Empty);
            if (!string.IsNullOrEmpty(token))
            {
                form.AddField("token", token);
            }

            return form;
        }

        // 旧 GET 方式の "key=value[&key2=value2...]" 文字列(呼び出し側の互換入力)を
        // 1 組ずつ POST フォームフィールドへ分解する。
        private static void AddExtraFields(WWWForm form, string extraQuery)
        {
            if (string.IsNullOrEmpty(extraQuery))
            {
                return;
            }

            foreach (var pair in extraQuery.Split('&'))
            {
                if (string.IsNullOrEmpty(pair))
                {
                    continue;
                }

                var eq = pair.IndexOf('=');
                var key = eq >= 0 ? pair.Substring(0, eq) : pair;
                var value = eq >= 0 ? pair.Substring(eq + 1) : string.Empty;
                form.AddField(Uri.UnescapeDataString(key), Uri.UnescapeDataString(value));
            }
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
