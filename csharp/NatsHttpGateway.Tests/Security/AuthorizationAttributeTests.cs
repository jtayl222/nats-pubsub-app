using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using NatsHttpGateway.Controllers;
using NUnit.Framework;

namespace NatsHttpGateway.Tests.Security;

/// <summary>
/// Unit tests to verify that controllers have correct authorization attributes.
/// These tests ensure security configuration is not accidentally removed.
/// </summary>
[TestFixture]
[Category("Security")]
public class AuthorizationAttributeTests
{
    [Test]
    public void HealthController_HasAllowAnonymousAttribute()
    {
        // Arrange
        var controllerType = typeof(HealthController);

        // Act
        var attribute = controllerType.GetCustomAttribute<AllowAnonymousAttribute>();

        // Assert
        Assert.That(attribute, Is.Not.Null,
            "HealthController should have [AllowAnonymous] attribute to allow unauthenticated health checks");
    }

    [Test]
    public void MessagesController_HasAuthorizeAttribute()
    {
        // Arrange
        var controllerType = typeof(MessagesController);

        // Act
        var attribute = controllerType.GetCustomAttribute<AuthorizeAttribute>();

        // Assert
        Assert.That(attribute, Is.Not.Null,
            "MessagesController should have [Authorize] attribute to protect message endpoints");
    }

    [Test]
    public void StreamsController_HasAuthorizeAttribute()
    {
        // Arrange
        var controllerType = typeof(StreamsController);

        // Act
        var attribute = controllerType.GetCustomAttribute<AuthorizeAttribute>();

        // Assert
        Assert.That(attribute, Is.Not.Null,
            "StreamsController should have [Authorize] attribute to protect stream endpoints");
    }

    [Test]
    public void ConsumersController_HasAuthorizeAttribute()
    {
        // Arrange
        var controllerType = typeof(ConsumersController);

        // Act
        var attribute = controllerType.GetCustomAttribute<AuthorizeAttribute>();

        // Assert
        Assert.That(attribute, Is.Not.Null,
            "ConsumersController should have [Authorize] attribute to protect consumer endpoints");
    }

    [Test]
    public void WebSocketMessagesController_HasAuthorizeAttribute()
    {
        // Arrange
        var controllerType = typeof(WebSocketMessagesController);

        // Act
        var attribute = controllerType.GetCustomAttribute<AuthorizeAttribute>();

        // Assert
        Assert.That(attribute, Is.Not.Null,
            "WebSocketMessagesController should have [Authorize] attribute to protect WebSocket endpoints");
    }

    [Test]
    public void ProtobufMessagesController_HasAuthorizeAttribute()
    {
        // Arrange
        var controllerType = typeof(ProtobufMessagesController);

        // Act
        var attribute = controllerType.GetCustomAttribute<AuthorizeAttribute>();

        // Assert
        Assert.That(attribute, Is.Not.Null,
            "ProtobufMessagesController should have [Authorize] attribute to protect protobuf endpoints");
    }

    [Test]
    public void AllProtectedControllers_HaveAuthorizeAttribute()
    {
        // Arrange
        var protectedControllerTypes = new[]
        {
            typeof(MessagesController),
            typeof(StreamsController),
            typeof(ConsumersController),
            typeof(WebSocketMessagesController),
            typeof(ProtobufMessagesController)
        };

        // Act & Assert
        foreach (var controllerType in protectedControllerTypes)
        {
            var attribute = controllerType.GetCustomAttribute<AuthorizeAttribute>();
            Assert.That(attribute, Is.Not.Null,
                $"{controllerType.Name} should have [Authorize] attribute");
        }
    }

    [Test]
    public void OnlyHealthController_AllowsAnonymousAccess()
    {
        // Arrange
        var allControllerTypes = new[]
        {
            typeof(HealthController),
            typeof(MessagesController),
            typeof(StreamsController),
            typeof(ConsumersController),
            typeof(WebSocketMessagesController),
            typeof(ProtobufMessagesController)
        };

        // Act & Assert
        foreach (var controllerType in allControllerTypes)
        {
            var allowAnonymous = controllerType.GetCustomAttribute<AllowAnonymousAttribute>();

            if (controllerType == typeof(HealthController))
            {
                Assert.That(allowAnonymous, Is.Not.Null,
                    "HealthController should allow anonymous access");
            }
            else
            {
                Assert.That(allowAnonymous, Is.Null,
                    $"{controllerType.Name} should NOT allow anonymous access");
            }
        }
    }
}
