using System;

namespace GaoWare.DependencyInjection.Attributes
{

    /// <summary>
    /// Made this interface available to be used by the dependency injection code generator
    /// If this interface will be inherited by a registered service, it will automatically be use
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
    public class RegisterInterfaceAttribute : Attribute
    {

    }
}