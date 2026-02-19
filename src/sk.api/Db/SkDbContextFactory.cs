namespace sk.api.Db;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;


public class DsstatsContextFactory : IDesignTimeDbContextFactory<SkDbContext>
{
    public SkDbContext CreateDbContext(string[] args)
    {
        var connectionString = "Data Source=/data/sk/sk.db";

        var optionsBuilder = new DbContextOptionsBuilder<SkDbContext>();
        optionsBuilder.UseSqlite(connectionString, options =>
        {
            options.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            options.MigrationsAssembly("sk.api");
        });

        return new SkDbContext(optionsBuilder.Options);
    }
}

