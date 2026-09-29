using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MofidEasySdk.Authentication;
using MofidEasySdk.Internal;

namespace MofidEasySdk;

/// <summary>
/// Client for the EasyTrader order API. Create one instance and reuse it; it is safe to use
/// from multiple threads.
/// </summary>
public sealed class EasyTraderClient : IEasyTraderClient
{
    private const string OrderPath = "core/api/v2/order";
    private const string DeleteOrderPath = "core/api/v2/delete-order";
    private const string SdkVersionHeader = "easy-sdk";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string SdkVersion = GetSdkVersion();

    // Shared by every client not created through DI. PooledConnectionLifetime makes it pick up DNS
    // changes; the per-request timeout comes from EasyTraderOptions.Timeout instead of HttpClient.Timeout.
    private static readonly HttpClient SharedHttpClient = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private readonly HttpClient _httpClient;
    private readonly IEasyTraderTokenProvider _tokenProvider;
    private readonly EasyTraderOptions _options;
    private readonly Uri _orderUri;
    private readonly Uri _deleteOrderUri;

    /// <summary>Creates a client that uses a token copied from the browser.</summary>
    /// <param name="accessToken">JWT from d.easytrader.ir, with or without a <c>"Bearer "</c> prefix.</param>
    /// <param name="options">Optional settings.</param>
    public EasyTraderClient(string accessToken, EasyTraderOptions? options = null)
        : this(new StaticTokenProvider(accessToken), options)
    {
    }

    /// <summary>Creates a client that reads the token from <paramref name="tokenProvider"/> before each request.</summary>
    public EasyTraderClient(IEasyTraderTokenProvider tokenProvider, EasyTraderOptions? options = null)
        : this(SharedHttpClient, tokenProvider, options ?? new EasyTraderOptions())
    {
    }

    /// <summary>
    /// Creates a client on an existing <see cref="HttpClient"/>. Used by
    /// <see cref="EasyTraderServiceCollectionExtensions.AddEasyTraderClient"/>.
    /// </summary>
    [ActivatorUtilitiesConstructor]
    public EasyTraderClient(HttpClient httpClient, IEasyTraderTokenProvider tokenProvider, IOptions<EasyTraderOptions> options)
        : this(httpClient, tokenProvider, options.Value)
    {
    }

    private EasyTraderClient(HttpClient httpClient, IEasyTraderTokenProvider tokenProvider, EasyTraderOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.Timeout, TimeSpan.Zero, nameof(options.Timeout));

        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _options = options;

