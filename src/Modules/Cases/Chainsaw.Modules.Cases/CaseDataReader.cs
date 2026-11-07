using System.Collections;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// The reader another module gets from a <see cref="CaseCommand"/>. It hides SQLite's own reader, whose statement
/// handle would lead back to the connection, and refuses to read after the module's work has returned.
/// </summary>
/// <param name="inner">SQLite's reader.</param>
/// <param name="isActive">Tells whether the work is still running.</param>
internal sealed class CaseDataReader(DbDataReader inner, Func<bool> isActive) : DbDataReader
{
    /// <inheritdoc />
    public override int Depth => inner.Depth;

    /// <inheritdoc />
    public override int FieldCount => inner.FieldCount;

    /// <inheritdoc />
    public override bool HasRows => inner.HasRows;

    /// <inheritdoc />
    public override bool IsClosed => inner.IsClosed;

    /// <inheritdoc />
    public override int RecordsAffected => inner.RecordsAffected;

    /// <inheritdoc />
    public override object this[int ordinal] => GetValue(ordinal);

    /// <inheritdoc />
    public override object this[string name] => GetValue(GetOrdinal(name));

    /// <inheritdoc />
    public override bool GetBoolean(int ordinal) => Active().GetBoolean(ordinal);

    /// <inheritdoc />
    public override byte GetByte(int ordinal) => Active().GetByte(ordinal);

    /// <inheritdoc />
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) =>
        Active().GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);

    /// <inheritdoc />
    public override char GetChar(int ordinal) => Active().GetChar(ordinal);

    /// <inheritdoc />
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) =>
        Active().GetChars(ordinal, dataOffset, buffer, bufferOffset, length);

    /// <inheritdoc />
    public override string GetDataTypeName(int ordinal) => Active().GetDataTypeName(ordinal);

    /// <inheritdoc />
    public override DateTime GetDateTime(int ordinal) => Active().GetDateTime(ordinal);

    /// <inheritdoc />
    public override decimal GetDecimal(int ordinal) => Active().GetDecimal(ordinal);

    /// <inheritdoc />
    public override double GetDouble(int ordinal) => Active().GetDouble(ordinal);

    /// <inheritdoc />
    [return: DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)]
    public override Type GetFieldType(int ordinal) => Active().GetFieldType(ordinal);

    /// <inheritdoc />
    public override float GetFloat(int ordinal) => Active().GetFloat(ordinal);

    /// <inheritdoc />
    public override Guid GetGuid(int ordinal) => Active().GetGuid(ordinal);

    /// <inheritdoc />
    public override short GetInt16(int ordinal) => Active().GetInt16(ordinal);

    /// <inheritdoc />
    public override int GetInt32(int ordinal) => Active().GetInt32(ordinal);

    /// <inheritdoc />
    public override long GetInt64(int ordinal) => Active().GetInt64(ordinal);

    /// <inheritdoc />
    public override string GetName(int ordinal) => Active().GetName(ordinal);

    /// <inheritdoc />
    public override int GetOrdinal(string name) => Active().GetOrdinal(name);

    /// <inheritdoc />
    public override string GetString(int ordinal) => Active().GetString(ordinal);

    /// <inheritdoc />
    public override object GetValue(int ordinal) => Active().GetValue(ordinal);

    /// <inheritdoc />
    public override int GetValues(object[] values) => Active().GetValues(values);

    /// <inheritdoc />
    public override bool IsDBNull(int ordinal) => Active().IsDBNull(ordinal);

    /// <inheritdoc />
    public override bool NextResult() => Active().NextResult();

    /// <inheritdoc />
    public override bool Read() => Active().Read();

    /// <inheritdoc />
    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Gives SQLite's reader while the work runs.</summary>
    /// <returns>The reader.</returns>
    /// <exception cref="InvalidOperationException">The work has returned.</exception>
    private DbDataReader Active() =>
        isActive() ? inner : throw new InvalidOperationException("The reader belongs to a read or write that has ended.");
}
