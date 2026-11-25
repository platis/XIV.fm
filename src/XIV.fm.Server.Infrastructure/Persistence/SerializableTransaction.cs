using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace XIV.fm.Server.Infrastructure.Persistence;

/// <summary>
/// Runs a unit of work in a serializable transaction and repeats it when PostgreSQL rejects it because
/// of a concurrent transaction. Every attempt uses a new context, so a rejected attempt leaves no state behind.
/// </summary>
internal static class SerializableTransaction
{
    private const int MaximumAttempts = 12;
    private const int InitialDelayMilliseconds = 10;
    private const int MaximumDelayMilliseconds = 400;

    /// <summary>
    /// The operation must call <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> itself and return
    /// whether its changes should be committed. An exhausted retry budget surfaces the final conflict, which
    /// <see cref="IsConflict"/> recognizes.
    /// </summary>
    public static async ValueTask<T> ExecuteAsync<T>(
        IDbContextFactory<XivFmDbContext> contextFactory,
        Func<XivFmDbContext, CancellationToken, Task<(T Result, bool Commit)>> operation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var context = await contextFactory
                    .CreateDbContextAsync(cancellationToken)
                    .ConfigureAwait(false);
                await using var transaction = await context.Database
                    .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                    .ConfigureAwait(false);
                var (result, commit) = await operation(context, cancellationToken).ConfigureAwait(false);
                if (commit)
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch (Exception exception) when (attempt < MaximumAttempts && IsConflict(exception))
            {
                await Task.Delay(CreateDelay(attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Entity Framework wraps transient PostgreSQL errors, so the cause can be several exceptions deep.
    /// </summary>
    public static bool IsConflict(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException)
                return true;
            if (current is PostgresException postgres)
            {
                return postgres.SqlState is PostgresErrorCodes.SerializationFailure
                    or PostgresErrorCodes.DeadlockDetected
                    or PostgresErrorCodes.UniqueViolation;
            }
        }

        return false;
    }

    private static TimeSpan CreateDelay(int attempt)
    {
        var maximum = Math.Min(MaximumDelayMilliseconds, InitialDelayMilliseconds << Math.Min(attempt, 6));
        return TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(1, maximum + 1));
    }
}
