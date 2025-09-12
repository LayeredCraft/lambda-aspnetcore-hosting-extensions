using LayeredCraft.Lambda.AspNetCore.Hosting.Extensions;
using LayeredCraft.Lambda.AspNetCore.HostingExtensions.Tests.TestKit.Attributes;
using Microsoft.AspNetCore.Builder;

namespace LayeredCraft.Lambda.AspNetCore.HostingExtensions.Tests.Extensions;

public class ApplicationBuilderExtensionsTests
{
    [Theory]
    [BaseAutoData]
    public void UseLambdaTimeoutLinkedCancellation_WithValidApplicationBuilder_ShouldNotThrow(IApplicationBuilder app)
    {
        // Act
        var action = () => app.UseLambdaTimeoutLinkedCancellation();
        
        // Assert
        action.Should().NotThrow("Extension method should register middleware without throwing");
    }
}