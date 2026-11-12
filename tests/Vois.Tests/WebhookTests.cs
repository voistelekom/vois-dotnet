using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Vois.Tests;

public class WebhookTests
{
    private const string Secret = "gizli-anahtar";

    private static string Body(string firedAt = "2026-11-12T10:00:00+03:00") =>
        "{\"id\":\"3b1f6c2e-8d4a-4f0e-9a51-2c7e0b9d6a14\",\"event\":\"call.ringing\",\"data\":{\"call_id\":\"x\",\"from\":\"905321234567\"},\"fired_at\":\"" + firedAt + "\"}";

    private static string Sig(string body, string secret = Secret)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }

    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-11-12T10:01:00+03:00");

    [Fact]
    public void Valid_signature_returns_event()
    {
        var b = Body();
        var e = VoisWebhook.Verify(b, Sig(b), Secret, 300, At);
        Assert.Equal(("3b1f6c2e-8d4a-4f0e-9a51-2c7e0b9d6a14", "call.ringing", "x"), (e.Id, e.Event, e.Data.GetProperty("call_id").GetString()));
        Assert.Equal(DateTimeOffset.Parse("2026-11-12T10:00:00+03:00"), e.FiredAt);
    }

    [Theory]
    [InlineData("baska")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha1=abc")]
    [InlineData("nohdr")]
    public void Bad_signatures_rejected(string? variant)
    {
        var b = Body();
        var header = variant switch
        {
            "baska" => Sig(b, "baska"),
            "nohdr" => Sig(b)[7..],
            _ => variant,
        };
        Assert.Throws<VoisSignatureException>(() => VoisWebhook.Verify(b, header, Secret, 300, At));
    }

    [Fact]
    public void Tampered_body_rejected()
    {
        var b = Body();
        Assert.Throws<VoisSignatureException>(() => VoisWebhook.Verify(b.Replace("905321234567", "905000000000"), Sig(b), Secret, 300, At));
    }

    [Fact]
    public void Stale_and_future_events_rejected_unless_tolerance_disabled()
    {
        var b = Body();
        var late = DateTimeOffset.Parse("2026-11-12T10:06:00+03:00");
        var e = Assert.Throws<VoisSignatureException>(() => VoisWebhook.Verify(b, Sig(b), Secret, 300, late));
        Assert.Contains("eski", e.Message);
        Assert.Equal("call.ringing", VoisWebhook.Verify(b, Sig(b), Secret, 0, late).Event);
        var future = Body("2026-11-12T10:10:00+03:00");
        Assert.Throws<VoisSignatureException>(() => VoisWebhook.Verify(future, Sig(future), Secret, 300, DateTimeOffset.Parse("2026-11-12T10:00:00+03:00")));
    }

    [Fact]
    public void Signed_non_json_and_empty_secret()
    {
        Assert.Throws<VoisSignatureException>(() => VoisWebhook.Verify("not json", Sig("not json"), Secret, 0));
        Assert.Throws<ArgumentException>(() => VoisWebhook.Verify(Body(), "sha256=x", ""));
    }
}
