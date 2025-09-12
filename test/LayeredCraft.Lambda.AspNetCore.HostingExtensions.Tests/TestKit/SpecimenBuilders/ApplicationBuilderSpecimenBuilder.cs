using AutoFixture.Kernel;
using LayeredCraft.Lambda.AspNetCore.HostingExtensions.Tests.TestKit.RequestSpecifications;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LayeredCraft.Lambda.AspNetCore.HostingExtensions.Tests.TestKit.SpecimenBuilders;

public class ApplicationBuilderSpecimenBuilder(IRequestSpecification requestSpecification) : ISpecimenBuilder
{
    public ApplicationBuilderSpecimenBuilder() : this(new ApplicationBuilderSpecification())
    {
    }

    public object Create(object request, ISpecimenContext context)
    {
        if (!requestSpecification.IsSatisfiedBy(request))
        {
            return new NoSpecimen();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();

        var app = Substitute.For<IApplicationBuilder>();
        app.ApplicationServices.Returns(serviceProvider);
        
        return app;
    }
}