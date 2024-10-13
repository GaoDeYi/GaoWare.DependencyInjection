using Microsoft.Extensions.DependencyInjection;

namespace GaoWare.DependencyInjection.Attributes;

/// <summary>
/// Creates a method to register all the services found in this project
/// </summary>
/// <remarks>
/// <para>The method this attribute is applied to:</para>
/// <para>   - Must be a partial method.</para>
/// <para>   - Must return <c>void</c>.</para>
/// <para>   - Must not be generic.</para>
/// <para>   - Must have an <see cref="IServiceCollection"/> as one of its parameters.</para>
/// <para>   - The method can be written as extension method for the <see cref="IServiceCollection"/> porameter</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public class DependencyRegistrationAttribute : Attribute
{
    
}