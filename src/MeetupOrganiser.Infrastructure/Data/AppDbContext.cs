using MeetupOrganiser.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace MeetupOrganiser.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<MeetupGroup> MeetupGroups => Set<MeetupGroup>();
    public DbSet<Speaker> Speakers => Set<Speaker>();
    public DbSet<MeetupEvent> MeetupEvents => Set<MeetupEvent>();
    public DbSet<EventSpeakerSession> EventSpeakerSessions => Set<EventSpeakerSession>();
    public DbSet<MagicLinkToken> MagicLinkTokens => Set<MagicLinkToken>();
    public DbSet<SocialPostTemplate> SocialPostTemplates => Set<SocialPostTemplate>();
    public DbSet<GeneratedAsset> GeneratedAssets => Set<GeneratedAsset>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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
