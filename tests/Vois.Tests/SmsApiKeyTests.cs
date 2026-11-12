using System.Net;
using Xunit;

namespace Vois.Tests;

public class SmsApiKeyTests
{
    private static (VoisSmsClient Client, FakeHandler Handler) Make(FakeHandler h) => (VoisSmsClient.FromApiKey("vois_2361_abc", new HttpClient(h)), h);

    private static string Ok(string data) => "{\"data\":" + data + ",\"err\":null}";

    [Fact]
    public async Task SendSingle_uses_v1_sms_path_with_bearer()
    {
        var (sms, h) = Make(new FakeHandler().Json(HttpStatusCode.OK, Ok("{\"pkgID\":7}")));
        Assert.Equal(7, await sms.SendSingleAsync("905321234567", "Merhaba", "BASLIK", "Deneme"));
        Assert.Equal((HttpMethod.Post, "https://api.vois.com.tr/v1/sms/create"), (h.Last.Request.Method, h.Last.Request.RequestUri!.ToString()));
        Assert.Equal("Bearer vois_2361_abc", h.Last.Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Every_method_maps_to_key_paths()
    {
        var h = new FakeHandler();
        for (var i = 0; i < 10; i++) h.Json(HttpStatusCode.OK, Ok("{\"credit\":1}"));
        var sms = VoisSmsClient.FromApiKey("k", new HttpClient(h));
        var calls = new (Func<Task> Call, string Method, string Path)[]
        {
            (() => sms.CreditAsync(), "GET", "credit"),
            (() => sms.GatewaysAsync(), "GET", "list-gateway"),
            (() => sms.InboundAsync(), "POST", "mo/list-request"),
            (() => sms.SendOtpAsync("9053", "x", "B"), "POST", "create-otp"),
            (() => sms.CancelAsync(5), "POST", "cancel"),
            (() => sms.PackagesAsync("a", "b"), "POST", "list"),
            (() => sms.SummaryAsync("a", "b"), "POST", "summary"),
            (() => sms.InquiryAsync("9053"), "POST", "inquiry-number"),
            (() => sms.SendersAsync(), "POST", "list-sender"),
            (() => sms.PackageItemsAsync(1), "POST", "list-item"),
        };
        foreach (var (call, method, path) in calls)
        {
            try { await call(); } catch (VoisException) { }
            Assert.Equal((method, "https://api.vois.com.tr/v1/sms/" + path), (h.Last.Request.Method.Method, h.Last.Request.RequestUri!.ToString()));
        }
    }

    [Fact]
    public async Task Credit_returns_value()
    {
        var (sms, _) = Make(new FakeHandler().Json(HttpStatusCode.OK, Ok("{\"credit\":12.5}")));
        Assert.Equal(12.5m, await sms.CreditAsync());
    }

    [Fact]
    public async Task Vois_style_403_maps_to_sms_exception()
    {
        var (sms, _) = Make(new FakeHandler().Json(HttpStatusCode.Forbidden, "{\"error\":\"sms_not_configured\",\"message\":\"SMS tanımlı değil.\"}"));
        var e = await Assert.ThrowsAsync<VoisSmsException>(() => sms.SendSingleAsync("9053", "x", "B", "T"));
        Assert.Equal(("sms_not_configured", 403, "SMS tanımlı değil."), (e.ErrorCode, e.HttpStatus, e.Message));
    }

    [Fact]
    public async Task Provider_error_shape_still_works()
    {
        var (sms, _) = Make(new FakeHandler().Json((HttpStatusCode)417, "{\"data\":null,\"err\":{\"status\":417,\"code\":\"ERR_USER_CREDIT_REQUIRED\",\"message\":\"x\"}}"));
        var e = await Assert.ThrowsAsync<VoisSmsException>(() => sms.CreditAsync());
        Assert.Equal("ERR_USER_CREDIT_REQUIRED", e.ErrorCode);
    }

    [Fact]
    public void Empty_key_rejected()
    {
        Assert.Throws<ArgumentException>(() => VoisSmsClient.FromApiKey(""));
    }

    [Fact]
    public async Task Legacy_constructor_unchanged()
    {
        var h = new FakeHandler().Json(HttpStatusCode.OK, Ok("{\"credit\":1}"));
        await new VoisSmsClient("u", "p", new HttpClient(h)).CreditAsync();
        Assert.Equal("https://api.vois.com.tr/user/credit", h.Last.Request.RequestUri!.ToString());
    }
}
