using Microsoft.Data.Sqlite;
using ShipmentCalendar.Data;
using ShipmentCalendar.Models;

namespace ShipmentCalendar.Repositories;

public class SqliteProcessDefinitionRepository : IProcessDefinitionRepository
{
    public async Task<IEnumerable<ProcessDefinition>> GetAllAsync()
    {
        var definitions = new List<ProcessDefinition>();
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ItemNumber, ProcessName, LeadTimeDays, SortOrder, IsVisible, CsvColumnName, WarningDaysBeforeDeadline, DepartmentId FROM ProcessDefinitions ORDER BY ItemNumber, SortOrder";
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            definitions.Add(ReadDefinition(reader));

        return definitions;
    }

    public async Task<IEnumerable<ProcessDefinition>> GetByItemNumberAsync(string itemNumber)
    {
        var definitions = new List<ProcessDefinition>();
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ItemNumber, ProcessName, LeadTimeDays, SortOrder, IsVisible, CsvColumnName, WarningDaysBeforeDeadline, DepartmentId FROM ProcessDefinitions WHERE ItemNumber = $in ORDER BY SortOrder";
        command.Parameters.AddWithValue("$in", itemNumber);
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            definitions.Add(ReadDefinition(reader));

        return definitions;
    }

    public async Task<IEnumerable<string>> GetItemNumbersAsync()
    {
        var names = new List<string>();
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT ItemNumber FROM ProcessDefinitions ORDER BY ItemNumber";
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));

        return names;
    }

    public async Task AddAsync(ProcessDefinition definition)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO ProcessDefinitions (ItemNumber, ProcessName, LeadTimeDays, SortOrder, IsVisible, CsvColumnName, WarningDaysBeforeDeadline, DepartmentId)
            VALUES ($in, $name, $days, $so, $vis, $csv, $warn, $dept)";
        command.Parameters.AddWithValue("$in", definition.ItemNumber);
        command.Parameters.AddWithValue("$name", definition.ProcessName);
        command.Parameters.AddWithValue("$days", definition.LeadTimeDays);
        command.Parameters.AddWithValue("$so", definition.SortOrder);
        command.Parameters.AddWithValue("$vis", definition.IsVisible ? 1 : 0);
        command.Parameters.AddWithValue("$csv", definition.CsvColumnName);
        command.Parameters.AddWithValue("$warn", definition.WarningDaysBeforeDeadline);
        command.Parameters.AddWithValue("$dept", definition.DepartmentId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateAsync(ProcessDefinition definition)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE ProcessDefinitions SET ProcessName=$name, LeadTimeDays=$days, SortOrder=$so, IsVisible=$vis, CsvColumnName=$csv, WarningDaysBeforeDeadline=$warn, DepartmentId=$dept
            WHERE Id=$id";
        command.Parameters.AddWithValue("$name", definition.ProcessName);
        command.Parameters.AddWithValue("$days", definition.LeadTimeDays);
        command.Parameters.AddWithValue("$so", definition.SortOrder);
        command.Parameters.AddWithValue("$vis", definition.IsVisible ? 1 : 0);
        command.Parameters.AddWithValue("$csv", definition.CsvColumnName);
        command.Parameters.AddWithValue("$warn", definition.WarningDaysBeforeDeadline);
        command.Parameters.AddWithValue("$dept", definition.DepartmentId);
        command.Parameters.AddWithValue("$id", definition.Id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ProcessDefinitions WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    private static ProcessDefinition ReadDefinition(SqliteDataReader reader) => new ProcessDefinition
    {
        Id = reader.GetInt32(0),
        ItemNumber = reader.GetString(1),
        ProcessName = reader.GetString(2),
        LeadTimeDays = reader.GetInt32(3),
        SortOrder = reader.GetInt32(4),
        IsVisible = reader.GetInt32(5) == 1,
        CsvColumnName = reader.GetString(6),
        WarningDaysBeforeDeadline = reader.GetInt32(7),
        DepartmentId = reader.GetInt32(8)
    };
}
