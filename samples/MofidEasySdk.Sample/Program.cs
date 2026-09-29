using MofidEasySdk;

// Log in to https://d.easytrader.ir, open DevTools > Network, and copy the token from the
// Authorization header of any API request. Then run:
//   dotnet run --project samples/MofidEasySdk.Sample -- <token>
// or run without arguments and paste the token when asked.
//
// WARNING: this sends a REAL order to your account, edits it, then deletes it.

string? token = args.Length > 0 ? args[0] : null;
if (string.IsNullOrWhiteSpace(token))
{
    Console.Write("Paste your EasyTrader access token: ");
    token = Console.ReadLine();
}

if (string.IsNullOrWhiteSpace(token))
{
    Console.WriteLine("No token given.");
    return 1;
}

// The token is passed straight to the SDK.
var client = new EasyTraderClient(token);

// The ID of the order currently in the matching engine. An edit replaces the order, so this
// changes after every successful edit. Whatever happens, the finally block deletes it.
string? liveOrderId = null;

try
{
    var added = await client.AddOrderAsync(new AddOrderRequest(
        SymbolIsin: "IRO1TBAN0001",
        Side: OrderSide.Buy,
        Price: 20520,
        Quantity: 244));
    liveOrderId = added.Id;
    Console.WriteLine($"Added order {added.Id}");

    var edited = await client.EditOrderAsync(new EditOrderRequest(
        OrderId: liveOrderId,
        SymbolIsin: "IRO1TBAN0001",
        Price: 20520,
        Quantity: 270));
    Console.WriteLine($"Edited order {liveOrderId} -> new order {edited.Id}");
    liveOrderId = edited.Id;

    await client.DeleteOrderAsync(liveOrderId);
    Console.WriteLine($"Deleted order {liveOrderId}");
    liveOrderId = null;
    return 0;
}
catch (EasyTraderOrderException ex) when (ex.Error?.Kind == OmsErrorKind.MoneyBalanceIsNotEnough)
{
    Console.WriteLine("Not enough money in the account.");
}
catch (EasyTraderOrderException ex)
{
    // The OMS may have created the order and then rejected it: ex.OrderId identifies it, but it isn't live.
    Console.WriteLine($"Order {ex.OrderId ?? "(no ID)"} rejected: {ex.Error?.Kind} - {ex.Error?.Message ?? ex.ServerMessage}");
}
catch (EasyTraderOrderStateUnknownException ex)
{
    Console.WriteLine(ex.Message);
}
catch (EasyTraderConnectionException ex)
{
    Console.WriteLine($"Could not reach EasyTrader, nothing was sent: {ex.Message}");
}
catch (EasyTraderAuthenticationException ex)
{
    Console.WriteLine($"Token problem: {ex.Message}");
}
catch (EasyTraderApiException ex)
{
    Console.WriteLine($"API error {(int)ex.StatusCode}: {ex.ResponseContent}");
}
finally
{
    if (liveOrderId is not null)
    {
        try
        {
            await client.DeleteOrderAsync(liveOrderId);
            Console.WriteLine($"Cleaned up: deleted order {liveOrderId}");
        }
        catch (EasyTraderException ex)
        {
            Console.WriteLine($"Could not delete order {liveOrderId}; delete it on d.easytrader.ir. ({ex.Message})");
        }
    }
}

return 1;
