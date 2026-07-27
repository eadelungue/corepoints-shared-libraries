using Npgsql;

namespace CorePoints.ProductService.Application.Interfaces;

public interface IDbConnectionFactory
{
    NpgsqlConnection CreateConnection();
}
