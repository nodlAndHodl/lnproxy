namespace LnProxyApi.Errors;
public class ProxyError
{
    public string Status { get; set; }
    public string Reason { get; set; }

    public ProxyError(string status, string reason)
    {
        Status = status;
        Reason = reason;
    }
}
