using System.Reflection;
using AutoFixture.Kernel;
using Microsoft.AspNetCore.Builder;

namespace LayeredCraft.Lambda.AspNetCore.HostingExtensions.Tests.TestKit.RequestSpecifications;

public class ApplicationBuilderSpecification : IRequestSpecification
{
    public bool IsSatisfiedBy(object request)
    {
        return request is ParameterInfo parameterInfo && parameterInfo.ParameterType == typeof(IApplicationBuilder) ||
               request is Type type && type == typeof(IApplicationBuilder);
    }
}