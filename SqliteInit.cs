using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
    public static void Init(string connectionString, string migrationsPath)
    {
        var migrationsFolders = IdentifyMigrationsItems(migrationsPath, FileAttributes.Directory);
        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            //Check current user version of database
            var currentVersion = CheckUserVersion(connection);
            foreach (var folder in migrationsFolders)
            {
                if (currentVersion >= folder.Key) continue;
                //Identify & run sorted upgrade scripts
                var migrationFiles = IdentifyMigrationsItems(folder.Value, FileAttributes.Normal);
                ApplyMigrations(connection, migrationFiles);

                //Update user version
                SetUserVersion(connection, folder.Key);
            }
        }
    }

    /// <summary>
    /// Run a sequence of migration scripts
    /// </summary>
    public static void ApplyMigrations(SqliteConnection connection, SortedDictionary<int, string> orderedMigrationScripts)
    {
        foreach (var migrationScriptPath in orderedMigrationScripts)
        {
            var migrationContents = File.ReadAllText(migrationScriptPath.Value);
            var cmd = connection.CreateCommand();
            cmd.CommandText = migrationContents;
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Get the current schema version (PRAGMA user_version)
    /// </summary>
    public static long CheckUserVersion(SqliteConnection connection)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return (long) cmd.ExecuteScalar()!;
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
    /// <param name="dirOrNormal">Look at Directory or Normal files - pick one</param>
    /// <returns>Numerically sorted list of full paths to item</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if dirOrNormal is neither Directory nor Normal.</exception> 
    public static SortedDictionary<int, string> IdentifyMigrationsItems(string migrationsPath, FileAttributes dirOrNormal)
    {
        var startsWithNumber = new Regex(@"^(\d+)", RegexOptions.Compiled);
        var sortedMigrations = new SortedDictionary<int, string>();
        // Look at each subfolder, attempt to parse it - it needs to start with a nonzero positive integer
        var migrationsDirectory = new DirectoryInfo(migrationsPath);
        IEnumerable<FileSystemInfo>? subItems = null;
        if(dirOrNormal == FileAttributes.Directory)
        {
            subItems = migrationsDirectory.EnumerateDirectories();
        }
        else if (dirOrNormal == FileAttributes.Normal)
        {
            subItems = migrationsDirectory.GetFiles();
        }
        else
        {
            throw new ArgumentOutOfRangeException("Only FileAttributes Directory or Normal (for files) are supported.");
        }
        foreach(var subItem in subItems)
        {
            int version;
            var match = startsWithNumber.Match(subItem.Name);
            if (match.Success)
            {
                if (Int32.TryParse(match.Groups[1].Value, out version))
                {
                    if(sortedMigrations.ContainsKey(version)) 
                        throw new InvalidDataException("Duplicate migration ID detected - only positive integers are supported.");
                    
                    sortedMigrations.Add(version, subItem.FullName);
                    //TODO log success 
                }
            }
        }

        return sortedMigrations;
    }

}