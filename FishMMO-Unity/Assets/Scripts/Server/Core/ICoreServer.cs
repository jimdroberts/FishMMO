using FishMMO.Database.Npgsql;
using FishMMO.Database.Redis;

namespace FishMMO.Server.Core
{
	/// <summary>
	/// Represents the core, engine-agnostic server logic.
	/// Defines the contract for initializing and accessing core server data.
	/// </summary>
	public interface ICoreServer
	{
		/// <summary>
		/// Gets the Npgsql database context factory for PostgreSQL operations.
		/// </summary>
		NpgsqlDbContextFactory NpgsqlDbContextFactory { get; }

		/// <summary>
		/// Gets the Redis database context factory for Redis operations.
		/// </summary>
		RedisDbContextFactory RedisDbContextFactory { get; }

		/// <summary>
		/// Gets the remote address of the server.
		/// </summary>
		string RemoteAddress { get; }

		/// <summary>
		/// Gets the local address of the server.
		/// </summary>
		string Address { get; }

		/// <summary>
		/// Gets the port number the server is listening on.
		/// </summary>
		ushort Port { get; }

		/// <summary>
		/// Initializes the core server with the specified remote address and server type name.
		/// </summary>
		/// <param name="remoteAddress">The remote address to use for initialization.</param>
		/// <param name="serverTypeName">The name of the server type.</param>
		void Initialize(string remoteAddress, string serverTypeName);
	}
}