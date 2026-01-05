using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NatsHttpGateway.Configuration;
using NUnit.Framework;

namespace NatsHttpGateway.Tests.Services;

/// <summary>
/// Unit tests for NatsService TLS/mTLS configuration.
/// Note: These tests verify configuration validation, not actual TLS connections.
/// </summary>
[TestFixture]
[Category("Security")]
public class NatsServiceTlsTests
{
    private Mock<ILogger<NatsHttpGateway.Services.NatsService>> _mockLogger = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<NatsHttpGateway.Services.NatsService>>();
    }

    private static IOptions<NatsOptions> CreateOptions(NatsOptions options)
    {
        return Options.Create(options);
    }

    [Test]
    public void Constructor_WithMissingCaFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var options = CreateOptions(new NatsOptions
        {
            Url = "nats://localhost:4222",
            CaFile = "/nonexistent/path/to/ca.pem"
        });

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new NatsHttpGateway.Services.NatsService(options, _mockLogger.Object));

        Assert.That(ex!.InnerException, Is.TypeOf<FileNotFoundException>());
        Assert.That(ex.InnerException!.Message, Does.Contain("CA certificate file not found"));
    }

    [Test]
    public void Constructor_WithMissingCertFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var tempCaFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempCaFile, "dummy ca content");

            var options = CreateOptions(new NatsOptions
            {
                Url = "nats://localhost:4222",
                CaFile = tempCaFile,
                CertFile = "/nonexistent/path/to/client.crt",
                KeyFile = "/nonexistent/path/to/client.key"
            });

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() =>
                new NatsHttpGateway.Services.NatsService(options, _mockLogger.Object));

            Assert.That(ex!.InnerException, Is.TypeOf<FileNotFoundException>());
            Assert.That(ex.InnerException!.Message, Does.Contain("Client certificate file not found"));
        }
        finally
        {
            File.Delete(tempCaFile);
        }
    }

    [Test]
    public void Constructor_WithMissingKeyFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var tempCaFile = Path.GetTempFileName();
        var tempCertFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempCaFile, "dummy ca content");
            File.WriteAllText(tempCertFile, "dummy cert content");

            var options = CreateOptions(new NatsOptions
            {
                Url = "nats://localhost:4222",
                CaFile = tempCaFile,
                CertFile = tempCertFile,
                KeyFile = "/nonexistent/path/to/client.key"
            });

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() =>
                new NatsHttpGateway.Services.NatsService(options, _mockLogger.Object));

            Assert.That(ex!.InnerException, Is.TypeOf<FileNotFoundException>());
            Assert.That(ex.InnerException!.Message, Does.Contain("Client key file not found"));
        }
        finally
        {
            File.Delete(tempCaFile);
            File.Delete(tempCertFile);
        }
    }

    [Test]
    public void Constructor_WithoutTlsConfig_DoesNotRequireCertFiles()
    {
        // Arrange - No TLS configuration, just NATS_URL
        // This test verifies that TLS is optional
        var options = CreateOptions(new NatsOptions
        {
            Url = "nats://localhost:4222"
            // No CaFile, CertFile, or KeyFile
        });

        // Act & Assert
        // This will fail to connect (no NATS server), but should NOT throw FileNotFoundException
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new NatsHttpGateway.Services.NatsService(options, _mockLogger.Object));

        // Should fail due to connection, not file not found
        Assert.That(ex!.InnerException, Is.Not.TypeOf<FileNotFoundException>(),
            "Without TLS config, NatsService should not require certificate files");
    }

    [Test]
    public void Constructor_ReadsNatsUrlFromOptions()
    {
        // Arrange
        var expectedUrl = "nats://custom-host:4223";
        var options = CreateOptions(new NatsOptions
        {
            Url = expectedUrl
        });

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new NatsHttpGateway.Services.NatsService(options, _mockLogger.Object));

        // The error message should contain the custom URL
        Assert.That(ex!.Message, Does.Contain(expectedUrl));
    }

    [Test]
    public void Constructor_UsesDefaultNatsUrl_WhenNotConfigured()
    {
        // Arrange - Use default NatsOptions which has default URL
        var options = CreateOptions(new NatsOptions());

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new NatsHttpGateway.Services.NatsService(options, _mockLogger.Object));

        // Should use default URL
        Assert.That(ex!.Message, Does.Contain("nats://localhost:4222"));
    }

    [Test]
    public void NatsOptions_IsTlsEnabled_ReturnsTrueWhenCaFileSet()
    {
        var options = new NatsOptions { CaFile = "/path/to/ca.pem" };
        Assert.That(options.IsTlsEnabled, Is.True);
    }

    [Test]
    public void NatsOptions_IsTlsEnabled_ReturnsFalseWhenCaFileNotSet()
    {
        var options = new NatsOptions();
        Assert.That(options.IsTlsEnabled, Is.False);
    }

    [Test]
    public void NatsOptions_IsMtlsEnabled_ReturnsTrueWhenCertAndKeySet()
    {
        var options = new NatsOptions
        {
            CertFile = "/path/to/cert.pem",
            KeyFile = "/path/to/key.pem"
        };
        Assert.That(options.IsMtlsEnabled, Is.True);
    }

    [Test]
    public void NatsOptions_IsMtlsEnabled_ReturnsFalseWhenOnlyCertSet()
    {
        var options = new NatsOptions { CertFile = "/path/to/cert.pem" };
        Assert.That(options.IsMtlsEnabled, Is.False);
    }

    [Test]
    public void NatsOptions_IsMtlsEnabled_ReturnsFalseWhenOnlyKeySet()
    {
        var options = new NatsOptions { KeyFile = "/path/to/key.pem" };
        Assert.That(options.IsMtlsEnabled, Is.False);
    }
}
