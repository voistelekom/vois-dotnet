using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Vois.Internal;

namespace Vois
{
    public sealed class CallOptions
    {
        /// <summary>Your own record id; comes back in webhooks and call history (≤64; letters, digits, . _ : -).</summary>
        public string? Reference { get; set; }
        /// <summary>Number shown to the callee; must be one of your account's lines.</summary>
        public string? CallerIdNumber { get; set; }
        /// <summary>Phone answers itself (only if VOIS enabled it for your account).</summary>
        public bool? AutoAnswer { get; set; }
    }

    public sealed class CdrFilter
    {
        /// <summary>inbound / outbound</summary>
        public string? Direction { get; set; }
        /// <summary>answered / no_answer / busy / failed</summary>
        public string? Status { get; set; }
        public string? Extension { get; set; }
        /// <summary>Caller or callee number (last 10 digits match).</summary>
        public string? Number { get; set; }
        public int? Page { get; set; }
        /// <summary>≤200, default 50.</summary>
        public int? PerPage { get; set; }
    }

    public sealed class CallResult
    {
        internal CallResult(string callId, string status) { CallId = callId; Status = status; }
        public string CallId { get; }
        public string Status { get; }
    }

    public sealed class Extension
    {
        internal Extension(JsonElement e)
        {
            Number = e.Str("extension") ?? "";
            Name = e.Str("name");
            Enabled = e.Bool("enabled");
            Registered = e.Bool("registered");
            InCall = e.Bool("in_call");
            UserAgent = e.Str("user_agent");
        }

        public string Number { get; }
        public string? Name { get; }
        public bool Enabled { get; }
        /// <summary>Phone is connected to the PBX; click-to-call works only then.</summary>
        public bool Registered { get; }
        public bool InCall { get; }
        public string? UserAgent { get; }
    }

    public sealed class ActiveCall
    {
        internal ActiveCall(JsonElement e)
        {
            CallId = e.Str("call_id") ?? "";
            Direction = e.Str("direction");
            From = e.Str("from");
            To = e.Str("to");
            Extension = e.Str("extension");
            State = e.Str("state");
            StartedAt = e.Date("started_at");
        }

        public string CallId { get; }
        public string? Direction { get; }
        public string? From { get; }
        public string? To { get; }
        public string? Extension { get; }
        /// <summary>RINGING, EARLY, ACTIVE, HELD</summary>
        public string? State { get; }
        public DateTimeOffset? StartedAt { get; }
    }

    public sealed class CallRecord
    {
        internal CallRecord(JsonElement e)
        {
            CallId = e.Str("call_id") ?? "";
            Direction = e.Str("direction");
            From = e.Str("from");
            To = e.Str("to");
            Extension = e.Str("extension");
            StartedAt = e.Date("started_at");
            AnsweredAt = e.Date("answered_at");
            EndedAt = e.Date("ended_at");
            Duration = e.Int("duration");
            TalkTime = e.Int("talk_time");
            Status = e.Str("status");
            RecordingAvailable = e.Bool("recording_available");
            Reference = e.Str("reference");
        }

        public string CallId { get; }
        public string? Direction { get; }
        public string? From { get; }
        public string? To { get; }
        public string? Extension { get; }
        public DateTimeOffset? StartedAt { get; }
        public DateTimeOffset? AnsweredAt { get; }
        public DateTimeOffset? EndedAt { get; }
        /// <summary>Total seconds.</summary>
        public int Duration { get; }
        /// <summary>Talk seconds.</summary>
        public int TalkTime { get; }
        /// <summary>answered / no_answer / busy / failed</summary>
        public string? Status { get; }
        public bool RecordingAvailable { get; }
        public string? Reference { get; }
    }

    public sealed class CdrPage
    {
        internal CdrPage(IReadOnlyList<CallRecord> items, int page, int perPage, int total)
        {
            Items = items; Page = page; PerPage = perPage; Total = total;
        }

        public IReadOnlyList<CallRecord> Items { get; }
        public int Page { get; }
        public int PerPage { get; }
        public int Total { get; }
    }

    /// <summary>
    /// VOIS Voice API client. Docs: https://docs.vois.com.tr/voice/
    /// Throws <see cref="VoisVoiceException"/> for API errors, <see cref="VoisException"/> for network/unexpected responses.
    /// </summary>
    public sealed class VoisVoiceClient : IDisposable
    {
        public const string DefaultBaseUrl = "https://api.vois.com.tr/v1/voice/";

        private static readonly Regex Uuid = new Regex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly string _base;
        private readonly string _apiKey;
        private readonly bool _xApiKey;

