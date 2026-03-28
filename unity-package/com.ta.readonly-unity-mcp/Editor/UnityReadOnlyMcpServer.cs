using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace TA.ReadOnlyUnityMcp
{
    internal sealed class UnityReadOnlyMcpServer
    {
        private const int MainThreadTimeoutMs = 30000;
        private static readonly int Port = ReadPort();

        private static readonly Lazy<UnityReadOnlyMcpServer> LazyInstance =
            new Lazy<UnityReadOnlyMcpServer>(() => new UnityReadOnlyMcpServer());

        private readonly object stateLock = new object();
        private HttpListener listener;
        private Thread listenerThread;

        public static UnityReadOnlyMcpServer Instance => LazyInstance.Value;

        private UnityReadOnlyMcpServer()
        {
        }

        public void Start()
        {
            lock (stateLock)
            {
                if (listener != null)
                {
                    return;
                }

                if (!HttpListener.IsSupported)
                {
                    Debug.LogWarning("[ReadOnlyUnityMcp] HttpListener is not supported on this platform.");
                    return;
                }

                listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                listener.Prefixes.Add($"http://localhost:{Port}/");

                try
                {
                    listener.Start();
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[ReadOnlyUnityMcp] Failed to start listener on port {Port}: {exception}");
                    listener.Close();
                    listener = null;
                    return;
                }

                listenerThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "ReadOnlyUnityMcpListener"
                };
                listenerThread.Start();

                Debug.Log($"[ReadOnlyUnityMcp] Listening on http://127.0.0.1:{Port}");
            }
        }

        public void Stop()
        {
            lock (stateLock)
            {
                if (listener == null)
                {
                    return;
                }

                try
                {
                    listener.Stop();
                    listener.Close();
                }
                catch
                {
                }

                listener = null;
                listenerThread = null;
            }
        }

        private void ListenLoop()
        {
            while (true)
            {
                HttpListenerContext context;
                HttpListener activeListener;

                lock (stateLock)
                {
                    if (listener == null)
                    {
                        return;
                    }

                    activeListener = listener;
                }

                try
                {
                    context = activeListener.GetContext();
                }
                catch (HttpListenerException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                ThreadPool.QueueUserWorkItem(_ => ProcessRequest(context));
            }
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            try
            {
                if (context.Request.HttpMethod == "OPTIONS")
                {
                    WriteJson(context.Response, 200, new { ok = true });
                    return;
                }

                if (context.Request.HttpMethod != "GET")
                {
                    WriteError(context.Response, 405, "Only GET is supported.");
                    return;
                }

                var path = NormalizePath(context.Request.Url.AbsolutePath);
                object payload;

                switch (path)
                {
                    case "/health":
                        payload = UnityReadOnlyMcpMainThread.Invoke(() => new
                        {
                            service = "readonly-unity-mcp",
                            version = "0.1.0",
                            unityVersion = Application.unityVersion,
                            projectPath = Directory.GetCurrentDirectory()
                        }, MainThreadTimeoutMs);
                        break;

                    case "/api/assets/info":
                        payload = UnityReadOnlyMcpMainThread.Invoke(() => UnityReadOnlyMcpQueries.GetAssetInfo(
                            GetString(context.Request.QueryString, "path"),
                            GetString(context.Request.QueryString, "guid"),
                            GetBool(context.Request.QueryString, "includeDependencies"),
                            GetBool(context.Request.QueryString, "includeReferencedBy")), MainThreadTimeoutMs);
                        break;

                    case "/api/assets/dependencies":
                        payload = UnityReadOnlyMcpMainThread.Invoke(() => UnityReadOnlyMcpQueries.GetAssetDependencies(
                            GetString(context.Request.QueryString, "path"),
                            GetString(context.Request.QueryString, "guid"),
                            GetBool(context.Request.QueryString, "recursive", true),
                            GetBool(context.Request.QueryString, "includeReferencedBy")), MainThreadTimeoutMs);
                        break;

                    case "/api/assets/find":
                        payload = UnityReadOnlyMcpMainThread.Invoke(() => UnityReadOnlyMcpQueries.FindAssets(
                            GetString(context.Request.QueryString, "type"),
                            GetString(context.Request.QueryString, "filter"),
                            GetInt(context.Request.QueryString, "limit", 200)), MainThreadTimeoutMs);
                        break;

                    case "/api/materials/info":
                        payload = UnityReadOnlyMcpMainThread.Invoke(() => UnityReadOnlyMcpQueries.GetMaterialInfo(
                            GetString(context.Request.QueryString, "path"),
                            GetString(context.Request.QueryString, "guid")), MainThreadTimeoutMs);
                        break;

                    case "/api/shaders/info":
                        payload = UnityReadOnlyMcpMainThread.Invoke(() => UnityReadOnlyMcpQueries.GetShaderInfo(
                            GetString(context.Request.QueryString, "path"),
                            GetString(context.Request.QueryString, "guid"),
                            GetBool(context.Request.QueryString, "includeUsage", true)), MainThreadTimeoutMs);
                        break;

                    case "/api/shaders/materials":
                        payload = UnityReadOnlyMcpMainThread.Invoke(() => UnityReadOnlyMcpQueries.FindMaterialsUsingShader(
                            GetString(context.Request.QueryString, "shaderName"),
                            GetString(context.Request.QueryString, "guid"),
                            GetBool(context.Request.QueryString, "includeRenderers", true)), MainThreadTimeoutMs);
                        break;

                    case "/api/shadergraphs/info":
                        payload = UnityReadOnlyMcpMainThread.Invoke(() => UnityReadOnlyMcpQueries.GetShaderGraphInfo(
                            GetString(context.Request.QueryString, "path"),
                            GetString(context.Request.QueryString, "guid")), MainThreadTimeoutMs);
                        break;

                    case "/api/scenes/info":
                        payload = UnityReadOnlyMcpMainThread.Invoke(UnityReadOnlyMcpQueries.GetSceneInfo, MainThreadTimeoutMs);
                        break;

                    case "/api/scenes/renderers":
                        payload = UnityReadOnlyMcpMainThread.Invoke(() => UnityReadOnlyMcpQueries.GetSceneRenderers(
                            GetString(context.Request.QueryString, "scenePath")), MainThreadTimeoutMs);
                        break;

                    default:
                        WriteError(context.Response, 404, $"Unknown route: {path}");
                        return;
                }

                WriteJson(context.Response, 200, new
                {
                    ok = true,
                    data = payload,
                    timestampUtc = DateTime.UtcNow.ToString("O")
                });
            }
            catch (Exception exception)
            {
                WriteError(context.Response, 500, exception.Message, exception.ToString());
            }
        }

        private static void WriteJson(HttpListenerResponse response, int statusCode, object payload)
        {
            var json = JsonConvert.SerializeObject(payload, Formatting.Indented);
            var bytes = Encoding.UTF8.GetBytes(json);
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentEncoding = Encoding.UTF8;
            response.Headers["Cache-Control"] = "no-store";
            response.Headers["Access-Control-Allow-Origin"] = "*";
            response.Headers["Access-Control-Allow-Methods"] = "GET, OPTIONS";
            response.Headers["Access-Control-Allow-Headers"] = "Content-Type";

            using (var stream = response.OutputStream)
            {
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        private static void WriteError(HttpListenerResponse response, int statusCode, string error, string details = null)
        {
            WriteJson(response, statusCode, new
            {
                ok = false,
                error,
                details,
                timestampUtc = DateTime.UtcNow.ToString("O")
            });
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "/";
            }

            if (path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal))
            {
                return path.TrimEnd('/');
            }

            return path;
        }

        private static string GetString(NameValueCollection query, string key)
        {
            var value = query[key];
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static bool GetBool(NameValueCollection query, string key, bool defaultValue = false)
        {
            var value = query[key];
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            return value.Equals("1", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetInt(NameValueCollection query, string key, int defaultValue)
        {
            var value = query[key];
            return int.TryParse(value, out var result) ? result : defaultValue;
        }

        private static int ReadPort()
        {
            var value = Environment.GetEnvironmentVariable("UNITY_READONLY_MCP_PORT")
                        ?? Environment.GetEnvironmentVariable("UNITY_MCP_PORT");

            if (int.TryParse(value, out var port) && port > 0 && port <= 65535)
            {
                return port;
            }

            return 51234;
        }
    }

    [InitializeOnLoad]
    internal static class UnityReadOnlyMcpMainThread
    {
        private static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();
        private static readonly int MainThreadId;

        static UnityReadOnlyMcpMainThread()
        {
            MainThreadId = Thread.CurrentThread.ManagedThreadId;
            EditorApplication.update += Pump;
        }

        public static T Invoke<T>(Func<T> action, int timeoutMs)
        {
            if (Thread.CurrentThread.ManagedThreadId == MainThreadId)
            {
                return action();
            }

            Exception capturedException = null;
            T result = default;

            using (var waitHandle = new ManualResetEventSlim(false))
            {
                Queue.Enqueue(() =>
                {
                    try
                    {
                        result = action();
                    }
                    catch (Exception exception)
                    {
                        capturedException = exception;
                    }
                    finally
                    {
                        waitHandle.Set();
                    }
                });

                if (!waitHandle.Wait(timeoutMs))
                {
                    throw new TimeoutException($"Unity main-thread work timed out after {timeoutMs}ms.");
                }
            }

            if (capturedException != null)
            {
                throw capturedException;
            }

            return result;
        }

        private static void Pump()
        {
            while (Queue.TryDequeue(out var action))
            {
                action();
            }
        }
    }
}
