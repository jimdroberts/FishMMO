using System;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace FishMMO.Database.Redis
{
	public class RedisDbContextFactory
	{
		private string configPath = "";
		private readonly Lazy<ConnectionMultiplexer> connectionMultiplexer;

		public RedisDbContextFactory(string configPath)
		{
			this.configPath = configPath;

			string basePath = string.IsNullOrWhiteSpace(this.configPath) ? AppDomain.CurrentDomain.BaseDirectory : this.configPath;

			IConfiguration configuration = new ConfigurationBuilder()
				.SetBasePath(basePath)
				.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
				.Build();

			string? connectionString = configuration.GetConnectionString("Redis");

			if (configuration == null || string.IsNullOrWhiteSpace(connectionString))
			{
				throw new InvalidOperationException("Redis configuration is missing or invalid.");
			}

			connectionMultiplexer = new Lazy<ConnectionMultiplexer>(() =>
			{
				return ConnectionMultiplexer.Connect(connectionString);
			});
		}

		public IDatabase GetDatabase()
		{
			return connectionMultiplexer.Value.GetDatabase();
		}
	}
}