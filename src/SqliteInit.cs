using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace SqliteInit;

/// <summary>
/// A basic schema management library. Look for folders that start with digits - that's the scema version number. 
/// Then inside each folder look for files that start with numbers - those are run sequentially. 
/// Call .Init() to apply migrations as required. 
/// </summary>
public class SqliteInit
{
    /// <summary>
    /// Creates and then disposes the SqliteConnection
    /// </summary>
    /// <param name="connectionString"></param>
    /// <param name="migrationsPath"></param>
    /// <param name="log">Optional logging callback</param>
    public static void Init(string connectionString, string migrationsPath, Action<LogLevel, string>? log = null)
    {
        using (var connection = new SqliteConnection(connectionString))
        {
            Init(connection, migrationsPath, log);
        }
    }

    public static void Init(SqliteConnection connection, string migrationsPath, Action<LogLevel, string>? log = null)
    {
        log?.Invoke(LogLevel.Information, $"Starting SqliteInit for migrations path: {migrationsPath}");

        if (!Directory.Exists(migrationsPath))
        {
            log?.Invoke(LogLevel.Error, $"Migrations path not found: {migrationsPath}");
            throw new DirectoryNotFoundException(
                $"Migrations path not found: {migrationsPath}");
        }

        SortedDictionary<int, string> migrationsFolders;
        try
        {
            migrationsFolders = IdentifyMigrationsItems(migrationsPath, FilesystemType.Directory);
            log?.Invoke(LogLevel.Debug, $"Found {migrationsFolders.Count} migration folder(s)");
        }
        catch (Exception ex)
        {
            log?.Invoke(LogLevel.Error, $"Failed to identify migration folders in '{migrationsPath}'. Error: {ex.Message}");
            throw new InvalidOperationException(
                $"Failed to identify migration folders in '{migrationsPath}'. Error: {ex.Message}", ex);
        }

        if (migrationsFolders.Count == 0)
        {
            log?.Invoke(LogLevel.Information, "No migration folders found. Nothing to apply.");
            // No migrations to apply - this might be intentional, so just return
            return;
        }

        connection.Open();
        //Check current user version of database
        var currentVersion = CheckUserVersion(connection);
        log?.Invoke(LogLevel.Information, $"Current database version: {currentVersion}");

        foreach (var folder in migrationsFolders)
        {
            if (currentVersion >= folder.Key)
            {
                log?.Invoke(LogLevel.Debug, $"Skipping version {folder.Key} (already applied)");
                continue;
            }

            log?.Invoke(LogLevel.Information, $"Applying migration version {folder.Key} from folder: {Path.GetFileName(folder.Value)}");

            //Identify & run sorted upgrade scripts
            SortedDictionary<int, string> migrationFiles;
            try
            {
                migrationFiles = IdentifyMigrationsItems(folder.Value, FilesystemType.File);
                log?.Invoke(LogLevel.Debug, $"Found {migrationFiles.Count} migration file(s) in version {folder.Key}");
            }
            catch (Exception ex)
            {
                log?.Invoke(LogLevel.Error, $"Failed to identify migration files in folder '{Path.GetFileName(folder.Value)}' (Version: {folder.Key})");
                throw new InvalidOperationException(
                    $"Failed to identify migration files in folder '{Path.GetFileName(folder.Value)}' " +
                    $"(Version: {folder.Key}, Path: {folder.Value}). Error: {ex.Message}", ex);
            }

            if (migrationFiles.Count == 0)
            {
                log?.Invoke(LogLevel.Error, $"Migration folder '{Path.GetFileName(folder.Value)}' (Version: {folder.Key}) contains no migration files");
                throw new InvalidOperationException(
                    $"Migration folder '{Path.GetFileName(folder.Value)}' (Version: {folder.Key}) " +
                    $"contains no migration files starting with digits. Each version folder must contain at least one numbered migration file.");
            }

            ApplyMigrations(connection, migrationFiles, log);

            //Update user version
            SetUserVersion(connection, folder.Key);
            log?.Invoke(LogLevel.Information, $"Successfully applied migration version {folder.Key}");
        }

        log?.Invoke(LogLevel.Information, "SqliteInit completed successfully");
    }

