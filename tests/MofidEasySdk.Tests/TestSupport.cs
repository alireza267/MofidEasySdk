using System.Buffers.Text;
using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Options;

namespace MofidEasySdk.Tests;

internal static class TestJwt
{
    public static string Create(DateTimeOffset expiresAt) => Create($$"""{"sub":"test","exp":{{expiresAt.ToUnixTimeSeconds()}}}""");

    public static string Create(string payloadJson)
    {
        static string Encode(string s) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(s));
        return $"{Encode("""{"alg":"HS256","typ":"JWT"}""")}.{Encode(payloadJson)}.signature";
    }

    public static string Valid() => Create(DateTimeOffset.UtcNow.AddHours(1));
}

/// <summary>Records every request and replies with a fixed response.</summary>
internal sealed class RecordingHandler(HttpStatusCode statusCode = HttpStatusCode.OK, string responseBody = TestResponses.Success) : HttpMessageHandler
{
    public List<CapturedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, request.Headers, body));
        return new HttpResponseMessage(statusCode) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
    }
}

/// <summary>Runs <paramref name="send"/> for every request, to simulate hangs and network failures.</summary>
internal sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    public static DelegateHandler Hang() => new(async (_, ct) =>
    {
        await Task.Delay(Timeout.Infinite, ct);
        throw new UnreachableException();
    });

    public static DelegateHandler Throw(Exception ex) => new((_, _) => Task.FromException<HttpResponseMessage>(ex));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        send(request, cancellationToken);
}

internal sealed record CapturedRequest(HttpMethod Method, Uri Uri, System.Net.Http.Headers.HttpRequestHeaders Headers, string? Body);

internal static class TestClient
{
    public static EasyTraderClient Create(HttpMessageHandler handler, string? token = null, EasyTraderOptions? options = null) =>
        Create(new HttpClient(handler), token, options);

    public static EasyTraderClient Create(HttpClient httpClient, string? token = null, EasyTraderOptions? options = null) =>
        new(httpClient, new StaticTokenProvider(token ?? TestJwt.Valid()), Options.Create(options ?? new EasyTraderOptions()));
}


internal static class TestResponses
{
    public const string Success = """{"isSuccessful":true,"id":"1121DNo37t!!=Q>r","message":"","omsError":null}""";
}
