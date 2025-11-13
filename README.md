# SqliteInit

Manage Sqlite schema migrations with a simple folder structure. One dependency on `Microsoft.Data.Sqlite`.

Folders that start with numbers are schema versions.

Files within that folder that start with numbers will be executed as SQL sequentially (sorted by number ascending). File extension is not considered.

Folders/Files that do not start with numbers will be skipped. In other words, we only include it if it starts with `^\d+`

In your app startup code, call `SqliteInit.Init()`, passing in the connection or connection string and path to your migrations code. After applying migrations, it will automatically update `PRAGMA user_version` to match the number of the folder. Future migrations check the version number against the migrations folder numbers and upgade if required. 

SqliteInit has good error handling and logging. To log, pass in an optional func. Note that, to avoid taking a dependency on `Microsoft.Extensions.Logging`, you must cast from `SqliteInit.LogLevel` to `Microsoft.Extensions.Logging.LogLevel`. If your host application is not using ILogger, then you may do what you wish with LogLevel in your func. 

```
var logger = app.Services.GetRequiredService<ILogger<Program>>();
SqliteInit.SqliteInit.Init(connectionString, schemaDir, (ll, s) => logger.Log((LogLevel)ll, s) );
```

Example folder structure:

```
-- 📂 Migrations
  -- 📂 001 Initial Tables
    -- 📄 001 Create Tables.sql
    -- 📄 002 Create Indexes.sql
    -- 📄 003 Insert Test Data.sql
  -- 📂 002 Alter Schema
    -- 📄 README.md # File ignored, does not start with digits
    -- 📄 001 Alter Schema.sql
  -- 📂 WIP Version 3 # Folder ignored, does not start with digits
    -- 📄 001 draft.sql # Ignored 
```

FAQ:

- *Why is this limited to Sqlite and not other databases?* Current version is stored in a Sqlite-specific feature, `PRAGMA user_version`. While it would be straightforward to add a user-version table and keep track there, I've never had a need to do it. 