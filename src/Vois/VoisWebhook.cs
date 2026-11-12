using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vois
{
    /// <summary>A verified VOIS webhook event.</summary>
    public sealed class WebhookEvent
    {
        internal WebhookEvent(string? id, string @event, JsonElement data, DateTimeOffset? firedAt)
        {
            Id = id; Event = @event; Data = data; FiredAt = firedAt;
        }

        /// <summary>Unique per event; store it to ignore duplicates.</summary>
        public string? Id { get; }
        /// <summary>call.ringing / call.answered / call.ended (or CRM events).</summary>
        public string Event { get; }
        public JsonElement Data { get; }
        public DateTimeOffset? FiredAt { get; }
    }

    /// <summary>Verify VOIS webhook requests. Docs: https://docs.vois.com.tr/voice/#imza</summary>
    public static class VoisWebhook
    {
        /// <summary>
        /// Checks the X-VOIS-Signature HMAC-SHA256 (constant-time) and that fired_at is within
        /// <paramref name="toleranceSeconds"/> of now (replay protection; 0 disables).
        /// Throws <see cref="VoisSignatureException"/> → answer 401.
        /// </summary>
        /// <param name="rawBody">The request body exactly as received (read it before any JSON model binding).</param>
        public static WebhookEvent Verify(string rawBody, string? signatureHeader, string secret, int toleranceSeconds = 300, DateTimeOffset? now = null)
        {
            if (string.IsNullOrEmpty(secret)) throw new ArgumentException("Webhook gizli anahtarı boş.", nameof(secret));
            string expected;
            using (var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
                expected = "sha256=" + Hex(h.ComputeHash(Encoding.UTF8.GetBytes(rawBody ?? "")));
            if (signatureHeader is null || !FixedTimeEquals(expected, signatureHeader.Trim()))
                throw new VoisSignatureException("Webhook imzası geçersiz.");

            JsonDocument doc;
            try { doc = JsonDocument.Parse(rawBody!); }
            catch (JsonException) { throw new VoisSignatureException("Webhook gövdesi okunamadı."); }
            using (doc)
            {
                var root = doc.RootElement;
                var ev = root.Str("event");
                if (root.ValueKind != JsonValueKind.Object || ev is null) throw new VoisSignatureException("Webhook gövdesi okunamadı.");
                DateTimeOffset? fired = root.Str("fired_at") is { } f && DateTimeOffset.TryParse(f, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTimeOffset?)null;
                if (toleranceSeconds > 0 && (fired is null || Math.Abs(((now ?? DateTimeOffset.UtcNow) - fired.Value).TotalSeconds) > toleranceSeconds))
                    throw new VoisSignatureException("Webhook olayı çok eski ya da zamanı geçersiz (tekrar gönderim olabilir).");
                var data = root.TryGetProperty("data", out var x) ? x.Clone() : default;
                return new WebhookEvent(root.Str("id"), ev, data, fired);
            }
        }

        private static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a.Length != b.Length) return false;
            var diff = 0;
            for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
