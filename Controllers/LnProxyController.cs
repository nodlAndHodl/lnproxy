using Microsoft.AspNetCore.Mvc;

namespace LnProxyApi.Controllers;

[ApiController]
[Route("[controller]")]
public class LnProxyController : ControllerBase
{
    private static readonly string[] Summaries = new[]
    {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    };

    private readonly ILogger<LnProxyController> _logger;

    public LnProxyController(ILogger<LnProxyController> logger)
    {
        _logger = logger;
    }

    [HttpGet(Name = "GetHodlInvoice")]
    public IEnumerable<LnInvoice> Get()
    {
        return Enumerable.Range(1, 5).Select(index => new LnInvoice
        {
            Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            TemperatureC = Random.Shared.Next(-20, 55),
            Summary = Summaries[Random.Shared.Next(Summaries.Length)]
        })
        .ToArray();
    }
}
