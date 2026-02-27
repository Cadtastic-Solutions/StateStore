using Microsoft.Extensions.Logging;
using StateStore.Abstractions;

namespace StateStore.Middleware;

/// <summary>
/// A middleware component that logs state store operations.
/// Serves as both a useful default and a reference implementation for middleware authors.
/// </summary>
public sealed class LoggingMiddleware : IStateStoreMiddleware
{
    private readonly ILogger<LoggingMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="LoggingMiddleware"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public LoggingMiddleware(ILogger<LoggingMiddleware> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<byte[]?> OnReadAsync(string key, Func<ValueTask<byte[]?>> next, CancellationToken cancellationToken)
    {
        _logger.LogDebug("StateStore reading key '{Key}'", key);
        var result = await next().ConfigureAwait(false);
        _logger.LogDebug("StateStore read key '{Key}': {Result}", key, result is not null ? $"{result.Length} bytes" : "not found");
        return result;
    }

    /// <inheritdoc />
    public async ValueTask OnWriteAsync(string key, ReadOnlyMemory<byte> data, Func<ValueTask> next, CancellationToken cancellationToken)
    {
        _logger.LogDebug("StateStore writing key '{Key}' ({Size} bytes)", key, data.Length);
        await next().ConfigureAwait(false);
        _logger.LogDebug("StateStore wrote key '{Key}' successfully", key);
    }

    /// <inheritdoc />
    public async ValueTask OnDeleteAsync(string key, Func<ValueTask> next, CancellationToken cancellationToken)
    {
        _logger.LogDebug("StateStore deleting key '{Key}'", key);
        await next().ConfigureAwait(false);
        _logger.LogDebug("StateStore deleted key '{Key}' successfully", key);
    }
}
