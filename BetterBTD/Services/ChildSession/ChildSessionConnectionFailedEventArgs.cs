namespace BetterBTD.Services.ChildSession;

internal sealed class ChildSessionConnectionFailedEventArgs(
    string message,
    int errorCode) : EventArgs
{
    public string Message { get; } = message;

    public int ErrorCode { get; } = errorCode;
}
