using System;
using System.Collections.Specialized;
using System.IO;

namespace TA.ReadOnlyUnityMcp
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
            if (exception is TimeoutException)
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
