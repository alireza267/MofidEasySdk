using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace MofidEasySdk;

/// <summary>Registers the EasyTrader client with dependency injection.</summary>
public static class EasyTraderServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IEasyTraderClient"/> as a typed <see cref="HttpClient"/>, plus a singleton
    /// <see cref="StaticTokenProvider"/> seeded with <see cref="EasyTraderOptions.AccessToken"/>.
    /// Register your own <see cref="IEasyTraderTokenProvider"/> before calling this to replace it.
    /// </summary>
    /// <returns>The HTTP client builder, for adding handlers. Avoid automatic retries on add/edit: a retried request can create a duplicate order.</returns>
    public static IHttpClientBuilder AddEasyTraderClient(this IServiceCollection services, Action<EasyTraderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.AddOptions<EasyTraderOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.TryAddSingleton(sp => new StaticTokenProvider(sp.GetRequiredService<IOptions<EasyTraderOptions>>().Value.AccessToken));
        services.TryAddSingleton<IEasyTraderTokenProvider>(sp => sp.GetRequiredService<StaticTokenProvider>());

        // EasyTraderClient applies EasyTraderOptions.Timeout itself, so it can report a timeout as an
        // unknown order state instead of a plain cancellation.
        return services.AddHttpClient<IEasyTraderClient, EasyTraderClient>(client => client.Timeout = Timeout.InfiniteTimeSpan);
    }
}