    /// <summary>
    /// Run a sequence of migration scripts
    /// </summary>
    public static void ApplyMigrations(SqliteConnection connection, SortedDictionary<int, string> orderedMigrationScripts, Action<LogLevel, string>? log = null)
    {
        foreach (var migrationScriptPath in orderedMigrationScripts)
        {
            try
            {
                log?.Invoke(LogLevel.Debug, $"Executing migration script: {Path.GetFileName(migrationScriptPath.Value)}");
                var migrationContents = File.ReadAllText(migrationScriptPath.Value);
                var cmd = connection.CreateCommand();
                cmd.CommandText = migrationContents;
                cmd.ExecuteNonQuery();
                log?.Invoke(LogLevel.Debug, $"Successfully executed migration script: {Path.GetFileName(migrationScriptPath.Value)}");
            }
            catch (Exception ex)
            {
                log?.Invoke(LogLevel.Error, $"Failed to apply migration script '{Path.GetFileName(migrationScriptPath.Value)}' (ID: {migrationScriptPath.Key})");
                throw new InvalidOperationException(
                    $"Failed to apply migration script '{Path.GetFileName(migrationScriptPath.Value)}' " +
                    $"(ID: {migrationScriptPath.Key}, Path: {migrationScriptPath.Value}). " +
                    $"Error: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Get the current schema version (PRAGMA user_version)
    /// </summary>
    public static long CheckUserVersion(SqliteConnection connection)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>
    /// Update PRAGMA user_version
    /// </summary>
    public static void SetUserVersion(SqliteConnection connection, int newVersion)
    {
        // Parameters not supported in Pragma statements https://docs.microsoft.com/en-us/dotnet/standard/data/sqlite/encryption?tabs=netcore-cli
        var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version = " + newVersion.ToString();
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Look for subitems that start with integers ^(\d+), e.g. `001 - Create Table foo.sql` and return them sorted numerically.
    /// Items that do not parse, e.g., `Beta - 002 - Add indexes.sql` will be skipped. 
    /// </summary>
    /// <param name="migrationsPath">Folder to inspect</param>
    /// <param name="dirOrFile">Look at Directory or Normal files - pick one</param>
    /// <returns>Numerically sorted list of full paths to item</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if dirOrNormal is neither Directory nor Normal.</exception> 
    public static SortedDictionary<int, string> IdentifyMigrationsItems(string migrationsPath, FilesystemType dirOrFile)
    {
        var startsWithNumber = new Regex(@"^(\d+)", RegexOptions.Compiled);
        var sortedMigrations = new SortedDictionary<int, string>();
        // Look at each subfolder, attempt to parse it - it needs to start with a nonzero positive integer
        var migrationsDirectory = new DirectoryInfo(migrationsPath);
        IEnumerable<FileSystemInfo>? subItems = [];
        if (dirOrFile == FilesystemType.Directory)
        {
            subItems = migrationsDirectory.EnumerateDirectories();
        }
        else if (dirOrFile == FilesystemType.File)
        {
            subItems = migrationsDirectory.GetFiles();
        }

        foreach (var subItem in subItems)
        {
            int version;
            var match = startsWithNumber.Match(subItem.Name);
            if (match.Success)
            {
                if (Int32.TryParse(match.Groups[1].Value, out version))
                {
                    if (sortedMigrations.ContainsKey(version))
                    {
                        var existingItem = sortedMigrations[version];
                        var itemType = dirOrFile == FilesystemType.Directory ? "folder" : "file";
                        throw new InvalidDataException(
                            $"Duplicate migration ID {version} detected in {migrationsPath}. " +
                            $"Conflicting {itemType}s: '{Path.GetFileName(existingItem)}' and '{subItem.Name}'. " +
                            $"Each migration must have a unique numeric prefix.");
                    }

                    sortedMigrations.Add(version, subItem.FullName);
                }
            }
        }

        return sortedMigrations;
    }

}

public enum FilesystemType
{
    Directory,
    File
}