using Temporalio.Activities;
using Microsoft.Extensions.Logging;

namespace TemporalWorkerApp.Activities;

public static class EmailActivity
{
    [Activity]
    public static async Task<string> SendEmail(string to, string subject, string body)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        logger.LogInformation("Sending email to {To} with subject {Subject}", to, subject);
        
        // Simulate email sending
        await Task.Delay(1000);
        
        var messageId = Guid.NewGuid().ToString();
        logger.LogInformation("Email sent successfully with message ID: {MessageId}", messageId);
        
        return messageId;
    }
}