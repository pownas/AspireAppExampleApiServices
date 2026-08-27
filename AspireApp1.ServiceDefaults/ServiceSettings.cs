namespace AspireApp1.ServiceDefaults;

/// <summary>
/// Configuration settings shared across worker services.
/// Bind from the "ServiceSettings" section in appsettings.json.
/// </summary>
public sealed class ServiceSettings
{
    public const string SectionName = "ServiceSettings";

    /// <summary>
    /// When true, periodic status and polling log messages are emitted at the Information level.
    /// When false (default), those messages are suppressed to reduce noise in the log output.
    /// </summary>
    public bool EnableVerboseStatusLogs { get; set; } = false;
}
