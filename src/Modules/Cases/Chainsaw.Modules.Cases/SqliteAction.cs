namespace Chainsaw.Modules.Cases;

/// <summary>SQLite's authorizer action codes (<c>sqlite3.h</c>), the ones <see cref="SqlGuard"/> tells apart.</summary>
internal enum SqliteAction
{
    /// <summary>CREATE INDEX; the details are the index and the table.</summary>
    CreateIndex = 1,

    /// <summary>CREATE TABLE; the first detail is the table.</summary>
    CreateTable = 2,

    /// <summary>CREATE TEMP INDEX.</summary>
    CreateTempIndex = 3,

    /// <summary>CREATE TEMP TABLE.</summary>
    CreateTempTable = 4,

    /// <summary>CREATE TEMP TRIGGER; the details are the trigger and the table.</summary>
    CreateTempTrigger = 5,

    /// <summary>CREATE TEMP VIEW.</summary>
    CreateTempView = 6,

    /// <summary>CREATE TRIGGER; the details are the trigger and the table.</summary>
    CreateTrigger = 7,

    /// <summary>CREATE VIEW.</summary>
    CreateView = 8,

    /// <summary>DELETE; the first detail is the table.</summary>
    Delete = 9,

    /// <summary>DROP INDEX.</summary>
    DropIndex = 10,

    /// <summary>DROP TABLE; the first detail is the table.</summary>
    DropTable = 11,

    /// <summary>DROP TEMP INDEX.</summary>
    DropTempIndex = 12,

    /// <summary>DROP TEMP TABLE.</summary>
    DropTempTable = 13,

    /// <summary>DROP TEMP TRIGGER; the details are the trigger and the table.</summary>
    DropTempTrigger = 14,

    /// <summary>DROP TEMP VIEW.</summary>
    DropTempView = 15,

    /// <summary>DROP TRIGGER; the details are the trigger and the table.</summary>
    DropTrigger = 16,

    /// <summary>DROP VIEW.</summary>
    DropView = 17,

    /// <summary>INSERT; the first detail is the table.</summary>
    Insert = 18,

    /// <summary>PRAGMA; the details are the setting and its argument.</summary>
    Pragma = 19,

    /// <summary>Reading a column; the details are the table and the column.</summary>
    Read = 20,

    /// <summary>SELECT.</summary>
    Select = 21,

    /// <summary>BEGIN, COMMIT or ROLLBACK.</summary>
    Transaction = 22,

    /// <summary>UPDATE of a column; the details are the table and the column.</summary>
    Update = 23,

    /// <summary>ATTACH, also used by VACUUM.</summary>
    Attach = 24,

    /// <summary>DETACH.</summary>
    Detach = 25,

    /// <summary>ALTER TABLE; the details are the database and the table.</summary>
    AlterTable = 26,

    /// <summary>REINDEX.</summary>
    Reindex = 27,

    /// <summary>ANALYZE.</summary>
    Analyze = 28,

    /// <summary>CREATE VIRTUAL TABLE.</summary>
    CreateVirtualTable = 29,

    /// <summary>DROP VIRTUAL TABLE.</summary>
    DropVirtualTable = 30,

    /// <summary>Calling a function; the first detail is empty and the second is the function's name.</summary>
    Function = 31,

    /// <summary>SAVEPOINT, RELEASE or ROLLBACK TO.</summary>
    Savepoint = 32,

    /// <summary>A recursive common table expression.</summary>
    Recursive = 33,
}
