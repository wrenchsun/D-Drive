using System;
using System.Collections;
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
    // [32_spec_web.md] §9-4/§5.1 W-9 — GAS の doGet/doPost 応答は script.googleusercontent.com への
    // 302 リダイレクトを経由する。UnityWebRequest は既定でリダイレクトに追従するが、その前提が
    // 実際に成立していることをローカルの HttpListener で「302 → 本文」を再現して確認する
    // (実デプロイでの確認はユーザーがデプロイ URL を用意してから行う。§9-4 は未確認のまま引き継ぎ)。
    public class SpecWebFetcherTests
    {
        private HttpListener _listener;

        [TearDown]
        public void TearDown()
        {
            StopListener();

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
            _ = Task.Run(() => ServeOnce(listenerRef, finalBody, statusCode));
            return prefix;
        }

        private static void ServeOnce(HttpListener listener, string finalBody, int statusCode)
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
