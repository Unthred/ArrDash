using ArrDash.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ArrDash.Data;

public sealed class ArrDashDbContext(DbContextOptions<ArrDashDbContext> options) : DbContext(options)
{
    public DbSet<PlayEventEntity> PlayEvents => Set<PlayEventEntity>();
    public DbSet<SyncCursorEntity> SyncCursors => Set<SyncCursorEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlayEventEntity>(entity =>
        {
            entity.HasIndex(e => new { e.Source, e.ExternalPlayId }).IsUnique();
            entity.HasIndex(e => new { e.Source, e.PlayedAtUtc });
            entity.HasIndex(e => new { e.Source, e.UserDisplayName });
            entity.HasIndex(e => new { e.MediaType, e.PlayedAtUtc });
            entity.Property(e => e.Source).HasMaxLength(32);
            entity.Property(e => e.ExternalPlayId).HasMaxLength(256);
            entity.Property(e => e.UserDisplayName).HasMaxLength(256);
            entity.Property(e => e.MediaType).HasMaxLength(32);
        });

        modelBuilder.Entity<SyncCursorEntity>(entity =>
        {
            entity.HasKey(e => e.Source);
            entity.Property(e => e.Source).HasMaxLength(32);
        });
    }
}
