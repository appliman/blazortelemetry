namespace Blazor2fa;

internal class SetupCode
{
	public string Account { get; internal set; } = string.Empty;

	public string AccountSecretKey { get; internal set; } = string.Empty;

	public string ManualEntryKey { get; internal set; } = string.Empty;

	public string QrCodeSetupImageUrl { get; internal set; } = string.Empty;
}
