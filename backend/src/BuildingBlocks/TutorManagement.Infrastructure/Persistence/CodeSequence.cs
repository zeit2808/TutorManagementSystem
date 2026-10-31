using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Infrastructure.Persistence;

/// <summary>
/// The one way every module hands out automatic codes (<see cref="EntityCode"/>). Each module keeps a
/// <c>{schema}.code_counters</c> table (created by <see cref="CreateCountersTableSql"/> in its migrations); a row per
/// center and kind is bumped atomically, so concurrent creates never get the same number. Inside an open
/// transaction the row stays locked until commit, and a rolled-back number is simply handed out again.
/// </summary>
public static partial class CodeSequence
{
    private const int MaxAttempts = 50;

    /// <param name="isTaken">
    /// Whether the entity table already holds the code — older rows may carry hand-typed codes that happen to match
    /// the pattern; those numbers are skipped.
    /// </param>
    public static async Task<string> NextAsync(DatabaseFacade database, string schema, Guid centerId, CodeKind kind,
        Func<string, CancellationToken, Task<bool>> isTaken, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(isTaken);
        if (centerId == Guid.Empty) throw new ArgumentException("A center is required.", nameof(centerId));
        var table = CountersTable(schema);
        var sql = $"""
            INSERT INTO {table} (center_id, kind, last_value) VALUES (@center, @kind, 1)
            ON CONFLICT (center_id, kind) DO UPDATE SET last_value = {table}.last_value + 1
            RETURNING last_value AS "Value"
            """;

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var next = await database.SqlQueryRaw<long>(sql,
                new NpgsqlParameter("center", centerId), new NpgsqlParameter("kind", kind.Prefix)).ToListAsync(token);
            var code = EntityCode.Format(kind, next.Single());
            if (!await isTaken(code, token)) return code;
        }
        throw new InvalidOperationException($"Could not allocate a free {kind.Prefix} code for center {centerId}.");
    }

    /// <summary>DDL for a module's counter table; call it from that module's migration.</summary>
    public static string CreateCountersTableSql(string schema) => $"""
        CREATE TABLE IF NOT EXISTS {CountersTable(schema)} (
            center_id uuid NOT NULL,
            kind character varying(4) NOT NULL,
            last_value bigint NOT NULL CHECK (last_value > 0),
            PRIMARY KEY (center_id, kind)
        );
        """;

    public static string DropCountersTableSql(string schema) => $"DROP TABLE IF EXISTS {CountersTable(schema)};";

    // The schema is spliced into SQL, so only plain lower-case identifiers are accepted.
    private static string CountersTable(string schema) => SchemaPattern().IsMatch(schema)
        ? $"{schema}.code_counters"
        : throw new ArgumentException("Schema must be a plain lower-case identifier.", nameof(schema));

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex SchemaPattern();
}
