using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SurveyBackend.Tests;

internal sealed class FailingSaveChangesInterceptor : SaveChangesInterceptor
{
    public Exception? Failure { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        return Failure is null
            ? base.SavingChangesAsync(eventData, result, cancellationToken)
            : ValueTask.FromException<InterceptionResult<int>>(Failure);
    }
}
