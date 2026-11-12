using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace Vois.Tests;

public class VoiceClientTests
{
    private const string Id = "9aeba619-e656-4afd-aacc-ba64dcf24aef";

    private static (VoisVoiceClient Client, FakeHandler Handler) Make(FakeHandler h) => (new VoisVoiceClient("vois_2361_abc", new HttpClient(h)), h);

    private static string Ok(string data, string meta = "{}") => "{\"data\":" + data + ",\"meta\":" + meta + "}";

    [Fact]
    public async Task Call_posts_with_bearer_and_returns_call_id()
    {
        var (v, h) = Make(new FakeHandler().Json(HttpStatusCode.Accepted, Ok("{\"call_id\":\"" + Id + "\",\"status\":\"ringing_extension\"}")));
        var r = await v.CallAsync("101", "05321234567", new CallOptions { Reference = "musteri-4521" });
        Assert.Equal((Id, "ringing_extension"), (r.CallId, r.Status));
        Assert.Equal((HttpMethod.Post, "https://api.vois.com.tr/v1/voice/calls"), (h.Last.Request.Method, h.Last.Request.RequestUri!.ToString()));
        Assert.Equal("Bearer vois_2361_abc", h.Last.Request.Headers.Authorization!.ToString());
        Assert.Equal("{\"extension\":\"101\",\"number\":\"05321234567\",\"reference\":\"musteri-4521\"}", h.Last.Body);
    }

    [Fact]
    public async Task Call_options_serialize_to_api_names()
    {
        var (v, h) = Make(new FakeHandler().Json(HttpStatusCode.Accepted, Ok("{\"call_id\":\"" + Id + "\",\"status\":\"ringing_extension\"}")));
        await v.CallAsync("101", "0532", new CallOptions { CallerIdNumber = "902124189159", AutoAnswer = true });
        Assert.Equal("{\"extension\":\"101\",\"number\":\"0532\",\"caller_id_number\":\"902124189159\",\"auto_answer\":true}", h.Last.Body);
    }

    [Fact]
    public async Task Hangup_true_then_false_on_404()
    {
        var (v, h) = Make(new FakeHandler()
            .Json(HttpStatusCode.OK, Ok("{\"hung_up\":true}"))
            .Json(HttpStatusCode.NotFound, "{\"error\":\"not_found\",\"message\":\"Süren çağrı bulunamadı.\"}"));
        Assert.True(await v.HangupAsync(Id));
        Assert.Equal((HttpMethod.Delete, "/v1/voice/calls/" + Id), (h.Last.Request.Method, h.Last.Request.RequestUri!.AbsolutePath));
        Assert.False(await v.HangupAsync(Id));
    }

