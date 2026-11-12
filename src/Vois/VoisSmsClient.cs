using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vois.Internal;

namespace Vois
{
    /// <summary>Optional fields for SMS sending (names map to the documented API fields).</summary>
    public sealed class SmsOptions
    {
        /// <summary>0 Standart, 1 Türkçe, 2 Unicode.</summary>
        public int? Encoding { get; set; }
        /// <summary>Minutes (60–1440; OTP 3–6).</summary>
        public int? Validity { get; set; }
        /// <summary>Scheduled send, "yyyy-MM-dd HH:mm".</summary>
        public string? SendingDate { get; set; }
        /// <summary>Commercial message (İYS check).</summary>
        public bool? Commercial { get; set; }
        public bool? SkipAhsQuery { get; set; }
        /// <summary>0 Bireysel, 1 Tacir, 2 Her ikisi.</summary>
        public int? RecipientType { get; set; }
        public string? Gateway { get; set; }
        /// <summary>Your own record id (customID).</summary>
        public string? CustomId { get; set; }
        /// <summary>Delivery report push URL (pushSettings.url).</summary>
        public string? PushUrl { get; set; }

        internal void AppendTo(Dictionary<string, object?> d)
        {
            d.AddIf("encoding", Encoding);
            d.AddIf("validity", Validity);
            d.AddIf("sendingDate", SendingDate);
            d.AddIf("commercial", Commercial);
            d.AddIf("skipAhsQuery", SkipAhsQuery);
            d.AddIf("recipientType", RecipientType);
            d.AddIf("gateway", Gateway);
            d.AddIf("customID", CustomId);
            if (PushUrl is not null) d["pushSettings"] = new Dictionary<string, object?> { ["url"] = PushUrl };
        }
    }

    /// <summary>One recipient of a personalized SMS.</summary>
    public sealed class PersonalizedMessage
    {
        public PersonalizedMessage(string number, string message, string? xid = null)
        {
            Number = number;
            Message = message;
            Xid = xid;
        }

        public string Number { get; }
        public string Message { get; }
        /// <summary>Your matching id for this recipient (≤32 chars).</summary>
        public string? Xid { get; }
    }

    /// <summary>
    /// VOIS SMS API client. Docs: https://docs.vois.com.tr/sms/
    /// Throws <see cref="VoisSmsException"/> for API errors, <see cref="VoisException"/> for network/unexpected responses.
    /// Report methods return the response's <c>data</c> as a <see cref="JsonElement"/>.
    /// </summary>
    public sealed class VoisSmsClient : IDisposable
    {
        public const string DefaultBaseUrl = "https://api.vois.com.tr/";

        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly string _base;
        private readonly AuthenticationHeaderValue _auth;

