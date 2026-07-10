using Npgsql;

namespace MainHub.Api.Data;

internal static class NpgsqlReaderExtensions
{
    public static string? GetNullableString(this NpgsqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static DateTime? GetNullableDateTime(this NpgsqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTime>(ordinal);

    public static int? GetNullableInt32(this NpgsqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    public static Guid? GetNullableGuid(this NpgsqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);

    public static TEnum GetEnum<TEnum>(this NpgsqlDataReader reader, int ordinal) where TEnum : struct, Enum
        => Enum.Parse<TEnum>(reader.GetString(ordinal));

    public static object NullableParam(object? value) => value ?? DBNull.Value;
}
