namespace MofidEasySdk;

/// <summary>The kind of request that failed.</summary>
public enum OrderOperation
{
    /// <summary><see cref="IEasyTraderClient.AddOrderAsync"/>.</summary>
    Add,

    /// <summary><see cref="IEasyTraderClient.EditOrderAsync"/>.</summary>
    Edit,

    /// <summary><see cref="IEasyTraderClient.DeleteOrderAsync"/>.</summary>
    Delete,
}
