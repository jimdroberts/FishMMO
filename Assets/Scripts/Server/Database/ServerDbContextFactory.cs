using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Server
{
    public class ServerDbContextFactory : IDesignTimeDbContextFactory<ServerDbContext>
    {
        public string databaseFile = "EFDatabase.sqlite";

        public ServerDbContext CreateDbContext()
        {
            return CreateDbContext(new string[] { });
        }
        
        public ServerDbContext CreateDbContext(string[] args)
        {
            // SQLITE
            /*string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), databaseFile);
            
            var optionsBuilder = new DbContextOptionsBuilder<ServerDbContext>();
            optionsBuilder*/
            
            // Postgresql
            var optionsBuilder = new DbContextOptionsBuilder<ServerDbContext>();
            optionsBuilder
                /**
                 * TODO:
                 * this should probably come from the configuration files for each server. That would give
                 * the user the ability to run servers as certain users (and thus limit what each server has permission
                 * to do
                 **/
                .UseNpgsql("Host=localhost;Database=fish_mmo;Username=user;Password=p@55w0rd!");
                //.UseSnakeCaseNamingConvention();

            return new ServerDbContext(optionsBuilder.Options);
        }
    }
}