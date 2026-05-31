using Npgsql;

namespace TicaTourAPI.Data;

public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly IConfiguration _configuration;

    public NpgsqlConnectionFactory(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<NpgsqlConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = _configuration.GetConnectionString("SupabaseDb");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("SupabaseDb connection string is missing.");
        }

        var connection = new NpgsqlConnection(connectionString);

        await connection.OpenAsync(cancellationToken);

        return connection;
    }
}