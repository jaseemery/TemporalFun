using Temporalio.Activities;
using Microsoft.Extensions.Logging;

namespace TemporalActivities.Sample;

public static class SampleActivities
{
    [Activity]
    public static async Task<string> ProcessOrder(string orderId, decimal amount)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        logger.LogInformation("Processing order {OrderId} with amount {Amount}", orderId, amount);
        
        // Simulate processing time
        await Task.Delay(1000);
        
        var result = $"Order {orderId} processed successfully for ${amount:F2}";
        logger.LogInformation("Order processing completed: {Result}", result);
        
        return result;
    }

    [Activity]
    public static async Task<bool> ValidatePayment(string paymentToken, decimal amount)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        logger.LogInformation("Validating payment token {PaymentToken} for amount {Amount}", paymentToken, amount);
        
        // Simulate payment validation
        await Task.Delay(500);
        
        // Simple validation logic for demo
        var isValid = !string.IsNullOrEmpty(paymentToken) && amount > 0;
        
        logger.LogInformation("Payment validation result: {IsValid}", isValid);
        return isValid;
    }

    [Activity]
    public static async Task<string> SendNotification(string recipient, string message)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        logger.LogInformation("Sending notification to {Recipient}: {Message}", recipient, message);
        
        // Simulate notification sending
        await Task.Delay(200);
        
        var notificationId = Guid.NewGuid().ToString();
        logger.LogInformation("Notification sent with ID: {NotificationId}", notificationId);
        
        return notificationId;
    }

    [Activity]
    public static async Task<Dictionary<string, object>> GenerateReport(string reportType, DateTime startDate, DateTime endDate)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        logger.LogInformation("Generating {ReportType} report from {StartDate} to {EndDate}", 
            reportType, startDate, endDate);
        
        // Simulate report generation
        await Task.Delay(2000);
        
        var report = new Dictionary<string, object>
        {
            ["reportId"] = Guid.NewGuid().ToString(),
            ["type"] = reportType,
            ["startDate"] = startDate,
            ["endDate"] = endDate,
            ["generatedAt"] = DateTime.UtcNow,
            ["recordCount"] = Random.Shared.Next(100, 1000),
            ["totalValue"] = Random.Shared.Next(10000, 100000)
        };
        
        logger.LogInformation("Report generated with ID: {ReportId}", report["reportId"]);
        return report;
    }
}