    [Fact]
    public async Task Read_endpoints_map_to_typed_results()
    {
        var (v, h) = Make(new FakeHandler()
            .Json(HttpStatusCode.OK, Ok("[{\"extension\":\"101\",\"name\":\"Satış 1\",\"enabled\":true,\"registered\":true,\"in_call\":false,\"user_agent\":\"Yealink\"}]"))
            .Json(HttpStatusCode.OK, Ok("[{\"call_id\":\"" + Id + "\",\"direction\":\"outbound\",\"from\":\"101\",\"to\":\"0532\",\"extension\":\"101\",\"state\":\"ACTIVE\",\"started_at\":\"2026-11-12T01:17:50+03:00\"}]"))
            .Json(HttpStatusCode.OK, Ok("[" + Cdr(Id) + "]", "{\"page\":1,\"per_page\":50,\"total\":1}"))
            .Json(HttpStatusCode.OK, Ok(Cdr(Id))));

        var ext = Assert.Single(await v.ExtensionsAsync());
        Assert.Equal(("101", "Satış 1", true, true, false, "Yealink"), (ext.Number, ext.Name, ext.Enabled, ext.Registered, ext.InCall, ext.UserAgent));

        var ac = Assert.Single(await v.ActiveCallsAsync());
        Assert.Equal((Id, "ACTIVE", "outbound"), (ac.CallId, ac.State, ac.Direction));
        Assert.Equal("/v1/voice/calls/active", h.Last.Request.RequestUri!.AbsolutePath);

        var page = await v.CdrAsync(new DateTime(2026, 11, 1), new DateTime(2026, 11, 12), new CdrFilter { Direction = "outbound", PerPage = 50 });
        Assert.Equal((1, 50, 1), (page.Page, page.PerPage, page.Total));
        var c = Assert.Single(page.Items);
        Assert.Equal((Id, "answered", 51, 51, true, "musteri-4521"), (c.CallId, c.Status, c.Duration, c.TalkTime, c.RecordingAvailable, c.Reference));
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 1, 17, 54, TimeSpan.FromHours(3)), c.AnsweredAt);
        Assert.Equal("?from=2026-11-01&to=2026-11-12&direction=outbound&per_page=50", h.Last.Request.RequestUri!.Query);

        Assert.Equal(Id, (await v.CdrShowAsync(Id.ToUpperInvariant())).CallId);
        Assert.Equal("/v1/voice/cdr/" + Id, h.Last.Request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CdrAll_walks_pages()
    {
        var (v, h) = Make(new FakeHandler()
            .Json(HttpStatusCode.OK, Ok("[" + Cdr("a") + "," + Cdr("b") + "]", "{\"page\":1,\"per_page\":2,\"total\":3}"))
            .Json(HttpStatusCode.OK, Ok("[" + Cdr("c") + "]", "{\"page\":2,\"per_page\":2,\"total\":3}")));
        var ids = new List<string>();
        await foreach (var c in v.CdrAllAsync(new DateTime(2026, 11, 1), new DateTime(2026, 11, 12), new CdrFilter { PerPage = 2 })) ids.Add(c.CallId);
        Assert.Equal(new[] { "a", "b", "c" }, ids);
        Assert.Equal(2, h.Requests.Count);
        Assert.Contains("page=2", h.Last.Request.RequestUri!.Query);
    }

    [Fact]
    public async Task DownloadRecording_to_directory_uses_server_filename()
    {
        var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 0x49, 0x44, 0x33, 1 }) };
        resp.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        resp.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "cagri-" + Id + ".mp3" };
        var (v, h) = Make(new FakeHandler().Raw(resp));
        var dir = Directory.CreateTempSubdirectory("vois").FullName;
        try
        {
            var saved = await v.DownloadRecordingAsync(Id, dir);
            Assert.Equal(Path.Combine(dir, "cagri-" + Id + ".mp3"), saved);
            Assert.Equal(new byte[] { 0x49, 0x44, 0x33, 1 }, await File.ReadAllBytesAsync(saved));
            Assert.Single(Directory.GetFiles(dir));
            Assert.Equal("/v1/voice/recordings/" + Id, h.Last.Request.RequestUri!.AbsolutePath);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task DownloadRecording_to_file_and_404_leaves_nothing()
    {
        var ok = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 0x52, 0x49, 0x46, 0x46 }) };
        ok.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/x-wav");
        var (v, _) = Make(new FakeHandler().Raw(ok).Json(HttpStatusCode.NotFound, "{\"error\":\"not_found\",\"message\":\"Kayıt bulunamadı.\"}"));
        var dir = Directory.CreateTempSubdirectory("vois").FullName;
        try
        {
            var file = Path.Combine(dir, "k.wav");
            Assert.Equal(file, await v.DownloadRecordingAsync(Id, file));
            File.Delete(file);
            var e = await Assert.ThrowsAsync<VoisVoiceException>(() => v.DownloadRecordingAsync(Id, file));
            Assert.Equal(("not_found", (int?)404), (e.ErrorCode, e.HttpStatus));
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Errors_map_to_voice_exception()
    {
        var (v, _) = Make(new FakeHandler()
            .Json(HttpStatusCode.Conflict, "{\"error\":\"extension_unavailable\",\"message\":\"Dahili şu an bağlı değil (telefon kayıtlı değil).\"}")
            .Raw(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("oops") }));
        var e = await Assert.ThrowsAsync<VoisVoiceException>(() => v.CallAsync("102", "0532"));
        Assert.Equal(("extension_unavailable", (int?)409, "Dahili şu an bağlı değil (telefon kayıtlı değil)."), (e.ErrorCode, e.HttpStatus, e.Message));
        var p = await Assert.ThrowsAsync<VoisException>(() => v.ExtensionsAsync());
        Assert.IsNotType<VoisVoiceException>(p);
        Assert.Equal(500, p.HttpStatus);
    }

    [Fact]
    public async Task Invalid_call_id_rejected_before_request()
    {
        var (v, h) = Make(new FakeHandler());
        await Assert.ThrowsAsync<ArgumentException>(() => v.CdrShowAsync("../../etc/passwd"));
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task Shared_httpclient_not_mutated_and_x_api_key_option()
    {
        var h = new FakeHandler().Json(HttpStatusCode.OK, Ok("[]"));
        var http = new HttpClient(h);
        var v = new VoisVoiceClient("vois_k", http, useXApiKeyHeader: true);
        await v.ExtensionsAsync();
        Assert.Equal("vois_k", h.Last.Request.Headers.GetValues("X-API-Key").Single());
        Assert.Null(h.Last.Request.Headers.Authorization);
        Assert.Empty(http.DefaultRequestHeaders);
    }

    private static string Cdr(string id) =>
        "{\"call_id\":\"" + id + "\",\"direction\":\"outbound\",\"from\":\"101\",\"to\":\"0532\",\"extension\":\"101\"," +
        "\"started_at\":\"2026-11-12T01:17:50+03:00\",\"answered_at\":\"2026-11-12T01:17:54+03:00\",\"ended_at\":\"2026-11-12T01:18:45+03:00\"," +
        "\"duration\":51,\"talk_time\":51,\"status\":\"answered\",\"recording_available\":true,\"reference\":\"musteri-4521\"}";
}
