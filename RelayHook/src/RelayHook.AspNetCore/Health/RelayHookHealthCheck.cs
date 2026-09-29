using Microsoft.Extensions.Diagnostics.HealthChecks;
using RelayHook.Core.Abstractions;
using RelayHook.SqlServer.Migrations;

namespace RelayHook.AspNetCore.Health;

internal sealed class RelayHookHealthCheck(ICallbackStorage storage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var version = await storage.GetSchemaVersionAsync(cancellationToken).ConfigureAwait(false);
            return version == SqlSchemaInstaller.ExpectedVersion
                ? HealthCheckResult.Healthy("RelayHook SQL schema is compatible.")
                : HealthCheckResult.Unhealthy(
                    $"RelayHook SQL schema version {version}; expected {SqlSchemaInstaller.ExpectedVersion}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("RelayHook SQL storage is unavailable.");
        }
    }
}
