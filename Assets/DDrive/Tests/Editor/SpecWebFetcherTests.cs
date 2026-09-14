using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using DDrive.Editor.Spec;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Editor
{
    // [32_spec_web.md] §9-4/§5.1/§7 W-9 — GAS の doGet/doPost 応答は script.googleusercontent.com への
    // 302 リダイレクトを経由する。UnityWebRequest は既定でリダイレクトに追従するが、その前提が
    // 実際に成立していることをローカルの HttpListener で「302 → 本文」を再現して確認する
    // (実デプロイでの確認はユーザーがデプロイ URL を用意してから行う。§9-4 は未確認のまま引き継ぎ)。
    //
    // 追補(2026-09-14): token を URL クエリで送らないことの確認を追加した([32] §7)。
    //   - FetchGet(旧: GET クエリで token を送っていた)が、実際には POST の本文で token を送り、
    //     初回リクエストの URL(パス+クエリ)には token が一切現れないこと
    //   - POST → 302 → GET(UnityWebRequest は POST への 302 でメソッドを GET に切り替える。
    //     本文は引き継がれない)でも、GAS 側の「① で確定 → ② は確定済みの内容を返すだけ」という
    //     段取りにより最終的な本文取得は成立すること(FetchPost 経由でも同様に確認する)
    //   - 取得の成功・キャッシュフォールバックの各経路で、Debug.Log に token 文字列が出力されないこと
    public class SpecWebFetcherTests
    {
        private HttpListener _listener;
        private readonly List<CapturedRequest> _captured = new();

        private readonly struct CapturedRequest
        {
            public readonly string Method;
            public readonly string PathAndQuery;
            public readonly string Body;

            public CapturedRequest(string method, string pathAndQuery, string body)
            {
                Method = method;
                PathAndQuery = pathAndQuery;
                Body = body;
            }
        }

        [TearDown]
        public void TearDown()
        {
            StopListener();
            _captured.Clear();

            var path = SpecWebFetcher.CachePathFor("__test_api");
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private void StopListener()
        {
            if (_listener == null)
            {
                return;
            }

            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch (Exception)
            {
                // テスト終了時の後始末なので失敗しても無視する。
            }

            _listener = null;
        }

        private string StartRedirectServer(string finalBody, int statusCode = 200)
        {
            var port = GetFreePort();
            var prefix = $"http://127.0.0.1:{port}/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            _listener.Start();

            var listenerRef = _listener;
            var capturedRef = _captured;
            _ = Task.Run(() => ServeOnce(listenerRef, capturedRef, finalBody, statusCode));
            return prefix;
        }

        private static void ServeOnce(HttpListener listener, List<CapturedRequest> captured, string finalBody, int statusCode)
        {
            try
            {
                while (listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try
                    {
                        ctx = listener.GetContext();
                    }
                    catch (Exception)
                    {
                        return; // Stop() された
                    }

                    string body = null;
                    if (ctx.Request.HasEntityBody)
                    {
                        using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                        body = reader.ReadToEnd();
                    }

                    lock (captured)
                    {
                        captured.Add(new CapturedRequest(ctx.Request.HttpMethod, ctx.Request.Url.PathAndQuery, body));
                    }

                    if (ctx.Request.Url.AbsolutePath == "/redirected")
                    {
                        var bytes = Encoding.UTF8.GetBytes(finalBody);
                        ctx.Response.ContentType = "application/json";
                        ctx.Response.StatusCode = statusCode;
                        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                        ctx.Response.OutputStream.Close();
                    }
                    else
                    {
                        // GAS の Content Service と同じ「まず 302」を再現する。
                        // GAS 実物では、この最初のリクエスト(doGet/doPost が実際に実行されるところ)で
                        // e.parameter/e.postData から token を読み取り、結果を確定させた上で 302 を返す
                        // (2 回目の /redirected へのリクエストは確定済みの内容を返すだけで token を使わない)。
                        ctx.Response.StatusCode = 302;
                        ctx.Response.RedirectLocation = ctx.Request.Url.GetLeftPart(UriPartial.Authority) + "/redirected";
                        ctx.Response.OutputStream.Close();
                    }
                }
            }
            catch (Exception)
            {
                // Stop() による例外はテストの後始末なので無視する。
            }
        }

        private static int GetFreePort()
        {
            var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            return port;
        }

        [UnityTest]
        public IEnumerator FetchGet_FollowsRedirect_AndReturnsFinalBody()
        {
            var prefix = StartRedirectServer("{\"ok\":true,\"pong\":true}");

            SpecWebFetchResult result = null;
            SpecWebFetcher.FetchGet(prefix, "__test_api", null, null, r => result = r);

            yield return WaitFor(() => result != null);

            Assert.IsNotNull(result, "タイムアウトしました(302 リダイレクトへの追従に失敗した可能性があります)");
            Assert.IsTrue(result.Success);
            Assert.IsFalse(result.FromCache);
            StringAssert.Contains("\"pong\":true", result.Json);
        }

        [UnityTest]
        public IEnumerator FetchGet_ServerDown_FallsBackToPreviousCache()
        {
            // 1 回目: 正常応答でキャッシュに保存させる。
            var prefix = StartRedirectServer("{\"ok\":true,\"cached\":true}");
            SpecWebFetchResult first = null;
            SpecWebFetcher.FetchGet(prefix, "__test_api", null, null, r => first = r);
            yield return WaitFor(() => first != null);
            Assert.IsTrue(first != null && first.Success, "1 回目の取得が成功している前提のテストです");

            // サーバーを止めて 2 回目の取得を失敗させる → 前回のキャッシュへフォールバックする([27]/[32] の既存挙動)。
            StopListener();

            SpecWebFetchResult second = null;
            SpecWebFetcher.FetchGet(prefix, "__test_api", null, null, r => second = r);
            yield return WaitFor(() => second != null);

            Assert.IsNotNull(second);
            Assert.IsTrue(second.Success);
            Assert.IsTrue(second.FromCache);
            StringAssert.Contains("cached", second.Json);
        }

        [UnityTest]
        public IEnumerator FetchGet_NoUrl_FailsWithoutThrowing()
        {
            SpecWebFetchResult result = null;
            Assert.DoesNotThrow(() => SpecWebFetcher.FetchGet(null, "__test_api", null, null, r => result = r));
            yield return WaitFor(() => result != null);

            Assert.IsFalse(result.Success);
        }

        // ── 追補(2026-09-14): token を URL クエリで送らないことの確認 ──

        [UnityTest]
        public IEnumerator FetchGet_SendsTokenInPostBody_NeverInUrl()
        {
            var prefix = StartRedirectServer("{\"ok\":true}");
            const string secretToken = "SECRET-READ-TOKEN-12345";

            SpecWebFetchResult result = null;
            SpecWebFetcher.FetchGet(prefix, "assets.list", secretToken, "includeArchived=1", r => result = r);
            yield return WaitFor(() => result != null);

            Assert.IsTrue(result.Success);

            CapturedRequest initial;
            lock (_captured)
            {
                Assert.GreaterOrEqual(_captured.Count, 1, "初回リクエストが記録されているはず");
                initial = _captured[0];
            }

            Assert.AreEqual("POST", initial.Method, "token を送るときは POST でなければならない");
            StringAssert.DoesNotContain(secretToken, initial.PathAndQuery, "token が URL(パス+クエリ)に出てはいけない");
            StringAssert.Contains("token=" + secretToken, initial.Body, "token は POST 本文に入っているはず");
            StringAssert.Contains("includeArchived", initial.Body, "extraQuery も POST 本文に入っているはず(URL には出ない)");
        }

        [UnityTest]
        public IEnumerator FetchPost_SendsTokenInPostBody_NeverInUrl()
        {
            var prefix = StartRedirectServer("{\"ok\":true}");
            const string secretToken = "SECRET-WRITE-TOKEN-67890";

            SpecWebFetchResult result = null;
            SpecWebFetcher.FetchPost(prefix, "assetState", secretToken, "{\"items\":[]}", r => result = r);
            yield return WaitFor(() => result != null);

            Assert.IsTrue(result.Success);

            CapturedRequest initial;
            lock (_captured)
            {
                Assert.GreaterOrEqual(_captured.Count, 1);
                initial = _captured[0];
            }

            Assert.AreEqual("POST", initial.Method);
            StringAssert.DoesNotContain(secretToken, initial.PathAndQuery, "token が URL(パス+クエリ)に出てはいけない");
            StringAssert.Contains("token=" + secretToken, initial.Body);
        }

        // POST → 302 → (UnityWebRequest は本文を引き継がず)GET → 本文取得、という GAS 実物の
        // doPost の段取りをローカルで再現して確認する(クラスコメント・[32] §7 参照)。
        [UnityTest]
        public IEnumerator FetchPost_FollowsRedirect_EvenThoughMethodBecomesGet_AndReturnsFinalBody()
        {
            var prefix = StartRedirectServer("{\"ok\":true,\"item\":{\"revision\":1}}");

            SpecWebFetchResult result = null;
            SpecWebFetcher.FetchPost(prefix, "assetState", "tok", "{\"items\":[]}", r => result = r);
            yield return WaitFor(() => result != null);

            Assert.IsNotNull(result, "タイムアウトしました(POST の 302 リダイレクトへの追従に失敗した可能性があります)");
            Assert.IsTrue(result.Success);
            StringAssert.Contains("\"revision\":1", result.Json);

            List<CapturedRequest> snapshot;
            lock (_captured)
            {
                snapshot = new List<CapturedRequest>(_captured);
            }

            Assert.AreEqual(2, snapshot.Count, "初回(POST) + リダイレクト先の 2 回のはず");
            Assert.AreEqual("POST", snapshot[0].Method, "初回は POST で doPost 相当が実行される");
            StringAssert.Contains("/redirected", snapshot[1].PathAndQuery, "2 回目はリダイレクト先");
            // UnityWebRequest は POST への 302 を GET として追従する(標準的な HTTP クライアント挙動)。
            Assert.AreEqual("GET", snapshot[1].Method, "302 追従後のリクエストは GET に切り替わる");
        }

        // ── 追補(2026-09-14): token が Debug.Log に出ないことの確認 ──

        [UnityTest]
        public IEnumerator FetchGet_DoesNotLogToken_OnSuccess()
        {
            var prefix = StartRedirectServer("{\"ok\":true}");
            const string secretToken = "SECRET-LOG-CHECK-AAAA";
            var logged = new List<string>();
            Application.LogCallback handler = (message, _, _) => logged.Add(message);
            Application.logMessageReceived += handler;

            try
            {
                SpecWebFetchResult result = null;
                SpecWebFetcher.FetchGet(prefix, "__test_api", secretToken, null, r => result = r);
                yield return WaitFor(() => result != null);
                Assert.IsTrue(result.Success);
            }
            finally
            {
                Application.logMessageReceived -= handler;
            }

            foreach (var message in logged)
            {
                StringAssert.DoesNotContain(secretToken, message, "token が Debug.Log に出てはいけない");
            }
        }

        [UnityTest]
        public IEnumerator FetchGet_DoesNotLogToken_OnCacheFallback()
        {
            // 1 回目: 正常応答でキャッシュに保存させる。
            var prefix = StartRedirectServer("{\"ok\":true,\"cached\":true}");
            const string secretToken = "SECRET-LOG-CHECK-BBBB";
            SpecWebFetchResult first = null;
            SpecWebFetcher.FetchGet(prefix, "__test_api", secretToken, null, r => first = r);
            yield return WaitFor(() => first != null);
            Assert.IsTrue(first != null && first.Success);

            StopListener();

            var logged = new List<string>();
            Application.LogCallback handler = (message, _, _) => logged.Add(message);
            Application.logMessageReceived += handler;

            try
            {
                SpecWebFetchResult second = null;
                SpecWebFetcher.FetchGet(prefix, "__test_api", secretToken, null, r => second = r);
                yield return WaitFor(() => second != null);
                Assert.IsNotNull(second);
                Assert.IsTrue(second.FromCache);
            }
            finally
            {
                Application.logMessageReceived -= handler;
            }

            foreach (var message in logged)
            {
                StringAssert.DoesNotContain(secretToken, message, "フォールバック時の警告ログにも token が出てはいけない");
            }
        }

        private static IEnumerator WaitFor(Func<bool> predicate, float timeoutSeconds = 10f)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!predicate() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }
    }
}
