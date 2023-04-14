using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Server
{
    public class ServerDbContextFactory : IDesignTimeDbContextFactory<ServerDbContext>
    {
		public string databaseFile = "EFDatabase.sqlite";

		public Server Server { get; private set; }

        public ServerDbContextFactory(Server server)
        {
            Server = server;
        }

		public ServerDbContext CreateDbContext()
        {
            return CreateDbContext(new string[] { });
        }
        
        public ServerDbContext CreateDbContext(string[] args)
        {
			DbContextOptionsBuilder optionsBuilder = new DbContextOptionsBuilder<ServerDbContext>();

			if (Server.configuration.TryGetString("DbAddress", out string dbAddress) &&
                Server.configuration.TryGetString("DbName", out string dbName) &&
                Server.configuration.TryGetString("DbUsername", out string dbUsername) &&
                Server.configuration.TryGetString("DbPassword", out string dbPassword))
            {
                string hostString = "Host=" + dbAddress + ";" +
                                    "Database=" + dbName + ";" +
                                    "Username=" + dbUsername + ";" +
                                    "Password=" + dbPassword;
                                    
				optionsBuilder.UseNpgsql(hostString);
				//.UseSnakeCaseNamingConvention();
				
			}
			return new ServerDbContext(optionsBuilder.Options);
		}
    }
}