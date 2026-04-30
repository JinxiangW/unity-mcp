using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace TA.UnityMcp
{
    internal sealed class UnityMcpServer
    {
        private static readonly int MainThreadTimeoutMs = ReadMainThreadTimeoutMs();
        private static readonly int Port = ReadPort();
        private static readonly bool EnableRequestLogging = ReadRequestLoggingEnabled();

        private static readonly Lazy<UnityMcpServer> LazyInstance =
            new Lazy<UnityMcpServer>(() => new UnityMcpServer());

        private readonly object stateLock = new object();
        private HttpListener listener;
        private Thread listenerThread;

        public static UnityMcpServer Instance => LazyInstance.Value;

        private UnityMcpServer()
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
                    Debug.LogWarning("[UnityMcp] HttpListener is not supported on this platform.");
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
                    if (IsExistingServerHealthy())
                    {
                        Debug.LogWarning($"[UnityMcp] Listener already active on port {Port}; reusing existing server.");
                        listener.Close();
                        listener = null;
                        return;
                    }

                    Debug.LogError($"[UnityMcp] Failed to start listener on port {Port}: {exception}");
                    listener.Close();
                    listener = null;
                    return;
                }

                listenerThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "UnityMcpListener"
                };
                listenerThread.Start();

                Debug.Log($"[UnityMcp] Listening on http://127.0.0.1:{Port}");
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
            var path = NormalizePath(context.Request.Url.AbsolutePath);
            var startedUtc = DateTime.UtcNow;

            try
            {
                if (path == "/api/materials/transfer-package")
                {
                    if (context.Request.HttpMethod != "POST")
                    {
                        WriteError(context.Response, 405, new McpErrorResponse
                        {
                            code = "METHOD_NOT_ALLOWED",
                            message = "Only POST is supported.",
                            context = new
                            {
                                route = path,
                                method = context.Request.HttpMethod
                            }
                        });
                        return;
                    }

                    if (EnableRequestLogging)
                    {
                        Debug.Log($"[UnityMcp] Request POST {path}");
                    }

                    var requestBody = ReadJsonBody(context.Request);
                    var postPayload = UnityMcpMainThread.Invoke(
                        () => UnityMcpQueries.CreateMaterialTransferPackage(requestBody),
                        MainThreadTimeoutMs);

                    WriteJson(context.Response, 200, new
                    {
                        ok = true,
                        data = postPayload,
                        timestampUtc = DateTime.UtcNow.ToString("O")
                    });

                    if (EnableRequestLogging)
                    {
                        Debug.Log($"[UnityMcp] Response {path} {(DateTime.UtcNow - startedUtc).TotalMilliseconds:F0}ms");
                    }

                    return;
                }

                if (context.Request.HttpMethod != "GET")
                {
                    WriteError(context.Response, 405, new McpErrorResponse
                    {
                        code = "METHOD_NOT_ALLOWED",
                        message = "Only GET is supported.",
                        context = new
                        {
                            route = path,
                            method = context.Request.HttpMethod
                        }
                    });
                    return;
                }

                if (EnableRequestLogging)
                {
                    Debug.Log($"[UnityMcp] Request GET {path} {BuildQuerySummary(context.Request.QueryString)}");
                }

                object payload;

                switch (path)
                {
                    case "/health":
                        payload = UnityMcpMainThread.Invoke(() => new
                        {
                            service = "unity-mcp",
                            version = "0.1.0",
                            unityVersion = Application.unityVersion,
                            timeoutMs = MainThreadTimeoutMs,
                            compatibility = new
                            {
                                shaderGraphPackageVersion = Compat.CompatServices.Context.shaderGraphPackageVersion,
                                renderPipelinePackageVersion = Compat.CompatServices.Context.renderPipelinePackageVersion,
                                hasVolumeType = Compat.CompatServices.Context.hasVolumeType,
                                hasShaderGraphAssembly = Compat.CompatServices.Context.hasShaderGraphAssembly
                            }
                        }, MainThreadTimeoutMs);
                        break;

                    case "/api/assets/info":
                        payload = UnityMcpMainThread.Invoke(() => UnityMcpQueries.GetAssetInfo(
                            GetString(context.Request.QueryString, "path"),
                            GetString(context.Request.QueryString, "guid"),
                            GetBool(context.Request.QueryString, "includeDependencies"),
                            GetBool(context.Request.QueryString, "includeReferencedBy")), MainThreadTimeoutMs);
                        break;

                    case "/api/assets/dependencies":
                        payload = UnityMcpMainThread.Invoke(() => UnityMcpQueries.GetAssetDependencies(
                            GetString(context.Request.QueryString, "path"),
                            GetString(context.Request.QueryString, "guid"),
                            GetBool(context.Request.QueryString, "recursive", true),
                            GetBool(context.Request.QueryString, "includeReferencedBy")), MainThreadTimeoutMs);
                        break;

                    case "/api/assets/find":
                        payload = UnityMcpMainThread.Invoke(() => UnityMcpQueries.FindAssets(
                            GetString(context.Request.QueryString, "type"),
                            GetString(context.Request.QueryString, "filter"),
                            GetInt(context.Request.QueryString, "limit", 200)), MainThreadTimeoutMs);
                        break;

                    case "/api/materials/info":
                        payload = UnityMcpMainThread.Invoke(() => UnityMcpQueries.GetMaterialInfo(
                            GetString(context.Request.QueryString, "path"),
                            GetString(context.Request.QueryString, "guid")), MainThreadTimeoutMs);
                        break;

                    case "/api/materials/export-spec":
                        payload = UnityMcpMainThread.Invoke(() => UnityMcpQueries.GetMaterialExportSpec(
                            GetString(context.Request.QueryString, "path"),
                            GetString(context.Request.QueryString, "guid"),
                            GetString(context.Request.QueryString, "exportProfile"),
                            GetBool(context.Request.QueryString, "includeShaderGraph", true),
                            GetBool(context.Request.QueryString, "recursiveShaderGraphs", true),
                            GetBool(context.Request.QueryString, "includeRawProperties", true)), MainThreadTimeoutMs);
                        break;

                    case "/api/shaders/info":
                        payload = UnityMcpMainThread.Invoke(() => UnityMcpQueries.GetShaderInfo(
                            GetString(context.Request.QueryString, "path"),
                            GetString(context.Request.QueryString, "guid"),
                            GetBool(context.Request.QueryString, "includeUsage", true)), MainThreadTimeoutMs);
                        break;

                    case "/api/shaders/materials":
                        payload = UnityMcpMainThread.Invoke(() => UnityMcpQueries.FindMaterialsUsingShader(
                            GetString(context.Request.QueryString, "shaderName"),
                            GetString(context.Request.QueryString, "guid"),
                            GetBool(context.Request.QueryString, "includeRenderers", true)), MainThreadTimeoutMs);
                        break;

                    case "/api/shadergraphs/info":
                        payload = UnityMcpMainThread.Invoke(() => UnityMcpQueries.GetShaderGraphInfo(
                            GetString(context.Request.QueryString, "path"),
                            GetString(context.Request.QueryString, "guid")), MainThreadTimeoutMs);
                        break;

                    case "/api/pipeline/info":
                        payload = UnityMcpMainThread.Invoke(UnityMcpQueries.GetRenderPipelineInfo, MainThreadTimeoutMs);
                        break;

                    case "/api/scenes/info":
                        payload = UnityMcpMainThread.Invoke(UnityMcpQueries.GetSceneInfo, MainThreadTimeoutMs);
                        break;

                    case "/api/scenes/renderers":
                        payload = UnityMcpMainThread.Invoke(() => UnityMcpQueries.GetSceneRenderers(
                            GetString(context.Request.QueryString, "scenePath")), MainThreadTimeoutMs);
                        break;

                    case "/api/scenes/lights":
                        payload = UnityMcpMainThread.Invoke(() => UnityMcpQueries.GetSceneLights(
                            GetString(context.Request.QueryString, "scenePath"),
                            GetString(context.Request.QueryString, "layers"),
                            GetString(context.Request.QueryString, "tag")), MainThreadTimeoutMs);
                        break;

                    case "/api/scenes/volumes":
                        payload = UnityMcpMainThread.Invoke(() => UnityMcpQueries.GetSceneVolumes(
                            GetString(context.Request.QueryString, "scenePath"),
                            GetString(context.Request.QueryString, "layers"),
                            GetString(context.Request.QueryString, "tag")), MainThreadTimeoutMs);
                        break;

                    case "/api/prefabs/info":
                        payload = UnityMcpMainThread.Invoke(() => UnityMcpQueries.GetPrefabInfo(
                            GetString(context.Request.QueryString, "path"),
                            GetString(context.Request.QueryString, "guid")), MainThreadTimeoutMs);
                        break;

                    case "/api/textures/info":
                        payload = UnityMcpMainThread.Invoke(() => UnityMcpQueries.GetTextureInfo(
                            GetString(context.Request.QueryString, "path"),
                            GetString(context.Request.QueryString, "guid")), MainThreadTimeoutMs);
                        break;

                    case "/api/animations/info":
                        payload = UnityMcpMainThread.Invoke(() => UnityMcpQueries.GetAnimationInfo(
                            GetString(context.Request.QueryString, "path"),
                            GetString(context.Request.QueryString, "guid")), MainThreadTimeoutMs);
                        break;

                    case "/api/project/settings":
                        payload = UnityMcpMainThread.Invoke(UnityMcpQueries.GetProjectSettings, MainThreadTimeoutMs);
                        break;

                    case "/api/project/packages":
                        payload = UnityMcpMainThread.Invoke(UnityMcpQueries.GetProjectPackages, MainThreadTimeoutMs);
                        break;

                    default:
                        WriteError(context.Response, 404, new McpErrorResponse
                        {
                            code = "UNKNOWN_ROUTE",
                            message = $"Unknown route: {path}",
                            context = new
                            {
                                route = path
                            }
                        });
                        return;
                }

                WriteJson(context.Response, 200, new
                {
                    ok = true,
                    data = payload,
                    timestampUtc = DateTime.UtcNow.ToString("O")
                });

                if (EnableRequestLogging)
                {
                    Debug.Log($"[UnityMcp] Response {path} {(DateTime.UtcNow - startedUtc).TotalMilliseconds:F0}ms");
                }
            }
            catch (Exception exception)
            {
                var error = McpErrorResponse.FromException(exception, path, context.Request.QueryString);
                var statusCode = error.code == "ASSET_NOT_FOUND" ? 404 : error.code == "CONFLICT" ? 409 : error.code == "INVALID_REQUEST" || error.code == "INVALID_ARGUMENT" ? 400 : error.code == "TIMEOUT" ? 504 : 500;
                WriteError(context.Response, statusCode, error);

                if (EnableRequestLogging)
                {
                    Debug.LogWarning($"[UnityMcp] Error {path} {error.code} {(DateTime.UtcNow - startedUtc).TotalMilliseconds:F0}ms {error.message}");
                }
            }
        }

        private static JObject ReadJsonBody(HttpListenerRequest request)
        {
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8))
            {
                var text = reader.ReadToEnd();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return new JObject();
                }

                return JObject.Parse(text);
            }
        }

        private static bool IsExistingServerHealthy()
        {
            try
            {
                var request = WebRequest.CreateHttp($"http://127.0.0.1:{Port}/health");
                request.Method = "GET";
                request.Timeout = 1000;
                request.ReadWriteTimeout = 1000;
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    return response.StatusCode == HttpStatusCode.OK;
                }
            }
            catch
            {
                return false;
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

            using (var stream = response.OutputStream)
            {
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        private static void WriteError(HttpListenerResponse response, int statusCode, McpErrorResponse error)
        {
            WriteJson(response, statusCode, new
            {
                ok = false,
                error = error.message,
                errorCode = error.code,
                context = error.context,
                details = error.details,
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
            var value = Environment.GetEnvironmentVariable("UNITY_MCP_PORT");

            if (int.TryParse(value, out var port) && port > 0 && port <= 65535)
            {
                return port;
            }

            return 51234;
        }

        private static int ReadMainThreadTimeoutMs()
        {
            var value = Environment.GetEnvironmentVariable("UNITY_MCP_TIMEOUT_MS");
            if (int.TryParse(value, out var timeoutMs) && timeoutMs >= 1000)
            {
                return timeoutMs + 5000;
            }

            return 30000;
        }

        private static bool ReadRequestLoggingEnabled()
        {
            var value = Environment.GetEnvironmentVariable("UNITY_MCP_LOG_REQUESTS");
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.Equals("1", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildQuerySummary(NameValueCollection query)
        {
            if (query == null || query.Count == 0)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            foreach (var key in query.AllKeys)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                parts.Add($"{key}={query[key]}");
            }

            return parts.Count == 0 ? string.Empty : $"?{string.Join("&", parts.ToArray())}";
        }
    }

    [InitializeOnLoad]
    internal static class UnityMcpMainThread
    {
        private static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();
        private static readonly int MainThreadId;

        static UnityMcpMainThread()
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
