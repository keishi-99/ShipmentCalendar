using ShipmentCalendar.Models;

namespace ShipmentCalendar.Repositories;

public interface IProductRepository
{
    Task<IEnumerable<Product>> GetAllAsync();
    Task<Product?> GetByProductNameAsync(string productName);
    Task AddOrUpdateAsync(Product product);
    Task DeleteAsync(string productName);
}
