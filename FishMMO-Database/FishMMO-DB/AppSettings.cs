using System;

namespace FishMMO.Database
{
	[Serializable]
	public class AppSettings
	{
		public NpgsqlSettings Npgsql;
		public RedisSettings Redis;
	}

	[Serializable]
	public class NpgsqlSettings
	{
		public string Database;
		public string Username;
		public string Password;
		public string Host;
		public string Port;
	}

	[Serializable]
	public class RedisSettings
	{
		public string Host;
		public string Port;
		public string Password;
	}
}