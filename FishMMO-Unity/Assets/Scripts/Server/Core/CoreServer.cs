using System;
using System.IO;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Redis;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Server.Core
{
	/// <summary>
	/// Core, engine-agnostic server logic: configuration and database initialization.
	/// </summary>
	public class CoreServer : ICoreServer
	{
		/// <summary>
		/// Gets the Npgsql database context factory for PostgreSQL operations.
		/// </summary>
		public NpgsqlDbContextFactory NpgsqlDbContextFactory { get; private set; }

		/// <summary>
		/// Gets the Redis database context factory for Redis operations.
		/// </summary>
		public RedisDbContextFactory RedisDbContextFactory { get; private set; }

		/// <summary>
		/// Gets the remote address of the server.
		/// </summary>
		public string RemoteAddress { get; private set; }

		/// <summary>
		/// Gets the local address of the server.
		/// </summary>
		public string Address { get; private set; }

		/// <summary>
		/// Gets the port number the server is listening on.
		/// </summary>
		public ushort Port { get; private set; }

		private readonly IServerConfiguration config;
		private readonly IServerEvents events;

		private ServerType serverType = ServerType.Invalid;

		/// <summary>
		/// Initializes a new instance of the <see cref="CoreServer"/> class.
		/// </summary>
		/// <param name="config">The server configuration provider.</param>
		/// <param name="events">The server events handler.</param>
		public CoreServer(IServerConfiguration config, IServerEvents events)
		{
			this.config = config ?? throw new ArgumentNullException(nameof(config));
			this.events = events ?? throw new ArgumentNullException(nameof(events));
		}

		/// <summary>
		/// Initializes the core server with the specified remote address and server type name.
		/// </summary>
		/// <param name="remoteAddress">The remote address to use for initialization.</param>
		/// <param name="serverTypeName">The name of the server type.</param>
		/// <exception cref="InvalidOperationException">Thrown if the server type is invalid.</exception>
		public void Initialize(string remoteAddress, string serverTypeName)
		{
			RemoteAddress = remoteAddress;
			serverType = GetServerType(serverTypeName);

			if (serverType == ServerType.Invalid)
			{
				throw new InvalidOperationException($"Invalid server type: {serverTypeName}");
			}

			string workingDirectory = Constants.GetWorkingDirectory();
			Log.Debug("Server", $"Working directory: {workingDirectory}");

			config.SaveDefaultsIfMissing(serverTypeName, serverType.ToString(), workingDirectory);

			Address = config.GetString("Address", "127.0.0.1");
			Port = config.GetUShort("Port", 7777);

#if UNITY_EDITOR
			string dbConfigurationPath = Path.Combine(
				Path.Combine(workingDirectory, Constants.Configuration.SetupDirectory),
				"Development");
			NpgsqlDbContextFactory = new NpgsqlDbContextFactory(dbConfigurationPath, false);
#else
				NpgsqlDbContextFactory = new NpgsqlDbContextFactory(workingDirectory, false);
#endif

			RaiseLifecycleEvent();
		}

		/// <summary>
		/// Determines the <see cref="ServerType"/> based on the server type name string.
		/// </summary>
		/// <param name="serverTypeName">The name of the server type.</param>
		/// <returns>The corresponding <see cref="ServerType"/> enum value.</returns>
		private static ServerType GetServerType(string serverTypeName)
		{
			string upper = serverTypeName.ToUpperInvariant();
			if (upper.StartsWith("LOGIN"))
			{
				return ServerType.Login;
			}
			if (upper.StartsWith("WORLD"))
			{
				return ServerType.World;
			}
			if (upper.StartsWith("SCENE"))
			{
				return ServerType.Scene;
			}
			return ServerType.Invalid;
		}

		/// <summary>
		/// Raises the appropriate server lifecycle event based on the current server type.
		/// </summary>
		private void RaiseLifecycleEvent()
		{
			switch (serverType)
			{
				case ServerType.Login:
					events.OnLoginServerInitialized?.Invoke();
					break;
				case ServerType.World:
					events.OnWorldServerInitialized?.Invoke();
					break;
				case ServerType.Scene:
					events.OnSceneServerInitialized?.Invoke();
					break;
			}
		}
	}
}