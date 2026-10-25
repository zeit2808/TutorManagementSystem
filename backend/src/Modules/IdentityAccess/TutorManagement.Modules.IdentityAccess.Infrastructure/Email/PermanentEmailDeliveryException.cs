namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Email;

/// <summary>
/// The provider refused this one message for good (malformed or non-existent recipient). Retrying cannot help,
/// unlike a timeout, a lost connection or a login failure, which are retried with back-off.
/// </summary>
public sealed class PermanentEmailDeliveryException(string message, Exception? innerException = null)
    : Exception(message, innerException);
