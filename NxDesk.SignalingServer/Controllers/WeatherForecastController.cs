using Microsoft.AspNetCore.Mvc;

namespace NxDesk.SignalingServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private static readonly string[] Summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

        private readonly ILogger<WeatherForecastController> _logger;

        public WeatherForecastController(ILogger<WeatherForecastController> logger)
        {
            _logger = logger;
        }

        [HttpGet(Name = "GetWeatherForecast")]
        public IEnumerable<WeatherForecast> Get()
        {
            return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = Summaries[Random.Shared.Next(Summaries.Length)]
            })
            .ToArray();
        }
    }


    /// <summary>
    /// Este es un Controlador de API (HTTP) simple.
    /// Swagger SÍ puede descubrir y probar esto.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")] // Ruta: /api/HealthCheck
    public class HealthCheckController : ControllerBase
    {
        /// <summary>
        /// Un endpoint simple para verificar que el servidor está en línea.
        /// </summary>
        [HttpGet]
        public IActionResult GetStatus()
        {
            return Ok(new { Status = "NxDesk.SignalingServer is running!" });
        }
    }
}
