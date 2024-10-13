using GaoWare.DependencyInjection.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace GaoWare.DependencyInjection.Generator.Sample;

// This code will not compile until you build the project with the Source Generators

public static partial class TestClass
{

    [DependencyRegistration]
    public static partial void TestGenerator(IServiceCollection? serviceCollection);

    [DependencyRegistration]
    public static partial void TestGen(this IServiceCollection? serviceCollection);

}


[RegisterInterface]
public interface ITestService
{
    
}

public interface ITestServiceWithoutAttribute
{
    
}

[RegisterService(ServiceLifetime.Scoped)]
public class Service1 : ITestService
{
    
}

[RegisterService(ServiceLifetime.Scoped, InterfaceType = typeof(ITestServiceWithoutAttribute))]
public class Service2 : ITestServiceWithoutAttribute
{
    
}

[RegisterService(ServiceLifetime.Transient, ServiceKey = "TestKey")]
public class Service3 : ITestServiceWithoutAttribute
{
    
}