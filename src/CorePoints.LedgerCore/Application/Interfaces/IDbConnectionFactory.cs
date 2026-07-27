using Npgsql;

namespace CorePoints.LedgerCore.Application.Interfaces;

public interface IDbConnectionFactory
{
    NpgsqlConnection CreateConnection();
}
