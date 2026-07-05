using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AspireApp1.StateStore;

/// <summary>
/// Handles database schema creation and evolution.
/// Calls EnsureCreatedAsync for a fresh database and then adds any new tables
/// that may be missing from existing databases (schema evolution without migrations).
/// </summary>
public static class DatabaseInitializer
{
    public static async Task EnsureSchemaAsync(StateStoreDbContext db, CancellationToken cancellationToken = default)
    {
        // Creates the full schema if the database is new; no-op if it already exists
        await db.Database.EnsureCreatedAsync(cancellationToken);

        // Idempotent DDL for tables added in later iterations — only supported by relational providers.
        // Skipped when using the EF Core in-memory provider (e.g. in unit tests).
        if (!db.Database.IsRelational())
        {
            return;
        }

        // Idempotent DDL for tables added in later iterations — safe to run against
        // both fresh databases (tables already created above) and existing databases
        // that were created before these tables were added to the EF Core model.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "FlowRunRecords" (
                "Id"            SERIAL PRIMARY KEY,
                "FlowRunId"     VARCHAR(64)  NOT NULL,
                "FlowName"      VARCHAR(128) NOT NULL,
                "CorrelationId" VARCHAR(64)  NOT NULL,
                "TraceId"       VARCHAR(64),
                "StartedAt"     TIMESTAMPTZ  NOT NULL,
                "CompletedAt"   TIMESTAMPTZ,
                "Status"        VARCHAR(32)  NOT NULL,
                "ErrorMessage"  TEXT
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_FlowRunRecords_FlowRunId"
                ON "FlowRunRecords" ("FlowRunId");
            CREATE INDEX IF NOT EXISTS "IX_FlowRunRecords_CorrelationId"
                ON "FlowRunRecords" ("CorrelationId");
            CREATE INDEX IF NOT EXISTS "IX_FlowRunRecords_TraceId"
                ON "FlowRunRecords" ("TraceId");

            CREATE TABLE IF NOT EXISTS "FlowStepRecords" (
                "Id"           SERIAL PRIMARY KEY,
                "FlowRunId"    VARCHAR(64)  NOT NULL,
                "StepName"     VARCHAR(128) NOT NULL,
                "ServiceName"  VARCHAR(128) NOT NULL,
                "StepOrder"    INT          NOT NULL,
                "Status"       VARCHAR(32)  NOT NULL,
                "StartedAt"    TIMESTAMPTZ,
                "CompletedAt"  TIMESTAMPTZ,
                "ErrorMessage" TEXT,
                "TraceId"      VARCHAR(64),
                "SpanId"       VARCHAR(32)
            );
            CREATE INDEX IF NOT EXISTS "IX_FlowStepRecords_FlowRunId"
                ON "FlowStepRecords" ("FlowRunId");
            CREATE INDEX IF NOT EXISTS "IX_FlowStepRecords_TraceId"
                ON "FlowStepRecords" ("TraceId");

            CREATE TABLE IF NOT EXISTS "SpanRecords" (
                "Id"            SERIAL PRIMARY KEY,
                "TraceId"       VARCHAR(64)  NOT NULL,
                "SpanId"        VARCHAR(32)  NOT NULL,
                "ParentSpanId"  VARCHAR(32),
                "ServiceName"   VARCHAR(128) NOT NULL,
                "OperationName" VARCHAR(256) NOT NULL,
                "StartTime"     TIMESTAMPTZ  NOT NULL,
                "EndTime"       TIMESTAMPTZ,
                "Status"        VARCHAR(32)  NOT NULL,
                "ErrorMessage"  TEXT,
                "HttpStatusCode" INT,
                "CreatedAt"     TIMESTAMPTZ  NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_SpanRecords_TraceId"
                ON "SpanRecords" ("TraceId");
            """, cancellationToken);
    }
}
