using Temporalio.Activities;
using Microsoft.Extensions.Logging;

namespace TemporalWorkerApp.Activities;

public static class DatabaseActivity
{
    [Activity]
    public static async Task<bool> SaveData(string tableName, Dictionary<string, object> data)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        logger.LogInformation("Saving data to table {TableName}", tableName);
        
        // Simulate database operation
        await Task.Delay(500);
        
        logger.LogInformation("Data saved successfully to {TableName}", tableName);
        return true;
    }
    
    [Activity]
    public static async Task<Dictionary<string, object>?> GetData(string tableName, string id)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        logger.LogInformation("Retrieving data from table {TableName} with ID {Id}", tableName, id);
        
        // Simulate database query
        await Task.Delay(300);
        
        var result = new Dictionary<string, object>
        {
            ["id"] = id,
            ["timestamp"] = DateTime.UtcNow,
            ["status"] = "active"
        };
        
        logger.LogInformation("Data retrieved successfully from {TableName}", tableName);
        return result;
    }
}