using Invoicesrpc;
using LnProxyApi.LndGrpc.Services;
using LnProxyApi.Errors;
using LnProxyApi.Models;
using Microsoft.AspNetCore.Mvc;

namespace LnProxyApi.Controllers;

[ApiController]
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
    [ProducesResponseType(typeof(LnProxyResponse),StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProxyError), StatusCodes.Status500InternalServerError)]
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

            return Ok(new LnProxyResponse{ ProxyInvoice = response.PaymentRequest });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating proxy request invoice.");

            if (ex.Message.Contains("StatusCode=", StringComparison.InvariantCultureIgnoreCase))
            {
                //we're capturing the error emitted from lncli, which is a bit of a hack
                var detail = ex.Message.Split("Detail=")[1];
                return StatusCode(500, new ProxyError("ERROR", detail.Replace("\\", "").Replace(")", "").Replace("\"", "").Trim()));
            }
            return StatusCode(500, new ProxyError("ERROR", ex.Message));
        }
    }
}
