namespace Universa.Desktop.Models
{
    public enum VerificationState
    {
        None,
        Requested,
        Started,
        WaitingForKey,
        KeysExchanged,
        KeysVerified,
        Completed,
        Cancelled
    }
} 