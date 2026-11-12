using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vois.Internal;

namespace Vois
{
    /// <summary>A webhook registered with your API key.</summary>
    public sealed class WebhookEntry
    {
        internal WebhookEntry(JsonElement e)
        {
            Id = e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number ? id.GetInt64() : 0;
            Event = e.Str("event") ?? "";
            Url = e.Str("url") ?? "";
            Active = e.Bool("active");
            CreatedAt = e.Str("created_at");
        }

        public long Id { get; }
        public string Event { get; }
        public string Url { get; }
        public bool Active { get; }
        public string? CreatedAt { get; }
    }

    /// <summary>Result of <see cref="VoisWebhookClient.CreateAsync"/>; <see cref="Secret"/> is shown only here.</summary>
    public sealed class WebhookCreated
    {
        internal WebhookCreated(JsonElement e)
        {
            Id = e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number ? id.GetInt64() : 0;
            Event = e.Str("event") ?? "";
            Url = e.Str("url") ?? "";
            Secret = e.Str("secret") ?? "";
        }

        public long Id { get; }
        public string Event { get; }
        public string Url { get; }
        /// <summary>Signing secret for <see cref="VoisWebhook.Verify"/>; store it, it is not shown again.</summary>
        public string Secret { get; }
    }

    /// <summary>
    /// Manage the webhooks created with your API key (scope webhooks:manage). Docs: https://docs.vois.com.tr/voice/#webhook-api
    /// A key sees and deletes only its own webhooks. Throws <see cref="VoisWebhookException"/> for API errors,
    /// <see cref="VoisException"/> for network/unexpected responses.
    /// </summary>
    public sealed class VoisWebhookClient : IDisposable
    {
        public const string DefaultBaseUrl = "https://api.vois.com.tr/v1/";

        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly string _base;
        private readonly string _apiKey;

        public VoisWebhookClient(string apiKey, HttpClient? httpClient = null, string baseUrl = DefaultBaseUrl)
        {
            if (string.IsNullOrEmpty(apiKey)) throw new ArgumentException("VOIS API anahtarı gerekli.");
            _apiKey = apiKey;
            _ownsHttp = httpClient is null;
            _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _base = Http.Base(baseUrl);
        }

        public async Task<IReadOnlyList<WebhookEntry>> ListAsync(CancellationToken ct = default)
        {
            var data = await RequestAsync(HttpMethod.Get, "webhooks", null, ct).ConfigureAwait(false);
            var list = new List<WebhookEntry>();
            if (data.ValueKind == JsonValueKind.Array)
                foreach (var item in data.EnumerateArray()) list.Add(new WebhookEntry(item));
            return list;
        }

        public async Task<WebhookCreated> CreateAsync(string @event, string url, CancellationToken ct = default)
        {
            var data = await RequestAsync(HttpMethod.Post, "webhooks", new Dictionary<string, object?> { ["event"] = @event, ["url"] = url }, ct).ConfigureAwait(false);
            return new WebhookCreated(data);
        }

        /// <summary>False if it does not exist (or belongs to someone else).</summary>
        public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
        {
            try
            {
                await RequestAsync(HttpMethod.Delete, "webhooks/" + id, null, ct).ConfigureAwait(false);
                return true;
            }
            catch (VoisWebhookException e) when (e.HttpStatus == 404)
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
        }

        private async Task<JsonElement> RequestAsync(HttpMethod method, string path, object? body, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(method, _base + path);
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _apiKey);
            if (body is not null) req.Content = Http.JsonBody(body);
            var (res, text) = await Http.SendAsync(_http, req, "Webhook", ct).ConfigureAwait(false);
            using (res)
            {
                var status = (int)res.StatusCode;
                if (status >= 400)
                {
                    using var err = Http.TryParse(text);
                    if (err is not null && err.RootElement.ValueKind == JsonValueKind.Object && err.RootElement.Str("error") is { } code)
                        throw new VoisWebhookException(err.RootElement.Str("message") ?? code, code, status);
                    throw new VoisException($"VOIS API hata döndürdü (HTTP {status}).", status);
                }
                if (status == 204) return default;
                using var doc = Http.TryParse(text);
                if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("data", out var data))
                    throw new VoisException($"VOIS API'den beklenmeyen yanıt (HTTP {status}).", status);
                return data.Clone();
            }
        }
    }
}
