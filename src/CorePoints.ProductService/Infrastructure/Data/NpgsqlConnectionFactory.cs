using CorePoints.ProductService.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace CorePoints.ProductService.Infrastructure.Data;

public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public NpgsqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("ProductDb")
            ?? throw new InvalidOperationException("ProductDb connection string is not configured.");
    }

    public NpgsqlConnection CreateConnection() => new(_connectionString);
}
