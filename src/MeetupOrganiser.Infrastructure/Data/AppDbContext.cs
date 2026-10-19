using MeetupOrganiser.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace MeetupOrganiser.Infrastructure.Data;

public class AppDbContext : DbContext
{
    private readonly SpeakerPhotoInterceptor? _photoInterceptor;

    public AppDbContext(DbContextOptions<AppDbContext> options, SpeakerPhotoInterceptor? photoInterceptor = null) : base(options)
    {
        _photoInterceptor = photoInterceptor;
    }

    public DbSet<MeetupGroup> MeetupGroups => Set<MeetupGroup>();
    public DbSet<Speaker> Speakers => Set<Speaker>();
    public DbSet<MeetupEvent> MeetupEvents => Set<MeetupEvent>();
    public DbSet<EventSpeakerSession> EventSpeakerSessions => Set<EventSpeakerSession>();
    public DbSet<MagicLinkToken> MagicLinkTokens => Set<MagicLinkToken>();
    public DbSet<SocialPostTemplate> SocialPostTemplates => Set<SocialPostTemplate>();
    public DbSet<GeneratedAsset> GeneratedAssets => Set<GeneratedAsset>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<SpeakerBookingToken> SpeakerBookingTokens => Set<SpeakerBookingToken>();
    public bool SkipSpeakerPhotoLocalization { get; set; }
    public bool AllowBookedDateChange { get; set; }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        SaveChangesAsync(acceptAllChangesOnSuccess).GetAwaiter().GetResult();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        SaveChangesAsync(true, cancellationToken);

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        if (!SkipSpeakerPhotoLocalization && _photoInterceptor is not null)
        {
            await _photoInterceptor.LocalizeAsync(this, cancellationToken);
            SkipSpeakerPhotoLocalization = true;
            try
            {
                return await SaveWithCapacityGuardAsync(acceptAllChangesOnSuccess, cancellationToken);
            }
            finally
            {
                SkipSpeakerPhotoLocalization = false;
            }
        }
        return await SaveWithCapacityGuardAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private async Task<int> SaveWithCapacityGuardAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken)
    {
        ChangeTracker.DetectChanges();
        var eventIds = ChangeTracker.Entries<EventSpeakerSession>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .SelectMany(e => e.State == EntityState.Modified
                ? new[] { e.Entity.MeetupEventId, e.OriginalValues.GetValue<Guid>(nameof(EventSpeakerSession.MeetupEventId)) }
                : new[] { e.Entity.MeetupEventId })
            .Concat(ChangeTracker.Entries<MeetupEvent>()
                .Where(e => e.State is EntityState.Added or EntityState.Modified)
                .Select(e => e.Entity.Id)).Distinct().Order().ToArray();

        if (eventIds.Length == 0)
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        async Task<int> SaveGuardedAsync()
        {
            foreach (var id in eventIds)
            {
                await LockEventAsync(id, cancellationToken);
                var stored = await MeetupEvents.AsNoTracking().Where(e => e.Id == id)
                    .Select(e => new { e.IsOpenForBookings }).SingleOrDefaultAsync(cancellationToken);
                var tracked = ChangeTracker.Entries<MeetupEvent>().FirstOrDefault(e => e.Entity.Id == id);
                if (stored is not null && tracked is not null && tracked.State == EntityState.Modified &&
                    tracked.Property(e => e.EventDateTimeUtc).IsModified && !AllowBookedDateChange &&
                    await EventSpeakerSessions.AsNoTracking().AnyAsync(s => s.MeetupEventId == id, cancellationToken))
                    throw new InvalidOperationException("This date has assigned speakers. Reload the event and acknowledge the date change before saving.");
                var bookingFlagChanged = tracked is not null && (tracked.State == EntityState.Added ||
                    tracked.Property(e => e.IsOpenForBookings).IsModified);
                var isOpen = bookingFlagChanged ? tracked!.Entity.IsOpenForBookings : stored?.IsOpenForBookings ?? false;
                if (!isOpen)
                    continue;

                var assigned = await EventSpeakerSessions.AsNoTracking()
                    .Where(s => s.MeetupEventId == id).Select(s => new { s.Id, s.SpeakerId }).ToListAsync(cancellationToken);
                var places = assigned.ToDictionary(s => s.Id, s => s.SpeakerId);
                foreach (var entry in ChangeTracker.Entries<EventSpeakerSession>()
                    .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
                {
                    places.Remove(entry.Entity.Id);
                    if (entry.State != EntityState.Deleted && entry.Entity.MeetupEventId == id)
                        places[entry.Entity.Id] = entry.Entity.SpeakerId;
                }
                if (places.Count > 2 || places.Values.Distinct().Count() != places.Count)
                    throw new InvalidOperationException("An open meetup date has two places and cannot assign the same speaker twice.");
            }
            return await base.SaveChangesAsync(false, cancellationToken);
        }

        if (!Database.IsRelational() || Database.CurrentTransaction is not null)
        {
            var count = await SaveGuardedAsync();
            if (acceptAllChangesOnSuccess) ChangeTracker.AcceptAllChanges();
            return count;
        }

        var result = await Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
            var count = await SaveGuardedAsync();
            await transaction.CommitAsync(cancellationToken);
            return count;
        });
        if (acceptAllChangesOnSuccess) ChangeTracker.AcceptAllChanges();
        return result;
    }

    public async Task LockEventAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        if (Database.IsSqlServer())
            await Database.ExecuteSqlInterpolatedAsync(
                $"SELECT [Id] FROM [MeetupEvents] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {eventId}", cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SpeakerBookingToken>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.TokenHash).IsRequired().HasMaxLength(64);
            entity.Property(t => t.Email).HasMaxLength(250);
            entity.Property(t => t.PublicKey).HasMaxLength(64);
            entity.HasIndex(t => t.TokenHash).IsUnique();
            entity.HasIndex(t => new { t.MeetupGroupId, t.Email, t.CreatedAtUtc });
            entity.HasIndex(t => t.BookedSessionId).IsUnique();
            entity.HasOne(t => t.MeetupGroup).WithMany().HasForeignKey(t => t.MeetupGroupId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(t => t.Speaker).WithMany().HasForeignKey(t => t.SpeakerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(t => t.BookedSession).WithMany().HasForeignKey(t => t.BookedSessionId).OnDelete(DeleteBehavior.SetNull);
        });

        // MeetupGroup
        modelBuilder.Entity<MeetupGroup>(entity =>
        {
            entity.HasKey(g => g.Id);
            entity.Property(g => g.Name).IsRequired().HasMaxLength(200);
            entity.Property(g => g.MeetupUrlName).HasMaxLength(100);
            entity.Property(g => g.MeetupApiKeyOverride).HasMaxLength(500);
            entity.HasMany(g => g.Events)
                  .WithOne(e => e.MeetupGroup)
                  .HasForeignKey(e => e.MeetupGroupId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(g => g.Sponsors)
                  .WithOne()
                  .HasForeignKey(s => s.MeetupGroupId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Speaker
        modelBuilder.Entity<Speaker>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Name).IsRequired().HasMaxLength(200);
            entity.HasIndex(s => s.Email);
        });

        // MeetupEvent
        modelBuilder.Entity<MeetupEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(250);
            entity.HasIndex(e => e.EventDateTimeUtc);
            entity.HasMany(e => e.Sessions)
                  .WithOne(s => s.MeetupEvent)
                  .HasForeignKey(s => s.MeetupEventId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.GeneratedAssets)
                  .WithOne(a => a.MeetupEvent)
                  .HasForeignKey(a => a.MeetupEventId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // EventSpeakerSession
        modelBuilder.Entity<EventSpeakerSession>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.TalkTitle).IsRequired().HasMaxLength(300);
            entity.HasOne(s => s.Speaker)
                  .WithMany(sp => sp.Sessions)
                  .HasForeignKey(s => s.SpeakerId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(s => s.MagicLinkTokens)
                  .WithOne(t => t.EventSpeakerSession)
                  .HasForeignKey(t => t.EventSpeakerSessionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // MagicLinkToken
        modelBuilder.Entity<MagicLinkToken>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.TokenHash).IsRequired().HasMaxLength(128);
            entity.HasIndex(t => t.TokenHash).IsUnique();
            entity.HasOne(t => t.Speaker)
                  .WithMany(s => s.MagicLinkTokens)
                  .HasForeignKey(t => t.SpeakerId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // SocialPostTemplate
        modelBuilder.Entity<SocialPostTemplate>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Platform).IsRequired().HasMaxLength(50);
            entity.Property(t => t.Name).IsRequired().HasMaxLength(150);
        });

        // GeneratedAsset
        modelBuilder.Entity<GeneratedAsset>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.AssetType).IsRequired().HasMaxLength(50);
        });

        // AppSetting
        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.HasKey(s => s.Key);
            entity.Property(s => s.Key).HasMaxLength(150);
            entity.Property(s => s.Category).HasMaxLength(50);
            entity.Property(s => s.Description).HasMaxLength(300);
        });
    }
}
