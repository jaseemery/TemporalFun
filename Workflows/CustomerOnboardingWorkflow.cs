// Workflows/CustomerOnboardingWorkflow.cs
using Temporalio.Workflows;
using TemporalWorkerApp.Activities;

namespace TemporalWorkerApp.Workflows;


[Workflow("customer-onboarding")]
public class CustomerOnboardingWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(CustomerOnboardingRequest request)
    {
        // Send welcome email
        var emailResult = await Workflow.ExecuteActivityAsync(
            () => EmailActivity.SendEmail(
                request.Email, 
                "Welcome to Our Platform!", 
                $"Welcome {request.Name}! Your account has been created."
            ),
            new() { ScheduleToCloseTimeout = TimeSpan.FromMinutes(5) }
        );

        // Create customer record in database
        var customerData = new Dictionary<string, object>
        {
            ["customerId"] = request.CustomerId,
            ["name"] = request.Name,
            ["email"] = request.Email,
            ["status"] = "active",
            ["createdAt"] = DateTime.UtcNow,
            ["emailSent"] = true
        };

        await Workflow.ExecuteActivityAsync(
            () => DatabaseActivity.SaveData("customers", customerData),
            new() { ScheduleToCloseTimeout = TimeSpan.FromMinutes(5) }
        );

        return $"Customer {request.Name} onboarded successfully with ID: {request.CustomerId}";
    }

    [WorkflowSignal]
    public async Task UpdateCustomerStatusAsync(string newStatus)
    {
        // Handle status updates during onboarding
        await Task.CompletedTask;
    }

    [WorkflowQuery]
    public string GetOnboardingStatus() => "In Progress";
}

// Request record for the workflow
public record CustomerOnboardingRequest(
    string CustomerId, 
    string Name, 
    string Email
);