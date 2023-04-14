using Microsoft.EntityFrameworkCore;

namespace Server
{
    public class DatabaseInitializerSystem : ServerBehaviour
    {
        public override void InitializeOnce()
        {
            using ServerDbContext dbContext = Server.DbContextFactory.CreateDbContext();

            // TODO: this should probably be moved out of here; we should use EF migration tools so that
            // database updates can be run separately from running the servers
            //dbContext.Database.EnsureDeleted();

            dbContext.Database.EnsureCreated();
            dbContext.Database.Migrate();
        }
    }
}