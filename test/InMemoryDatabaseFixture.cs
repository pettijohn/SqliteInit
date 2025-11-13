using Microsoft.Data.Sqlite;

namespace test;

/// <summary>
/// xUnit fixture that provides an in-memory SQLite database for testing.
/// The database is created once per test class and shared across all tests in that class.
/// </summary>
public class InMemoryDatabaseFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    private bool _disposed = false;

    public InMemoryDatabaseFixture()
    {
        // Create in-memory SQLite connection with shared cache
        // Using shared cache allows multiple connections to access the same in-memory database
        var uniqueDbName = $"test_db_{Guid.NewGuid():N}";
        ConnectionString = $"DataSource={uniqueDbName};Mode=Memory;Cache=Shared";
        _connection = new SqliteConnection(ConnectionString);
        _connection.Open();
    }

    /// <summary>
    /// Gets the connection string for the in-memory database.
    /// Use this to create additional connections that share the same database.
    /// </summary>
    public string ConnectionString { get; }

    /// <summary>
    /// Gets the active database connection.
    /// This connection remains open for the lifetime of the fixture.
    /// </summary>
    public SqliteConnection Connection => _connection;

    /// <summary>
    /// Disposes the database connection and cleans up resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _connection?.Dispose();
            }
            _disposed = true;
        }
    }
}
