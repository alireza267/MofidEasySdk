using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace MofidEasySdk.Tests;

public class EasyTraderClientTests
{
    private static readonly AddOrderRequest AnyAddRequest = new("IRO1TBAN0001", OrderSide.Buy, 1, 1);

    private static void AssertJsonEqual(string expected, string? actual) =>
        Assert.True(JsonElement.DeepEquals(JsonDocument.Parse(expected).RootElement, JsonDocument.Parse(actual!).RootElement),
            $"Expected {expected} but got {actual}");

    [Fact]
    public async Task AddOrder_sends_post_matching_api_contract()
    {
        var handler = new RecordingHandler();
        var token = TestJwt.Valid();
        var client = TestClient.Create(handler, token);

        await client.AddOrderAsync(new AddOrderRequest("IRO1TBAN0001", OrderSide.Buy, 20520, 244));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api-mts.orbis.easytrader.ir/core/api/v2/order", request.Uri.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(token, request.Headers.Authorization.Parameter);
        Assert.Equal("0.1.0", Assert.Single(request.Headers.GetValues("easy-sdk")));
        AssertJsonEqual(
            """{"order":{"price":20520,"quantity":244,"side":0,"validityType":0,"symbolIsin":"IRO1TBAN0001","orderFrom":1000}}""",
            request.Body);
    }

    [Fact]
    public async Task EditOrder_sends_put_matching_api_contract()
    {
        var handler = new RecordingHandler();
        var client = TestClient.Create(handler);

        await client.EditOrderAsync(new EditOrderRequest("1121DNo37t!!=Q>r", "IRO1TBAN0001", 20520, 270));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("https://api-mts.orbis.easytrader.ir/core/api/v2/order", request.Uri.ToString());
        AssertJsonEqual(
            """{"modifyOrder":{"price":20520,"quantity":270,"validityType":0,"symbolIsin":"IRO1TBAN0001","orderFrom":1000,"parentId":"1121DNo37t!!=Q>r"}}""",
            request.Body);
    }

    [Fact]
    public async Task DeleteOrder_sends_delete_with_body_matching_api_contract()
    {
        var handler = new RecordingHandler();
        var client = TestClient.Create(handler);

        await client.DeleteOrderAsync("1121DNo37t}1*5@Z");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("https://api-mts.orbis.easytrader.ir/core/api/v2/delete-order", request.Uri.ToString());
        AssertJsonEqual("""{"orderId":"1121DNo37t}1*5@Z","orderFrom":1000}""", request.Body);
    }

