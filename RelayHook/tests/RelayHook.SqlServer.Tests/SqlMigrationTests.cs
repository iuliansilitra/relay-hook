using System.Reflection;
using RelayHook.SqlServer.Migrations;

namespace RelayHook.SqlServer.Tests;

public sealed class SqlMigrationTests
{
    [Fact]
    public async Task InitialMigration_ShouldContainOwnedTablesAndClaimIndexes()
    {
        var assembly = typeof(SqlSchemaInstaller).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(".Migrations.001_Initial.sql", StringComparison.Ordinal));
        await using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);

        var sql = await reader.ReadToEndAsync();

        Assert.Contains("[Callback].[Job]", sql, StringComparison.Ordinal);
        Assert.Contains("[Callback].[Attempt]", sql, StringComparison.Ordinal);
        Assert.Contains("IX_Callback_Job_Claim", sql, StringComparison.Ordinal);
        Assert.Contains("IX_Callback_Job_ExpiredLease", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ClientSecret", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApiKey", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SecondMigration_ShouldPreserveCrashAttemptsAndIndexRetention()
    {
        var assembly = typeof(SqlSchemaInstaller).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(
                ".Migrations.002_CompletedAttemptCountAndRetention.sql",
                StringComparison.Ordinal));
        await using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);

        var sql = await reader.ReadToEndAsync();

        Assert.Contains("CompletedAttemptCount", sql, StringComparison.Ordinal);
        Assert.Contains("IX_Callback_Job_Retention", sql, StringComparison.Ordinal);
        Assert.Contains("CompletedAt] IS NOT NULL", sql, StringComparison.Ordinal);
    }
}
