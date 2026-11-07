namespace Chainsaw.Modules.Cases;

/// <summary>Who is running SQL on a case's connection right now, which decides what <see cref="SqlGuard"/> allows.</summary>
internal enum GuardMode
{
    /// <summary>
    /// Nobody: the mode between operations, so that a command kept past its work, or any other stray statement, is
    /// refused.
    /// </summary>
    Idle,

    /// <summary>The Cases module's own code: everything except changing or removing sealed records.</summary>
    Internal,

    /// <summary>
    /// Another module's schema step: its own tables may be created, changed and dropped, but the protected tables,
    /// transactions, attached databases and settings may not be touched.
    /// </summary>
    Migrating,

    /// <summary>
    /// Another module's work in a write transaction: rows in its own tables only, no schema changes and no savepoints
    /// (a rolled-back savepoint would take back sealed records the work has already been given).
    /// </summary>
    Writing,

    /// <summary>Another module's work in a read transaction: only reading.</summary>
    Reading,
}
