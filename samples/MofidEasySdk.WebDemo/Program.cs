using System.Diagnostics;
using System.Text.Json.Serialization;
using MofidEasySdk;

// A local web page for trying the SDK: paste your token, then add, edit and delete REAL orders.
// Run:  dotnet run --project samples/MofidEasySdk.WebDemo
// Then open http://localhost:5080 (it opens automatically).

var builder = WebApplication.CreateBuilder(args);
var port = builder.Configuration.GetValue("Port", 5080);

// The app holds a trading token, so it only accepts connections from this machine.
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
        var host = context.Request.Host.Host;
        var isLocalHost = host is "localhost" or "127.0.0.1" or "[::1]" or "::1";
        if (!isLocalHost || !context.Request.Headers.ContainsKey(DemoApi.ClientHeader))
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

var url = $"http://localhost:{port}";
app.Lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine($"EasyTrader SDK demo running at {url}  (Ctrl+C to stop)");
    if (!app.Configuration.GetValue("OpenBrowser", true))
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
