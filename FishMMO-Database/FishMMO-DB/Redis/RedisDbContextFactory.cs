using System;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace FishMMO.Database.Redis
{
	public class RedisDbContextFactory
	{
		private string configPath = "";
		private static string? host;
		private static string? port;
		private static string? password;

		private static Lazy<ConfigurationOptions> ConfigOptions
		= new Lazy<ConfigurationOptions>(() =>
		{
			var configOptions = new ConfigurationOptions();
			configOptions.EndPoints.Add(host + ":" + port);
			configOptions.Password = password;
			configOptions.AbortOnConnectFail = false;
			return configOptions;
		});

		private static Lazy<ConnectionMultiplexer> multiplexer
		= new Lazy<ConnectionMultiplexer>(() =>
		{
			return ConnectionMultiplexer.Connect(ConfigOptions.Value);
		});

		public static ConnectionMultiplexer Connection { get { return multiplexer.Value; } }

		public RedisDbContextFactory(string configPath)
		{
			this.configPath = configPath;

			string basePath = string.IsNullOrWhiteSpace(this.configPath) ? AppDomain.CurrentDomain.BaseDirectory : this.configPath;

			IConfiguration configuration = new ConfigurationBuilder()
				.SetBasePath(basePath)
				.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
				.Build();

			host = configuration.GetSection("Redis")["Host"] ?? "127.0.0.1";
			port = configuration.GetSection("Redis")["Port"] ?? "6379";
			password = configuration?.GetSection("Redis")["Password"] ?? "pass";
		}

		public void CloseRedis()
		{
			Connection.Close();
		}

		public IDatabase GetDatabase()
		{
			return Connection.GetDatabase();
		}
	}
}