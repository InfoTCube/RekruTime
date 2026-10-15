using Npgsql;

namespace RekruTime.Api.Data;

public static class DbErrors
{
    // Finds the Postgres error anywhere in the chain: EF wraps it in DbUpdateException, and transient ones
    // (deadlocks) additionally in InvalidOperationException from the execution strategy.
    public static PostgresException? Postgres(Exception e) =>
        e as PostgresException ?? (e.InnerException is { } inner ? Postgres(inner) : null);

    // Two transactions inserting overlapping interviews at the same moment: Postgres reports either the
    // exclusion violation or – when both wait on each other inside the constraint check – a deadlock.
    public static bool IsRoomTaken(Exception e) =>
        Postgres(e)?.SqlState is PostgresErrorCodes.ExclusionViolation or PostgresErrorCodes.DeadlockDetected;

    public static bool IsUniqueViolation(Exception e, string? constraint = null) =>
        Postgres(e) is { SqlState: PostgresErrorCodes.UniqueViolation } p && (constraint is null || p.ConstraintName == constraint);
}
