# MofidEasySdk

A .NET 10 client for adding, editing and deleting orders on the Mofid EasyTrader platform ([d.easytrader.ir](https://d.easytrader.ir)).

It handles the request formats, the bearer token, token-expiry checks and error mapping, so your code only deals with orders.

**Docs and examples:** [alireza267.github.io/MofidEasySdk](https://alireza267.github.io/MofidEasySdk/)

**Place orders online:** [alireza267.github.io/MofidEasySdk/trade](https://alireza267.github.io/MofidEasySdk/trade/). This needs the EasyTrader API to allow the site (see [Online order page](#online-order-page)).

## Get your access token

1. Log in to [d.easytrader.ir](https://d.easytrader.ir).
2. Open DevTools (F12) → **Network**, and click any request to `api-mts.orbis.easytrader.ir`.
3. Copy the value of the `Authorization` header. With or without the `Bearer ` prefix is fine.

The token is a JWT and expires. The SDK reads its `exp` claim and throws `EasyTraderAuthenticationException` before sending once it has expired. When that happens, copy a fresh token.

> Treat the token like a password: it can place orders on your account. Keep it out of source control.

## Quick start

```csharp
using MofidEasySdk;

var client = new EasyTraderClient(token);

// Add
OrderResponse added = await client.AddOrderAsync(new AddOrderRequest(
    SymbolIsin: "IRO1TBAN0001",
    Side: OrderSide.Buy,
    Price: 20520,
    Quantity: 244));

// Edit: this replaces the order with a new one, which has a new ID
OrderResponse edited = await client.EditOrderAsync(new EditOrderRequest(
    OrderId: added.Id,
    SymbolIsin: "IRO1TBAN0001",
    Price: 20520,
    Quantity: 270));

// Delete: use the ID returned by the edit, not the original one
await client.DeleteOrderAsync(edited.Id);
```

Create one client and reuse it. It is thread-safe and doesn't need disposing.

Every method returns an `OrderResponse` with the order's `Id` and the server's `Message`. A returned response always means the request was accepted, and `Id` is always set. Every other outcome throws; see [Errors](#errors).

> **Edits create a new order.** Once an edit is booked, the old order is no longer in the matching engine. Use `edited.Id` for any later edit or delete.

Orders are always valid for the trading day (`OrderValidity.Day`). Other validity types aren't supported yet.

## Handling rejected orders

The server may create an order, assign it an ID, and then have the OMS reject it. It still answers HTTP 200, but with `isSuccessful: false`. The SDK throws `EasyTraderOrderException`:

- `Error` says why the order was rejected.
- `OrderId` holds the ID of the rejected order, or `null` if none was assigned. A rejected order isn't live and doesn't need deleting.
- `ServerMessage` holds the server's `message` field.

```csharp
try
{
    await client.AddOrderAsync(request);
}
catch (EasyTraderOrderException ex)
{
    switch (ex.Error?.Kind)
    {
        case OmsErrorKind.MoneyBalanceIsNotEnough: /* top up */ break;
        case OmsErrorKind.PriceIsNotInRange:       /* adjust price */ break;
        case OmsErrorKind.UserMoneyIsNotReady:     /* try again shortly */ break;
        default: Console.WriteLine($"{ex.Error?.Name} ({ex.Error?.Code}): {ex.Error?.Message}"); break;
    }
}
```

`OmsError` has `Kind` (an `OmsErrorKind` enum), the server's `Name` (for example `PriceIsNotInRangeError`), its Persian `Message`, and its `Code`. Error names this SDK version doesn't know yet come back as `OmsErrorKind.Unknown`, with `Name` still set.

| `OmsErrorKind` | Meaning |
|---|---|
| `MoneyBalanceIsNotEnough` | Not enough money in the account |
| `PriceIsNotInRange` | Price outside the allowed range |
| `VolumeIsNotInRange` | Quantity outside the allowed range |
| `SymbolIsClose` | The symbol is closed |
| `GroupStateNotAllow` | The symbol group's state doesn't allow trading |
| `BuyNotAllowedInThisSymbol` / `SellNotAllowedInThisSymbol` | Buying / selling not allowed on this symbol |
| `SellOnBlockAsset` | Blocked assets can't be sold |
| `LowPricedSellIsNotAllowed` | Sell order below the minimum value |
| `BuyFromSelf` / `SellToSelf` | The order could trade against your own order |
| `UserMoneyIsNotReady` | Balance not ready yet; try again |
| `DuplicatedOrder` | Duplicate order |
| `OrderInterval` | Orders sent too close together |
| `RequestIsExpired` | The request expired |
| `NotEnoughBaseSymbolAssetForTabaee` | Put option on a base symbol needs the underlying shares |
| `ModifyOrder` / `DeleteOrder` | The order couldn't be edited / deleted |
| `ErrorInGetOrderRule`, `LoadSymbol`, `InternalSystem` | Server-side failures |
| `Custom` | Server-defined error; see `Message` and `Code` |
| `Unknown` | Name not recognized by this SDK version |

## Timeouts: unknown order state

If no response arrives within `Timeout` (30 s by default), or the connection breaks after the request was sent, the server may or may not have applied it. The SDK throws `EasyTraderOrderStateUnknownException`:

- `Operation` says which request it was: `Add`, `Edit` or `Delete`.
- `TargetOrderId` holds the order an edit or delete targeted.

**Don't blindly retry.** A retried add can place a duplicate order. Check your orders on d.easytrader.ir first.

```csharp
catch (EasyTraderOrderStateUnknownException ex)
{
    logger.LogWarning("Order state unknown after {Operation} of {OrderId}; check before retrying", ex.Operation, ex.TargetOrderId);
}
```

If the SDK couldn't connect at all (DNS, TCP or TLS failure), nothing was sent. It throws `EasyTraderConnectionException` instead, and a retry is safe.

## Replacing an expired token

```csharp
var tokens = new StaticTokenProvider(token);
var client = new EasyTraderClient(tokens);

// later, when the token expires:
tokens.SetToken(newToken);
Console.WriteLine(tokens.ExpiresAt);
```

To load the token from your own source (a file, a secret store, ...), implement `IEasyTraderTokenProvider`. It is called before every request.

## Dependency injection

```csharp
builder.Services.AddEasyTraderClient(options =>
{
    options.AccessToken = builder.Configuration["EasyTrader:Token"];
});

// inject IEasyTraderClient anywhere; to swap the token at runtime:
app.Services.GetRequiredService<StaticTokenProvider>().SetToken(newToken);
```

`AddEasyTraderClient` returns an `IHttpClientBuilder`, so you can add your own handlers. Don't add automatic retries for add or edit: if a request times out after the server accepted it, a retry can place a duplicate order.

## Options

| Option | Default | Description |
|---|---|---|
| `BaseAddress` | `https://api-mts.orbis.easytrader.ir/` | API address |
| `AccessToken` | – | Initial token (DI only) |
| `OrderFrom` | `1000` | `orderFrom` value sent with each request |
| `TokenExpirySkew` | 30 s | Treat the token as expired this long before `exp` |
| `Timeout` | 30 s | How long to wait for a response before throwing `EasyTraderOrderStateUnknownException` |

## Errors

| Exception | When | Order changed? |
|---|---|---|
| `ArgumentException` | Invalid input (empty order ID, ISIN not 12 characters, price or quantity ≤ 0, unsupported validity) | No, nothing was sent |
| `EasyTraderAuthenticationException` | Token missing, malformed or expired, or the server returned 401/403 | No |
| `EasyTraderConnectionException` | Couldn't connect (DNS, TCP or TLS failure) | No, nothing was sent. Safe to retry. |
| `EasyTraderOrderException` | The order system rejected the request (`isSuccessful: false`). `Error` holds the reason and `OrderId` the rejected order's ID. | No |
| `EasyTraderOrderStateUnknownException` | Timed out, or the connection broke after sending | **Unknown.** Check before retrying. |
| `EasyTraderApiException` | Any other non-success status, or an unexpected response (for example an accepted add with no `id`). `StatusCode` and `ResponseContent` hold the details. | Unknown |

All SDK exceptions derive from `EasyTraderException`. Cancelling through your `CancellationToken` throws `OperationCanceledException`; if you cancel after the request was sent, the order state is unknown.

## Online order page

[`docs/trade`](docs/trade) is a static page on GitHub Pages where anyone can paste their token and add, edit or delete orders from the browser. It follows the same rules as the SDK: the same request bodies, token expiry check, rejection details, and "unknown state" after a timeout.

The browser sends requests straight from `alireza267.github.io` to `api-mts.orbis.easytrader.ir`, so the **API's CORS settings must allow it**. Today they only allow `https://d.easytrader.ir`. The API needs:

```
Access-Control-Allow-Origin:  https://alireza267.github.io   (in addition to https://d.easytrader.ir)
Access-Control-Allow-Headers: … existing …, easy-sdk
```

Until then, the page detects the block on load, says so, and disables ordering.

The page is locked down with a Content Security Policy. It loads scripts only from itself, can only connect to the EasyTrader API, keeps the token in memory, and refuses to run inside a frame.

## Web demo

`samples/MofidEasySdk.WebDemo` is a web page for trying the SDK without writing code. Paste your token, fill in the order fields, and add, edit or delete orders. It tracks each order's status (live, replaced, deleted, rejected, unknown) and logs every response, including the OMS error messages.

**The orders are real.** The app keeps the token in memory and never writes it to disk.

### Run it on GitHub (no install)

[![Open in GitHub Codespaces](https://github.com/codespaces/badge.svg)](https://codespaces.new/alireza267/MofidEasySdk?quickstart=1)

1. Click the button and choose **Create codespace**. The first start takes a few minutes while it installs .NET and builds.
2. The demo starts by itself, and the page opens in a new browser tab. If it doesn't, open the **Ports** tab and click the globe icon next to **EasyTrader demo**.
3. Stop the codespace when you're done (**github.com/codespaces → ⋯ → Stop codespace**), so it doesn't use up your free hours.

The port is private: only you can open it, after signing in to GitHub.

### Run it on your machine

```
dotnet run --project samples/MofidEasySdk.WebDemo
```

It opens http://localhost:5080 in your browser. Only connections from your own machine are accepted. Use `--Port=5090` to change the port.

## Console sample

`samples/MofidEasySdk.Sample` adds a real order, edits it, then deletes it. It tracks which order is live, so it deletes that order even if a step fails:

```
dotnet run --project samples/MofidEasySdk.Sample -- <token>
```

Or run it without arguments and paste the token when asked.

## Build and test

```
dotnet build
dotnet test
```

## Contributing

`main` is protected: changes go in through pull requests reviewed by the code owner. See [CONTRIBUTING.md](CONTRIBUTING.md).
