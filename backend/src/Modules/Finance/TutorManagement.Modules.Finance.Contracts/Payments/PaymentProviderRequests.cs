namespace TutorManagement.Modules.Finance.Contracts.Payments;

/// <summary>
/// Connect or change the center's SePay account. <c>currentPassword</c> is always required.
/// Leave <c>webhookSecret</c> and <c>apiToken</c> empty to keep the stored ones; both are required the first time.
/// <c>mode</c>: Off, Shadow or Auto (Auto only from Shadow). <c>expectedVersion</c> is 0 the first time.
/// The receiving account always follows the tuition bank settings.
/// </summary>
public sealed record SavePaymentProviderRequest(string CurrentPassword, string Mode, string? WebhookSecret = null,
    string? ApiToken = null, int ExpectedVersion = 0);
