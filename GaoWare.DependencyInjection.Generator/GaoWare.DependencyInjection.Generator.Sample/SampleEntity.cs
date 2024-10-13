using GaoWare.DependencyInjection.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace GaoWare.DependencyInjection.Generator.Sample;

// This code will not compile until you build the project with the Source Generators

public partial class TestClass
{

    [GenerateServiceRegistration]
    public virtual partial void TestGenerator(IServiceCollection? serviceCollection);
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

[RegisterService(ServiceLifetime.Scoped, interfaceType: typeof(ITestServiceWithoutAttribute))]
public class Service2 : ITestServiceWithoutAttribute
{
    
}

[RegisterService(ServiceLifetime.Transient)]
public class Service3 : ITestServiceWithoutAttribute
{
    
}