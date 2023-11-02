using Invoicesrpc;
using LndGrpc;
using LnProxyApi.Models;
using Lnrpc;
using Microsoft.AspNetCore.Mvc;

namespace LnProxyApi.Controllers;

[ApiController]
[Route("[controller]")]
public class LnProxyController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<LnProxyController> _logger;

    public LnProxyController(IConfiguration configuration, ILogger<LnProxyController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("spec")]
    public IActionResult Post([FromBody] LnProxyModel request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var lightningService = new LightningService(_configuration, _logger);
            _logger.LogInformation($"Creating proxy request invoice {request.Invoice}: {@request}");

            AddHoldInvoiceResp response = lightningService.CreateHodlInvoice(
                request.Invoice,
                request.Description,
                request.DescriptionHash,
                request.RoutingMsat
            );

            return Ok(new { ProxyInvoice = response.PaymentRequest });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating proxy request invoice.");

            if (ex.Message.Contains("StatusCode=", StringComparison.InvariantCultureIgnoreCase))
            {
                var detail = ex.Message.Split("Detail=")[1];
                return StatusCode(500, new { Status = "ERROR", Reason = detail.Replace("\\", "").Replace(")", "").Replace("\"", "").Trim() });
            }
            return StatusCode(500, new { Status = "ERROR", Reason = ex.Message });
        }
    }

}
