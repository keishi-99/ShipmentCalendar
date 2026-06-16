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

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ItemNumber, ProcessName, SetupTimeMinutes, WorkTimeMinutes, SortOrder, IsVisible, DestinationCode, WarningDaysBeforeDeadline, DepartmentId, CoolTimeMinutes, OutsourceLeadDays FROM ProcessDefinitions ORDER BY ItemNumber, SortOrder";
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

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ItemNumber, ProcessName, SetupTimeMinutes, WorkTimeMinutes, SortOrder, IsVisible, DestinationCode, WarningDaysBeforeDeadline, DepartmentId, CoolTimeMinutes, OutsourceLeadDays FROM ProcessDefinitions WHERE ItemNumber = $in ORDER BY SortOrder";
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

        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO ProcessDefinitions (ItemNumber, ProcessName, SetupTimeMinutes, WorkTimeMinutes, SortOrder, IsVisible, DestinationCode, WarningDaysBeforeDeadline, DepartmentId, CoolTimeMinutes, OutsourceLeadDays)
            VALUES ($in, $name, $setup, $work, $so, $vis, $dest, $warn, $dept, $cool, $outsource)";
        command.Parameters.AddWithValue("$in", definition.ItemNumber);
        command.Parameters.AddWithValue("$name", definition.ProcessName);
        command.Parameters.AddWithValue("$setup", definition.SetupTimeMinutes);
        command.Parameters.AddWithValue("$work", definition.WorkTimeMinutes);
        command.Parameters.AddWithValue("$so", definition.SortOrder);
        command.Parameters.AddWithValue("$vis", definition.IsVisible ? 1 : 0);
        command.Parameters.AddWithValue("$dest", definition.DestinationCode);
        command.Parameters.AddWithValue("$warn", definition.WarningDaysBeforeDeadline);
        command.Parameters.AddWithValue("$dept", definition.DepartmentId);
        command.Parameters.AddWithValue("$cool", definition.CoolTimeMinutes);
        command.Parameters.AddWithValue("$outsource", definition.OutsourceLeadDays);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateAsync(ProcessDefinition definition)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE ProcessDefinitions SET ProcessName=$name, SetupTimeMinutes=$setup, WorkTimeMinutes=$work, SortOrder=$so, IsVisible=$vis, DestinationCode=$dest, WarningDaysBeforeDeadline=$warn, DepartmentId=$dept, CoolTimeMinutes=$cool, OutsourceLeadDays=$outsource
            WHERE Id=$id";
        command.Parameters.AddWithValue("$name", definition.ProcessName);
        command.Parameters.AddWithValue("$setup", definition.SetupTimeMinutes);
        command.Parameters.AddWithValue("$work", definition.WorkTimeMinutes);
        command.Parameters.AddWithValue("$so", definition.SortOrder);
        command.Parameters.AddWithValue("$vis", definition.IsVisible ? 1 : 0);
        command.Parameters.AddWithValue("$dest", definition.DestinationCode);
        command.Parameters.AddWithValue("$warn", definition.WarningDaysBeforeDeadline);
        command.Parameters.AddWithValue("$dept", definition.DepartmentId);
        command.Parameters.AddWithValue("$cool", definition.CoolTimeMinutes);
        command.Parameters.AddWithValue("$outsource", definition.OutsourceLeadDays);
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

    public async Task ReplaceForItemNumberAsync(string itemNumber, IEnumerable<ProcessDefinition> definitions)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM ProcessDefinitions WHERE ItemNumber = $in";
            deleteCommand.Parameters.AddWithValue("$in", itemNumber);
            await deleteCommand.ExecuteNonQueryAsync();
        }

        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = @"
            INSERT INTO ProcessDefinitions (ItemNumber, ProcessName, SetupTimeMinutes, WorkTimeMinutes, SortOrder, IsVisible, DestinationCode, WarningDaysBeforeDeadline, DepartmentId, CoolTimeMinutes, OutsourceLeadDays)
            VALUES ($in, $name, $setup, $work, $so, $vis, $dest, $warn, $dept, $cool, $outsource)";
        var inParam = insertCommand.Parameters.Add("$in", SqliteType.Text);
        var nameParam = insertCommand.Parameters.Add("$name", SqliteType.Text);
        var setupParam = insertCommand.Parameters.Add("$setup", SqliteType.Real);
        var workParam = insertCommand.Parameters.Add("$work", SqliteType.Real);
        var soParam = insertCommand.Parameters.Add("$so", SqliteType.Integer);
        var visParam = insertCommand.Parameters.Add("$vis", SqliteType.Integer);
        var destParam = insertCommand.Parameters.Add("$dest", SqliteType.Text);
        var warnParam = insertCommand.Parameters.Add("$warn", SqliteType.Integer);
        var deptParam = insertCommand.Parameters.Add("$dept", SqliteType.Integer);
        var coolParam = insertCommand.Parameters.Add("$cool", SqliteType.Real);
        var outsourceParam = insertCommand.Parameters.Add("$outsource", SqliteType.Integer);

        foreach (var definition in definitions)
        {
            inParam.Value = itemNumber;
            nameParam.Value = definition.ProcessName;
            setupParam.Value = definition.SetupTimeMinutes;
            workParam.Value = definition.WorkTimeMinutes;
            soParam.Value = definition.SortOrder;
            visParam.Value = definition.IsVisible ? 1 : 0;
            destParam.Value = definition.DestinationCode;
            warnParam.Value = definition.WarningDaysBeforeDeadline;
            deptParam.Value = definition.DepartmentId;
            coolParam.Value = definition.CoolTimeMinutes;
            outsourceParam.Value = definition.OutsourceLeadDays;
            await insertCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static ProcessDefinition ReadDefinition(SqliteDataReader reader) => new ProcessDefinition
    {
        Id = reader.GetInt32(0),
        ItemNumber = reader.GetString(1),
        ProcessName = reader.GetString(2),
        SetupTimeMinutes = reader.GetDouble(3),
        WorkTimeMinutes = reader.GetDouble(4),
        SortOrder = reader.GetInt32(5),
        IsVisible = reader.GetInt32(6) == 1,
        DestinationCode = reader.GetString(7),
        WarningDaysBeforeDeadline = reader.GetInt32(8),
        DepartmentId = reader.GetInt32(9),
        CoolTimeMinutes = reader.GetDouble(10),
        OutsourceLeadDays = reader.GetInt32(11)
    };
}
