namespace AspireApp1.StateStore;

using Microsoft.EntityFrameworkCore;

public class StateStoreDbContext(DbContextOptions<StateStoreDbContext> options) : DbContext(options)
{
    public DbSet<JobStateRecord> JobStates => Set<JobStateRecord>();
    public DbSet<ServiceHealthRecord> ServiceHealthRecords => Set<ServiceHealthRecord>();
    public DbSet<ChainRunRecord> ChainRunRecords => Set<ChainRunRecord>();
    public DbSet<FlowRunRecord> FlowRunRecords => Set<FlowRunRecord>();
    public DbSet<FlowStepRecord> FlowStepRecords => Set<FlowStepRecord>();
    public DbSet<SpanRecord> SpanRecords => Set<SpanRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JobStateRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.JobId).IsUnique();
            entity.HasIndex(e => e.CorrelationId);
            entity.HasIndex(e => e.TraceId);
            entity.HasIndex(e => new { e.ServiceName, e.Status });
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.JobId).HasMaxLength(64);
            entity.Property(e => e.ServiceName).HasMaxLength(128);
            entity.Property(e => e.TraceId).HasMaxLength(64);
            entity.Property(e => e.SpanId).HasMaxLength(32);
            entity.Property(e => e.CorrelationId).HasMaxLength(64);
        });

        modelBuilder.Entity<ServiceHealthRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ServiceName);
            entity.HasIndex(e => e.CheckedAt);
            entity.Property(e => e.ServiceName).HasMaxLength(128);
            entity.Property(e => e.CheckedByService).HasMaxLength(128);
            entity.Property(e => e.TraceId).HasMaxLength(64);
        });

        modelBuilder.Entity<ChainRunRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ChainRunId).IsUnique();
            entity.HasIndex(e => e.CorrelationId);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.ChainRunId).HasMaxLength(64);
            entity.Property(e => e.CorrelationId).HasMaxLength(64);
            entity.Property(e => e.TraceId).HasMaxLength(64);
        });

        modelBuilder.Entity<FlowRunRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.FlowRunId).IsUnique();
            entity.HasIndex(e => e.CorrelationId);
            entity.HasIndex(e => e.TraceId);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.FlowRunId).HasMaxLength(64);
            entity.Property(e => e.FlowName).HasMaxLength(128);
            entity.Property(e => e.CorrelationId).HasMaxLength(64);
            entity.Property(e => e.TraceId).HasMaxLength(64);
        });

        modelBuilder.Entity<FlowStepRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.FlowRunId);
            entity.HasIndex(e => e.TraceId);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.FlowRunId).HasMaxLength(64);
            entity.Property(e => e.StepName).HasMaxLength(128);
            entity.Property(e => e.ServiceName).HasMaxLength(128);
            entity.Property(e => e.TraceId).HasMaxLength(64);
            entity.Property(e => e.SpanId).HasMaxLength(32);
            entity.Property(e => e.RetryAttempt).HasDefaultValue(0);
            entity.Property(e => e.MaxRetries).HasDefaultValue(0);
        });

        modelBuilder.Entity<SpanRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TraceId);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.TraceId).HasMaxLength(64);
            entity.Property(e => e.SpanId).HasMaxLength(32);
            entity.Property(e => e.ParentSpanId).HasMaxLength(32);
            entity.Property(e => e.ServiceName).HasMaxLength(128);
            entity.Property(e => e.OperationName).HasMaxLength(256);
        });
    }
}