    [Fact]
    public async Task Body_is_sent_with_content_length_not_chunked()
    {
        var handler = new RecordingHandler();
        var client = TestClient.Create(handler);

        await client.AddOrderAsync(AnyAddRequest);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(request.Body!), request.ContentHeaders!.ContentLength);
        Assert.Equal("application/json", request.ContentHeaders.ContentType!.MediaType);
        Assert.Equal("utf-8", request.ContentHeaders.ContentType.CharSet);
        Assert.NotEqual(true, request.Headers.TransferEncodingChunked);
    }

    [Fact]
    public async Task Sell_side_and_custom_order_from_are_sent()
    {
        var handler = new RecordingHandler();
        var client = TestClient.Create(handler, options: new EasyTraderOptions { OrderFrom = 7 });

        await client.AddOrderAsync(new AddOrderRequest("IRO1TBAN0001", OrderSide.Sell, 100, 1));

        using var body = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.Equal(1, body.RootElement.GetProperty("order").GetProperty("side").GetInt32());
        Assert.Equal(7, body.RootElement.GetProperty("order").GetProperty("orderFrom").GetInt32());
    }

    [Fact]
    public async Task Bearer_prefix_on_pasted_token_is_removed()
    {
        var handler = new RecordingHandler();
        var token = TestJwt.Valid();
        var client = TestClient.Create(handler, "Bearer " + token);

        await client.DeleteOrderAsync("id");

        Assert.Equal(token, handler.Requests[0].Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Expired_token_throws_without_sending()
    {
        var handler = new RecordingHandler();
        var client = TestClient.Create(handler, TestJwt.Create(DateTimeOffset.UtcNow.AddMinutes(-1)));

        await Assert.ThrowsAsync<EasyTraderAuthenticationException>(() => client.DeleteOrderAsync("id"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Token_inside_expiry_skew_is_treated_as_expired()
    {
        var handler = new RecordingHandler();
        var client = TestClient.Create(handler, TestJwt.Create(DateTimeOffset.UtcNow.AddSeconds(10)));

        await Assert.ThrowsAsync<EasyTraderAuthenticationException>(() => client.DeleteOrderAsync("id"));
    }

    [Fact]
    public void Malformed_token_is_rejected()
    {
        Assert.Throws<EasyTraderAuthenticationException>(() => new StaticTokenProvider("not-a-jwt"));
        Assert.Throws<EasyTraderAuthenticationException>(() => new StaticTokenProvider("a.!!!.c"));
    }

    [Fact]
    public async Task Missing_token_throws()
    {
        var handler = new RecordingHandler();
        var client = new EasyTraderClient(new HttpClient(handler), new StaticTokenProvider(),
            Microsoft.Extensions.Options.Options.Create(new EasyTraderOptions()));

        await Assert.ThrowsAsync<EasyTraderAuthenticationException>(() => client.DeleteOrderAsync("id"));
    }

    [Fact]
    public void SetToken_exposes_expiry()
    {
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds());
        var provider = new StaticTokenProvider();

        provider.SetToken(TestJwt.Create(expiresAt));

        Assert.Equal(expiresAt, provider.ExpiresAt);
    }

    [Fact]
    public async Task ClearToken_removes_the_token()
    {
        var handler = new RecordingHandler();
        var tokens = new StaticTokenProvider(TestJwt.Valid());
        var client = new EasyTraderClient(new HttpClient(handler), tokens,
            Microsoft.Extensions.Options.Options.Create(new EasyTraderOptions()));
        Assert.True(tokens.HasToken);

        tokens.ClearToken();

        Assert.False(tokens.HasToken);
        Assert.Null(tokens.ExpiresAt);
        await Assert.ThrowsAsync<EasyTraderAuthenticationException>(() => client.DeleteOrderAsync("id"));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Rejected_token_maps_to_authentication_exception(HttpStatusCode status)
    {
        var client = TestClient.Create(new RecordingHandler(status, "denied"));

        var ex = await Assert.ThrowsAsync<EasyTraderAuthenticationException>(() => client.DeleteOrderAsync("id"));
        Assert.Equal(status, ex.StatusCode);
        Assert.Equal("denied", ex.ResponseContent);
    }

    [Fact]
    public async Task Error_status_maps_to_api_exception_with_body()
    {
        var client = TestClient.Create(new RecordingHandler(HttpStatusCode.BadRequest, """{"message":"invalid price"}"""));

        var ex = await Assert.ThrowsAsync<EasyTraderApiException>(() => client.AddOrderAsync(AnyAddRequest));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("invalid price", ex.ResponseContent);
    }

    [Fact]
    public async Task Success_response_is_parsed()
    {
        var client = TestClient.Create(new RecordingHandler());

        var response = await client.AddOrderAsync(AnyAddRequest);

        Assert.Equal(new OrderResponse("1121DNo37t!!=Q>r", ""), response);
    }

    [Fact]
    public async Task Pascal_case_response_is_parsed()
    {
        var client = TestClient.Create(new RecordingHandler(HttpStatusCode.OK, """{"IsSuccessful":true,"Id":"abc","Message":"done"}"""));

        var response = await client.AddOrderAsync(AnyAddRequest);

        Assert.Equal(new OrderResponse("abc", "done"), response);
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("")]
    [InlineData("""{"foo":1}""")]
    public async Task Success_status_with_unexpected_body_throws_api_exception(string body)
    {
        var client = TestClient.Create(new RecordingHandler(HttpStatusCode.OK, body));

        var ex = await Assert.ThrowsAsync<EasyTraderApiException>(() => client.DeleteOrderAsync("id"));
        Assert.Equal(body, ex.ResponseContent);
    }

    [Theory]
    [InlineData("""{"isSuccessful":true}""")]
    [InlineData("""{"isSuccessful":true,"id":null}""")]
    [InlineData("""{"isSuccessful":true,"id":""}""")]
    public async Task Accepted_add_or_edit_without_id_throws_api_exception(string body)
    {
        var client = TestClient.Create(new RecordingHandler(HttpStatusCode.OK, body));

        await Assert.ThrowsAsync<EasyTraderApiException>(() => client.AddOrderAsync(AnyAddRequest));
        await Assert.ThrowsAsync<EasyTraderApiException>(
            () => client.EditOrderAsync(new EditOrderRequest("old", "IRO1TBAN0001", 1, 1)));
    }

    [Fact]
    public async Task Accepted_delete_without_id_returns_the_deleted_order_id()
    {
        var client = TestClient.Create(new RecordingHandler(HttpStatusCode.OK, """{"isSuccessful":true}"""));

        var response = await client.DeleteOrderAsync("to-delete");

        Assert.Equal("to-delete", response.Id);
    }

    [Fact]
    public async Task Rejected_order_throws_with_known_error_kind()
    {
        const string body = """
            {"isSuccessful":false,"id":null,"message":"",
             "omsError":[{"name":"PriceIsNotInRangeError","error":"قیمت خارج از محدوده مجاز می‌باشد","code":3}]}
            """;
        var client = TestClient.Create(new RecordingHandler(HttpStatusCode.OK, body));

        var ex = await Assert.ThrowsAsync<EasyTraderOrderException>(() => client.AddOrderAsync(AnyAddRequest));

        Assert.Equal(new OmsError(OmsErrorKind.PriceIsNotInRange, "PriceIsNotInRangeError", "قیمت خارج از محدوده مجاز می‌باشد", 3), ex.Error);
        Assert.Null(ex.OrderId);
        Assert.Contains("PriceIsNotInRangeError", ex.Message);
    }

    [Fact]
    public async Task Order_created_then_rejected_by_oms_exposes_its_id()
    {
        const string body = """{"isSuccessful":false,"id":"rejected-id","omsError":[{"name":"SymbolIsCloseError","error":"نماد بسته است","code":5}]}""";
        var client = TestClient.Create(new RecordingHandler(HttpStatusCode.OK, body));

        var ex = await Assert.ThrowsAsync<EasyTraderOrderException>(() => client.AddOrderAsync(AnyAddRequest));

        Assert.Equal("rejected-id", ex.OrderId);
        Assert.Equal(OmsErrorKind.SymbolIsClose, ex.Error!.Kind);
        Assert.Contains("rejected-id", ex.Message);
    }

    [Fact]
    public async Task Rejected_order_on_error_status_throws_order_exception()
    {
        const string body = """{"isSuccessful":false,"omsError":[{"Name":"MoneyBalanceIsNotEnoughError","Error":"مانده کاربر کافی نیست","Code":"1"}]}""";
        var client = TestClient.Create(new RecordingHandler(HttpStatusCode.BadRequest, body));

        var ex = await Assert.ThrowsAsync<EasyTraderOrderException>(() => client.AddOrderAsync(AnyAddRequest));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal(OmsErrorKind.MoneyBalanceIsNotEnough, ex.Error!.Kind);
        Assert.Equal(1, ex.Error.Code);
    }

    [Fact]
    public async Task Custom_error_keeps_server_code_and_message()
    {
        const string body = """{"isSuccessful":false,"omsError":[{"name":"OmsCustomError","error":"پیام سفارشی","code":42}]}""";
        var client = TestClient.Create(new RecordingHandler(HttpStatusCode.OK, body));

        var ex = await Assert.ThrowsAsync<EasyTraderOrderException>(() => client.DeleteOrderAsync("id"));

        Assert.Equal(new OmsError(OmsErrorKind.Custom, "OmsCustomError", "پیام سفارشی", 42), ex.Error);
    }

    [Fact]
    public async Task Unrecognized_error_name_maps_to_unknown()
    {
        const string body = """{"isSuccessful":false,"omsError":[{"name":"SomeNewError","error":"x","code":99}]}""";
        var client = TestClient.Create(new RecordingHandler(HttpStatusCode.OK, body));

        var ex = await Assert.ThrowsAsync<EasyTraderOrderException>(() => client.DeleteOrderAsync("id"));

        Assert.Equal(OmsErrorKind.Unknown, ex.Error!.Kind);
        Assert.Equal("SomeNewError", ex.Error.Name);
    }

    [Fact]
    public async Task Rejection_without_error_details_uses_message()
    {
        var client = TestClient.Create(new RecordingHandler(HttpStatusCode.OK, """{"isSuccessful":false,"message":"سفارش یافت نشد"}"""));

        var ex = await Assert.ThrowsAsync<EasyTraderOrderException>(() => client.DeleteOrderAsync("id"));

        Assert.Null(ex.Error);
        Assert.Equal("سفارش یافت نشد", ex.ServerMessage);
        Assert.Contains("سفارش یافت نشد", ex.Message);
    }

    [Theory]
    [InlineData("MoneyBalanceIsNotEnoughError", OmsErrorKind.MoneyBalanceIsNotEnough)]
    [InlineData("InternalSystemError", OmsErrorKind.InternalSystem)]
    [InlineData("ErrorInGetOrderRuleError", OmsErrorKind.ErrorInGetOrderRule)]
    [InlineData("SellOnBlockAssetError", OmsErrorKind.SellOnBlockAsset)]
    [InlineData("NotEnoughBaseSymbolAssetForTabaeeError", OmsErrorKind.NotEnoughBaseSymbolAssetForTabaee)]
    [InlineData("OmsCustomError", OmsErrorKind.Custom)]
    [InlineData("Custom", OmsErrorKind.Unknown)]
    [InlineData("1", OmsErrorKind.Unknown)]
    [InlineData(null, OmsErrorKind.Unknown)]
    public void Error_names_map_to_kinds(string? name, OmsErrorKind expected) =>
        Assert.Equal(expected, OmsErrorKindNames.FromName(name));

    [Fact]
    public async Task Timeout_on_add_reports_unknown_order_state()
    {
        var client = TestClient.Create(DelegateHandler.Hang(), options: new EasyTraderOptions { Timeout = TimeSpan.FromMilliseconds(50) });

        var ex = await Assert.ThrowsAsync<EasyTraderOrderStateUnknownException>(() => client.AddOrderAsync(AnyAddRequest));

        Assert.Equal(OrderOperation.Add, ex.Operation);
        Assert.Null(ex.TargetOrderId);
        Assert.IsAssignableFrom<OperationCanceledException>(ex.InnerException);
    }

    [Fact]
    public async Task Timeout_on_edit_reports_the_targeted_order()
    {
        var client = TestClient.Create(DelegateHandler.Hang(), options: new EasyTraderOptions { Timeout = TimeSpan.FromMilliseconds(50) });

        var ex = await Assert.ThrowsAsync<EasyTraderOrderStateUnknownException>(
            () => client.EditOrderAsync(new EditOrderRequest("old-id", "IRO1TBAN0001", 1, 1)));

        Assert.Equal(OrderOperation.Edit, ex.Operation);
        Assert.Equal("old-id", ex.TargetOrderId);
        Assert.Contains("old-id", ex.Message);
    }

    [Fact]
    public async Task HttpClient_timeout_also_reports_unknown_order_state()
    {
        var httpClient = new HttpClient(DelegateHandler.Hang()) { Timeout = TimeSpan.FromMilliseconds(50) };
        var client = TestClient.Create(httpClient);

        await Assert.ThrowsAsync<EasyTraderOrderStateUnknownException>(() => client.DeleteOrderAsync("id"));
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_timeout()
    {
        var client = TestClient.Create(DelegateHandler.Hang());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.AddOrderAsync(AnyAddRequest, cts.Token));

        Assert.Equal(cts.Token, ex.CancellationToken);
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError)]
    [InlineData(HttpRequestError.ConnectionError)]
    [InlineData(HttpRequestError.SecureConnectionError)]
    public async Task Failure_before_sending_is_a_safe_connection_error(HttpRequestError error)
    {
        var client = TestClient.Create(DelegateHandler.Throw(new HttpRequestException(error, "boom")));

        var ex = await Assert.ThrowsAsync<EasyTraderConnectionException>(() => client.AddOrderAsync(AnyAddRequest));

        Assert.Equal(OrderOperation.Add, ex.Operation);
    }

    [Theory]
    [InlineData(HttpRequestError.ResponseEnded)]
    [InlineData(HttpRequestError.InvalidResponse)]
    [InlineData(HttpRequestError.Unknown)]
    public async Task Failure_after_sending_reports_unknown_order_state(HttpRequestError error)
    {
        var client = TestClient.Create(DelegateHandler.Throw(new HttpRequestException(error, "boom")));

        var ex = await Assert.ThrowsAsync<EasyTraderOrderStateUnknownException>(() => client.DeleteOrderAsync("to-delete"));

        Assert.Equal(OrderOperation.Delete, ex.Operation);
        Assert.Equal("to-delete", ex.TargetOrderId);
    }

    [Theory]
    [InlineData("", 100, 1)]
    [InlineData("IRO1TBAN", 100, 1)]
    [InlineData("IRO1TBAN0001", 0, 1)]
    [InlineData("IRO1TBAN0001", 100, 0)]
    [InlineData("IRO1TBAN0001", -5, 1)]
    public async Task Invalid_add_request_throws_before_sending(string isin, long price, long quantity)
    {
        var handler = new RecordingHandler();
        var client = TestClient.Create(handler);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => client.AddOrderAsync(new AddOrderRequest(isin, OrderSide.Buy, price, quantity)));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Unsupported_validity_throws_before_sending()
    {
        var handler = new RecordingHandler();
        var client = TestClient.Create(handler);
        const OrderValidity goodTillCancel = (OrderValidity)2;

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => client.AddOrderAsync(new AddOrderRequest("IRO1TBAN0001", OrderSide.Buy, 1, 1, goodTillCancel)));
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => client.EditOrderAsync(new EditOrderRequest("id", "IRO1TBAN0001", 1, 1, goodTillCancel)));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Non_positive_timeout_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TestClient.Create(new RecordingHandler(), options: new EasyTraderOptions { Timeout = TimeSpan.Zero }));
    }

    [Fact]
    public async Task Edit_returns_id_of_the_new_order()
    {
        var client = TestClient.Create(new RecordingHandler(HttpStatusCode.OK, """{"isSuccessful":true,"id":"new-order-id"}"""));

        var edited = await client.EditOrderAsync(new EditOrderRequest("old-order-id", "IRO1TBAN0001", 1, 1));

        Assert.Equal("new-order-id", edited.Id);
    }

    [Fact]
    public async Task Empty_order_id_throws()
    {
        var client = TestClient.Create(new RecordingHandler());

        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.DeleteOrderAsync(" "));
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => client.EditOrderAsync(new EditOrderRequest("", "IRO1TBAN0001", 1, 1)));
    }

    [Fact]
    public async Task Custom_base_address_without_trailing_slash_is_supported()
    {
        var handler = new RecordingHandler();
        var client = TestClient.Create(handler, options: new EasyTraderOptions { BaseAddress = new Uri("https://example.test/api") });

        await client.DeleteOrderAsync("id");

        Assert.Equal("https://example.test/api/core/api/v2/delete-order", handler.Requests[0].Uri.ToString());
    }

    [Fact]
    public async Task Dependency_injection_registration_resolves_working_client()
    {
        var handler = new RecordingHandler();
        var token = TestJwt.Valid();
        var services = new ServiceCollection();
        services.AddEasyTraderClient(o => o.AccessToken = token)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IEasyTraderClient>();
        await client.DeleteOrderAsync("id");

        Assert.Equal(token, handler.Requests[0].Headers.Authorization!.Parameter);

        var newToken = TestJwt.Valid() + "x";
        provider.GetRequiredService<StaticTokenProvider>().SetToken(newToken);
        await provider.GetRequiredService<IEasyTraderClient>().DeleteOrderAsync("id");

        Assert.Equal(newToken, handler.Requests[1].Headers.Authorization!.Parameter);
    }

    [Fact]
    public void Client_is_not_disposable_so_di_does_not_track_it()
    {
        Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(EasyTraderClient)));
    }
}
