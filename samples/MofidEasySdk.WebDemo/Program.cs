using System.Diagnostics;
using System.Text.Json.Serialization;
using MofidEasySdk;

// A web page for trying the SDK: paste your token, then add, edit and delete REAL orders.
// Run:  dotnet run --project samples/MofidEasySdk.WebDemo
// Then open http://localhost:5080 (it opens automatically).
// In GitHub Codespaces it's reached through the codespace's private forwarded port instead.

var builder = WebApplication.CreateBuilder(args);
var port = builder.Configuration.GetValue("Port", 5080);

// The hosts the page may be reached on. In a codespace, GitHub forwards the port to
// https://<codespace>-<port>.<forwarding domain>, which only the codespace owner can open.
var allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "localhost", "127.0.0.1", "[::1]", "::1" };
var codespaceName = Environment.GetEnvironmentVariable("CODESPACE_NAME");
var forwardingDomain = Environment.GetEnvironmentVariable("GITHUB_CODESPACES_PORT_FORWARDING_DOMAIN");
var codespaceHost = string.IsNullOrEmpty(codespaceName) || string.IsNullOrEmpty(forwardingDomain)
    ? null
    : $"{codespaceName}-{port}.{forwardingDomain}";
if (codespaceHost is not null)
{
    allowedHosts.Add(codespaceHost);
}

// The app holds a trading token, so it only accepts connections from this machine
// (or, in a codespace, from GitHub's port forwarding, which connects locally).
builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenLocalhost(port));

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// The token isn't configured here: the user pastes it into the page, which calls PUT /api/token.
builder.Services.AddEasyTraderClient(options => builder.Configuration.GetSection("EasyTrader").Bind(options));

var app = builder.Build();

// Only this app's own page may call the API. Checking the Host header blocks DNS-rebinding
// attacks, and requiring a custom header forces a CORS preflight that other websites fail,
// so a page open in another tab can't place orders with the stored token.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        if (!allowedHosts.Contains(context.Request.Host.Host) || !context.Request.Headers.ContainsKey(DemoApi.ClientHeader))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
    }

    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");

api.MapGet("/token", (StaticTokenProvider tokens) => DemoApi.TokenStatus(tokens));

api.MapPut("/token", (TokenInput input, StaticTokenProvider tokens) =>
{
    try
    {
        tokens.SetToken(input.Token);
        return Results.Ok(DemoApi.TokenStatus(tokens));
    }
    catch (EasyTraderAuthenticationException ex)
    {
        return Results.BadRequest(new DemoResult(false, "auth", ex.Message));
    }
});

api.MapDelete("/token", (StaticTokenProvider tokens) =>
{
    tokens.ClearToken();
    return Results.Ok(DemoApi.TokenStatus(tokens));
});

// Order calls deliberately ignore the browser's request-aborted token: closing the tab must not
// cancel an order request halfway, which would leave its state unknown. The SDK timeout still applies.
api.MapPost("/orders", (AddOrderInput input, IEasyTraderClient client) =>
    DemoApi.Run(() => client.AddOrderAsync(new AddOrderRequest(input.SymbolIsin, input.Side, input.Price, input.Quantity))));

api.MapPost("/orders/edit", (EditOrderInput input, IEasyTraderClient client) =>
    DemoApi.Run(() => client.EditOrderAsync(new EditOrderRequest(input.OrderId, input.SymbolIsin, input.Price, input.Quantity))));

api.MapPost("/orders/delete", (DeleteOrderInput input, IEasyTraderClient client) =>
    DemoApi.Run(() => client.DeleteOrderAsync(input.OrderId)));

var url = codespaceHost is null ? $"http://localhost:{port}" : $"https://{codespaceHost}";
app.Lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine($"EasyTrader SDK demo running at {url}  (Ctrl+C to stop)");

    // Codespaces opens the forwarded port in your browser itself.
    if (codespaceHost is not null || !app.Configuration.GetValue("OpenBrowser", true))
    {
        return;
    }

    try
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    catch
    {
        // No browser available; the URL is printed above.
    }
});

app.Run();

internal static class DemoApi
{
    public const string ClientHeader = "X-Demo-Client";

    public static TokenStatusResult TokenStatus(StaticTokenProvider tokens) => new(tokens.HasToken, tokens.ExpiresAt);

    /// <summary>Calls the SDK and turns each outcome into a JSON result the page can show.</summary>
    public static async Task<IResult> Run(Func<Task<OrderResponse>> call)
    {
        try
        {
            var response = await call();
            return Results.Ok(new DemoResult(true, "accepted", response.Message, response.Id));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new DemoResult(false, "invalid", ex.Message));
        }
        catch (EasyTraderOrderException ex)
        {
            return Results.UnprocessableEntity(new DemoResult(false, "rejected", ex.Message, ex.OrderId, ex.Error));
        }
        catch (EasyTraderOrderStateUnknownException ex)
        {
            return Results.Json(new DemoResult(false, "unknownState", ex.Message, ex.TargetOrderId), statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (EasyTraderConnectionException ex)
        {
            return Results.Json(new DemoResult(false, "notSent", ex.Message), statusCode: StatusCodes.Status502BadGateway);
        }
        catch (EasyTraderAuthenticationException ex)
        {
            return Results.Json(new DemoResult(false, "auth", ex.Message), statusCode: StatusCodes.Status401Unauthorized);
        }
        catch (EasyTraderApiException ex)
        {
            return Results.Json(new DemoResult(false, "apiError", ex.Message), statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

internal sealed record TokenInput(string Token);

internal sealed record AddOrderInput(string SymbolIsin, OrderSide Side, long Price, long Quantity);

internal sealed record EditOrderInput(string OrderId, string SymbolIsin, long Price, long Quantity);

internal sealed record DeleteOrderInput(string OrderId);

internal sealed record TokenStatusResult(bool HasToken, DateTimeOffset? ExpiresAt);

/// <param name="Ok">Whether the request was accepted.</param>
/// <param name="Outcome">accepted, rejected, unknownState, notSent, auth, apiError or invalid.</param>
/// <param name="Message">Server or SDK message.</param>
/// <param name="OrderId">The new/affected order ID, the rejected order's ID, or the order whose state is unknown.</param>
/// <param name="Error">OMS rejection details.</param>
internal sealed record DemoResult(bool Ok, string Outcome, string? Message, string? OrderId = null, OmsError? Error = null);
