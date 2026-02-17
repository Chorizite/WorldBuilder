using System.Net;
using WorldBuilder.Server.Hubs;
using WorldBuilder.Server.Services;

namespace WorldBuilder.Server {
    /// <summary>
    /// The main program class for the server application.
    /// </summary>
    public class Program {
        /// <summary>
        /// The main entry point for the server application.
        /// </summary>
        /// <param name="args">The command line arguments</param>
        public static void Main(string[] args) {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSignalR();
            builder.Services.AddSingleton<IWorldEventStore, InMemoryWorldEventStore>();

            // Only configure Kestrel if we're not in a test environment
            if (builder.Environment.EnvironmentName != "Testing") {
                builder.WebHost.ConfigureKestrel(options => {
                    options.Listen(IPAddress.Loopback, 0, listenOptions => listenOptions.UseHttps());
                });
            }

            var app = builder.Build();
            app.MapHub<WorldHub>("/world");
            app.Run();
        }
    }
}