        /// <param name="apiKey">Key created in the VOIS panel (Ayarlar › Açık REST API &amp; Webhook).</param>
        /// <param name="httpClient">Optional shared HttpClient; it is not modified or disposed.</param>
        /// <param name="baseUrl">Defaults to https://api.vois.com.tr/v1/voice/</param>
        /// <param name="useXApiKeyHeader">Send the key as X-API-Key instead of Authorization: Bearer.</param>
        public VoisVoiceClient(string apiKey, HttpClient? httpClient = null, string baseUrl = DefaultBaseUrl, bool useXApiKeyHeader = false)
        {
            if (string.IsNullOrEmpty(apiKey)) throw new ArgumentException("Ses API anahtarı gerekli.");
            _apiKey = apiKey;
            _xApiKey = useXApiKeyHeader;
            _ownsHttp = httpClient is null;
            _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _base = Http.Base(baseUrl);
        }

        /// <summary>Click-to-call: rings <paramref name="extension"/>; when answered, dials <paramref name="number"/>.</summary>
        public async Task<CallResult> CallAsync(string extension, string number, CallOptions? options = null, CancellationToken ct = default)
        {
            var d = new Dictionary<string, object?> { ["extension"] = extension, ["number"] = number };
            d.AddIf("caller_id_number", options?.CallerIdNumber);
            d.AddIf("auto_answer", options?.AutoAnswer);
            d.AddIf("reference", options?.Reference);
            var data = await RequestAsync(HttpMethod.Post, "calls", d, ct).ConfigureAwait(false);
            return new CallResult(data.Str("call_id") ?? "", data.Str("status") ?? "");
        }

        /// <summary>Hang up a live call. Returns false if the call no longer exists.</summary>
        public async Task<bool> HangupAsync(string callId, CancellationToken ct = default)
        {
            var path = "calls/" + Id(callId);
            try
            {
                return (await RequestAsync(HttpMethod.Delete, path, null, ct).ConfigureAwait(false)).Bool("hung_up");
            }
            catch (VoisVoiceException e) when (e.HttpStatus == 404)
            {
                return false;
            }
        }

        public async Task<IReadOnlyList<ActiveCall>> ActiveCallsAsync(CancellationToken ct = default)
            => Items(await RequestAsync(HttpMethod.Get, "calls/active", null, ct).ConfigureAwait(false)).Select(e => new ActiveCall(e)).ToList();

        public async Task<IReadOnlyList<Extension>> ExtensionsAsync(CancellationToken ct = default)
            => Items(await RequestAsync(HttpMethod.Get, "extensions", null, ct).ConfigureAwait(false)).Select(e => new Extension(e)).ToList();

        /// <summary>One page of call history (range ≤31 days).</summary>
        public async Task<CdrPage> CdrAsync(DateTime from, DateTime to, CdrFilter? filter = null, CancellationToken ct = default)
        {
            var q = new List<string> { "from=" + D(from), "to=" + D(to) };
            void Add(string k, object? v) { if (v is not null) q.Add(k + "=" + Uri.EscapeDataString(Convert.ToString(v, CultureInfo.InvariantCulture)!)); }
            Add("direction", filter?.Direction);
            Add("status", filter?.Status);
            Add("extension", filter?.Extension);
            Add("number", filter?.Number);
            Add("page", filter?.Page);
            Add("per_page", filter?.PerPage);
            var (data, meta) = await RequestFullAsync(HttpMethod.Get, "cdr?" + string.Join("&", q), null, ct).ConfigureAwait(false);
            var items = Items(data).Select(e => new CallRecord(e)).ToList();
            return new CdrPage(items, meta.Int("page"), meta.Int("per_page"), meta.Int("total"));
        }

        /// <summary>Every call in the range, fetching pages as you iterate.</summary>
        public async IAsyncEnumerable<CallRecord> CdrAllAsync(DateTime from, DateTime to, CdrFilter? filter = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            var page = filter?.Page ?? 1;
            while (true)
            {
                var f = new CdrFilter { Direction = filter?.Direction, Status = filter?.Status, Extension = filter?.Extension, Number = filter?.Number, PerPage = filter?.PerPage, Page = page };
                var res = await CdrAsync(from, to, f, ct).ConfigureAwait(false);
                foreach (var c in res.Items) yield return c;
                if (res.Items.Count == 0 || res.PerPage <= 0 || page * res.PerPage >= res.Total) yield break;
                page++;
            }
        }

        public async Task<CallRecord> CdrShowAsync(string callId, CancellationToken ct = default)
            => new CallRecord(await RequestAsync(HttpMethod.Get, "cdr/" + Id(callId), null, ct).ConfigureAwait(false));

