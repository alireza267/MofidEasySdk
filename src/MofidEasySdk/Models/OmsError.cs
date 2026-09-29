namespace MofidEasySdk;

/// <summary>A rejection returned by the order management system (OMS).</summary>
/// <param name="Kind">Known error type, or <see cref="OmsErrorKind.Unknown"/> for a name this SDK version doesn't know.</param>
/// <param name="Name">Error name exactly as sent by the server, for example <c>PriceIsNotInRangeError</c>.</param>
/// <param name="Message">Description from the server (in Persian).</param>
/// <param name="Code">Numeric error code from the server.</param>
public sealed record OmsError(OmsErrorKind Kind, string Name, string Message, int Code);
