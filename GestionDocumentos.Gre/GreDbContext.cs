using Microsoft.EntityFrameworkCore;

namespace GestionDocumentos.Gre;

public sealed class GreDbContext : DbContext
{
    public GreDbContext(DbContextOptions<GreDbContext> options)
        : base(options)
    {
    }

    public DbSet<GreInfo> GreInfos => Set<GreInfo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GreInfo>(entity =>
        {
            entity.ToTable("GreInfos");
            entity.HasKey(e => e.id);
        });
    }
}
