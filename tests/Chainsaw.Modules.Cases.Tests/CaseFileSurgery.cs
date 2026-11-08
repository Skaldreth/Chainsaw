using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Chainsaw.Modules.Cases.Tests;

/// <summary>
/// Opens a closed case file's database with SQLite and the key, past the module's API, guard and triggers, the way
/// someone with the key and another program could, and writes the changed database back into the file.
/// </summary>
internal static class CaseFileSurgery
{
    public static void Change(CaseFixture fixture, string caseNumber, Action<SqliteConnection> change)
    {
        Batteries_V2.Init();
        var path = fixture.CaseFile(caseNumber);
        var (header, database) = Split(File.ReadAllBytes(path));
        var fileKey = OperatorFileKey(fixture, header);
        var databaseKey = new byte[32];
        CaseCrypto.DeriveDatabaseKey(fileKey, header.CaseId, databaseKey);

        var temporary = path + ".surgery";
        File.WriteAllBytes(temporary, database);
        try
        {
            using (var connection = new SqliteConnection($"Data Source={temporary};Pooling=False"))
            {
                connection.Open();
                Assert.Equal(0, raw.sqlite3_key(connection.Handle, CaseCrypto.ToRawKeyLiteral(databaseKey)));
                change(connection);
            }

            Write(path, header, File.ReadAllBytes(temporary), fileKey);
        }
        finally
        {
            File.Delete(temporary);
            CryptographicOperations.ZeroMemory(fileKey);
        }
    }

    /// <summary>
    /// Rewrites a closed case file's header with the file key from this fixture's vault, sealing the file again,
    /// the way a later build or someone with the key could.
    /// </summary>
    public static void RewriteHeader(CaseFixture fixture, string path, Func<CaseHeader, CaseHeader> change)
    {
        var (header, database) = Split(File.ReadAllBytes(path));
        var fileKey = OperatorFileKey(fixture, header);
        Write(path, change(header), database, fileKey);
    }

    /// <summary>
    /// Rewrites a closed case file's header without the file key, leaving the old seal in place, the way someone
    /// without the key could.
    /// </summary>
    public static void RewriteHeaderWithoutKey(string path, Func<CaseHeader, CaseHeader> change)
    {
        var bytes = File.ReadAllBytes(path);
        using var input = new MemoryStream(bytes);
        var header = CaseContainer.ReadHeader(input);
        using var output = new MemoryStream();
        CaseContainer.WriteHeader(output, change(header));
        output.Write(bytes.AsSpan((int)input.Position));
        File.WriteAllBytes(path, output.ToArray());
    }

    /// <summary>Splits a case file into its header and its database, without the seal.</summary>
    public static (CaseHeader Header, byte[] Database) Split(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var header = CaseContainer.ReadHeader(stream);
        var start = (int)stream.Position;
        return (header, bytes[start..^CaseFileSeal.TrailerLength]);
    }

    /// <summary>Writes a whole case file from a header and a database, sealed with the file key.</summary>
    public static void Write(string path, CaseHeader header, byte[] database, byte[] fileKey)
    {
        using var input = new MemoryStream(database);
        using var output = new MemoryStream();
        CaseFiles.Write(output, header, input, fileKey, hash: null);
        File.WriteAllBytes(path, output.ToArray());
    }

    /// <summary>Opens the file key with the fixture's vault.</summary>
    public static byte[] OperatorFileKey(CaseFixture fixture, CaseHeader header)
    {
        var wrappingKey = new byte[32];
        var fileKey = new byte[32];
        fixture.PurposeKeys.DeriveKey(CaseCrypto.EnvelopePurpose, wrappingKey);
        Assert.True(KeyEnvelopes.TryOpenOperator(header, wrappingKey, fileKey));
        return fileKey;
    }

    public static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
