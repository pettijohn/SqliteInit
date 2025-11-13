using Microsoft.Data.Sqlite;
using SqliteInit;

namespace test;

/// <summary>
/// Comprehensive unit tests for SqliteInit library.
/// Tests all public methods and exception scenarios using an in-memory SQLite database.
/// </summary>
public class SqliteInitTests : IClassFixture<InMemoryDatabaseFixture>
{
    private readonly InMemoryDatabaseFixture _fixture;
    private readonly string _testMigrationsPath;

    public SqliteInitTests(InMemoryDatabaseFixture fixture)
    {
        _fixture = fixture;
        _testMigrationsPath = Path.Combine(Path.GetTempPath(), $"migrations_{Guid.NewGuid():N}");
    }

    #region Init Tests - Happy Path

    [Fact]
    public void Init_WithConnectionString_AppliesMigrationsSuccessfully()
    {
        // Arrange
        var migrationsPath = CreateTestMigrations(new[]
        {
            ("001_initial", new[] { "001_create_products.sql" })
        });
        var connectionString = "DataSource=:memory:";

        // Act
        SqliteInit.SqliteInit.Init(connectionString, migrationsPath);

        // Assert - need to verify with a new connection since Init disposes it
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        var version = SqliteInit.SqliteInit.CheckUserVersion(connection);
        Assert.Equal(0, version); // Memory database is gone after Init disposes connection

        // Cleanup
        Directory.Delete(migrationsPath, true);
    }

    [Fact]
    public void Init_WithConnection_AppliesMigrationsSuccessfully()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var migrationsPath = CreateTestMigrations(new[]
        {
            ("002_categories", new[] { "001_create_categories.sql" })
        });

        // Act
        SqliteInit.SqliteInit.Init(connection, migrationsPath);

        // Assert
        var version = SqliteInit.SqliteInit.CheckUserVersion(connection);
        Assert.Equal(2, version);
        AssertTableExists(connection, "Categories");

