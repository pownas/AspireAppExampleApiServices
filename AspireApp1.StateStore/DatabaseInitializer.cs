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
        // NOTE: Datetime columns are TEXT to match EF Core's SQLite convention,
        // which stores DateTimeOffset values as ISO 8601 strings.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "FlowRunRecords" (
                "Id"            INTEGER PRIMARY KEY AUTOINCREMENT,
                "FlowRunId"     TEXT NOT NULL,
                "FlowName"      TEXT NOT NULL,
                "CorrelationId" TEXT NOT NULL,
                "TraceId"       TEXT,
                "StartedAt"     TEXT NOT NULL,
                "CompletedAt"   TEXT,
                "Status"        TEXT NOT NULL,
                "ErrorMessage"  TEXT
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_FlowRunRecords_FlowRunId"
                ON "FlowRunRecords" ("FlowRunId");
            CREATE INDEX IF NOT EXISTS "IX_FlowRunRecords_CorrelationId"
                ON "FlowRunRecords" ("CorrelationId");
            CREATE INDEX IF NOT EXISTS "IX_FlowRunRecords_TraceId"
                ON "FlowRunRecords" ("TraceId");

            CREATE TABLE IF NOT EXISTS "FlowStepRecords" (
                "Id"           INTEGER PRIMARY KEY AUTOINCREMENT,
                "FlowRunId"    TEXT NOT NULL,
                "StepName"     TEXT NOT NULL,
                "ServiceName"  TEXT NOT NULL,
                "StepOrder"    INTEGER NOT NULL,
                "Status"       TEXT NOT NULL,
                "StartedAt"    TEXT,
                "CompletedAt"  TEXT,
                "ErrorMessage" TEXT,
                "TraceId"      TEXT,
                "SpanId"       TEXT
            );
            CREATE INDEX IF NOT EXISTS "IX_FlowStepRecords_FlowRunId"
                ON "FlowStepRecords" ("FlowRunId");
            CREATE INDEX IF NOT EXISTS "IX_FlowStepRecords_TraceId"
                ON "FlowStepRecords" ("TraceId");

            CREATE TABLE IF NOT EXISTS "SpanRecords" (
                "Id"            INTEGER PRIMARY KEY AUTOINCREMENT,
                "TraceId"       TEXT NOT NULL,
                "SpanId"        TEXT NOT NULL,
                "ParentSpanId"  TEXT,
                "ServiceName"   TEXT NOT NULL,
                "OperationName" TEXT NOT NULL,
                "StartTime"     TEXT NOT NULL,
                "EndTime"       TEXT,
                "Status"        TEXT NOT NULL,
                "ErrorMessage"  TEXT,
                "HttpStatusCode" INTEGER,
                "CreatedAt"     TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_SpanRecords_TraceId"
                ON "SpanRecords" ("TraceId");
            """, cancellationToken);
    }
}
