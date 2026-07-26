using CorePoints.LedgerCore.Application.Interfaces;
using Npgsql;

namespace CorePoints.LedgerCore.Infrastructure.Data;

public sealed class NpgsqlConnectionFactory(IConfiguration configuration) : IDbConnectionFactory
{
    private readonly string _connectionString = configuration.GetConnectionString("LedgerDb")
        ?? throw new InvalidOperationException("LedgerDb connection string not configured.");

    public NpgsqlConnection CreateConnection() => new(_connectionString);
}
