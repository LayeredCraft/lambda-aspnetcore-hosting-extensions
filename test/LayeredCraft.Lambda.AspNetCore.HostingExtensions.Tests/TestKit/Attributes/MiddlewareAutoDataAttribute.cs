using Amazon.Lambda.Core;
using AutoFixture;
using AutoFixture.Xunit3;
using LayeredCraft.Lambda.AspNetCore.HostingExtensions.Tests.TestKit.SpecimenBuilders;

namespace LayeredCraft.Lambda.AspNetCore.HostingExtensions.Tests.TestKit.Attributes;

public sealed class MiddlewareAutoDataAttribute() : AutoDataAttribute(CreateFixture)
{
    internal static IFixture CreateFixture()
    {
        return BaseFixtureFactory.CreateFixture(fixture =>
        {
            fixture.Freeze<ILambdaContext>();
            // Add Middleware-specific customizations
            fixture.Customizations.Add(new HttpContextSpecimenBuilder());
            fixture.Customizations.Add(new ApplicationBuilderSpecimenBuilder());
            fixture.Customizations.Add(new RequestDelegateSpecimenBuilder());
        });
    }
}

public sealed class InlineMiddlewareAutoDataAttribute(params object[] values)
    : InlineAutoDataAttribute(MiddlewareAutoDataAttribute.CreateFixture, values)
{
    // This class allows for inline data to be combined with the MiddlewareAutoDataAttribute customizations
}