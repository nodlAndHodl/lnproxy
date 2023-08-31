//https://raw.githubusercontent.com/lightningnetwork/lnd/master/lnrpc/lightning.proto
//https://raw.githubusercontent.com/lightningnetwork/lnd/master/lnrpc/invoicesrpc/invoices.proto for finding hodlInvoice
using Grpc.Net.Client;
using System.Security.Cryptography.X509Certificates;

namespace LNDNodeClient.LightningHelpers
{
    public class LightningHelpers
    {
        private readonly IConfiguration _configuration;
        private readonly string pathToMacaroon;
        private readonly string pathToSslCertificate;
        private readonly string GRPCHost;
        public LightningHelpers(IConfiguration configuration)
        {
            _configuration = configuration;

            pathToMacaroon = _configuration["AppSettings:PathToMacaroon"]!;
            pathToSslCertificate = _configuration["AppSettings:PathToSslCertificate"]!;
            GRPCHost = _configuration["AppSettings:GRPCHost"]!;
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

            var channel = GrpcChannel.ForAddress(
                $"https://{GRPCHost}",
                new GrpcChannelOptions
                {
                    HttpHandler = httpClientHandler,
                });

            return channel;
        }

        public Lnrpc.Lightning.LightningClient GetClient()
        {
            var channel = GetGrpcChannel();
            var client = new Lnrpc.Lightning.LightningClient(channel);
            return client;
        }

        public Invoicesrpc.Invoices.InvoicesClient GetInvoiceClient()
        {
            var channel = GetGrpcChannel();
            var client = new Invoicesrpc.Invoices.InvoicesClient(channel);
            return client;
        }

        public string GetMacaroon()
        {
            byte[] macaroonBytes = File.ReadAllBytes(pathToMacaroon);
            var macaroon = BitConverter.ToString(macaroonBytes).Replace("-", "");

            return macaroon;
        }
    }
}