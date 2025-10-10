// tournament-controller/Data.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

public class TournamentDb : DbContext
{
    public TournamentDb(DbContextOptions<TournamentDb> options) : base(options) { }

    public DbSet<Team> Teams => Set<Team>();
    public DbSet<SessionRow> Sessions => Set<SessionRow>();
    public DbSet<GameResult> Games => Set<GameResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // --- Team ---
        modelBuilder.Entity<Team>(e =>
        {
            e.ToTable("teams");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(128);
            e.Property(x => x.CreatedUtc).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();      // unique team names
        });

        // --- SessionRow ---
        modelBuilder.Entity<SessionRow>(SessionRowConfig);

        // --- GameResult ---
        var u64ToI64 = new ValueConverter<ulong, long>(
            v => unchecked((long)v),
            v => unchecked((ulong)v)
        );

        modelBuilder.Entity<SessionRow>(e =>
{
    e.ToTable("sessions");
    e.HasKey(x => x.Id);

    e.Property(x => x.Session).IsRequired().HasMaxLength(64);
    e.Property(x => x.ObsType).IsRequired().HasMaxLength(32);
    e.Property(x => x.TotalGames).IsRequired();
    e.Property(x => x.CreatedUtc).IsRequired();

    e.HasIndex(x => x.Session).IsUnique();

    e.HasOne(x => x.Team)
     .WithMany(x => x.Sessions)
     .HasForeignKey(x => x.TeamId)
     .OnDelete(DeleteBehavior.Cascade);
});
    }

    private static void SessionRowConfig(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<SessionRow> e)
    {
        e.ToTable("sessions");
        e.HasKey(x => x.Id);

        e.Property(x => x.Session).IsRequired().HasMaxLength(64);
        e.Property(x => x.ObsType).IsRequired().HasMaxLength(32);
        e.Property(x => x.TotalGames).IsRequired();
        e.Property(x => x.CreatedUtc).IsRequired();

        e.HasIndex(x => x.Session).IsUnique(); // externally visible session token must be unique

        e.HasOne(x => x.Team)
         .WithMany(x => x.Sessions)
         .HasForeignKey(x => x.TeamId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

public class Team
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public ICollection<SessionRow> Sessions { get; set; } = new List<SessionRow>();
}

public class SessionRow
{
    public int Id { get; set; }
    public string Session { get; set; } = "";     // controller session id (Guid N)
    public string ObsType { get; set; } = "Dense11";
    public int TotalGames { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public int TeamId { get; set; }
    public Team Team { get; set; } = default!;
    public ICollection<GameResult> Games { get; set; } = new List<GameResult>();
}

public class GameResult
{
    public int Id { get; set; }
    public ulong Seed { get; set; }
    public int Score { get; set; }
    public int Length { get; set; }
    public int Steps { get; set; }
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedUtc { get; set; }

    public int SessionRowId { get; set; }
    public SessionRow Session { get; set; } = default!;
}
