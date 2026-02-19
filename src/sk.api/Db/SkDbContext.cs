using Microsoft.EntityFrameworkCore;

namespace sk.api.Db;

public class SkDbContext : DbContext
{
    public SkDbContext(DbContextOptions<SkDbContext> options) : base(options)
    {
    }
}
