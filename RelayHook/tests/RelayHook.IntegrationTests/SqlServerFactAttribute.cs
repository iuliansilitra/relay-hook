namespace RelayHook.IntegrationTests;

internal sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RELAYHOOK_SQLSERVER")))
        {
            Skip = "Set RELAYHOOK_SQLSERVER to a disposable SQL Server database to run this test.";
        }
    }
}
