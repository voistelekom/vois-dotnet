using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Vois.Tests;

public class SmsClientTests
{
    private static (VoisSmsClient Client, FakeHandler Handler) Make(FakeHandler h, string? baseUrl = null)
        => (baseUrl is null ? new VoisSmsClient("kullanici", "sifre", new HttpClient(h)) : new VoisSmsClient("kullanici", "sifre", new HttpClient(h), baseUrl), h);

    private static string Ok(string data) => "{\"data\":" + data + ",\"err\":null}";

    private static JsonElement Body(FakeHandler h) => JsonDocument.Parse(h.Last.Body!).RootElement;

    [Fact]
    public async Task SendSingle_posts_documented_body_with_basic_auth()
    {
        var (sms, h) = Make(new FakeHandler().Json(HttpStatusCode.OK, Ok("{\"pkgID\":48213377}")));

        var id = await sms.SendSingleAsync("905321234567", "Merhaba", "BASLIK", "Randevu hatirlatma", new SmsOptions { Encoding = 1, CustomId = "r-1" });

        Assert.Equal(48213377, id);
        var r = h.Last.Request;
        Assert.Equal(HttpMethod.Post, r.Method);
        Assert.Equal("https://api.vois.com.tr/sms/create", r.RequestUri!.ToString());
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("kullanici:sifre")), r.Headers.Authorization!.ToString());
        Assert.Equal("application/json", r.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("{\"type\":1,\"sendingType\":0,\"title\":\"Randevu hatirlatma\",\"sender\":\"BASLIK\",\"number\":\"905321234567\",\"content\":\"Merhaba\",\"encoding\":1,\"customID\":\"r-1\"}", h.Last.Body);
    }

    [Fact]
    public async Task Options_map_to_documented_field_names()
    {
        var (sms, h) = Make(new FakeHandler().Json(HttpStatusCode.OK, Ok("{\"pkgID\":1}")));
        await sms.SendSingleAsync("9053", "x", "B", "Baslik", new SmsOptions
        {
            Validity = 60, Commercial = true, SendingDate = "2026-11-15 10:00", PushUrl = "https://ornek.com/r",
            SkipAhsQuery = true, RecipientType = 1, Gateway = "g-uuid",
        });
        var b = Body(h);
        Assert.Equal(60, b.GetProperty("validity").GetInt32());
        Assert.True(b.GetProperty("commercial").GetBoolean());
        Assert.Equal("2026-11-15 10:00", b.GetProperty("sendingDate").GetString());
        Assert.Equal("https://ornek.com/r", b.GetProperty("pushSettings").GetProperty("url").GetString());
        Assert.True(b.GetProperty("skipAhsQuery").GetBoolean());
        Assert.Equal(1, b.GetProperty("recipientType").GetInt32());
        Assert.Equal("g-uuid", b.GetProperty("gateway").GetString());
        Assert.False(b.TryGetProperty("customID", out _));
    }

    [Fact]
    public async Task Bulk_personalized_and_otp()
    {
        var h = new FakeHandler().Json(HttpStatusCode.OK, Ok("{\"pkgID\":1}")).Json(HttpStatusCode.OK, Ok("{\"pkgID\":2}")).Json(HttpStatusCode.OK, Ok("{\"pkgID\":3}"));
        var (sms, _) = Make(h);

        Assert.Equal(1, await sms.SendBulkAsync(new[] { "905321234567", "905441234567" }, "Kampanya", "BASLIK", "Kasim kampanyasi"));
        var b = Body(h);
        Assert.Equal(1, b.GetProperty("sendingType").GetInt32());
        Assert.Equal(2, b.GetProperty("numbers").GetArrayLength());

        Assert.Equal(2, await sms.SendPersonalizedAsync(new[] { new PersonalizedMessage("905321234567", "Ayşe Hanım", "F-1") }, "BASLIK", "Fatura bildirimi"));
        b = Body(h);
        Assert.Equal(2, b.GetProperty("sendingType").GetInt32());
        var n = b.GetProperty("numbers")[0];
        Assert.Equal(("905321234567", "Ayşe Hanım", "F-1"), (n.GetProperty("nr").GetString(), n.GetProperty("msg").GetString(), n.GetProperty("xid").GetString()));
        Assert.False(b.TryGetProperty("content", out _));

        Assert.Equal(3, await sms.SendOtpAsync("905321234567", "Kod: 1", "BASLIK", new SmsOptions { Validity = 3 }));
        Assert.Equal("/sms/create-otp", h.Last.Request.RequestUri!.AbsolutePath);
        Assert.Equal("{\"number\":\"905321234567\",\"sender\":\"BASLIK\",\"content\":\"Kod: 1\",\"validity\":3}", h.Last.Body);
    }

    [Fact]
    public async Task Cancel_requires_identifier_and_sends_id_or_custom_id()
    {
        var h = new FakeHandler().Json(HttpStatusCode.OK, Ok("{\"status\":0}")).Json(HttpStatusCode.OK, Ok("{\"status\":0}"));
        var (sms, _) = Make(h);
        await Assert.ThrowsAsync<ArgumentException>(() => sms.CancelAsync());
        await sms.CancelAsync(pkgId: 48213390);
        Assert.Equal("{\"id\":48213390}", h.Last.Body);
        await sms.CancelAsync(customId: "k-7");
        Assert.Equal("{\"customID\":\"k-7\"}", h.Last.Body);
    }

    [Fact]
    public async Task Reports_and_account()
    {
        var list = "{\"stats\":{\"totalRecord\":1},\"list\":[{\"id\":1}]}";
        var h = new FakeHandler();
        foreach (var _ in Enumerable.Range(0, 8)) h.Json(HttpStatusCode.OK, Ok(list));
        h.Json(HttpStatusCode.OK, Ok("{\"credit\":25400}"));
        var (sms, _) = Make(h);

        var p = await sms.PackagesAsync("2026-11-01 00:00", "2026-11-10 23:59", pageSize: 50);
        Assert.Equal(1, p.GetProperty("stats").GetProperty("totalRecord").GetInt32());
        Assert.Equal(("/sms/list", "{\"startDate\":\"2026-11-01 00:00\",\"finishDate\":\"2026-11-10 23:59\",\"pageIndex\":0,\"pageSize\":50}"), (h.Last.Request.RequestUri!.AbsolutePath, h.Last.Body));

        await sms.PackageItemsAsync(pkgId: 48213402);
        Assert.Equal(("/sms/list-item", "{\"pkgID\":48213402,\"pageIndex\":0,\"pageSize\":100}"), (h.Last.Request.RequestUri!.AbsolutePath, h.Last.Body));

        await sms.SummaryAsync("2026-11-01 00:00", "2026-11-30 23:59");
        Assert.Equal("/sms/summary", h.Last.Request.RequestUri!.AbsolutePath);

        await sms.InquiryAsync("905321234567");
        Assert.Equal(("/sms/inquiry-number", "{\"gsm\":\"905321234567\"}"), (h.Last.Request.RequestUri!.AbsolutePath, h.Last.Body));

        await sms.SendersAsync();
        Assert.Equal(("/sms/list-sender", "{\"pageIndex\":0,\"pageSize\":100}"), (h.Last.Request.RequestUri!.AbsolutePath, h.Last.Body));

        await sms.GatewaysAsync();
        Assert.Equal((HttpMethod.Get, "/sms/list-gateway"), (h.Last.Request.Method, h.Last.Request.RequestUri!.AbsolutePath));

        await sms.InboundAsync("RET", "2026-11-01 00:00", "2026-11-10 23:59");
        Assert.Equal(("/mo/list-request", "{\"keyword\":\"RET\",\"startDate\":\"2026-11-01 00:00\",\"finishDate\":\"2026-11-10 23:59\",\"pageIndex\":0,\"pageSize\":100}"), (h.Last.Request.RequestUri!.AbsolutePath, h.Last.Body));

        var raw = await sms.SendAsync(new Dictionary<string, object?> { ["type"] = 4, ["sendingType"] = 1 });
        Assert.Equal("{\"type\":4,\"sendingType\":1}", h.Last.Body);
        Assert.Equal(JsonValueKind.Object, raw.ValueKind);

        Assert.Equal(25400m, await sms.CreditAsync());
        Assert.Equal((HttpMethod.Get, "/user/credit"), (h.Last.Request.Method, h.Last.Request.RequestUri!.AbsolutePath));
    }

    [Fact]
    public async Task Api_error_becomes_sms_exception_with_turkish_message()
    {
        var (sms, _) = Make(new FakeHandler().Json((HttpStatusCode)417, "{\"data\":null,\"err\":{\"status\":417,\"code\":\"ERR_USER_CREDIT_REQUIRED\",\"message\":\"ERR_USER_CREDIT_REQUIRED\"}}"));
        var e = await Assert.ThrowsAsync<VoisSmsException>(() => sms.CreditAsync());
        Assert.Equal(("ERR_USER_CREDIT_REQUIRED", 417, (int?)417), (e.ErrorCode, e.Status, e.HttpStatus));
        Assert.Contains("kredi", e.Message);
    }

    [Fact]
    public async Task Auth_error_and_error_inside_200()
    {
        var h = new FakeHandler()
            .Json(HttpStatusCode.Forbidden, "{\"data\":null,\"err\":{\"status\":401,\"code\":\"ERR_UNAUTHORIZED_REQUEST\",\"message\":\"x\"}}")
            .Json(HttpStatusCode.OK, "{\"data\":null,\"err\":{\"status\":417,\"code\":\"ERR_INVALID_PARAM\",\"message\":\"x\"}}");
        var (sms, _) = Make(h);
        var e = await Assert.ThrowsAsync<VoisSmsException>(() => sms.CreditAsync());
        Assert.Equal(("ERR_UNAUTHORIZED_REQUEST", 401, (int?)403), (e.ErrorCode, e.Status, e.HttpStatus));
        await Assert.ThrowsAsync<VoisSmsException>(() => sms.CreditAsync());
    }

    [Fact]
    public async Task Non_json_and_network_errors_are_vois_exception()
    {
        var h = new FakeHandler()
            .Raw(new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>") })
            .Throw(new HttpRequestException("down"));
        var (sms, _) = Make(h);
        var e = await Assert.ThrowsAsync<VoisException>(() => sms.CreditAsync());
        Assert.IsNotType<VoisSmsException>(e);
        Assert.Equal(502, e.HttpStatus);
        var n = await Assert.ThrowsAsync<VoisException>(() => sms.CreditAsync());
        Assert.Null(n.HttpStatus);
    }

    [Fact]
    public async Task Custom_base_url_and_empty_credentials()
    {
        var (sms, h) = Make(new FakeHandler().Json(HttpStatusCode.OK, Ok("{\"credit\":1}")), "https://ornek.test");
        await sms.CreditAsync();
        Assert.Equal("https://ornek.test/user/credit", h.Last.Request.RequestUri!.ToString());
        Assert.Throws<ArgumentException>(() => new VoisSmsClient("", "x"));
    }
}
