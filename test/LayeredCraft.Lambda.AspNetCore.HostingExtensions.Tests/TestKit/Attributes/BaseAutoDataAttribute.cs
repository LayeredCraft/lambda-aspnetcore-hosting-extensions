using AutoFixture;
using AutoFixture.Xunit3;

namespace LayeredCraft.Lambda.AspNetCore.HostingExtensions.Tests.TestKit.Attributes;

public sealed class BaseAutoDataAttribute() : AutoDataAttribute(CreateFixture)
{
    internal static IFixture CreateFixture()
    {
        return BaseFixtureFactory.CreateFixture(fixture =>
        {
            // Add any common customizations for all tests here if needed in the future
        });
    }
}

public sealed class InlineBaseAutoDataAttribute(params object[] values)
    : InlineAutoDataAttribute(BaseAutoDataAttribute.CreateFixture, values)
{
}