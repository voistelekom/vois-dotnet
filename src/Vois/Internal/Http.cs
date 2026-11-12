using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Vois.Internal
{
    internal static class Http
    {
        internal static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // request bodies only; keeps Turkish text readable
        };

        internal const string UserAgent = "vois-dotnet/1.1";

        internal static string Base(string baseUrl) => baseUrl.TrimEnd('/') + "/";

        internal static HttpContent JsonBody(object body) =>
            new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");

        internal static async Task<(HttpResponseMessage Response, string Body)> SendAsync(
            HttpClient http, HttpRequestMessage request, string apiName, CancellationToken ct)
        {
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.Accept.ParseAdd("application/json");
            HttpResponseMessage res;
            try
            {
                res = await http.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !ct.IsCancellationRequested))
            {
                throw new VoisException($"VOIS {apiName} API'ye ulaşılamadı: {e.Message}", null, e);
            }
            var body = res.Content is null ? "" : await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            return (res, body);
        }

        internal static JsonDocument? TryParse(string body)
        {
            try
            {
                return JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        internal static void AddIf(this Dictionary<string, object?> d, string key, object? value)
        {
            if (value is not null) d[key] = value;
        }
    }
}
