using Temporalio.Activities;
using Microsoft.Extensions.Logging;

namespace PaymentService.Activities;

public static class PaymentActivities
{
    [Activity]
    public static async Task<PaymentValidationResult> ValidatePayment(PaymentInfo paymentInfo)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        logger.LogInformation("Validating payment for amount {Amount}", paymentInfo.Amount);
        
        // Payment validation logic
        await Task.Delay(500);
        
        return new PaymentValidationResult
        {
            IsValid = paymentInfo.Amount > 0 && !string.IsNullOrEmpty(paymentInfo.CardNumber),
            ValidationId = Guid.NewGuid().ToString()
        };
    }

    [Activity]
    public static async Task<PaymentResult> ProcessPayment(PaymentInfo paymentInfo, decimal amount)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        logger.LogInformation("Processing payment of {Amount} for card ending in {CardLast4}", 
            amount, paymentInfo.CardNumber[^4..]);
        
        // Payment processing logic
        await Task.Delay(2000);
        
        return new PaymentResult
        {
            TransactionId = $"TXN_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..8]}",
            Status = "Completed",
            ProcessedAmount = amount,
            ProcessedAt = DateTime.UtcNow
        };
    }
}

public record PaymentInfo(string CardNumber, string ExpiryDate, decimal Amount);
public record PaymentValidationResult(bool IsValid, string ValidationId);
public record PaymentResult(string TransactionId, string Status, decimal ProcessedAmount, DateTime ProcessedAt);