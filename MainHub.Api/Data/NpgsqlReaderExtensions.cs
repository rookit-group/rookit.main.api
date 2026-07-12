using Npgsql;

namespace MainHub.Api.Data;

// Small helpers used by every repository to read query results and pass
// parameters, since Npgsql (the raw Postgres driver) has no automatic
// object mapping the way MongoDB.Driver does.
//
// With Mongo, `collection.Find(...)` gives you back a fully-typed
// UserEntity/VehicleEntity/etc. automatically, including nulls, because the
// driver deserializes BSON documents straight into your C# class.
// With Npgsql, a query just returns an NpgsqlDataReader - a cursor over rows
// and columns identified by *position* (ordinal), not by property name. You
// have to read each column out by hand and build the object yourself (see
// the `Map(...)` methods in each repository). These extensions exist so that
// "give me column N, or null if the DB has NULL there" isn't repeated
// everywhere.
internal static class NpgsqlReaderExtensions
{
    // reader.GetString(ordinal) throws if the column is SQL NULL - IsDBNull
    // is how you check for that first, similar in spirit to checking a BSON
    // field is BsonNull before reading it.
    public static string? GetNullableString(this NpgsqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static DateTime? GetNullableDateTime(this NpgsqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTime>(ordinal);

    public static int? GetNullableInt32(this NpgsqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    public static Guid? GetNullableGuid(this NpgsqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);

    // Enums are stored as plain text columns (e.g. "Petrol", "Awd") instead of
    // a Mongo BsonRepresentation(BsonType.String) attribute on the property.
    // Reading one back means parsing that text into the C# enum by hand.
    public static TEnum GetEnum<TEnum>(this NpgsqlDataReader reader, int ordinal) where TEnum : struct, Enum
        => Enum.Parse<TEnum>(reader.GetString(ordinal));

    // Npgsql command parameters don't understand C# `null` directly - you
    // must pass DBNull.Value instead, or the driver throws. This wraps that
    // so call sites can just say NullableParam(someNullableField).
    public static object NullableParam(object? value) => value ?? DBNull.Value;
}
