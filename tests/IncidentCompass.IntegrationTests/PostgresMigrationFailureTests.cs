using IncidentCompass.Infrastructure.Postgres;
using Microsoft.Extensions.DependencyInjection;
using static IncidentCompass.IntegrationTests.PostgresMigrationDurableDataAssertions;
using static IncidentCompass.IntegrationTests.PostgresMigrationTestSupport;

namespace IncidentCompass.IntegrationTests;

[Collection(PostgresRepositoryCollection.CollectionName)]
public sealed class PostgresMigrationFailureTests(PostgresRepositoryFixture fixture)
{
    [DockerAvailableFact]
    public async Task FailedVersion15LeavesVersion14DurableAndThenUpgradesCleanly()
    {
        await using var database = await MigrationDatabase.CreateAsync(fixture);
        using (var failing = CreateServiceProvider(
                   database.ConnectionString, new FailingMigrationInjector(15)))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => failing
                .GetRequiredService<PostgresMigrationRunner>()
                .MigrateAsync(TestContext.Current.CancellationToken));
        }

        Assert.Equal(
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14],
            await ReadAppliedVersionsAsync(database.ConnectionString));
        Assert.Equal("Failed", (await ReadMigrationRecordAsync(database.ConnectionString, 15))!.Status);
        var actionId = await SeedPreProjectionActionAsync(database.ConnectionString, "upgrade-from-v14");
        var costHistory = await SeedCostRollupHistoryAsync(database.ConnectionString, "v14");

        await RunMigrationsAsync(database.ConnectionString);

        Assert.Equal(
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17],
            await ReadAppliedVersionsAsync(database.ConnectionString));
        Assert.True(await HasRequiredV02IndexesAndColumnsAsync(database.ConnectionString));
        await AssertPreProjectionActionPreservedAsync(database.ConnectionString, actionId);
        await AssertCostRollupHistoryPreservedAsync(database.ConnectionString, costHistory);
    }

    [DockerAvailableFact]
    public async Task FailedVersion16LeavesVersion15DurableAndThenUpgradesCleanly()
    {
        await using var database = await MigrationDatabase.CreateAsync(fixture);
        using (var failing = CreateServiceProvider(
                   database.ConnectionString, new FailingMigrationInjector(16)))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => failing
                .GetRequiredService<PostgresMigrationRunner>()
                .MigrateAsync(TestContext.Current.CancellationToken));
        }

        Assert.Equal(
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15],
            await ReadAppliedVersionsAsync(database.ConnectionString));
        Assert.Equal("Failed", (await ReadMigrationRecordAsync(database.ConnectionString, 16))!.Status);
        var actionId = await SeedPreProjectionActionAsync(database.ConnectionString, "upgrade-from-v15");
        var costHistory = await SeedCostRollupHistoryAsync(database.ConnectionString, "v15");

        await RunMigrationsAsync(database.ConnectionString);

        Assert.Equal(
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17],
            await ReadAppliedVersionsAsync(database.ConnectionString));
        Assert.True(await HasRequiredV02IndexesAndColumnsAsync(database.ConnectionString));
        await AssertPreProjectionActionPreservedAsync(database.ConnectionString, actionId);
        await AssertCostRollupHistoryPreservedAsync(database.ConnectionString, costHistory);
    }

    [DockerAvailableFact]
    public async Task FailedVersion17LeavesVersion16DurableAndThenUpgradesCleanly()
    {
        await using var database = await MigrationDatabase.CreateAsync(fixture);
        using (var failing = CreateServiceProvider(
                   database.ConnectionString, new FailingMigrationInjector(17)))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => failing
                .GetRequiredService<PostgresMigrationRunner>()
                .MigrateAsync(TestContext.Current.CancellationToken));
        }

        Assert.Equal(
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16],
            await ReadAppliedVersionsAsync(database.ConnectionString));
        Assert.Equal("Failed", (await ReadMigrationRecordAsync(database.ConnectionString, 17))!.Status);
        var costHistory = await SeedCostRollupHistoryAsync(database.ConnectionString, "v16");

        await RunMigrationsAsync(database.ConnectionString);

        Assert.Equal(
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17],
            await ReadAppliedVersionsAsync(database.ConnectionString));
        Assert.True(await HasRequiredV02IndexesAndColumnsAsync(database.ConnectionString));
        await AssertCostRollupHistoryPreservedAsync(database.ConnectionString, costHistory);
    }

    [DockerAvailableFact]
    public async Task InjectedFailureMakesMigrationHostedServiceFailStartupAndRecordsDiagnostic()
    {
        await using var database = await MigrationDatabase.CreateAsync(fixture);
        using var host = CreateMigrationHost(database.ConnectionString, new FailingMigrationInjector(2));

        var exception = await Record.ExceptionAsync(() => host.StartAsync());

        Assert.NotNull(exception);
        Assert.Contains("migration 2", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(host.Services.GetRequiredService<IPostgresMigrationReadiness>().IsReady);

        var record = await ReadMigrationRecordAsync(database.ConnectionString, 2);
        Assert.NotNull(record);
        Assert.Equal("v0.2-memory-file-sync", record.Name);
        Assert.Equal("Failed", record.Status);
        Assert.NotNull(record.FailedAtUtc);
        Assert.Contains("injected", record.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
