using Google.Protobuf;
using Grpc.Core;
using Invoicesrpc;
using Lnrpc;
using Routerrpc;

namespace LndGrpc
{
    public class LightningService
    {
        private readonly Dictionary<Invoice.Types.InvoiceState, string> invoiceState = 
			new Dictionary<Invoice.Types.InvoiceState, string>
        {
            { Invoice.Types.InvoiceState.Canceled, "CANCELED" },
            { Invoice.Types.InvoiceState.Settled, "SETTLED" },
            { Invoice.Types.InvoiceState.Open, "OPEN" }
        };

        private LnGrpcClientService lnGrpcService;
        private readonly ILogger _logger;

        public LightningService(IConfiguration configuration, ILogger logger)
        {
            _logger = logger;
            lnGrpcService = new LnGrpcClientService(configuration);
        }

        private ByteString HexStringToByteString(string hexString)
        {
            int length = hexString.Length;
            byte[] bytes = new byte[length / 2];

            for (int i = 0; i < length; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hexString.Substring(i, 2), 16);
            }

            return ByteString.CopyFrom(bytes);
        }

        public PayReq DecodePayRequest(string invoice)
        {
            try
            {
                var client = lnGrpcService.GetLightningClient();
                var payReq = new PayReqString
                {
                    PayReq = invoice,
                };

                var invoiceResponse = client.DecodePayReq(payReq);
                return invoiceResponse;
            }
            catch (Exception ex)
            {
                throw new Exception("Error decoding invoice", ex);
            }
        }

        public async Task SettleAndPayInvoice(PayReq originalInvoice, string request)
        {
            var routerClient = lnGrpcService.GetRouterClient();
            var req = new SendPaymentRequest()
            {
                PaymentRequest = request,
                TimeoutSeconds = 600
            };

            var call = routerClient.SendPaymentV2(req);
            await foreach (var payment in call.ResponseStream.ReadAllAsync())
            {
                try
                {	var invoiceClient = lnGrpcService.GetInvoiceClient();
                    if (payment.Status == Payment.Types.PaymentStatus.Failed ||
                         payment.Status == Payment.Types.PaymentStatus.Unknown)
                    {  
						var cancel = new CancelInvoiceMsg()
						{
							PaymentHash = HexStringToByteString(originalInvoice.PaymentHash)
						};
						var canceledRes = invoiceClient.CancelInvoice(cancel);
                        _logger.LogWarning("Canceled invoice", canceledRes.ToString());
                    }

                    if (payment.Status == Payment.Types.PaymentStatus.Succeeded)
                    {
                        
                        var s = new SettleInvoiceMsg
                        {
                            Preimage = HexStringToByteString(payment.PaymentPreimage)
                        };
                        var settled = invoiceClient.SettleInvoice(s);
						_logger.LogInformation("Settled Invoice", settled.ToString());
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError("An exception occurred settling the invoice:", ex.Message);
                }
            }
        }

        public async Task SubscribeToHodlInvoice(ByteString rHash, PayReq originalInvoice, string originalRequest)
        {
            var invoiceClient = lnGrpcService.GetInvoiceClient();

            var sub = new SubscribeSingleInvoiceRequest() { RHash = rHash };
            var call = invoiceClient.SubscribeSingleInvoice(sub);
            await foreach (var invoice in call.ResponseStream.ReadAllAsync())
            {
                try
                {
                    if (invoice.State == Invoice.Types.InvoiceState.Accepted)
                    {
                        _logger.LogInformation($"Invoice was ACCEPTED", invoice);
                        await SettleAndPayInvoice(originalInvoice, originalRequest);
                    }
                    else if (invoiceState.TryGetValue(invoice.State, out string? stateString))
                    {
                        _logger.LogInformation($"Invoice was {stateString}", invoice);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError("An exception occurred settling invoice", ex.Message);
                    throw ex;
                }

            }
        }

        public AddHoldInvoiceResp CreateHodlInvoice(string payRequestString, string? payReqDescription, string? payReqHash)
        {
            try
            {
                var payReqFromInvoice = DecodePayRequest(payRequestString);
				if(payReqFromInvoice.Features.ContainsKey(30)){
                    throw new Exception("Cannot wrap AMP invoice");
                }

                var invoiceClient = lnGrpcService.GetInvoiceClient();
                var hodlInvoice = new AddHoldInvoiceRequest()
                {
                    Memo = !string.IsNullOrEmpty(payReqDescription) ?
                        payReqDescription : payReqFromInvoice.Description,
                    DescriptionHash = !string.IsNullOrEmpty(payReqHash) ?
                        HexStringToByteString(payReqHash) : HexStringToByteString(payReqFromInvoice.DescriptionHash),
                    Hash = HexStringToByteString(payReqFromInvoice.PaymentHash),
                    ValueMsat = payReqFromInvoice.NumMsat,
                    CltvExpiry = (ulong)payReqFromInvoice.CltvExpiry + 10,
                };

                var invoiceResponse = invoiceClient.AddHoldInvoice(hodlInvoice);
                _ = SubscribeToHodlInvoice(hodlInvoice.Hash, payReqFromInvoice, payRequestString);
                return invoiceResponse;
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}