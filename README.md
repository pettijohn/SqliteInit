# SqliteInit

Manage Sqlite schema migrations with a simple folder structure. One dependency on `Microsoft.Data.Sqlite`.

Folders that start with numbers are schema versions.

Files within that folder that start with numbers will be executed as SQL sequentially (sorted by number ascending). File extension is not considered.

Folders/Files that do not start with numbers will be skipped. In other words, we only include it if it starts with `^\d+`


In your app startup code, call `SqliteInit.Init()`, passing in the connection or connection string and path to your migrations code. After applying migrations, it will automatically update `PRAGMA user_version` to match the number of the folder. Future migrations check the version number against the migrations folder numbers and upgade if required. 

Example:

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