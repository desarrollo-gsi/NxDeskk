using System.Security.Principal;

namespace NxDesk.Host
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            bool esAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            Console.ForegroundColor = esAdmin ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine("==============================================");
            Console.WriteLine($"¿MODO ADMINISTRADOR?: {(esAdmin ? "SÍ (Correcto)" : "NO (Fallará en Task Manager)")}");
            Console.WriteLine("==============================================");
            Console.ResetColor();

            if (!esAdmin)
            {
                Console.WriteLine("ADVERTENCIA: Cierra y ejecuta como 'Administrador' para controlar todo.");
            }

            var host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddSingleton<WebRTCHostService>();
                })
                .Build();

            using (var scope = host.Services.CreateScope())
            {
                var hostService = scope.ServiceProvider.GetRequiredService<WebRTCHostService>();
                await hostService.StartAsync();
                await host.WaitForShutdownAsync();
            }
        }
    }
}