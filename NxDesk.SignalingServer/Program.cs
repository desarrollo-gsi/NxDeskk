using NxDesk.SignalingServer.Hubs; // Importa tu Hub

var builder = WebApplication.CreateBuilder(args);

// --- 1. Configuración de Servicios ---

// Agregar servicios de SignalR al contenedor de dependencias.
builder.Services.AddSignalR();

// Agregar CORS (Cross-Origin Resource Sharing)
// ESTO ES CRÍTICO. Sin esto, tu Cliente WPF y tu Host C++
// no podrán conectarse al servidor (serán bloqueados por seguridad).
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllOrigins",
        policy =>
        {
            policy.AllowAnyHeader()
                  .AllowAnyMethod()
                  // Permite que cualquier origen se conecte.
                  // Para producción, deberías restringir esto a tus dominios conocidos.
                  .SetIsOriginAllowed(origin => true)
                  .AllowCredentials();
        });
});

// Agregar servicios para controladores de API (si también sirves una API REST)
builder.Services.AddControllers();
// (Opcional) Agregar Swagger/OpenAPI para probar
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


// --- 2. Construcción de la App ---
var app = builder.Build();

// --- 3. Configuración del Pipeline HTTP ---

// (Opcional) Configurar Swagger en modo de desarrollo
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// ¡Importante! Habilita la política de CORS que definiste.
app.UseCors("AllowAllOrigins");

app.UseAuthorization();

// Mapea los controladores de API
app.MapControllers();

// Mapea tu Hub de SignalR a una ruta URL.
// Los clientes se conectarán a: "http://[tu_servidor]/signalinghub"
app.MapHub<SignalingHub>("/signalinghub");

// Inicia el servidor
app.Run();