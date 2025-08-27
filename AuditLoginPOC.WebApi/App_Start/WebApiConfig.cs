using System.Web.Http;
using AuditLoginPOC.Core.Handlers;
using AuditLoginPOC.Core.Interfaces;
using AuditLoginPOC.Core.Services;
using AuditLoginPOC.WebApi.Services;
using System.Web.Http.Cors;
using FluentValidation.WebApi;
using FluentValidation;
using AuditLoginPOC.WebApi.Models;
using AuditLoginPOC.WebApi.Validators;
using System.Web.Http.Dependencies;
using AuditLoginPOC.WebApi.Infrastructure;

namespace AuditLoginPOC.WebApi
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // Web API configuration and services
            var cors = new EnableCorsAttribute("*", "*", "*");
            config.EnableCors(cors);

            // Configure Dependency Injection
            ConfigureDependencyInjection(config);

            // Configure FluentValidation
            FluentValidationModelValidatorProvider.Configure(config);

            // Web API routes
            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );

            // Configure JSON serialization
            config.Formatters.JsonFormatter.SerializerSettings.Formatting = Newtonsoft.Json.Formatting.Indented;
            config.Formatters.JsonFormatter.SerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore;

            // Register audit logging services
            RegisterAuditServices(config);
        }

        private static void ConfigureDependencyInjection(HttpConfiguration config)
        {
            // Create a simple DI container (in production, use a proper DI container like Unity, Autofac, etc.)
            var container = new SimpleDependencyResolver();
            
            // Register validators
            container.Register<IValidator<Person>, PersonValidator>();
            
            // Register services
            container.Register<IRequestCapturingService, ContentReplacementCapturingService>();
            container.Register<IDoSProtectionService, SizeLimitProtectionService>();
            container.Register<IAuditLoggingService, ConsoleAuditLoggingService>();
            
            config.DependencyResolver = container;
        }

        private static void RegisterAuditServices(HttpConfiguration config)
        {
            // Get services from DI container
            var capturingService = config.DependencyResolver.GetService(typeof(IRequestCapturingService)) as IRequestCapturingService;
            var protectionService = config.DependencyResolver.GetService(typeof(IDoSProtectionService)) as IDoSProtectionService;
            var auditService = config.DependencyResolver.GetService(typeof(IAuditLoggingService)) as IAuditLoggingService;

            // Register the audit message handler
            var auditHandler = new ComposableAuditMessageHandler(
                capturingService,
                protectionService,
                auditService);

            // Insert the handler at the beginning of the pipeline
            config.MessageHandlers.Insert(0, auditHandler);
        }
    }
}
