using System.ComponentModel.DataAnnotations;
namespace LnProxyApi.Models;

public class LnProxyModel
{
    [Required]
    public required string Invoice { get; set; }
    public string? Description { get; set; }
    public string? DescriptionHash { get; set; }
    public string? RoutingMsat { get; set; }
}

public class LnProxyResponse
{
    public required string ProxyInvoice { get; set; }
}