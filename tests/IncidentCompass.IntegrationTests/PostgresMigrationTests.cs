using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Infrastructure.Configuration;
using IncidentCompass.Infrastructure.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace IncidentCompass.IntegrationTests;

[Collection(PostgresRepositoryCollection.CollectionName)]
public sealed class PostgresMigrationTests(PostgresRepositoryFixture fixture)
{
    private static readonly string[] ReleasedV011Scripts =
    [
        "001-enable-pgvector.sql",
        "004-observability-cost.sql",
        "006-tool-audit.sql",
        "007-intake.sql",
        "008-triage-ledger.sql",
        "009-triage-reports-minimal.sql",
        "010-memory.sql"
    ];

    [DockerAvailableFact]
    public async Task FreshAndReleasedUpgradeReachTheSameSchemaAndKeepDurableRows()
    {
        await using var fresh = await MigrationDatabase.CreateAsync(fixture);
        await using var upgraded = await MigrationDatabase.CreateAsync(fixture);

        await RunMigrationsAsync(fresh.ConnectionString);
        await PostgresSchemaTestHelper.ApplyReleasedV011ScriptsAsync(
            upgraded.ConnectionString,
            ReleasedV011Scripts);
        await InsertReleasedDurableRowsAsync(upgraded.ConnectionString);
        await RunMigrationsAsync(upgraded.ConnectionString);

        Assert.Equal(
            await ReadSchemaSignatureAsync(fresh.ConnectionString),
            await ReadSchemaSignatureAsync(upgraded.ConnectionString));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16], await ReadAppliedVersionsAsync(fresh.ConnectionString));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16], await ReadAppliedVersionsAsync(upgraded.ConnectionString));
        Assert.True(await HasRequiredV02IndexesAndColumnsAsync(fresh.ConnectionString));
        Assert.True(await HasRequiredV02IndexesAndColumnsAsync(upgraded.ConnectionString));
        Assert.Equal(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            await ReadGuidAsync(
                upgraded.ConnectionString,
                "SELECT job_id FROM incidentcompass.triage_reports WHERE id = '55555555-5555-5555-5555-555555555555';"));
        await AssertReportRowsAreImmutableAsync(upgraded.ConnectionString);

        foreach (var tableName in new[]
                 {
                     "signals", "faults", "triage_jobs", "triage_artifacts",
                     "triage_ledger", "triage_reports", "triage_evidence", "memory_items", "memory_chunks"
                 })
        {
            Assert.Equal(1, await CountAsync(upgraded.ConnectionString, tableName));
        }
    }

    [DockerAvailableFact]
    public async Task RerunningTheMigratorIsIdempotent()
    {
        await using var database = await MigrationDatabase.CreateAsync(fixture);

        await RunMigrationsAsync(database.ConnectionString);
        var firstRun = await ReadMigrationRecordsAsync(database.ConnectionString);

        await RunMigrationsAsync(database.ConnectionString);
        var secondRun = await ReadMigrationRecordsAsync(database.ConnectionString);

        Assert.Equal(firstRun, secondRun);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16], secondRun.Select(record => record.Version));
        Assert.All(secondRun, record => Assert.Equal("Applied", record.Status));
    }

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

        await RunMigrationsAsync(database.ConnectionString);

        Assert.Equal(
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16],
            await ReadAppliedVersionsAsync(database.ConnectionString));
        Assert.True(await HasRequiredV02IndexesAndColumnsAsync(database.ConnectionString));
        await AssertPreProjectionActionPreservedAsync(database.ConnectionString, actionId);
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

        await RunMigrationsAsync(database.ConnectionString);

        Assert.Equal(
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16],
            await ReadAppliedVersionsAsync(database.ConnectionString));
        Assert.True(await HasRequiredV02IndexesAndColumnsAsync(database.ConnectionString));
        await AssertPreProjectionActionPreservedAsync(database.ConnectionString, actionId);
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

    private static async Task RunMigrationsAsync(string connectionString)
    {
        using var services = CreateServiceProvider(
            connectionString,
            new NoPostgresMigrationFailureInjector());
        await services.GetRequiredService<PostgresMigrationRunner>()
            .MigrateAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<Guid> SeedPreProjectionActionAsync(
        string connectionString,
        string proposalKey)
    {
        var origin = await ActionApprovalTestSupport.SeedOriginAsync(connectionString);
        var actionId = Guid.NewGuid();
        await ActionApprovalTestSupport.ExecuteAsync(connectionString, """
            INSERT INTO incidentcompass.action_approvals (
                id, tenant_id, origin_report_id, fault_id, job_id, attempt,
                tool_id, proposal_key, category, mode, logical_target_id,
                adapter_binding_fingerprint, approval_contract_version, provenance_sha256,
                state, canonical_payload, payload_sha256, approval_sha256,
                proposal_artifact_id, review_summary, created_at_utc, expires_at_utc)
            VALUES (
                @id, @tenant, @report, @fault, @job, 1,
                'ticket_create', @proposal_key, 'ticket_create', 'live', 'ticket:configured-repository',
                @hash, 1, @hash, 'requested', convert_to('{"title":"upgrade proof"}', 'UTF8'),
                @hash, @hash, @artifact, 'Upgrade proof action.',
                clock_timestamp(), clock_timestamp() + interval '1 hour');
            """,
            ("id", actionId),
            ("tenant", origin.TenantId),
            ("report", origin.ReportId),
            ("fault", origin.FaultId),
            ("job", origin.JobId),
            ("proposal_key", proposalKey),
            ("hash", new string('a', 64)),
            ("artifact", origin.EvidenceArtifactId));
        return actionId;
    }

    private static async Task AssertPreProjectionActionPreservedAsync(
        string connectionString,
        Guid actionId)
    {
        Assert.Equal(1, Convert.ToInt64(await ActionApprovalTestSupport.ScalarAsync(
            connectionString,
            "SELECT count(*) FROM incidentcompass.action_approvals WHERE id = @id;",
            ("id", actionId))));
        Assert.Equal(0, Convert.ToInt64(await ActionApprovalTestSupport.ScalarAsync(connectionString, """
            SELECT count(*)
            FROM incidentcompass.action_approvals
            WHERE id = @id
              AND (external_resource_kind IS NOT NULL OR external_resource_id IS NOT NULL OR
                   external_before_state IS NOT NULL OR external_after_state IS NOT NULL);
            """, ("id", actionId))));
    }

    private static IHost CreateMigrationHost(
        string connectionString,
        IPostgresMigrationFailureInjector failureInjector) =>
        new HostBuilder()
            .ConfigureServices(services =>
            {
                ConfigureMigrationServices(services, connectionString, failureInjector);
                services.AddSingleton<IHostedService, PostgresMigrationHostedService>();
            })
            .Build();

    private static ServiceProvider CreateServiceProvider(
        string connectionString,
        IPostgresMigrationFailureInjector failureInjector)
    {
        var services = new ServiceCollection();
        ConfigureMigrationServices(services, connectionString, failureInjector);
        return services.BuildServiceProvider();
    }

    private static void ConfigureMigrationServices(
        IServiceCollection services,
        string connectionString,
        IPostgresMigrationFailureInjector failureInjector)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MigrationTests"] = connectionString,
                ["IncidentCompass:Postgres:ConnectionStringName"] = "MigrationTests"
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<PostgresOptions>(
            configuration.GetSection(PostgresOptions.SectionName));
        services.AddSingleton<PostgresDataSourceProvider>();
        services.AddSingleton<PostgresMigrationReadiness>();
        services.AddSingleton<IPostgresMigrationReadiness>(
            serviceProvider => serviceProvider.GetRequiredService<PostgresMigrationReadiness>());
        services.AddSingleton<IPostgresMigrationFailureInjector>(failureInjector);
        services.AddSingleton<PostgresMigrationRunner>();
    }

    private static async Task InsertReleasedDurableRowsAsync(string connectionString)
    {
        const string sql = """
            INSERT INTO incidentcompass.triage_config_snapshots (
                config_hash, serialized_config, instructions, created_at_utc)
            VALUES ('released-config', '{}'::jsonb, '{}'::jsonb, clock_timestamp());

            INSERT INTO incidentcompass.signals (
                id, tenant_id, source, fingerprint, fingerprint_version, fingerprint_strength,
                service_name, environment, error_type, summary, body, observed_at_utc, received_at_utc)
            VALUES (
                '11111111-1111-1111-1111-111111111111', 'tenant-a', 'released-test',
                'released-fingerprint', 1, 'strong', 'orders', 'test', 'ReleasedException',
                'released signal', '{}'::jsonb, clock_timestamp(), clock_timestamp());

            INSERT INTO incidentcompass.faults (
                id, trigger_signal_id, tenant_id, status, fingerprint, fingerprint_version,
                fingerprint_strength, service_name, environment, created_at_utc)
            VALUES (
                '22222222-2222-2222-2222-222222222222',
                '11111111-1111-1111-1111-111111111111', 'tenant-a', 'Completed',
                'released-fingerprint', 1, 'strong', 'orders', 'test', clock_timestamp());

            UPDATE incidentcompass.signals
            SET fault_id = '22222222-2222-2222-2222-222222222222'
            WHERE id = '11111111-1111-1111-1111-111111111111';

            INSERT INTO incidentcompass.triage_jobs (
                id, fault_id, status, attempt, config_hash, created_at_utc, updated_at_utc)
            VALUES (
                '33333333-3333-3333-3333-333333333333',
                '22222222-2222-2222-2222-222222222222', 'Succeeded', 1,
                'released-config', clock_timestamp(), clock_timestamp());

            INSERT INTO incidentcompass.triage_artifacts (
                id, job_id, attempt, kind, redacted_payload, content_hash, created_at_utc)
            VALUES (
                '44444444-4444-4444-4444-444444444444',
                '33333333-3333-3333-3333-333333333333', NULL, 'TriggerSignal',
                '{}'::jsonb, 'released-artifact', clock_timestamp());

            INSERT INTO incidentcompass.triage_ledger (
                fault_id, job_id, attempt, event_type, config_hash, created_at_utc)
            VALUES (
                '22222222-2222-2222-2222-222222222222',
                '33333333-3333-3333-3333-333333333333', 1, 'WorkerCompleted',
                'released-config', clock_timestamp());

            INSERT INTO incidentcompass.triage_reports (
                id, fault_id, status, summary, classification, confidence, config_hash, created_at_utc)
            VALUES (
                '55555555-5555-5555-5555-555555555555',
                '22222222-2222-2222-2222-222222222222', 'Completed', 'released report',
                'KnownIncident', 'High', 'released-config', clock_timestamp());

            INSERT INTO incidentcompass.triage_evidence (
                id, report_id, kind, artifact_id, reference, created_at_utc)
            VALUES (
                '66666666-6666-6666-6666-666666666666',
                '55555555-5555-5555-5555-555555555555', 'TriggerSignal',
                '44444444-4444-4444-4444-444444444444', 'released-artifact', clock_timestamp());

            INSERT INTO incidentcompass.memory_items (
                id, tenant_id, kind, source, title, content, content_hash, version, created_at_utc)
            VALUES (
                '77777777-7777-7777-7777-777777777777', 'tenant-a', 'runbook',
                'runbooks/released.md', 'Released runbook', 'Retained durable memory item.',
                'released-memory', 1, clock_timestamp());

            INSERT INTO incidentcompass.memory_chunks (
                id, memory_item_id, tenant_id, chunk_position, text, text_hash,
                embedding_provider, embedding_model, embedding_dimensions,
                embedding_values, embedding_vector, created_at_utc)
            VALUES (
                '88888888-8888-8888-8888-888888888888',
                '77777777-7777-7777-7777-777777777777', 'tenant-a', 0,
                'Retained durable memory chunk.', 'released-memory-chunk',
                'mock', 'test', 2, ARRAY[0.1, 0.2]::real[], '[0.1,0.2]'::vector,
                clock_timestamp());
            """;
        await ExecuteAsync(connectionString, sql);
    }

    private static async Task<IReadOnlyList<string>> ReadSchemaSignatureAsync(string connectionString)
    {
        const string sql = """
            SELECT 'index:' || indexname
            FROM pg_indexes
            WHERE schemaname = 'incidentcompass'
            UNION ALL
            SELECT 'constraint:' || conname
            FROM pg_constraint
            WHERE connamespace = 'incidentcompass'::regnamespace
            ORDER BY 1;
            """;
        return await ReadStringsAsync(connectionString, sql);
    }

    private static async Task<bool> HasRequiredV02IndexesAndColumnsAsync(string connectionString)
    {
        const string sql = """
            SELECT (
                (SELECT count(*)
                 FROM pg_indexes
                 WHERE schemaname = 'incidentcompass'
                   AND indexname IN (
                       'ux_memory_items_active_seed_owner_source',
                       'ux_memory_items_seed_owner_content',
                       'ix_memory_items_active_lookup',
                       'ix_memory_items_seed_owner_generation')) = 4
                AND
                (SELECT count(*)
                 FROM information_schema.columns
                 WHERE table_schema = 'incidentcompass'
                   AND table_name = 'memory_items'
                   AND column_name IN (
                       'service_name', 'component', 'release_name', 'is_active',
                       'seed_managed', 'updated_at_utc', 'superseded_at_utc',
                       'seed_owner', 'seed_generation')) = 9
                AND
                (SELECT count(*)
                 FROM information_schema.columns
                 WHERE table_schema = 'incidentcompass'
                   AND table_name IN ('signals', 'faults')
                   AND column_name IN ('grouping_rule_id', 'grouping_rule_version')) = 4
                AND
                (SELECT count(*)
                 FROM information_schema.columns
                 WHERE table_schema = 'incidentcompass'
                   AND table_name = 'signals'
                   AND column_name IN ('suppression_rule_id', 'effective_suppression_window_minutes')) = 2
                AND
                (SELECT count(*)
                 FROM pg_indexes
                 WHERE schemaname = 'incidentcompass'
                   AND indexname IN (
                       'ux_faults_open_group',
                       'ix_faults_versioned_group_lookup',
                       'ix_signals_versioned_neighbor_lookup',
                       'ix_signals_suppression_audit',
                       'ix_recurrence_states_escalation_intent')) = 5
                AND
                (SELECT count(*)
                 FROM information_schema.columns
                 WHERE table_schema = 'incidentcompass'
                   AND table_name = 'triage_reports'
                   AND column_name IN ('job_id', 'supersedes_report_id')) = 2
                AND
                (SELECT count(*)
                 FROM pg_indexes
                 WHERE schemaname = 'incidentcompass'
                   AND indexname IN (
                       'ux_triage_reports_job',
                       'ux_triage_reports_supersedes',
                       'ix_triage_reports_fault_history')) = 3
                AND
                EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = 'incidentcompass'
                      AND table_name = 'recurrence_states')
                AND
                (SELECT count(*)
                 FROM pg_constraint
                 WHERE connamespace = 'incidentcompass'::regnamespace
                   AND conname IN (
                       'ck_triage_artifacts_kind', 'ck_triage_evidence_kind',
                       'ck_triage_reports_no_self_supersede')
                   AND (pg_get_constraintdef(oid) LIKE '%RecurrenceState%'
                        OR conname = 'ck_triage_reports_no_self_supersede')) = 3
                AND
                (SELECT count(*)
                 FROM information_schema.columns
                 WHERE table_schema = 'incidentcompass'
                   AND table_name = 'triage_jobs'
                   AND column_name IN (
                       'retriage_trigger_job_id', 'supersedes_report_id',
                       'retry_without_consuming_attempt')) = 3
                AND
                (SELECT count(*)
                 FROM pg_indexes
                 WHERE schemaname = 'incidentcompass'
                   AND indexname IN (
                       'ux_triage_jobs_retriage_trigger',
                       'ix_triage_reports_created_desc',
                       'ix_faults_tenant_id')) = 3
                AND
                EXISTS (
                    SELECT 1
                    FROM pg_trigger
                    WHERE tgrelid = 'incidentcompass.triage_reports'::regclass
                      AND tgname = 'trg_triage_reports_immutable')
                AND
                (SELECT count(*)
                 FROM pg_constraint
                 WHERE connamespace = 'incidentcompass'::regnamespace
                 AND conname IN (
                       'uq_triage_reports_id_fault',
                       'fk_triage_reports_superseded_same_fault')) = 2
                AND
                (SELECT count(*)
                 FROM information_schema.tables
                 WHERE table_schema = 'incidentcompass'
                   AND table_name IN ('action_approvals', 'action_approval_provenance')) = 2
                AND
                (SELECT count(*)
                 FROM pg_indexes
                 WHERE schemaname = 'incidentcompass'
                   AND indexname IN (
                       'ix_action_approvals_tenant_created',
                       'ix_action_approvals_dispatch_candidates',
                       'ix_action_approvals_external_resource')) = 3
                AND
                (SELECT count(*)
                 FROM information_schema.columns
                 WHERE table_schema = 'incidentcompass'
                   AND table_name = 'action_approvals'
                   AND column_name IN (
                       'external_resource_kind', 'external_resource_id',
                       'external_before_state', 'external_after_state')) = 4
                AND
                (SELECT count(*)
                 FROM pg_constraint
                 WHERE connamespace = 'incidentcompass'::regnamespace
                   AND conname IN (
                       'ck_action_approvals_external_resource_kind',
                       'ck_action_approvals_external_resource_id',
                       'ck_action_approvals_external_before_state_bound',
                       'ck_action_approvals_external_after_state_bound',
                       'ck_action_approvals_external_projection_shape',
                       'ck_action_approvals_external_projection_transition')) = 6
                AND
                (SELECT count(*)
                 FROM pg_trigger
                 WHERE NOT tgisinternal
                   AND tgname IN (
                       'trg_action_approvals_immutable',
                       'trg_action_approvals_no_delete',
                       'trg_action_approval_provenance_sealed',
                       'trg_action_approval_provenance_immutable',
                       'trg_action_review_artifact_immutable')) = 5
                AND
                (SELECT count(*)
                 FROM pg_constraint
                 WHERE connamespace = 'incidentcompass'::regnamespace
                   AND conname IN (
                       'ck_triage_artifacts_kind',
                       'triage_ledger_event_type_check',
                       'triage_ledger_decision_check',
                       'triage_ledger_tool_status_check',
                       'triage_ledger_status_shape_check',
                       'triage_ledger_action_ref_check')) = 6
                AND
                EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = 'incidentcompass'
                      AND table_name = 'post_report_action_intents')
                AND
                EXISTS (
                    SELECT 1
                    FROM pg_indexes
                    WHERE schemaname = 'incidentcompass'
                      AND indexname = 'ix_post_report_action_intents_candidates')
                AND
                (SELECT count(*)
                 FROM pg_trigger
                 WHERE NOT tgisinternal
                   AND tgname IN (
                       'trg_post_report_action_intents_transition',
                       'trg_post_report_action_intents_no_delete')) = 2
                AND
                EXISTS (
                    SELECT 1
                    FROM pg_constraint
                    WHERE conrelid = 'incidentcompass.triage_artifacts'::regclass
                      AND conname = 'ck_triage_artifacts_kind'
                      AND pg_get_constraintdef(oid) LIKE '%ProposedAction%'
                      AND pg_get_constraintdef(oid) LIKE '%ActionResult%')
            );
            """;
        return Convert.ToBoolean(await ExecuteScalarAsync(connectionString, sql));
    }

    private static async Task AssertReportRowsAreImmutableAsync(string connectionString)
    {
        var exception = await Record.ExceptionAsync(() => ExecuteAsync(
            connectionString,
            "UPDATE incidentcompass.triage_reports SET summary = 'mutated' WHERE id = '55555555-5555-5555-5555-555555555555';"));

        var postgresException = Assert.IsType<PostgresException>(exception);
        Assert.Contains("immutable", postgresException.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<Guid> ReadGuidAsync(string connectionString, string sql)
    {
        var value = await ExecuteScalarAsync(connectionString, sql);
        return (Guid)value!;
    }
    private static async Task<IReadOnlyList<int>> ReadAppliedVersionsAsync(string connectionString)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = new NpgsqlCommand("""
            SELECT version
            FROM incidentcompass.schema_migrations
            WHERE status = 'Applied'
            ORDER BY version;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var versions = new List<int>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            versions.Add(reader.GetInt32(0));
        }

        return versions;
    }

    private static async Task<IReadOnlyList<MigrationRecord>> ReadMigrationRecordsAsync(string connectionString)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = new NpgsqlCommand("""
            SELECT version, name, checksum, status, applied_at_utc, failed_at_utc, error_message
            FROM incidentcompass.schema_migrations
            ORDER BY version;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var records = new List<MigrationRecord>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            records.Add(ReadMigrationRecord(reader));
        }

        return records;
    }

    private static async Task<MigrationRecord?> ReadMigrationRecordAsync(string connectionString, int version)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = new NpgsqlCommand("""
            SELECT version, name, checksum, status, applied_at_utc, failed_at_utc, error_message
            FROM incidentcompass.schema_migrations
            WHERE version = $1;
            """, connection);
        command.Parameters.AddWithValue(version);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        return await reader.ReadAsync(TestContext.Current.CancellationToken)
            ? ReadMigrationRecord(reader)
            : null;
    }

    private static MigrationRecord ReadMigrationRecord(NpgsqlDataReader reader) =>
        new(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
            reader.IsDBNull(6) ? null : reader.GetString(6));

    private static async Task<int> CountAsync(string connectionString, string tableName) =>
        Convert.ToInt32(await ExecuteScalarAsync(
            connectionString,
            $"SELECT count(*) FROM incidentcompass.{tableName};"));

    private static async Task<IReadOnlyList<string>> ReadStringsAsync(string connectionString, string sql)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var values = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<object?> ExecuteScalarAsync(string connectionString, string sql)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    private sealed record MigrationRecord(
        int Version,
        string Name,
        string Checksum,
        string Status,
        DateTimeOffset? AppliedAtUtc,
        DateTimeOffset? FailedAtUtc,
        string? ErrorMessage);

    private sealed class FailingMigrationInjector(int failedVersion) : IPostgresMigrationFailureInjector
    {
        public void ThrowIfRequested(int version)
        {
            if (version == failedVersion)
            {
                throw new InvalidOperationException($"Injected migration failure for version {version}.");
            }
        }
    }

    private sealed class MigrationDatabase(
        string connectionString,
        string databaseName,
        string rootConnectionString) : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;

        public static async Task<MigrationDatabase> CreateAsync(PostgresRepositoryFixture fixture)
        {
            var rootConnectionString = await fixture.GetConnectionStringAsync();
            var databaseName = "migration_" + Guid.NewGuid().ToString("N");
            var databaseConnectionString = new NpgsqlConnectionStringBuilder(rootConnectionString)
            {
                Database = databaseName
            }.ConnectionString;

            await using var connection = new NpgsqlConnection(rootConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand($"CREATE DATABASE {databaseName};", connection);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

            return new MigrationDatabase(databaseConnectionString, databaseName, rootConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            var rootBuilder = new NpgsqlConnectionStringBuilder(rootConnectionString);
            await using var connection = new NpgsqlConnection(rootBuilder.ConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE);",
                connection);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }
}
