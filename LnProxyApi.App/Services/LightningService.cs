using Google.Protobuf;
using Grpc.Core;
using Invoicesrpc;
using Lnrpc;
using Routerrpc;
using LnProxyApi.Helpers;

namespace LnProxyApi.LndGrpc.Services;

public class LightningService
    {
        public long RoutingFeeBaseMsat { get {return 1000;} } // 1 sat
        public long RoutingFeePPM { get { return 1000; } }// 1 sat
        public long MinFeeBudgetMsat { get { return 1000; } }// 1 sat
        public long ExpiryBuffer { get { return 300; } }// 5 minutes
        public long CltvDeltaAlpha { get { return 42; } }//42 blocks
        public long CltvDeltaBeta { get { return 42; } }// 42 blocks
        public long MaxCltvExpiry { get { return 1800; } }// 30 minutes
        public long MinCltvExpiry { get { return 200; } }// 3 minutes
        public long RoutingBudgetAlpha { get { return 1000; } }// 1 sat
        public long RoutingBudgetBeta { get { return 1_500_000; } }// 1.5 sat
        public long MaxExpiry { get { return 604800; } } // 7 days

        private readonly Dictionary<Invoice.Types.InvoiceState, string> invoiceState = 
			new Dictionary<Invoice.Types.InvoiceState, string>
        {
            { Invoice.Types.InvoiceState.Canceled, "CANCELED" },
            { Invoice.Types.InvoiceState.Settled, "SETTLED" },
            { Invoice.Types.InvoiceState.Open, "OPEN" }
        };

        private LnGrpcClientService _lnGrpcService;
        private readonly ILogger <LightningService> _logger;

        public LightningService(IConfiguration configuration,ILogger <LightningService> logger)
        {
            _logger = logger;
            _lnGrpcService = new LnGrpcClientService(configuration);
        }


        private PayReq DecodePayRequest(string invoice)
        {
            try
            {
                var client = _lnGrpcService.GetLightningClient();
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
            var routerClient = _lnGrpcService.GetRouterClient();
            var estimateFee = EstimateRouteFee(originalInvoice).Result;
            var req = new SendPaymentRequest()
            {
                PaymentRequest = request,
                FeeLimitMsat = feeLimitMsat,
                CltvLimit = (int)CalculateCltvExpiry(estimateFee),
                TimeoutSeconds = 600
            };


            var call = routerClient.SendPaymentV2(req);
            await foreach (var payment in call.ResponseStream.ReadAllAsync())
            {
                try
                {	var invoiceClient = _lnGrpcService.GetInvoiceClient();
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
            var invoiceClient = _lnGrpcService.GetInvoiceClient();

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

        public long CalculateExpiry(PayReq payReqFromInvoice)
        {
            if (payReqFromInvoice.Timestamp + payReqFromInvoice.Expiry < DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ExpiryBuffer)
            {
                throw new Exception("payment request expiration is too close.");
            }

            long expiry = payReqFromInvoice.Expiry;

            if (expiry > MaxExpiry)
            {
                expiry = MaxExpiry;
            }

            long currentUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long adjustedExpiry = payReqFromInvoice.Timestamp + expiry - currentUnixTime - ExpiryBuffer;

            return adjustedExpiry;
        }


        private async Task<RouteFeeResponse> EstimateRouteFee(PayReq payReqFromInvoice)
        {
            try
            {
                var client = _lnGrpcService.GetRouterClient();
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

        public long CalculateCltvExpiry(RouteFeeResponse estimateFee){
            var cltvExpiry = estimateFee.TimeLockDelay + CltvDeltaAlpha + CltvDeltaBeta;
            _logger.LogInformation($"CLTV expiry from estimate of routing fees: {cltvExpiry}");
            if(cltvExpiry > MaxCltvExpiry){
                _logger.LogError($"CLTV expiry too high from estimate of routing fees: {cltvExpiry}");
                throw new Exception("CLTV expiry too high from estimate of routing fees");
            }

            if(cltvExpiry < MinCltvExpiry){
                cltvExpiry = MinCltvExpiry;
            }
            return cltvExpiry;
        }

        public void ValidateInvoice(PayReq payReqFromInvoice, AddHoldInvoiceRequest hodlInvoice)
        {
            //TODO move expiry
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

        public long CalculateValueMsat(PayReq payReqFromInvoice, long feeBudgetMsat, long routingFeeMsat, string? payReqRoutingMsat){
            var valueMsat = payReqFromInvoice.NumMsat + feeBudgetMsat + routingFeeMsat;
            if(valueMsat < payReqFromInvoice.NumMsat){
                _logger.LogError($"Value too low from estimate of routing fees: {valueMsat}");
                throw new Exception("Value too low from estimate of routing fees");
            }
            if(!string.IsNullOrWhiteSpace(payReqRoutingMsat)){
                int.TryParse(payReqRoutingMsat, out int payReqRoutingMsatInt);
                if (payReqRoutingMsatInt < (MinFeeBudgetMsat + routingFeeMsat)){
                    _logger.LogWarning($"Routing fee budget too low {payReqRoutingMsatInt}");
                    throw new Exception("Routing fee budget too low");
                }
                valueMsat = payReqFromInvoice.NumMsat - routingFeeMsat + payReqRoutingMsatInt;    
            }

            return valueMsat;
        }


        public AddHoldInvoiceResp CreateHodlInvoice(string payRequestString, string? payReqDescription, string? payReqHash, string? payReqRoutingMsat)
        {
            try
            {
                var payReqFromInvoice = DecodePayRequest(payRequestString);
        
                var routingFeeMsat = RoutingFeeBaseMsat + payReqFromInvoice.NumMsat * RoutingFeePPM / 1_000_000;
                var minFeeBudgetMsat = EstimateRouteFee(payReqFromInvoice).Result;
                var cltvExpiry = CalculateCltvExpiry(minFeeBudgetMsat);
                var feeBudgetMsat = minFeeBudgetMsat.RoutingFeeMsat + RoutingBudgetAlpha + (minFeeBudgetMsat.RoutingFeeMsat * RoutingBudgetBeta / 1_000_000);
                
                var valueMsat = CalculateValueMsat(payReqFromInvoice, feeBudgetMsat, routingFeeMsat, payReqRoutingMsat);

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

                ValidateInvoice(payReqFromInvoice, hodlInvoice);

                var invoiceResponse = _lnGrpcService.GetInvoiceClient().AddHoldInvoice(hodlInvoice);
                _logger.LogInformation($"Created hodl invoice: {@invoiceResponse}");
                _ = SubscribeToHodlInvoice(hodlInvoice.Hash, payReqFromInvoice, payRequestString, feeBudgetMsat);
                return invoiceResponse;
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
