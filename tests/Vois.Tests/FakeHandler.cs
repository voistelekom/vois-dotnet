using System.Net;
using System.Text;

namespace Vois.Tests;

/// <summary>Returns queued responses and records every request (with its body read eagerly).</summary>
public sealed class FakeHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();
    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = new();

    public FakeHandler Json(HttpStatusCode status, string json, Action<HttpResponseMessage>? configure = null)
    {
        _responses.Enqueue(() =>
        {
            var r = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            configure?.Invoke(r);
            return r;
        });
        return this;
    }

    public FakeHandler Raw(HttpResponseMessage response)
    {
        _responses.Enqueue(() => response);
        return this;
    }

    public FakeHandler Throw(Exception e)
    {
        _responses.Enqueue(() => throw e);
        return this;
    }

    public (HttpRequestMessage Request, string? Body) Last => Requests[^1];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request, body));
        if (_responses.Count == 0) throw new InvalidOperationException("no queued response");
        return _responses.Dequeue()();
    }
}
