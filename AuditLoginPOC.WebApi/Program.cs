using System;
using Microsoft.Owin.Hosting;

namespace AuditLoginPOC.WebApi
{
    public class Program
    {
        static void Main(string[] args)
        {
            var baseAddress = "http://localhost:5000";

            using (WebApp.Start<Startup>(url: baseAddress))
            {
                Console.WriteLine($"AuditLoginPOC Web API is running at {baseAddress}");
                Console.WriteLine("Press any key to exit.");
                Console.ReadKey();
            }
        }
    }
}
