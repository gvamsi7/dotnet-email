namespace ISH.CustomerPortal.API.Security;

public sealed class ApiSecurityOptions
{
    public const string SectionName = "ApiSecurity";
    public const string DevelopmentKey = "local-development-key-change-me";
    public string ApiKey { get; set; } = string.Empty;
}
