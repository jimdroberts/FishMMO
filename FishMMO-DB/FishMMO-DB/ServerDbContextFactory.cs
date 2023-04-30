using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FishMMO_DB
{
    public class ServerDbContextFactory : IDesignTimeDbContextFactory<ServerDbContext>
    {

		//public Server Server { get; private set; }

        public ServerDbContextFactory(/*Server server*/)
        {
            //Server = server;
        }

		public ServerDbContext CreateDbContext()
        {
            return CreateDbContext(new string[] { });
        }
        
        public ServerDbContext CreateDbContext(string[] args)
        {
			DbContextOptionsBuilder optionsBuilder = new DbContextOptionsBuilder<ServerDbContext>();

			string dbAddress = "127.0.0.1";
			int dbPort = 5432;
			var dbName = "fishmmo";
			var dbUsername = "postgres";
			var dbPassword = "test";

			string hostString = "Host=" + dbAddress + ";" +
			                    "Port=" + dbPort + ";" +
			                    "Database=" + dbName + ";" +
			                    "Username=" + dbUsername + ";" +
			                    "Password=" + dbPassword + ";";

			hostString = "Host=localhost;Database=fish_mmo;Username=user;Password=pass";
                                    
			optionsBuilder.UseNpgsql(hostString).UseSnakeCaseNamingConvention();
            
			return new ServerDbContext(optionsBuilder.Options);
		}
    }
}