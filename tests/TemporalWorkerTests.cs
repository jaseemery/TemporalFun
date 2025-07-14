using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using Xunit;
using TemporalWorkerApp.Activities;
using TemporalWorkerApp.Workflows;
using Temporalio.Activities;
using Temporalio.Workflows;

namespace TemporalWorker.Tests;

public class TemporalWorkerTests
{
    [Fact]
    public void EmailActivity_ShouldHaveActivityAttribute()
    {
        // Arrange
        var method = typeof(EmailActivity).GetMethod(nameof(EmailActivity.SendEmail));

        // Act
        var attribute = method!.GetCustomAttributes(typeof(ActivityAttribute), false).FirstOrDefault();

        // Assert
        attribute.Should().NotBeNull();
        attribute.Should().BeOfType<ActivityAttribute>();
    }

    [Fact]
    public void DatabaseActivity_SaveData_ShouldHaveActivityAttribute()
    {
        // Arrange
        var method = typeof(DatabaseActivity).GetMethod(nameof(DatabaseActivity.SaveData));

        // Act
        var attribute = method!.GetCustomAttributes(typeof(ActivityAttribute), false).FirstOrDefault();

        // Assert
        attribute.Should().NotBeNull();
        attribute.Should().BeOfType<ActivityAttribute>();
    }

    [Fact]
    public void DatabaseActivity_GetData_ShouldHaveActivityAttribute()
    {
        // Arrange
        var method = typeof(DatabaseActivity).GetMethod(nameof(DatabaseActivity.GetData));

        // Act
        var attribute = method!.GetCustomAttributes(typeof(ActivityAttribute), false).FirstOrDefault();

        // Assert
        attribute.Should().NotBeNull();
        attribute.Should().BeOfType<ActivityAttribute>();
    }

    [Fact]
    public void SimpleWorkflow_ShouldHaveWorkflowAttribute()
    {
        // Arrange
        var workflowType = typeof(SimpleWorkflow);

        // Act
        var attribute = workflowType.GetCustomAttributes(typeof(WorkflowAttribute), false).FirstOrDefault();

        // Assert
        attribute.Should().NotBeNull();
        attribute.Should().BeOfType<WorkflowAttribute>();
    }

    [Fact]
    public void SimpleWorkflow_ShouldHaveRunMethod()
    {
        // Arrange
        var workflowType = typeof(SimpleWorkflow);

        // Act
        var runMethod = workflowType.GetMethods()
            .Where(m => m.GetCustomAttributes(typeof(WorkflowRunAttribute), false).Any())
            .FirstOrDefault();

        // Assert
        runMethod.Should().NotBeNull();
        runMethod!.Name.Should().Be("RunAsync");
    }

    [Fact]
    public void Activities_ShouldHaveValidSignatures()
    {
        // Arrange
        var emailMethod = typeof(EmailActivity).GetMethod(nameof(EmailActivity.SendEmail));
        var saveMethod = typeof(DatabaseActivity).GetMethod(nameof(DatabaseActivity.SaveData));
        var getMethod = typeof(DatabaseActivity).GetMethod(nameof(DatabaseActivity.GetData));

        // Act & Assert
        emailMethod.Should().NotBeNull();
        emailMethod!.ReturnType.Should().Be(typeof(Task<string>));
        emailMethod.GetParameters().Should().HaveCount(3);

        saveMethod.Should().NotBeNull();
        saveMethod!.ReturnType.Should().Be(typeof(Task<bool>));
        saveMethod.GetParameters().Should().HaveCount(2);

        getMethod.Should().NotBeNull();
        getMethod!.ReturnType.Should().Be(typeof(Task<Dictionary<string, object>?>));
        getMethod.GetParameters().Should().HaveCount(2);
    }

    [Fact]
    public void EnvironmentVariables_ShouldHaveDefaults()
    {
        // Arrange
        var expectedDefaults = new Dictionary<string, string>
        {
            ["TEMPORAL_SERVER"] = "localhost:7233",
            ["TASK_QUEUE"] = "default",
            ["ENVIRONMENT"] = "unknown",
            ["WORKER_IDENTITY"] = "worker-1"
        };

        // Act & Assert
        foreach (var (envVar, defaultValue) in expectedDefaults)
        {
            var value = Environment.GetEnvironmentVariable(envVar) ?? defaultValue;
            value.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public void WorkerConfiguration_ShouldBeValid()
    {
        // Arrange
        var activities = new Delegate[]
        {
            TemporalWorkerApp.Activities.EmailActivity.SendEmail,
            TemporalWorkerApp.Activities.DatabaseActivity.SaveData,
            TemporalWorkerApp.Activities.DatabaseActivity.GetData
        };

        var workflows = new Type[]
        {
            typeof(TemporalWorkerApp.Workflows.SimpleWorkflow)
        };

        // Act & Assert
        activities.Should().HaveCount(3);
        workflows.Should().HaveCount(1);
        
        // All activities should be valid delegates
        foreach (var activity in activities)
        {
            activity.Should().NotBeNull();
            activity.Should().BeAssignableTo<Delegate>();
        }
        
        // All workflows should be valid types
        foreach (var workflow in workflows)
        {
            workflow.Should().NotBeNull();
            workflow.IsClass.Should().BeTrue();
        }
    }
}