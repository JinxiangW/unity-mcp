using System;
using System.Collections.Specialized;
using System.IO;
using Newtonsoft.Json;

namespace TA.UnityMcp
{
    internal sealed class McpErrorResponse
    {
        public string code;
        public string message;
        public object context;
        public string details;

        public static McpErrorResponse FromException(Exception exception, string route, NameValueCollection query)
        {
            var code = "INTERNAL_ERROR";
            if (exception.Data.Contains("errorCode") && exception.Data["errorCode"] is string explicitCode && !string.IsNullOrWhiteSpace(explicitCode))
            {
                code = explicitCode;
            }
            else if (exception is TimeoutException)
            {
                code = "TIMEOUT";
            }
            else if (exception is FileNotFoundException)
            {
                code = "ASSET_NOT_FOUND";
            }
            else if (exception is InvalidOperationException)
            {
                code = "INVALID_REQUEST";
            }
            else if (exception is ArgumentException)
            {
                code = "INVALID_ARGUMENT";
            }
            else if (exception is JsonException)
            {
                code = "INVALID_ARGUMENT";
            }

            return new McpErrorResponse
            {
                code = code,
                message = exception.Message,
                details = exception.ToString(),
                context = new
                {
                    route,
                    query = ToQueryObject(query)
                }
            };
        }

        private static object ToQueryObject(NameValueCollection query)
        {
            if (query == null)
            {
                return null;
            }

            var entries = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in query.AllKeys)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                entries[key] = query[key];
            }

            return entries;
        }
    }
}
