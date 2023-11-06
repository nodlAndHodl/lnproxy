using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Invoicesrpc;
using Lnrpc;
using Routerrpc;
using System.Security.Cryptography.X509Certificates;

namespace LnProxyApi.LndGrpc.Services;

public class LnGrpcClientService
{
    private readonly string pathToMacaroon;
    private readonly string pathToSslCertificate;
    private readonly string GRPCHost;

    public LnGrpcClientService(IConfiguration configuration)
    {
        pathToMacaroon = configuration["AppSettings:PathToMacaroon"]!;
        pathToSslCertificate = configuration["AppSettings:PathToSslCertificate"]!;
        GRPCHost = configuration["AppSettings:GRPCHost"]!;
    }

    private GrpcChannel GetGrpcChannel()
    {
        var rawCert = File.ReadAllBytes(pathToSslCertificate);
        Environment.SetEnvironmentVariable("GRPC_SSL_CIPHER_SUITES", "HIGH+ECDSA");
        var x509Cert = new X509Certificate2(rawCert);

        var httpClientHandler = new HttpClientHandler
        {   // HttpClientHandler will validate certificate chain trust by default. This won't work for a self-signed cert.
            // Therefore validate the certificate directly
            ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors)
                => x509Cert.Equals(cert)
        };

        var credentials = ChannelCredentials.Create(new SslCredentials(), CallCredentials.FromInterceptor(AddMacaroon));

        var channel = GrpcChannel.ForAddress(
            $"https://{GRPCHost}",
            new GrpcChannelOptions
            {
                HttpHandler = httpClientHandler,
                Credentials = credentials
            });

        return channel;
    }

    Task AddMacaroon(AuthInterceptorContext context, Metadata metadata)
    {
        metadata.Add(new Metadata.Entry("macaroon", GetMacaroon()));
        return Task.CompletedTask;
    }

    public Lightning.LightningClient GetLightningClient()
    {
        var channel = GetGrpcChannel();
        var client = new Lightning.LightningClient(channel);
        return client;
    }

    public Router.RouterClient GetRouterClient()
    {
        var channel = GetGrpcChannel();
        var client = new Router.RouterClient(channel);
        return client;
    }

    public Invoices.InvoicesClient GetInvoiceClient()
    {
        var channel = GetGrpcChannel();
        var client = new Invoices.InvoicesClient(channel);
        return client;
    }

    private string GetMacaroon()
    {
        byte[] macaroonBytes = File.ReadAllBytes(pathToMacaroon);
        var macaroon = BitConverter.ToString(macaroonBytes).Replace("-", "");
        return macaroon;
    }
}
