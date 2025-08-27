using Microsoft.Owin;
using Owin;
using System.Web.Http;

[assembly: OwinStartup(typeof(AuditLoginPOC.WebApi.Startup))]

namespace AuditLoginPOC.WebApi
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            var config = new HttpConfiguration();
            
            // Configure Web API
            WebApiConfig.Register(config);
            
            // Use Web API
            app.UseWebApi(config);
        }
    }
}
