using System;
using Microsoft.Extensions.Hosting;

namespace WorldBuilder.Server {
    public class Program {
        public static void Main(string[] args) {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorPages();
            builder.Services.AddSignalR();
            builder.WebHost.ConfigureKestrel(options => {
                /*
                try {
                    options.Listen(System.Net.IPAddress.Parse(host), port);
                }
                catch (Exception ex) {
                    Console.WriteLine(ex.Message);
                }
                */
            });

            var app = builder.Build();
            
            if (!app.Environment.IsDevelopment()) {
                app.UseExceptionHandler("/Error");
            }

            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthorization();

            app.MapRazorPages();
            app.MapHub<ChatHub>("/chat");

            app.Run();
        }
    }
}