        /// <summary>
        /// Save the call recording and return the saved file path. Recordings are WAV while fresh and MP3 after the PBX's
        /// nightly compression: pass a DIRECTORY and the file is saved as "cagri-&lt;call_id&gt;.wav|.mp3"; pass a file path to choose the name.
        /// Throws <see cref="VoisVoiceException"/> (not_found) if there is no recording.
        /// </summary>
        public async Task<string> DownloadRecordingAsync(string callId, string path, CancellationToken ct = default)
        {
            var id = Id(callId);
            using var req = Request(HttpMethod.Get, "recordings/" + id, null);
            req.Headers.TryAddWithoutValidation("User-Agent", Http.UserAgent);
            HttpResponseMessage res;
            try
            {
                res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !ct.IsCancellationRequested))
            {
                throw new VoisException("VOIS Ses API'ye ulaşılamadı: " + e.Message, null, e);
            }
            using (res)
            {
                var status = (int)res.StatusCode;
                if (status >= 400) Fail(status, await res.Content.ReadAsStringAsync().ConfigureAwait(false));
                var final = Directory.Exists(path) ? Path.Combine(path, "cagri-" + id + "." + Ext(res)) : path;
                var tmp = final + ".part";
                try
                {
                    using (var src = await res.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var dst = File.Create(tmp))
                        await src.CopyToAsync(dst, 81920, ct).ConfigureAwait(false);
                    if (File.Exists(final)) File.Delete(final);
                    File.Move(tmp, final);
                }
                catch
                {
                    if (File.Exists(tmp)) File.Delete(tmp);
                    throw;
                }
                return final;
            }
        }

        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
        }

        // ------------------------------------------------------------ internals

        private static string D(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static string Id(string callId)
        {
            if (callId is null || !Uuid.IsMatch(callId)) throw new ArgumentException("Geçersiz call_id.", nameof(callId));
            return callId.ToLowerInvariant();
        }

        private static IEnumerable<JsonElement> Items(JsonElement data)
            => data.ValueKind == JsonValueKind.Array ? data.EnumerateArray() : Enumerable.Empty<JsonElement>();

        private static string Ext(HttpResponseMessage res)
        {
            var name = res.Content.Headers.ContentDisposition?.FileNameStar ?? res.Content.Headers.ContentDisposition?.FileName;
            var ext = string.IsNullOrEmpty(name) ? "" : Path.GetExtension(name!.Trim('"')).TrimStart('.').ToLowerInvariant();
            if (ext.Length is >= 2 and <= 5) return ext;
            var type = res.Content.Headers.ContentType?.MediaType ?? "";
            return type.IndexOf("mpeg", StringComparison.OrdinalIgnoreCase) >= 0 ? "mp3" : "wav";
        }

        private HttpRequestMessage Request(HttpMethod method, string path, object? body)
        {
            var req = new HttpRequestMessage(method, _base + path);
            if (_xApiKey) req.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
            else req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
            if (body is not null) req.Content = Http.JsonBody(body);
            return req;
        }

        private async Task<JsonElement> RequestAsync(HttpMethod method, string path, object? body, CancellationToken ct)
            => (await RequestFullAsync(method, path, body, ct).ConfigureAwait(false)).Data;

        private async Task<(JsonElement Data, JsonElement Meta)> RequestFullAsync(HttpMethod method, string path, object? body, CancellationToken ct)
        {
            using var req = Request(method, path, body);
            var (res, text) = await Http.SendAsync(_http, req, "Ses", ct).ConfigureAwait(false);
            using (res)
            {
                var status = (int)res.StatusCode;
                if (status >= 400) Fail(status, text);
                using var doc = Http.TryParse(text);
                if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("data", out var data))
                    throw new VoisException($"VOIS Ses API'den beklenmeyen yanıt (HTTP {status}).", status);
                var meta = doc.RootElement.TryGetProperty("meta", out var m) ? m.Clone() : default;
                return (data.Clone(), meta);
            }
        }

        private static void Fail(int status, string text)
        {
            using var doc = Http.TryParse(text);
            if (doc is not null && doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.Str("error") is { } code)
                throw new VoisVoiceException(doc.RootElement.Str("message") ?? code, code, status);
            throw new VoisException($"VOIS Ses API hata döndürdü (HTTP {status}).", status);
        }
    }

    internal static class JsonExt
    {
        internal static string? Str(this JsonElement e, string name)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        internal static bool Bool(this JsonElement e, string name)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

        internal static int Int(this JsonElement e, string name)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

        internal static DateTimeOffset? Date(this JsonElement e, string name)
            => e.Str(name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTimeOffset?)null;
    }
}
