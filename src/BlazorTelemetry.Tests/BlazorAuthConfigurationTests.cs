using BlazorTelemetry.Host.Authentication;

namespace BlazorTelemetry.Tests;

public sealed class BlazorAuthConfigurationTests
{
    private const string STRONG_SECRET = "aJ39m7qB5sR8xW2vT6nY4pL0kF1dC9zE";

    [Fact]
    public void ProductionAcceptsIndividualSecretsAndSingleLegacyAdministrator()
    {
        new BlazorAuthConfiguration { AllowedUserLogins = ["admin"], SecretKey = STRONG_SECRET }.ValidateProductionConfiguration();
        new BlazorAuthConfiguration
        {
            AllowedUserLogins = ["admin", "other"],
            AdministratorSecrets = new() { ["ADMIN"] = STRONG_SECRET, ["other"] = STRONG_SECRET + "2" }
        }.ValidateProductionConfiguration();
    }

    [Theory]
    [InlineData("demo")]
    [InlineData("")]
    [InlineData("short-secret")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void ProductionRejectsWeakSecrets(string secret)
    {
        Assert.Throws<InvalidOperationException>(() => new BlazorAuthConfiguration { AllowedUserLogins = ["admin"], SecretKey = secret }.ValidateProductionConfiguration());
    }

    [Theory]
    [InlineData("demo@demo.com")]
    [InlineData(" admin ")]
    [InlineData("")]
    public void ProductionRejectsInvalidIdentifiers(string identifier)
    {
        Assert.Throws<InvalidOperationException>(() => new BlazorAuthConfiguration { AllowedUserLogins = [identifier], SecretKey = STRONG_SECRET }.ValidateProductionConfiguration());
    }

    [Fact]
    public void ProductionRejectsMissingDuplicateAndSharedAdministratorConfiguration()
    {
        Assert.Throws<InvalidOperationException>(() => new BlazorAuthConfiguration().ValidateProductionConfiguration());
        Assert.Throws<InvalidOperationException>(() => new BlazorAuthConfiguration { AllowedUserLogins = ["admin", "ADMIN"], SecretKey = STRONG_SECRET }.ValidateProductionConfiguration());
        Assert.Throws<InvalidOperationException>(() => new BlazorAuthConfiguration { AllowedUserLogins = ["admin", "other"], SecretKey = STRONG_SECRET }.ValidateProductionConfiguration());
        Assert.Throws<InvalidOperationException>(() => new BlazorAuthConfiguration
        {
            AllowedUserLogins = ["admin", "other"], AdministratorSecrets = new() { ["admin"] = STRONG_SECRET, ["other"] = STRONG_SECRET }
        }.ValidateProductionConfiguration());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void ProductionRejectsUnboundedCookieLifetime(int days)
    {
        Assert.Throws<InvalidOperationException>(() => new BlazorAuthConfiguration { AllowedUserLogins = ["admin"], SecretKey = STRONG_SECRET, CookieDurationInDays = days }.ValidateProductionConfiguration());
    }
}