        /// <param name="username">SMS API user given by VOIS (not your panel login).</param>
        /// <param name="password">SMS API password.</param>
        /// <param name="httpClient">Optional shared HttpClient (e.g. from IHttpClientFactory); it is not modified or disposed.</param>
        /// <param name="baseUrl">Defaults to https://api.vois.com.tr/</param>
        public VoisSmsClient(string username, string password, HttpClient? httpClient = null, string baseUrl = DefaultBaseUrl)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                throw new ArgumentException("SMS API kullanıcı adı ve şifresi gerekli.");
            _auth = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(username + ":" + password)));
            _ownsHttp = httpClient is null;
            _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _base = Http.Base(baseUrl);
        }

        // ------------------------------------------------------------ sending

        /// <summary>Raw POST /sms/create with any documented body (e.g. MMS). Returns data.</summary>
        public Task<JsonElement> SendAsync(IDictionary<string, object?> payload, CancellationToken ct = default)
            => RequestAsync(HttpMethod.Post, "sms/create", payload, ct);

        /// <summary>One number, one text. Returns the package id (pkgID).</summary>
        public Task<long> SendSingleAsync(string number, string content, string sender, string title, SmsOptions? options = null, CancellationToken ct = default)
        {
            var d = new Dictionary<string, object?> { ["type"] = 1, ["sendingType"] = 0, ["title"] = title, ["sender"] = sender, ["number"] = number, ["content"] = content };
            options?.AppendTo(d);
            return PkgIdAsync("sms/create", d, ct);
        }

        /// <summary>Same text to many numbers. Returns pkgID.</summary>
        public Task<long> SendBulkAsync(IEnumerable<string> numbers, string content, string sender, string title, SmsOptions? options = null, CancellationToken ct = default)
        {
            var d = new Dictionary<string, object?> { ["type"] = 1, ["sendingType"] = 1, ["title"] = title, ["sender"] = sender, ["numbers"] = numbers.ToArray(), ["content"] = content };
            options?.AppendTo(d);
            return PkgIdAsync("sms/create", d, ct);
        }

        /// <summary>Different text per number. Returns pkgID.</summary>
        public Task<long> SendPersonalizedAsync(IEnumerable<PersonalizedMessage> messages, string sender, string title, SmsOptions? options = null, CancellationToken ct = default)
        {
            var list = messages.Select(m =>
            {
                var x = new Dictionary<string, object?> { ["nr"] = m.Number, ["msg"] = m.Message };
                x.AddIf("xid", m.Xid);
                return x;
            }).ToArray();
            var d = new Dictionary<string, object?> { ["type"] = 1, ["sendingType"] = 2, ["title"] = title, ["sender"] = sender, ["numbers"] = list };
            options?.AppendTo(d);
            return PkgIdAsync("sms/create", d, ct);
        }

        /// <summary>Priority one-time-code SMS (POST /sms/create-otp). Returns pkgID.</summary>
        public Task<long> SendOtpAsync(string number, string content, string sender, SmsOptions? options = null, CancellationToken ct = default)
        {
            var d = new Dictionary<string, object?> { ["number"] = number, ["sender"] = sender, ["content"] = content };
            options?.AppendTo(d);
            return PkgIdAsync("sms/create-otp", d, ct);
        }

        /// <summary>Cancel a scheduled package by pkgID or by your customID.</summary>
        public Task<JsonElement> CancelAsync(long? pkgId = null, string? customId = null, CancellationToken ct = default)
        {
            if (pkgId is null && string.IsNullOrEmpty(customId))
                throw new ArgumentException("İptal için pkgID ya da customID verin.");
            var d = pkgId is not null ? new Dictionary<string, object?> { ["id"] = pkgId } : new Dictionary<string, object?> { ["customID"] = customId };
            return RequestAsync(HttpMethod.Post, "sms/cancel", d, ct);
        }

        // ------------------------------------------------------------ reports

        /// <summary>POST /sms/list. Dates "yyyy-MM-dd HH:mm".</summary>
        public Task<JsonElement> PackagesAsync(string startDate, string finishDate, long[]? ids = null, string[]? customIds = null,
            string? keyword = null, string? sender = null, int? status = null, int pageIndex = 0, int pageSize = 50, CancellationToken ct = default)
        {
            var d = new Dictionary<string, object?> { ["startDate"] = startDate, ["finishDate"] = finishDate };
            d.AddIf("ids", ids);
            d.AddIf("customIDs", customIds);
            d.AddIf("keyword", keyword);
            d.AddIf("sender", sender);
            d.AddIf("status", status);
            d["pageIndex"] = pageIndex;
            d["pageSize"] = pageSize;
            return RequestAsync(HttpMethod.Post, "sms/list", d, ct);
        }

        /// <summary>POST /sms/list-item: per-number status of one package (pkgID or customID).</summary>
        public Task<JsonElement> PackageItemsAsync(long? pkgId = null, string? customId = null, string? target = null, int? @operator = null,
            int? state = null, int pageIndex = 0, int pageSize = 100, CancellationToken ct = default)
        {
            if (pkgId is null && string.IsNullOrEmpty(customId))
                throw new ArgumentException("pkgID ya da customID verin.");
            var d = new Dictionary<string, object?>();
            d.AddIf("pkgID", pkgId);
            d.AddIf("customID", customId);
            d.AddIf("target", target);
            d.AddIf("operator", @operator);
            d.AddIf("state", state);
            d["pageIndex"] = pageIndex;
            d["pageSize"] = pageSize;
            return RequestAsync(HttpMethod.Post, "sms/list-item", d, ct);
        }

        public Task<JsonElement> SummaryAsync(string startDate, string finishDate, CancellationToken ct = default)
            => RequestAsync(HttpMethod.Post, "sms/summary", new Dictionary<string, object?> { ["startDate"] = startDate, ["finishDate"] = finishDate }, ct);

        /// <summary>Messages sent to one number (last month unless dates are given, max 3 months).</summary>
        public Task<JsonElement> InquiryAsync(string gsm, string? sender = null, string? startDate = null, string? finishDate = null, CancellationToken ct = default)
        {
            var d = new Dictionary<string, object?> { ["gsm"] = gsm };
            d.AddIf("sender", sender);
            d.AddIf("startDate", startDate);
            d.AddIf("finishDate", finishDate);
            return RequestAsync(HttpMethod.Post, "sms/inquiry-number", d, ct);
        }

        // ------------------------------------------------------------ account

        /// <summary>Remaining credit. Harmless: good first call to test credentials.</summary>
        public async Task<decimal> CreditAsync(CancellationToken ct = default)
        {
            var data = await RequestAsync(HttpMethod.Get, "user/credit", null, ct).ConfigureAwait(false);
            return data.ValueKind == JsonValueKind.Object && data.TryGetProperty("credit", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDecimal() : 0m;
        }

        /// <summary>Sender titles; only status 1 (approved) can be used.</summary>
        public Task<JsonElement> SendersAsync(int pageIndex = 0, int pageSize = 100, string? keyword = null, int? status = null, CancellationToken ct = default)
        {
            var d = new Dictionary<string, object?>();
            d.AddIf("keyword", keyword);
            d.AddIf("status", status);
            d["pageIndex"] = pageIndex;
            d["pageSize"] = pageSize;
            return RequestAsync(HttpMethod.Post, "sms/list-sender", d, ct);
        }

        public Task<JsonElement> GatewaysAsync(CancellationToken ct = default) => RequestAsync(HttpMethod.Get, "sms/list-gateway", null, ct);

        /// <summary>Inbound (MO) messages to your short number.</summary>
        public Task<JsonElement> InboundAsync(string? keyword = null, string? startDate = null, string? finishDate = null, int pageIndex = 0, int pageSize = 100, CancellationToken ct = default)
        {
            var d = new Dictionary<string, object?>();
            d.AddIf("keyword", keyword);
            d.AddIf("startDate", startDate);
            d.AddIf("finishDate", finishDate);
            d["pageIndex"] = pageIndex;
            d["pageSize"] = pageSize;
            return RequestAsync(HttpMethod.Post, "mo/list-request", d, ct);
        }

        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
        }

        // ------------------------------------------------------------ internals

        private async Task<long> PkgIdAsync(string path, Dictionary<string, object?> body, CancellationToken ct)
        {
            var data = await RequestAsync(HttpMethod.Post, path, body, ct).ConfigureAwait(false);
            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("pkgID", out var id) && id.ValueKind == JsonValueKind.Number)
                return id.GetInt64();
            throw new VoisException("Yanıtta paket numarası (pkgID) yok.");
        }

        private async Task<JsonElement> RequestAsync(HttpMethod method, string path, object? body, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(method, _base + path);
            req.Headers.Authorization = _auth;
            if (body is not null) req.Content = Http.JsonBody(body);
            var (res, text) = await Http.SendAsync(_http, req, "SMS", ct).ConfigureAwait(false);
            using (res)
            {
                var status = (int)res.StatusCode;
                using var doc = Http.TryParse(text);
                if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object)
                    throw new VoisException($"VOIS SMS API'den beklenmeyen yanıt (HTTP {status}).", status);
                var root = doc.RootElement;
                if (root.TryGetProperty("err", out var err) && err.ValueKind == JsonValueKind.Object)
                {
                    var code = err.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()! : "ERR_UNKNOWN";
                    var msg = SmsErrors.Messages.TryGetValue(code, out var tr) ? tr
                        : err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()! : code;
                    int? errStatus = err.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : (int?)null;
                    throw new VoisSmsException(msg, code, errStatus, status);
                }
                if (status >= 400)
                    throw new VoisException($"VOIS SMS API hata döndürdü (HTTP {status}).", status);
                return root.TryGetProperty("data", out var data) ? data.Clone() : default;
            }
        }
    }
}
