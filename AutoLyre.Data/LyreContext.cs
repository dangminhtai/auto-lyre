using AutoLyre.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AutoLyre.Data;

public class LyreContext : DbContext
{
    public LyreContext(DbContextOptions<LyreContext> options) : base(options) { }

    public DbSet<History> History { get; set; } = null!;
}