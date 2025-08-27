using System;
using System.Collections.Generic;
using System.Web.Http.Dependencies;

namespace AuditLoginPOC.WebApi.Infrastructure
{
    /// <summary>
    /// Simple dependency resolver for Web API
    /// In production, use a proper DI container like Unity, Autofac, etc.
    /// </summary>
    public class SimpleDependencyResolver : IDependencyResolver
    {
        private readonly Dictionary<Type, Type> _registrations = new Dictionary<Type, Type>();
        private readonly Dictionary<Type, object> _singletons = new Dictionary<Type, object>();

        public void Register<TInterface, TImplementation>() where TImplementation : TInterface
        {
            _registrations[typeof(TInterface)] = typeof(TImplementation);
        }

        public void RegisterSingleton<TInterface, TImplementation>() where TImplementation : TInterface
        {
            var implementation = Activator.CreateInstance<TImplementation>();
            _singletons[typeof(TInterface)] = implementation;
        }

        public object GetService(Type serviceType)
        {
            // Check if we have a singleton instance
            if (_singletons.TryGetValue(serviceType, out var singleton))
            {
                return singleton;
            }

            // Check if we have a registration
            if (_registrations.TryGetValue(serviceType, out var implementationType))
            {
                return Activator.CreateInstance(implementationType);
            }

            return null;
        }

        public IEnumerable<object> GetServices(Type serviceType)
        {
            var service = GetService(serviceType);
            return service != null ? new[] { service } : new object[0];
        }

        public IDependencyScope BeginScope()
        {
            return this; // Simple implementation - no scoping
        }

        public void Dispose()
        {
            // Nothing to dispose in this simple implementation
        }
    }
}
