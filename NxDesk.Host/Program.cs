using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;

namespace NxDesk.Host
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // CORRECCIÓN: Usar Microsoft.Extensions.Hosting.Host (totalmente calificado)
            var host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    // Registramos nuestra clase de lógica como un "Singleton"
                    services.AddSingleton<WebRTCHostService>();
                })
                .Build();

            // Obtenemos el servicio de host y lo iniciamos manualmente
            using (var scope = host.Services.CreateScope())
            {
                var hostService = scope.ServiceProvider.GetRequiredService<WebRTCHostService>();
                await hostService.StartAsync();

                // Esperamos a que la aplicación se cierre (ej. Ctrl+C)
                await host.WaitForShutdownAsync();
            }
        }
    }
}