        var baseAddress = options.BaseAddress.AbsoluteUri.EndsWith('/')
            ? options.BaseAddress
            : new Uri(options.BaseAddress.AbsoluteUri + "/");
        _orderUri = new Uri(baseAddress, OrderPath);
        _deleteOrderUri = new Uri(baseAddress, DeleteOrderPath);
    }

    /// <inheritdoc />
    public Task<OrderResponse> AddOrderAsync(AddOrderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateIsin(request.SymbolIsin, nameof(request.SymbolIsin));
        ValidatePriceAndQuantity(request.Price, request.Quantity);
        ValidateValidity(request.Validity);
        if (!Enum.IsDefined(request.Side))
        {
            throw new ArgumentException($"Unknown order side '{request.Side}'.", nameof(request));
        }

        var body = new AddOrderBody(new AddOrderPayload(
            request.Price,
            request.Quantity,
            request.Side,
            request.Validity,
            request.SymbolIsin,
            _options.OrderFrom));
        return SendAsync(OrderOperation.Add, targetOrderId: null, HttpMethod.Post, _orderUri, body, cancellationToken);
    }

    /// <inheritdoc />
    public Task<OrderResponse> EditOrderAsync(EditOrderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OrderId, nameof(request.OrderId));
        ValidateIsin(request.SymbolIsin, nameof(request.SymbolIsin));
        ValidatePriceAndQuantity(request.Price, request.Quantity);
        ValidateValidity(request.Validity);

        var body = new EditOrderBody(new EditOrderPayload(
            request.Price,
            request.Quantity,
            request.Validity,
            request.SymbolIsin,
            _options.OrderFrom,
            request.OrderId));
        return SendAsync(OrderOperation.Edit, request.OrderId, HttpMethod.Put, _orderUri, body, cancellationToken);
    }

    /// <inheritdoc />
    public Task<OrderResponse> DeleteOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        var body = new DeleteOrderBody(orderId, _options.OrderFrom);
        return SendAsync(OrderOperation.Delete, orderId, HttpMethod.Delete, _deleteOrderUri, body, cancellationToken);
    }

    private async Task<OrderResponse> SendAsync<TBody>(
        OrderOperation operation,
        string? targetOrderId,
        HttpMethod method,
        Uri uri,
        TBody body,
        CancellationToken cancellationToken)
    {
        var token = JwtToken.Parse(await _tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false));
        if (token.IsExpired(DateTimeOffset.UtcNow, _options.TokenExpirySkew))
        {
            throw new EasyTraderAuthenticationException(
                $"The access token expired at {token.ExpiresAt:u}. Copy a fresh token from d.easytrader.ir.");
        }

        using var request = new HttpRequestMessage(method, uri)
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation(SdkVersionHeader, SdkVersion);

        HttpStatusCode statusCode;
        string content;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(_options.Timeout);
            try
            {
                using var response = await _httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
                statusCode = response.StatusCode;
                content = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                // Report the caller's token, not the internal linked one.
                throw new OperationCanceledException(ex.Message, ex, cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                throw new EasyTraderOrderStateUnknownException(operation, targetOrderId, $"no response within {_options.Timeout.TotalSeconds:0.#} s", ex);
            }
            catch (HttpRequestException ex) when (ex.HttpRequestError is HttpRequestError.NameResolutionError
                                                      or HttpRequestError.ConnectionError
                                                      or HttpRequestError.SecureConnectionError)
            {
                throw new EasyTraderConnectionException(operation, ex);
            }
            catch (HttpRequestException ex)
            {
                throw new EasyTraderOrderStateUnknownException(operation, targetOrderId, ex.Message, ex);
            }
        }

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new EasyTraderAuthenticationException(
                $"EasyTrader rejected the access token ({(int)statusCode}). Copy a fresh token from d.easytrader.ir.",
                statusCode,
                content);
        }

        var order = OrderResponseParser.TryParse(content);
        if (order is { IsSuccessful: false })
        {
            throw new EasyTraderOrderException(order.Id, order.Message, order.Error, statusCode);
        }

        if (statusCode is < HttpStatusCode.OK or > (HttpStatusCode)299)
        {
            throw new EasyTraderApiException(statusCode, content);
        }

        if (order is null)
        {
            throw new EasyTraderApiException(statusCode, content, $"EasyTrader returned {(int)statusCode} but the body is not an order response: {content}");
        }

        // Delete responses may leave out the ID; the deleted order is the one we asked for.
        var id = string.IsNullOrWhiteSpace(order.Id) && operation == OrderOperation.Delete ? targetOrderId : order.Id;
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new EasyTraderApiException(statusCode, content, $"EasyTrader accepted the {operation} request but returned no order ID: {content}");
        }

        return new OrderResponse(id, order.Message);
    }

    private static void ValidateIsin(string isin, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(isin, paramName);
        if (isin.Length != 12 || !isin.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException($"'{isin}' is not a valid ISIN (expected 12 letters or digits, e.g. IRO1TBAN0001).", paramName);
        }
    }

    private static void ValidateValidity(OrderValidity validity)
    {
        if (!Enum.IsDefined(validity))
        {
            throw new ArgumentException($"Order validity '{validity}' is not supported. Only {nameof(OrderValidity.Day)} is supported.", nameof(validity));
        }
    }

    private static void ValidatePriceAndQuantity(long price, long quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(price);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
    }

    private static string GetSdkVersion()
    {
        var version = typeof(EasyTraderClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }
}
