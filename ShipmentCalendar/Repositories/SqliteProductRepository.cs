using Microsoft.Data.Sqlite;
using ShipmentCalendar.Data;
using ShipmentCalendar.Models;

namespace ShipmentCalendar.Repositories;

public class SqliteProductRepository : IProductRepository
{
    public async Task<IEnumerable<Product>> GetAllAsync()
    {
        var products = new List<Product>();
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ProductName, ItemNumber FROM Products ORDER BY ProductName";
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            products.Add(ReadProduct(reader));

        return products;
    }

    public async Task<Product?> GetByProductNameAsync(string productName)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ProductName, ItemNumber FROM Products WHERE ProductName = $pn";
        command.Parameters.AddWithValue("$pn", productName);
        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return ReadProduct(reader);

        return null;
    }

    public async Task AddOrUpdateAsync(Product product)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Products (ProductName, ItemNumber) VALUES ($pn, $item)
            ON CONFLICT(ProductName) DO UPDATE SET ItemNumber = $item";
        command.Parameters.AddWithValue("$pn", product.ProductName);
        command.Parameters.AddWithValue("$item", product.ItemNumber);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(string productName)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Products WHERE ProductName = $pn";
        command.Parameters.AddWithValue("$pn", productName);
        await command.ExecuteNonQueryAsync();
    }

    private static Product ReadProduct(SqliteDataReader reader) => new Product
    {
        Id = reader.GetInt32(0),
        ProductName = reader.GetString(1),
        ItemNumber = reader.GetString(2)
    };
}
