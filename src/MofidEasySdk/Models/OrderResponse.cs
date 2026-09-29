namespace MofidEasySdk;

/// <summary>
/// A request the order system accepted. Rejections throw <see cref="EasyTraderOrderException"/>
/// instead, so receiving this always means success.
/// </summary>
/// <param name="Id">
/// ID of the live order. Use it to edit or delete the order later. After an edit this is the ID of
/// the new order that replaced the old one; the old order is no longer in the matching engine.
/// </param>
/// <param name="Message">Message returned by the server, if any.</param>
public sealed record OrderResponse(string Id, string? Message);
