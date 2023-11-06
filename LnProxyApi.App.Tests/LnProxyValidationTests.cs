using LnProxyApi.LndGrpc.Services;
using Lnrpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Routerrpc;

[TestFixture]
public class InvoiceValidationTests
{
        private LightningService lightningService;
        
        [SetUp]
        public void Setup()
        {
            var configuration = new Mock<IConfiguration>().Object;
            var logger = new Mock<ILogger<LightningService>>().Object;
            lightningService = new LightningService(configuration, logger);
        }

        [Test]
        public void ValidateInvoiceValidInputNoExceptionThrown()
        {
            var payReqFromInvoice = new PayReq
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Expiry = 3600,
                Features = { },
                NumMsat = 1000
            };

            var hodlInvoice = new Invoicesrpc.AddHoldInvoiceRequest();

            // No exception should be thrown
            Assert.DoesNotThrow(() => lightningService.ValidateInvoice(payReqFromInvoice, hodlInvoice));
        }


    [Test]
    public void ValidateInvoicesExpirationTooCloseExceptionThrown()
    {
        var payReqFromInvoice = new PayReq
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Expiry = 1, // Set to an expiration time in the past
            Features = {},
            NumMsat = 1000 // Some non-zero value
        };

       var hodlInvoice = new Invoicesrpc.AddHoldInvoiceRequest();

        // Exception should be thrown with the specified message
        var ex = Assert.Throws<Exception>(() => lightningService.ValidateInvoice(payReqFromInvoice, hodlInvoice));
        Assert.That(ex.Message, Is.EqualTo("payment request expiration is too close."));
    }


    [Test]
    public void ValidateInvoicesAmpInvoiceExceptionThrown()
    {
        var payReqFromInvoice = new PayReq
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Expiry = 3600,
            Features = { { 30,  new Feature()} }, 
            NumMsat = 1000
        };

        var hodlInvoice = new Invoicesrpc.AddHoldInvoiceRequest();

        // Exception should be thrown with the specified message
        var ex = Assert.Throws<Exception>(() => lightningService.ValidateInvoice(payReqFromInvoice, hodlInvoice));
        Assert.That(ex.Message, Is.EqualTo("Cannot wrap AMP invoice"));
    }


 [Test]
    public void CalcValueMsat_ValidInput_ReturnsExpectedValue()
    {
        var payReqFromInvoice = new PayReq
        {
            NumMsat = 1000 // Some value
        };
        long feeBudgetMsat = 500;
        long routingFeeMsat = 200;
        string? payReqRoutingMsat = null;

        long result = lightningService.CalculateValueMsat(payReqFromInvoice, feeBudgetMsat, routingFeeMsat, payReqRoutingMsat);

        Assert.That(result, Is.EqualTo(1700)); // 1000 + 500 + 200
    }

    [Test]
    public void CalcValueMsat_InvalidValueFromRoutingFees_ThrowsException()
    {
        var payReqFromInvoice = new PayReq
        {
            NumMsat = 1000
        };
        long feeBudgetMsat = -500; // Simulate a negative value
        long routingFeeMsat = 200;
        string? payReqRoutingMsat = null;

        var ex = Assert.Throws<Exception>(() => lightningService.CalculateValueMsat(payReqFromInvoice, feeBudgetMsat, routingFeeMsat, payReqRoutingMsat));
        Assert.That(ex.Message, Is.EqualTo("Value too low from estimate of routing fees"));
    }

    [Test]
    public void CalcValueMsat_OverflowOnRoutingFeeBudget_ThrowsException()
    {
        var payReqFromInvoice = new PayReq
        {
            NumMsat = 1000
        };
        long feeBudgetMsat = 10000; // Simulate an overflow
        long routingFeeMsat = 200;
        string payReqRoutingMsat = long.MaxValue.ToString();

        var ex = Assert.Throws<Exception>(() => lightningService.CalculateValueMsat(payReqFromInvoice, feeBudgetMsat, routingFeeMsat, payReqRoutingMsat));
        Assert.That(ex.Message, Is.EqualTo("Routing fee budget too low"));
    }

    [Test]
    public void CalcValueMsat_RoutingFeeBudgetTooLow_ThrowsException()
    {
        var payReqFromInvoice = new PayReq
        {
            NumMsat = 1000
        };
        long feeBudgetMsat = 500;
        long routingFeeMsat = 200;
        string payReqRoutingMsat = "100"; // Below the MinFeeBudgetMsat

        var ex = Assert.Throws<Exception>(() => lightningService.CalculateValueMsat(payReqFromInvoice, feeBudgetMsat, routingFeeMsat, payReqRoutingMsat));
        Assert.That(ex.Message, Is.EqualTo("Routing fee budget too low"));
    }

    [Test]
    public void CalcCltvExpiry_ValidInput_ReturnsExpectedValue_GreaterThan_MinCltvExpiry()
    {
        var estimateFee = new RouteFeeResponse
        {
            TimeLockDelay = 200,
        };

        long result = lightningService.CalculateCltvExpiry(estimateFee);

        Assert.That(result, Is.EqualTo(200 + lightningService.CltvDeltaAlpha + lightningService.CltvDeltaBeta));
    }


    [Test]
    public void CalcCltvExpiry_ValidInput_ReturnsDefault_MinCltvExpiry()
    {
        var estimateFee = new RouteFeeResponse
        {
            TimeLockDelay = 100,
        };

        long result = lightningService.CalculateCltvExpiry(estimateFee);

        Assert.That(result, Is.EqualTo(200));
    }


    [Test]
    public void CalcCltvExpiry_ExceedsMaxCltvExpiry_ThrowsException()
    {
        var estimateFee = new RouteFeeResponse
        {
            TimeLockDelay = 2000, // A value that exceeds MaxCltvExpiry
        };

        var ex = Assert.Throws<Exception>(() => lightningService.CalculateCltvExpiry(estimateFee));
        Assert.That(ex.Message, Is.EqualTo("CLTV expiry too high from estimate of routing fees"));
    }

    [Test]
    public void CalcCltvExpiry_BelowMinCltvExpiry_SetsToMinCltvExpiry()
    {
        var estimateFee = new RouteFeeResponse
        {
            TimeLockDelay = 10, // A value below MinCltvExpiry
        };

        long result = lightningService.CalculateCltvExpiry(estimateFee);

        Assert.That(result, Is.EqualTo(lightningService.MinCltvExpiry));
    }

  //Runs
  [Test]
    public void CalculateExpiry_ExpiryTooClose_ThrowsException()
    {
        var payReqFromInvoice = new PayReq
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Expiry = lightningService.ExpiryBuffer - 1
        };

        Assert.Throws<Exception>(() => lightningService.CalculateExpiry(payReqFromInvoice));
    }

    //Runs
    [Test]
    public void CalculateExpiry_WithinExpiryBuffer_ReturnsExpectedValue()
    {
        var payReqFromInvoice = new PayReq
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Expiry = lightningService.ExpiryBuffer + 100 // Within ExpiryBuffer
        };

        long result = lightningService.CalculateExpiry(payReqFromInvoice);

        Assert.That(result, Is.EqualTo(100));
    }

    [Test]
    public void CalculateExpiry_ExceedsExpiryBuffer_SetsToExpiryBuffer()
    {
        var payReqFromInvoice = new PayReq
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Expiry = lightningService.ExpiryBuffer + 200 // Exceeds ExpiryBuffer
        };

        long result = lightningService.CalculateExpiry(payReqFromInvoice);

        Assert.That(result, Is.EqualTo(200));
    }

    [Test]
    public void CalculateExpiry_CurrentUnixTimeIsInFuture_ReturnsAdjustedExpiry()
    {
        long futureUnixTime = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds(); // Unix time in the future
        var payReqFromInvoice = new PayReq
        {
            Timestamp = futureUnixTime,
            Expiry = 300
        };

        long result = lightningService.CalculateExpiry(payReqFromInvoice);

        Assert.That(result, Is.EqualTo(600));
    }
}