        // Cleanup
        Directory.Delete(migrationsPath, true);
    }

    [Fact]
    public void Init_WithMultipleVersions_AppliesInOrder()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var migrationsPath = CreateTestMigrations(new[]
        {
            ("003_orders", new[] { "001_create_orders.sql" }),
            ("004_customers", new[] { "001_create_customers.sql" })
        });

        // Act
        SqliteInit.SqliteInit.Init(connection, migrationsPath);

        // Assert
        var version = SqliteInit.SqliteInit.CheckUserVersion(connection);
        Assert.Equal(4, version);
        AssertTableExists(connection, "Orders");
        AssertTableExists(connection, "Customers");

        // Cleanup
        Directory.Delete(migrationsPath, true);
    }

    [Fact]
    public void Init_WithMultipleFilesPerVersion_AppliesInOrder()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var migrationsPath = CreateTestMigrations(new[]
        {
            ("005_inventory", new[] 
            { 
                "001_create_warehouses.sql",
                "002_create_inventory.sql",
                "003_add_indexes.sql"
            })
        });

        // Act
        SqliteInit.SqliteInit.Init(connection, migrationsPath);

        // Assert
        var version = SqliteInit.SqliteInit.CheckUserVersion(connection);
        Assert.Equal(5, version);
        AssertTableExists(connection, "Warehouses");
        AssertTableExists(connection, "Inventory");

        // Cleanup
        Directory.Delete(migrationsPath, true);
    }

    [Fact]
    public void Init_SkipsAlreadyAppliedVersions()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var migrationsPath = CreateTestMigrations(new[]
        {
            ("006_shipping", new[] { "001_create_shipping.sql" }),
            ("007_payments", new[] { "001_create_payments.sql" })
        });

        // Set current version to 6
        SqliteInit.SqliteInit.SetUserVersion(connection, 6);

        // Act
        SqliteInit.SqliteInit.Init(connection, migrationsPath);

        // Assert
        var version = SqliteInit.SqliteInit.CheckUserVersion(connection);
        Assert.Equal(7, version);
        AssertTableExists(connection, "Payments");

        // Cleanup
        Directory.Delete(migrationsPath, true);
    }

    [Fact]
    public void Init_WithEmptyMigrationsFolder_DoesNothing()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var emptyPath = Path.Combine(Path.GetTempPath(), $"empty_{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyPath);
        var initialVersion = SqliteInit.SqliteInit.CheckUserVersion(connection);

        // Act
        SqliteInit.SqliteInit.Init(connection, emptyPath);

        // Assert
        var finalVersion = SqliteInit.SqliteInit.CheckUserVersion(connection);
        Assert.Equal(initialVersion, finalVersion);

        // Cleanup
        Directory.Delete(emptyPath);
    }

    #endregion

    #region Init Tests - Exception Scenarios

    [Fact]
    public void Init_WithNonExistentPath_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}");

        // Act & Assert
        var ex = Assert.Throws<DirectoryNotFoundException>(() =>
            SqliteInit.SqliteInit.Init(connection, nonExistentPath));
        
        Assert.Contains("Migrations path not found", ex.Message);
        Assert.Contains(nonExistentPath, ex.Message);
    }

    [Fact]
    public void Init_WithEmptyVersionFolder_ThrowsInvalidOperationException()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var migrationsPath = Path.Combine(Path.GetTempPath(), $"migrations_{Guid.NewGuid():N}");
        Directory.CreateDirectory(migrationsPath);
        var versionFolder = Path.Combine(migrationsPath, "008_empty");
        Directory.CreateDirectory(versionFolder);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SqliteInit.SqliteInit.Init(connection, migrationsPath));
        
        Assert.Contains("contains no migration files", ex.Message);
        Assert.Contains("Version: 8", ex.Message);

        // Cleanup
        Directory.Delete(migrationsPath, true);
    }

    [Fact]
    public void Init_WithInvalidSqlScript_ThrowsInvalidOperationException()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var migrationsPath = Path.Combine(Path.GetTempPath(), $"migrations_{Guid.NewGuid():N}");
        Directory.CreateDirectory(migrationsPath);
        var versionFolder = Path.Combine(migrationsPath, "009_bad_sql");
        Directory.CreateDirectory(versionFolder);
        File.WriteAllText(Path.Combine(versionFolder, "001_invalid.sql"), "INVALID SQL SYNTAX HERE;");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SqliteInit.SqliteInit.Init(connection, migrationsPath));
        
        Assert.Contains("Failed to apply migration script", ex.Message);
        Assert.Contains("001_invalid.sql", ex.Message);

        // Cleanup
        Directory.Delete(migrationsPath, true);
    }

    #endregion

    #region CheckUserVersion Tests

    [Fact]
    public void CheckUserVersion_OnNewDatabase_ReturnsZero()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        // Act
        var version = SqliteInit.SqliteInit.CheckUserVersion(connection);

        // Assert
        Assert.Equal(0, version);
    }

    [Fact]
    public void CheckUserVersion_AfterSetUserVersion_ReturnsCorrectValue()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        SqliteInit.SqliteInit.SetUserVersion(connection, 42);

        // Act
        var version = SqliteInit.SqliteInit.CheckUserVersion(connection);

        // Assert
        Assert.Equal(42, version);
    }

    #endregion

    #region SetUserVersion Tests

    [Fact]
    public void SetUserVersion_UpdatesVersionCorrectly()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        // Act
        SqliteInit.SqliteInit.SetUserVersion(connection, 100);

        // Assert
        var version = SqliteInit.SqliteInit.CheckUserVersion(connection);
        Assert.Equal(100, version);
    }

    [Fact]
    public void SetUserVersion_CanUpdateMultipleTimes()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        // Act & Assert
        SqliteInit.SqliteInit.SetUserVersion(connection, 1);
        Assert.Equal(1, SqliteInit.SqliteInit.CheckUserVersion(connection));

        SqliteInit.SqliteInit.SetUserVersion(connection, 5);
        Assert.Equal(5, SqliteInit.SqliteInit.CheckUserVersion(connection));

        SqliteInit.SqliteInit.SetUserVersion(connection, 10);
        Assert.Equal(10, SqliteInit.SqliteInit.CheckUserVersion(connection));
    }

    #endregion

    #region IdentifyMigrationsItems Tests

    [Fact]
    public void IdentifyMigrationsItems_WithDirectories_ReturnsSortedList()
    {
        // Arrange
        var basePath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(basePath);
        Directory.CreateDirectory(Path.Combine(basePath, "001_first"));
        Directory.CreateDirectory(Path.Combine(basePath, "003_third"));
        Directory.CreateDirectory(Path.Combine(basePath, "002_second"));
        Directory.CreateDirectory(Path.Combine(basePath, "not_a_migration"));

        // Act
        var result = SqliteInit.SqliteInit.IdentifyMigrationsItems(basePath, FilesystemType.Directory);

        // Assert
        // Fourth item didn't get picked up 
        Assert.Equal(3, result.Count);
        Assert.True(result.ContainsKey(1));
        Assert.True(result.ContainsKey(2));
        Assert.True(result.ContainsKey(3));
        Assert.EndsWith("001_first", result[1]);
        Assert.EndsWith("002_second", result[2]);
        Assert.EndsWith("003_third", result[3]);

        // Cleanup
        Directory.Delete(basePath, true);
    }

    [Fact]
    public void IdentifyMigrationsItems_WithFiles_ReturnsSortedList()
    {
        // Arrange
        var basePath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(basePath);
        File.WriteAllText(Path.Combine(basePath, "001_first.sql"), "");
        File.WriteAllText(Path.Combine(basePath, "003_third.sql"), "");
        File.WriteAllText(Path.Combine(basePath, "002_second.sql"), "");
        File.WriteAllText(Path.Combine(basePath, "readme.txt"), "");

        // Act
        var result = SqliteInit.SqliteInit.IdentifyMigrationsItems(basePath, FilesystemType.File);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.True(result.ContainsKey(1));
        Assert.True(result.ContainsKey(2));
        Assert.True(result.ContainsKey(3));

        // Cleanup
        Directory.Delete(basePath, true);
    }

    [Fact]
    public void IdentifyMigrationsItems_WithDuplicateIds_ThrowsInvalidDataException()
    {
        // Arrange
        var basePath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(basePath);
        Directory.CreateDirectory(Path.Combine(basePath, "001_first"));
        Directory.CreateDirectory(Path.Combine(basePath, "001_duplicate"));

        // Act & Assert
        var ex = Assert.Throws<InvalidDataException>(() =>
            SqliteInit.SqliteInit.IdentifyMigrationsItems(basePath, FilesystemType.Directory));
        
        Assert.Contains("Duplicate migration ID 1", ex.Message);
        Assert.Contains("001_first", ex.Message);
        Assert.Contains("001_duplicate", ex.Message);

        // Cleanup
        Directory.Delete(basePath, true);
    }

    [Fact]
    public void IdentifyMigrationsItems_SkipsNonNumericPrefixes()
    {
        // Arrange
        var basePath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(basePath);
        Directory.CreateDirectory(Path.Combine(basePath, "001_valid"));
        Directory.CreateDirectory(Path.Combine(basePath, "Beta_002_invalid"));
        Directory.CreateDirectory(Path.Combine(basePath, "v003_invalid"));
        Directory.CreateDirectory(Path.Combine(basePath, "no_number"));

        // Act
        var result = SqliteInit.SqliteInit.IdentifyMigrationsItems(basePath, FilesystemType.Directory);

        // Assert
        Assert.Single(result);
        Assert.True(result.ContainsKey(1));

        // Cleanup
        Directory.Delete(basePath, true);
    }

    #endregion

    #region ApplyMigrations Tests

    [Fact]
    public void ApplyMigrations_AppliesScriptsInOrder()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        
        var basePath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(basePath);
        File.WriteAllText(Path.Combine(basePath, "001_table1.sql"), 
            "CREATE TABLE TestTable1 (Id INTEGER PRIMARY KEY);");
        File.WriteAllText(Path.Combine(basePath, "002_table2.sql"), 
            "CREATE TABLE TestTable2 (Id INTEGER PRIMARY KEY);");

        var migrations = SqliteInit.SqliteInit.IdentifyMigrationsItems(basePath, FilesystemType.File);

        // Act
        SqliteInit.SqliteInit.ApplyMigrations(connection, migrations);

        // Assert
        AssertTableExists(connection, "TestTable1");
        AssertTableExists(connection, "TestTable2");

        // Cleanup
        Directory.Delete(basePath, true);
    }

    [Fact]
    public void ApplyMigrations_WithInvalidSql_ThrowsInvalidOperationException()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        
        var basePath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(basePath);
        File.WriteAllText(Path.Combine(basePath, "001_bad.sql"), "THIS IS NOT VALID SQL;");

        var migrations = SqliteInit.SqliteInit.IdentifyMigrationsItems(basePath, FilesystemType.File);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SqliteInit.SqliteInit.ApplyMigrations(connection, migrations));
        
        Assert.Contains("Failed to apply migration script", ex.Message);
        Assert.Contains("001_bad.sql", ex.Message);

        // Cleanup
        Directory.Delete(basePath, true);
    }

    #endregion

    #region Logging Tests

    [Fact]
    public void Init_WithLogging_InvokesLogCallback()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var migrationsPath = CreateTestMigrations(new[]
        {
            ("010_logging_test", new[] { "001_create_test_table.sql" })
        });

        var logMessages = new List<(LogLevel level, string message)>();
        Action<LogLevel, string> log = (level, msg) => logMessages.Add((level, msg));

        // Act
        SqliteInit.SqliteInit.Init(connection, migrationsPath, log);

        // Assert
        Assert.NotEmpty(logMessages);
        Assert.Contains(logMessages, m => m.level == LogLevel.Information && m.message.Contains("Starting SqliteInit"));
        Assert.Contains(logMessages, m => m.level == LogLevel.Information && m.message.Contains("completed successfully"));
        Assert.Contains(logMessages, m => m.level == LogLevel.Information && m.message.Contains("Current database version"));
        Assert.Contains(logMessages, m => m.level == LogLevel.Information && m.message.Contains("Applying migration version 10"));

        // Cleanup
        Directory.Delete(migrationsPath, true);
    }

    [Fact]
    public void Init_WithLogging_LogsDebugMessages()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var migrationsPath = CreateTestMigrations(new[]
        {
            ("011_debug_test", new[] { "001_script1.sql", "002_script2.sql" })
        });

        var logMessages = new List<(LogLevel level, string message)>();
        Action<LogLevel, string> log = (level, msg) => logMessages.Add((level, msg));

        // Act
        SqliteInit.SqliteInit.Init(connection, migrationsPath, log);

        // Assert
        var debugMessages = logMessages.Where(m => m.level == LogLevel.Debug).ToList();
        Assert.NotEmpty(debugMessages);
        Assert.Contains(debugMessages, m => m.message.Contains("Found") && m.message.Contains("migration folder"));
        Assert.Contains(debugMessages, m => m.message.Contains("Found") && m.message.Contains("migration file"));
        Assert.Contains(debugMessages, m => m.message.Contains("Executing migration script"));
        Assert.Contains(debugMessages, m => m.message.Contains("Successfully executed migration script"));

        // Cleanup
        Directory.Delete(migrationsPath, true);
    }

    [Fact]
    public void Init_WithLogging_LogsErrorsBeforeExceptions()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var migrationsPath = Path.Combine(Path.GetTempPath(), $"migrations_{Guid.NewGuid():N}");
        Directory.CreateDirectory(migrationsPath);
        var versionFolder = Path.Combine(migrationsPath, "012_error_test");
        Directory.CreateDirectory(versionFolder);
        File.WriteAllText(Path.Combine(versionFolder, "001_bad.sql"), "INVALID SQL;");

        var logMessages = new List<(LogLevel level, string message)>();
        Action<LogLevel, string> log = (level, msg) => logMessages.Add((level, msg));

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            SqliteInit.SqliteInit.Init(connection, migrationsPath, log));

        var errorMessages = logMessages.Where(m => m.level == LogLevel.Error).ToList();
        Assert.NotEmpty(errorMessages);
        Assert.Contains(errorMessages, m => m.message.Contains("Failed to apply migration script"));

        // Cleanup
        Directory.Delete(migrationsPath, true);
    }

    [Fact]
    public void Init_WithLogging_LogsSkippedVersions()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var migrationsPath = CreateTestMigrations(new[]
        {
            ("013_skip_test", new[] { "001_create_table.sql" }),
            ("014_apply_test", new[] { "001_create_table2.sql" })
        });

        SqliteInit.SqliteInit.SetUserVersion(connection, 13);

        var logMessages = new List<(LogLevel level, string message)>();
        Action<LogLevel, string> log = (level, msg) => logMessages.Add((level, msg));

        // Act
        SqliteInit.SqliteInit.Init(connection, migrationsPath, log);

        // Assert
        Assert.Contains(logMessages, m => m.level == LogLevel.Debug && m.message.Contains("Skipping version 13"));
        Assert.Contains(logMessages, m => m.level == LogLevel.Information && m.message.Contains("Applying migration version 14"));

        // Cleanup
        Directory.Delete(migrationsPath, true);
    }

    [Fact]
    public void Init_WithLogging_LogsWhenNoMigrationsFound()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var emptyPath = Path.Combine(Path.GetTempPath(), $"empty_{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyPath);

        var logMessages = new List<(LogLevel level, string message)>();
        Action<LogLevel, string> log = (level, msg) => logMessages.Add((level, msg));

        // Act
        SqliteInit.SqliteInit.Init(connection, emptyPath, log);

        // Assert
        Assert.Contains(logMessages, m => m.level == LogLevel.Information && m.message.Contains("No migration folders found"));

        // Cleanup
        Directory.Delete(emptyPath);
    }

    [Fact]
    public void Init_WithoutLogging_DoesNotThrowNullReferenceException()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var migrationsPath = CreateTestMigrations(new[]
        {
            ("015_no_logging", new[] { "001_create_table.sql" })
        });

        // Act - should not throw
        SqliteInit.SqliteInit.Init(connection, migrationsPath, null);

        // Assert
        var version = SqliteInit.SqliteInit.CheckUserVersion(connection);
        Assert.Equal(15, version);

        // Cleanup
        Directory.Delete(migrationsPath, true);
    }

    [Fact]
    public void ApplyMigrations_WithLogging_LogsEachScript()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        
        var basePath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(basePath);
        File.WriteAllText(Path.Combine(basePath, "001_table1.sql"), 
            "CREATE TABLE LogTest1 (Id INTEGER PRIMARY KEY);");
        File.WriteAllText(Path.Combine(basePath, "002_table2.sql"), 
            "CREATE TABLE LogTest2 (Id INTEGER PRIMARY KEY);");

        var migrations = SqliteInit.SqliteInit.IdentifyMigrationsItems(basePath, FilesystemType.File);
        var logMessages = new List<(LogLevel level, string message)>();
        Action<LogLevel, string> log = (level, msg) => logMessages.Add((level, msg));

        // Act
        SqliteInit.SqliteInit.ApplyMigrations(connection, migrations, log);

        // Assert
        Assert.Contains(logMessages, m => m.level == LogLevel.Debug && m.message.Contains("Executing migration script: 001_table1.sql"));
        Assert.Contains(logMessages, m => m.level == LogLevel.Debug && m.message.Contains("Successfully executed migration script: 001_table1.sql"));
        Assert.Contains(logMessages, m => m.level == LogLevel.Debug && m.message.Contains("Executing migration script: 002_table2.sql"));
        Assert.Contains(logMessages, m => m.level == LogLevel.Debug && m.message.Contains("Successfully executed migration script: 002_table2.sql"));

        // Cleanup
        Directory.Delete(basePath, true);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a test migrations directory structure with the specified versions and files.
    /// </summary>
    /// <param name="versions">Array of tuples containing (versionFolderName, sqlFileNames[])</param>
    /// <returns>Path to the created migrations directory</returns>
    private string CreateTestMigrations((string folderName, string[] sqlFiles)[] versions)
    {
        var basePath = Path.Combine(Path.GetTempPath(), $"migrations_{Guid.NewGuid():N}");
        Directory.CreateDirectory(basePath);

        foreach (var (folderName, sqlFiles) in versions)
        {
            var versionPath = Path.Combine(basePath, folderName);
            Directory.CreateDirectory(versionPath);

            foreach (var sqlFile in sqlFiles)
            {
                var tableName = ExtractTableNameFromFileName(sqlFile);
                var sql = GenerateSampleSql(tableName);
                File.WriteAllText(Path.Combine(versionPath, sqlFile), sql);
            }
        }

        return basePath;
    }

    /// <summary>
    /// Extracts a table name from a migration file name.
    /// </summary>
    private string ExtractTableNameFromFileName(string fileName)
    {
        // Remove number prefix and extension, capitalize
        var name = fileName.Split('_').Last().Replace(".sql", "");
        return char.ToUpper(name[0]) + name.Substring(1);
    }

    /// <summary>
    /// Generates sample SQL for creating tables based on the table name.
    /// </summary>
    private string GenerateSampleSql(string tableName)
    {
        return tableName switch
        {
            "Products" => @"
                CREATE TABLE Products (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Price REAL NOT NULL,
                    CategoryId INTEGER
                );",
            "Categories" => @"
                CREATE TABLE Categories (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Description TEXT
                );",
            "Orders" => @"
                CREATE TABLE Orders (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CustomerId INTEGER NOT NULL,
                    OrderDate TEXT NOT NULL,
                    TotalAmount REAL NOT NULL
                );",
            "Customers" => @"
                CREATE TABLE Customers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FirstName TEXT NOT NULL,
                    LastName TEXT NOT NULL,
                    Email TEXT UNIQUE NOT NULL
                );",
            "Warehouses" => @"
                CREATE TABLE Warehouses (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Location TEXT NOT NULL
                );",
            "Inventory" => @"
                CREATE TABLE Inventory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProductId INTEGER NOT NULL,
                    WarehouseId INTEGER NOT NULL,
                    Quantity INTEGER NOT NULL
                );",
            "Shipping" => @"
                CREATE TABLE Shipping (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    OrderId INTEGER NOT NULL,
                    ShippingDate TEXT,
                    TrackingNumber TEXT
                );",
            "Payments" => @"
                CREATE TABLE Payments (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    OrderId INTEGER NOT NULL,
                    PaymentDate TEXT NOT NULL,
                    Amount REAL NOT NULL,
                    PaymentMethod TEXT NOT NULL
                );",
            _ => $@"
                CREATE TABLE [{tableName}] (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL
                );"
        };
    }

    /// <summary>
    /// Asserts that a table exists in the database.
    /// </summary>
    private void AssertTableExists(SqliteConnection connection, string tableName)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@tableName;";
        cmd.Parameters.AddWithValue("@tableName", tableName);
        var result = cmd.ExecuteScalar();
        Assert.NotNull(result);
        Assert.Equal(tableName, result);
    }

    #endregion
}
