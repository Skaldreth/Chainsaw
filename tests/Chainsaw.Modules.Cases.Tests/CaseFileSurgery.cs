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
        var bytes = File.ReadAllBytes(path);
        using var stream = new MemoryStream(bytes);
        var header = CaseContainer.ReadHeader(stream);
        var databaseStart = (int)stream.Position;

        var wrappingKey = new byte[32];
        var fileKey = new byte[32];
        var databaseKey = new byte[32];
        fixture.PurposeKeys.DeriveKey(CaseCrypto.EnvelopePurpose, wrappingKey);
        Assert.True(KeyEnvelopes.TryOpenOperator(header, wrappingKey, fileKey));
        CaseCrypto.DeriveDatabaseKey(fileKey, header.CaseId, databaseKey);

        var temporary = path + ".surgery";
        File.WriteAllBytes(temporary, bytes[databaseStart..]);
        try
        {
            using (var connection = new SqliteConnection($"Data Source={temporary};Pooling=False"))
            {
                connection.Open();
                Assert.Equal(0, raw.sqlite3_key(connection.Handle, CaseCrypto.ToRawKeyLiteral(databaseKey)));
                change(connection);
            }

            File.WriteAllBytes(path, [.. bytes[..databaseStart], .. File.ReadAllBytes(temporary)]);
        }
        finally
        {
            File.Delete(temporary);
            CryptographicOperations.ZeroMemory(fileKey);
        }
    }

    public static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
