using Google.Protobuf;
using Grpc.Core;
using Invoicesrpc;
using Lnrpc;
using Routerrpc;
using LnProxyApi.Helpers;

namespace LnProxyApi.LndGrpc.Services;

public class LightningService
    {
        const long RoutingFeeBaseMsat = 1000; 
        const long RoutingFeePPM = 1000;
	    const long MinFeeBudgetMsat = 1000;
        const long ExpiryBuffer = 300;
        const long CltvDeltaAlpha = 42;
        const long CltvDeltaBeta = 42;
		const long MaxCltvExpiry = 1800;
		const long MinCltvExpiry =  200;
        const long RoutingBudgetAlpha = 1000;
        const long RoutingBudgetBeta = 1_500_000;

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


        private PayReq DecodePayRequest(string invoice)
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
                _logger.LogError(ex, "Error decoding invoice");
                throw;
            }
        }

        private async Task SettleAndPayInvoice(PayReq originalInvoice, string request, long feeLimitMsat)
        {
            var routerClient = lnGrpcService.GetRouterClient();
            var estimateFee = EstimateRouteFee(originalInvoice).Result;
            var req = new SendPaymentRequest()
            {
                PaymentRequest = request,
                FeeLimitMsat = feeLimitMsat,
                CltvLimit = (int)CalcCltvExpiry(estimateFee),
                TimeoutSeconds = 600
            };


            var call = routerClient.SendPaymentV2(req);
            await foreach (var payment in call.ResponseStream.ReadAllAsync())
            {
                try
                {	var invoiceClient = lnGrpcService.GetInvoiceClient();
                    if (payment.Status == Payment.Types.PaymentStatus.Failed)
                    {  
						var cancel = new CancelInvoiceMsg()
						{
							PaymentHash = HexStringHelper.HexStringToByteString(originalInvoice.PaymentHash)
						};
                        try
                        {
                            _logger.LogCritical($"Canceling invoice: {@originalInvoice}");
                            _logger.LogCritical($"Canceling payment: {@payment}");
                            var canceledRes = invoiceClient.CancelInvoice(cancel);
                            _logger.LogInformation($"Canceled : {@canceledRes}");

                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to cancel invoice: {@originalInvoice}");
                        }

                    }

                    if (payment.Status == Payment.Types.PaymentStatus.Succeeded)
                    {
                        var s = new SettleInvoiceMsg
                        {
                            Preimage = HexStringHelper.HexStringToByteString(payment.PaymentPreimage)
                        };
                        var settled = invoiceClient.SettleInvoice(s);
                        _logger.LogInformation($"Invoice From payment : {@payment} : {@settled}");
                        _logger.LogInformation($"Settled : {@settled}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An exception occurred settling the invoice.");
                }
            }
        }

        public async Task SubscribeToHodlInvoice(ByteString rHash, PayReq originalInvoice, string originalRequest, long feeLimitMsat)
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
                        _logger.LogInformation($"Invoice was ACCEPTED : {@invoice}");
                        await SettleAndPayInvoice(originalInvoice, originalRequest, feeLimitMsat);
                    }
                    else if (invoiceState.TryGetValue(invoice.State, out string? stateString))
                    {
                        _logger.LogInformation($"Invoice was {stateString}: {@invoice}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"An exception occurred settling invoice: {@invoice}");
                }
            }
        }

        private long CalculateExpiry(PayReq payReqFromInvoice)
        {
            long expiry = payReqFromInvoice.Expiry;

            if (expiry > ExpiryBuffer)
            {
                expiry = ExpiryBuffer;
            }

            long currentUnixTime = (long)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long adjustedExpiry = (long)(payReqFromInvoice.Timestamp + expiry - currentUnixTime - ExpiryBuffer);

            return adjustedExpiry;
        }


        private async Task<RouteFeeResponse> EstimateRouteFee(PayReq payReqFromInvoice)
        {
            try
            {
                var client = lnGrpcService.GetRouterClient();
                var route = new RouteFeeRequest()
                {   
                    AmtSat = payReqFromInvoice.NumSatoshis,
                    Dest =HexStringHelper.HexStringToByteString(payReqFromInvoice.Destination)
                };

                var estimateFee = await client.EstimateRouteFeeAsync(route);
                _logger.LogInformation($"Estimate of routing fees: {estimateFee}");
                if(estimateFee == null){
                    throw new Exception("Error getting route fee from payment request");
                }
                return estimateFee;
            }
            catch (Exception ex)
            {   
                _logger.LogError(ex, "Error getting route fee");
                throw;
            }
        }

        private int CalcCltvExpiry(RouteFeeResponse estimateFee){
            var cltvExpiry = estimateFee.TimeLockDelay + CltvDeltaAlpha + CltvDeltaBeta;
            _logger.LogInformation($"CLTV expiry from estimate of routing fees: {cltvExpiry}");
            if(cltvExpiry > MaxCltvExpiry){
                _logger.LogError($"CLTV expiry too high from estimate of routing fees: {cltvExpiry}");
                throw new Exception("CLTV expiry too high from estimate of routing fees");
            }

            if(cltvExpiry < MinCltvExpiry){
                cltvExpiry = MinCltvExpiry;
            }
            return (int)cltvExpiry;
        }

        private void ValidateInvoices(PayReq payReqFromInvoice, AddHoldInvoiceRequest hodlInvoice)
        {
            if (payReqFromInvoice.Timestamp + payReqFromInvoice.Expiry < DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ExpiryBuffer)
            {
                throw new Exception("payment request expiration is too close.");
            }
            if (!string.IsNullOrWhiteSpace(hodlInvoice.Memo) && !string.IsNullOrWhiteSpace(hodlInvoice.DescriptionHash.ToStringUtf8()))
            {
                throw new Exception("Cannot set both Description and DescriptionHash");
            }
            if (payReqFromInvoice.Features.ContainsKey(30))
            {
                throw new Exception("Cannot wrap AMP invoice");
            }
            if (payReqFromInvoice.NumMsat == 0)
            {
                throw new Exception("Invoice must have a value");
            }
        }
        public AddHoldInvoiceResp CreateHodlInvoice(string payRequestString, string? payReqDescription, string? payReqHash, string? payReqRoutingMsat)
        {
            try
            {
                var payReqFromInvoice = DecodePayRequest(payRequestString);
        
                var valueMsat = payReqFromInvoice.NumMsat;
                var routing_fee_msat = RoutingFeeBaseMsat + payReqFromInvoice.NumMsat * RoutingFeePPM / 1_000_000;
                var min_fee_budget_msat = EstimateRouteFee(payReqFromInvoice).Result;
                var cltvExpiry = CalcCltvExpiry(min_fee_budget_msat);
                var fee_budget_msat = min_fee_budget_msat.RoutingFeeMsat + RoutingBudgetAlpha + (min_fee_budget_msat.RoutingFeeMsat * RoutingBudgetBeta / 1_000_000);
                
                if(string.IsNullOrWhiteSpace(payReqRoutingMsat)){
                    valueMsat = payReqFromInvoice.NumMsat + fee_budget_msat + routing_fee_msat;
                }

                if(!string.IsNullOrWhiteSpace(payReqRoutingMsat)){
                    int.TryParse(payReqRoutingMsat, out int payReqRoutingMsatInt);
                    try{
                        _=checked(payReqRoutingMsatInt);
                    }
                    catch(OverflowException ex){
                        _logger.LogError(ex, "Overflow error on routing fee budget");
                        throw new Exception("Overflow error on routing fee budget");
                    }
                    if (payReqRoutingMsatInt < (MinFeeBudgetMsat + routing_fee_msat)){
                        _logger.LogWarning($"Routing fee budget too low ${payReqRoutingMsatInt}");
                        _logger.LogWarning($"Routing fee budget too low: ${payRequestString}");
                        throw new Exception("Routing fee budget too low");
                    }
                    valueMsat = payReqFromInvoice.NumMsat - routing_fee_msat + payReqRoutingMsatInt;    
                }

                var invoiceClient = lnGrpcService.GetInvoiceClient();
                var hodlInvoice = new AddHoldInvoiceRequest()
                {
                    Memo = !string.IsNullOrWhiteSpace(payReqDescription) ?
                        payReqDescription : payReqFromInvoice.Description,
                    DescriptionHash = !string.IsNullOrWhiteSpace(payReqHash) ?
                        HexStringHelper.HexStringToByteString(payReqHash) :
                        HexStringHelper.HexStringToByteString(payReqFromInvoice.DescriptionHash),
                    Hash = HexStringHelper.HexStringToByteString(payReqFromInvoice.PaymentHash),
                    ValueMsat = valueMsat,
                    CltvExpiry = (uint)cltvExpiry,
                    Expiry = CalculateExpiry(payReqFromInvoice)
                };

                ValidateInvoices(payReqFromInvoice, hodlInvoice);

                var invoiceResponse = invoiceClient.AddHoldInvoice(hodlInvoice);
                _logger.LogInformation($"Created hodl invoice: {@invoiceResponse}");
                _ = SubscribeToHodlInvoice(hodlInvoice.Hash, payReqFromInvoice, payRequestString, fee_budget_msat);
                return invoiceResponse;
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
