using Temporalio.Workflows;

namespace TemporalWorkerApp.Workflows;

[Workflow("order-processing")]
public class OrderProcessingWorkflow
{
    [WorkflowRun]
    public async Task<OrderResult> RunAsync(OrderRequest request)
    {
        // Validate order
        var orderData = await Workflow.ExecuteActivityAsync(
            () => Activities.DatabaseActivity.GetData("orders", request.OrderId),
            new() { ScheduleToCloseTimeout = TimeSpan.FromMinutes(5) }
        );

        if (orderData == null)
        {
            return new OrderResult(request.OrderId, "Invalid", "Order not found");
        }

        // Send notification email
        await Workflow.ExecuteActivityAsync(
            () => Activities.EmailActivity.SendEmail(
                request.CustomerEmail, 
                "Order Confirmation", 
                $"Your order {request.OrderId} is being processed."
            ),
            new() { ScheduleToCloseTimeout = TimeSpan.FromMinutes(2) }
        );

        // Simulate processing time
        await Workflow.DelayAsync(TimeSpan.FromSeconds(2));

        return new OrderResult(request.OrderId, "Completed", "Order processed successfully");
    }

    [WorkflowSignal]
    public async Task CancelOrderAsync(string reason)
    {
        // Handle order cancellation - using simplified activity call
        await Workflow.DelayAsync(TimeSpan.FromMilliseconds(100));
    }

    [WorkflowQuery]
    public string GetOrderStatus() => "Processing";
}

[Workflow("user-onboarding")]
public class UserOnboardingWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(UserOnboardingRequest request)
    {
        // Send welcome email
        await Workflow.ExecuteActivityAsync(
            () => Activities.EmailActivity.SendEmail(
                request.Email,
                "Welcome!",
                $"Welcome {request.Name} to our platform!"
            ),
            new() { ScheduleToCloseTimeout = TimeSpan.FromMinutes(2) }
        );

        // Wait for email verification (simulate with delay)
        await Workflow.DelayAsync(TimeSpan.FromSeconds(5));

        // Send onboarding completion email
        await Workflow.ExecuteActivityAsync(
            () => Activities.EmailActivity.SendEmail(
                request.Email,
                "Onboarding Complete",
                $"Your account setup is complete, {request.Name}!"
            ),
            new() { ScheduleToCloseTimeout = TimeSpan.FromMinutes(2) }
        );

        return $"User {request.Name} onboarded successfully";
    }

    [WorkflowQuery]
    public string GetOnboardingStep() => "In Progress";
}

[Workflow("simple-workflow")]
public class SimpleWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(string input)
    {
        // Simple workflow for testing hot reload
        await Workflow.DelayAsync(TimeSpan.FromSeconds(1));
        return $"Processed: {input}";
    }

    [WorkflowQuery]
    public string GetStatus() => "Running";
}

// Data models
public record OrderRequest(string OrderId, string CustomerEmail, decimal Amount);
public record OrderResult(string OrderId, string Status, string Message);
public record UserOnboardingRequest(string Name, string Email);