using ArrDash.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ArrDash.Data;

public sealed class ArrDashDbContext(DbContextOptions<ArrDashDbContext> options) : DbContext(options)
{
    public DbSet<PlayEventEntity> PlayEvents => Set<PlayEventEntity>();
    public DbSet<SyncCursorEntity> SyncCursors => Set<SyncCursorEntity>();
    public DbSet<TraktAccountEntity> TraktAccounts => Set<TraktAccountEntity>();
    public DbSet<TraktHistoryLinkEntity> TraktHistoryLinks => Set<TraktHistoryLinkEntity>();
    public DbSet<MediaIdentityEntity> MediaIdentities => Set<MediaIdentityEntity>();
    public DbSet<MediaInventoryItemEntity> MediaInventoryItems => Set<MediaInventoryItemEntity>();
    public DbSet<ArrTagEntity> ArrTags => Set<ArrTagEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlayEventEntity>(entity =>
        {
            entity.HasIndex(e => new { e.Source, e.ExternalPlayId }).IsUnique();
            entity.HasIndex(e => new { e.Source, e.PlayedAtUtc });
            entity.HasIndex(e => new { e.Source, e.UserDisplayName });
            entity.HasIndex(e => new { e.MediaType, e.PlayedAtUtc });
            entity.HasIndex(e => e.CanonicalMediaKey);
            entity.Property(e => e.Source).HasMaxLength(32);
            entity.Property(e => e.ExternalPlayId).HasMaxLength(256);
            entity.Property(e => e.UserDisplayName).HasMaxLength(256);
            entity.Property(e => e.MediaType).HasMaxLength(32);
            entity.Property(e => e.Origin).HasMaxLength(32);
            entity.Property(e => e.ImdbId).HasMaxLength(32);
            entity.Property(e => e.CanonicalMediaKey).HasMaxLength(512);
        });

        modelBuilder.Entity<SyncCursorEntity>(entity =>
        {
            entity.HasKey(e => e.Source);
            entity.Property(e => e.Source).HasMaxLength(32);
        });

        modelBuilder.Entity<TraktAccountEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(64);
            entity.Property(e => e.TraktUsername).HasMaxLength(128);
            entity.Property(e => e.CanonicalUserName).HasMaxLength(256);
            entity.HasIndex(e => e.TraktUsername);
        });

        modelBuilder.Entity<TraktHistoryLinkEntity>(entity =>
        {
            entity.HasIndex(e => new { e.AccountId, e.TraktHistoryId }).IsUnique();
            entity.HasIndex(e => e.PlayEventId);
            entity.Property(e => e.AccountId).HasMaxLength(64);
            entity.Property(e => e.Direction).HasMaxLength(16);
            entity.Property(e => e.CanonicalMediaKey).HasMaxLength(512);
        });

        modelBuilder.Entity<MediaIdentityEntity>(entity =>
        {
            entity.HasKey(e => e.CanonicalMediaKey);
            entity.Property(e => e.CanonicalMediaKey).HasMaxLength(512);
            entity.Property(e => e.MediaType).HasMaxLength(32);
            entity.Property(e => e.ImdbId).HasMaxLength(32);
            entity.HasIndex(e => e.ImdbId);
            entity.HasIndex(e => e.TmdbId);
            entity.HasIndex(e => e.TraktId);
        });

        modelBuilder.Entity<MediaInventoryItemEntity>(entity =>
        {
            entity.HasIndex(e => new { e.Source, e.SourceItemId }).IsUnique();
            entity.HasIndex(e => e.TmdbId);
            entity.HasIndex(e => e.TvdbId);
            entity.HasIndex(e => e.SizeOnDiskBytes);
            entity.HasIndex(e => e.LastSeenUtc);
            entity.Property(e => e.Source).HasMaxLength(32);
            entity.Property(e => e.MediaType).HasMaxLength(32);
            entity.Property(e => e.TitleSlug).HasMaxLength(512);
            entity.Property(e => e.ImdbId).HasMaxLength(32);
        });

        modelBuilder.Entity<ArrTagEntity>(entity =>
        {
            entity.HasIndex(e => new { e.Source, e.TagId }).IsUnique();
            entity.Property(e => e.Source).HasMaxLength(32);
            entity.Property(e => e.Label).HasMaxLength(256);
        });
    }
}
