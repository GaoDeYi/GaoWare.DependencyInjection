using System;
using Microsoft.Extensions.DependencyInjection;

namespace GaoWare.DependencyInjection.Attributes
{


    /// <summary>
    /// Registers the class as a service for dependency injection
    /// Automatically go through the interfaces which are inherited and use them for registration
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public class RegisterServiceAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RegisterServiceAttribute"/> class
        /// that's used to register a class as a service 
        /// </summary>
        public RegisterServiceAttribute()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RegisterServiceAttribute"/> class
        /// that's used to register a class as a service 
        /// </summary>
        /// <param name="serviceLifetime">The lifetime of the service</param>
        public RegisterServiceAttribute(ServiceLifetime serviceLifetime)
        {
            ServiceLifetime = serviceLifetime;
        }

        /// <summary>
        /// Gets or sets the lifetime of the service.
        /// </summary>
        public ServiceLifetime ServiceLifetime { get; set; } = ServiceLifetime.Scoped;

        /// <summary>
        /// Gets or sets the interface which should be registered with this service
        /// </summary>
        /// <remarks>
        /// The interface defined here, does not have to use the <see cref="RegisterInterfaceAttribute"/>.
        /// If not set, then the generator will go through all the inherited interfaces
        /// </remarks>
        public Type? InterfaceType { get; set; }

        /// <summary>
        /// Gets or sets the name the service should register with.
        /// </summary>
        /// <remarks>
        /// If it is not set, no keyed name will be set
        /// </remarks>
        public string? ServiceKey { get; set; }

    }
}