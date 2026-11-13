using AnalyticDashboard.Infrastructure;
using AnalyticDashboard.Infrastructure.Auth.Email;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AnalyticDashboard.IntegrationTests.Auth;

public sealed class SmtpConfigurationTests
{
    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["ConnectionStrings:Default"] = "Host=localhost;Database=unused;Username=unused",
        ["Smtp:Host"] = "localhost",
        ["Smtp:Port"] = "1025",
        ["Smtp:FromEmail"] = "noreply@example.com",
        ["Smtp:FromName"] = "Aperture",
        ["Smtp:Username"] = "",
        ["Smtp:Password"] = ""
    };

    private static ServiceProvider CreateProvider(
        Dictionary<string, string?> settings,
        string environment = "Production")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environment));
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(null, SmtpEncryptionMode.StartTls)]
    [InlineData("StartTls", SmtpEncryptionMode.StartTls)]
    [InlineData("SslOnConnect", SmtpEncryptionMode.SslOnConnect)]
    public void Configuration_ShouldAcceptEncryptedModes_AndDefaultToRequiredStartTls(
        string? mode,
        SmtpEncryptionMode expected)
    {
        var settings = ValidSettings();
        if (mode is not null)
        {
            settings["Smtp:EncryptionMode"] = mode;
        }
        using var provider = CreateProvider(settings);

        provider.GetRequiredService<IStartupValidator>().Validate();

        Assert.Equal(expected, provider.GetRequiredService<IOptions<SmtpOptions>>().Value.EncryptionMode);
    }

    [Theory]
    [InlineData("Development", true, true)]
    [InlineData("Development", false, false)]
    [InlineData("Production", true, false)]
    [InlineData("Production", false, false)]
    [InlineData("Staging", true, false)]
    [InlineData("Testing", true, false)]
    public void Configuration_ShouldAllowUnencryptedSmtpOnlyWhenEnabledInDevelopment(
        string environment,
        bool allowUnencrypted,
        bool valid)
    {
        var settings = ValidSettings();
        settings["Smtp:Host"] = "mailpit";
        settings["Smtp:EncryptionMode"] = "None";
        settings["Smtp:AllowUnencryptedInDevelopment"] = allowUnencrypted.ToString();

        using var provider = CreateProvider(settings, environment);
        var startupValidator = provider.GetRequiredService<IStartupValidator>();
        var exception = Record.Exception(startupValidator.Validate);

        if (valid)
        {
            Assert.Null(exception);
        }
        else
        {
            Assert.IsType<OptionsValidationException>(exception);
        }
    }

    [Fact]
    public void Configuration_ShouldAllowProductionSmtpWithRequiredTls()
    {
        var settings = ValidSettings();
        settings["Smtp:EncryptionMode"] = "StartTls";
        settings["Smtp:AllowUnencryptedInDevelopment"] = "true";

        using var provider = CreateProvider(settings, Environments.Production);

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Theory]
    [InlineData("Smtp:Host", "")]
    [InlineData("Smtp:Host", "smtp://example.com")]
    [InlineData("Smtp:Port", "0")]
    [InlineData("Smtp:Port", "65536")]
    [InlineData("Smtp:FromEmail", "")]
    [InlineData("Smtp:FromEmail", "not-an-address")]
    [InlineData("Smtp:FromName", " ")]
    [InlineData("Smtp:EncryptionMode", "999")]
    [InlineData("Smtp:Username", "user")]
    [InlineData("Smtp:Password", "password")]
    [InlineData("Smtp:UseSsl", "true")]
    [InlineData("Smtp:UseSsl", "false")]
    public void Configuration_ShouldRejectInvalidSettingsAtStartup(string key, string value)
    {
        var settings = ValidSettings();
        settings[key] = value;
        using var provider = CreateProvider(settings);

        var startupValidator = provider.GetRequiredService<IStartupValidator>();
        Assert.Throws<OptionsValidationException>(startupValidator.Validate);
    }

    [Fact]
    public void Configuration_ShouldRejectUnknownEncryptionName()
    {
        var settings = ValidSettings();
        settings["Smtp:EncryptionMode"] = "StartTlsWhenAvailable";
        using var provider = CreateProvider(settings);

        var startupValidator = provider.GetRequiredService<IStartupValidator>();
        Assert.Throws<InvalidOperationException>(startupValidator.Validate);
    }

    [Theory]
    [InlineData("None", false)]
    [InlineData("StartTls", true)]
    [InlineData("SslOnConnect", true)]
    public void Configuration_ShouldRequireEncryptionForCredentials(string mode, bool valid)
    {
        var settings = ValidSettings();
        settings["Smtp:EncryptionMode"] = mode;
        settings["Smtp:Username"] = "user";
        settings["Smtp:Password"] = "password";
        settings["Smtp:AllowUnencryptedInDevelopment"] = "true";
        using var provider = CreateProvider(settings, Environments.Development);

        var startupValidator = provider.GetRequiredService<IStartupValidator>();
        var exception = Record.Exception(startupValidator.Validate);

        if (valid)
        {
            Assert.Null(exception);
        }
        else
        {
            Assert.IsType<OptionsValidationException>(exception);
        }
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "AnalyticDashboard.IntegrationTests";

        public string ContentRootPath { get; set; } = "";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
