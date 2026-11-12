using System.Net;
using Xunit;

namespace Vois.Tests;

public class WebhookClientTests
{
    private static (VoisWebhookClient Client, FakeHandler Handler) Make(FakeHandler h) => (new VoisWebhookClient("vois_2361_abc", new HttpClient(h)), h);

    [Fact]
    public async Task List_returns_rows()
    {
        var (w, h) = Make(new FakeHandler().Json(HttpStatusCode.OK,
            "{\"data\":[{\"id\":3,\"event\":\"call.ended\",\"url\":\"https://a.example/h\",\"active\":true,\"created_at\":\"2026-11-12T10:00:00+03:00\"}],\"meta\":{\"total\":1}}"));
        var rows = await w.ListAsync();
        var r = Assert.Single(rows);
        Assert.Equal((3L, "call.ended", "https://a.example/h", true), (r.Id, r.Event, r.Url, r.Active));
        Assert.Equal((HttpMethod.Get, "https://api.vois.com.tr/v1/webhooks"), (h.Last.Request.Method, h.Last.Request.RequestUri!.ToString()));
        Assert.Equal("Bearer vois_2361_abc", h.Last.Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Create_posts_and_returns_secret()
    {
        var (w, h) = Make(new FakeHandler().Json(HttpStatusCode.Created, "{\"data\":{\"id\":9,\"event\":\"call.ended\",\"url\":\"https://a.example/h\",\"secret\":\"whsec_x\"}}"));
        var r = await w.CreateAsync("call.ended", "https://a.example/h");
        Assert.Equal((9L, "whsec_x"), (r.Id, r.Secret));
        Assert.Equal((HttpMethod.Post, "https://api.vois.com.tr/v1/webhooks"), (h.Last.Request.Method, h.Last.Request.RequestUri!.ToString()));
        Assert.Equal("{\"event\":\"call.ended\",\"url\":\"https://a.example/h\"}", h.Last.Body);
    }

    [Fact]
    public async Task Delete_true_on_204_false_on_404()
    {
        var (w, h) = Make(new FakeHandler().Json(HttpStatusCode.NoContent, "").Json(HttpStatusCode.NotFound, "{\"error\":\"not_found\",\"message\":\"Yok\"}"));
        Assert.True(await w.DeleteAsync(9));
        Assert.Equal((HttpMethod.Delete, "https://api.vois.com.tr/v1/webhooks/9"), (h.Last.Request.Method, h.Last.Request.RequestUri!.ToString()));
        Assert.False(await w.DeleteAsync(10));
    }

    [Fact]
    public async Task Errors_map_to_webhook_exception_or_vois_exception()
    {
        var (w, _) = Make(new FakeHandler().Json((HttpStatusCode)422, "{\"error\":\"https_required\",\"message\":\"HTTPS gerekli\"}")
            .Json(HttpStatusCode.Unauthorized, "{\"error\":\"unauthorized\",\"message\":\"x\"}").Json(HttpStatusCode.InternalServerError, "oops"));
        var e = await Assert.ThrowsAsync<VoisWebhookException>(() => w.CreateAsync("call.ended", "http://x"));
        Assert.Equal(("https_required", 422, "HTTPS gerekli"), (e.ErrorCode, e.HttpStatus, e.Message));
        Assert.Equal("unauthorized", (await Assert.ThrowsAsync<VoisWebhookException>(() => w.ListAsync())).ErrorCode);
        await Assert.ThrowsAsync<VoisException>(() => w.ListAsync());
    }

    [Fact]
    public void Empty_key_rejected()
    {
        Assert.Throws<ArgumentException>(() => new VoisWebhookClient(""));
    }

    [Fact]
    public async Task Null_data_throws_vois_exception()
    {
        var (w, _) = Make(new FakeHandler().Json(HttpStatusCode.OK, "{\"data\":null}").Json(HttpStatusCode.Created, "{\"data\":null}"));
        Assert.Contains("beklenmeyen yanıt", (await Assert.ThrowsAsync<VoisException>(() => w.ListAsync())).Message);
        Assert.Contains("beklenmeyen yanıt", (await Assert.ThrowsAsync<VoisException>(() => w.CreateAsync("call.ended", "https://a.example/h"))).Message);
    }
}
