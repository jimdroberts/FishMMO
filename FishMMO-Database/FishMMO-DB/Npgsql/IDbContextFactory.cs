using System;
using System.Threading;
using System.Threading.Tasks;

namespace FishMMO.Database.Npgsql
{
	/// <summary>
	/// Core factory interface for creating NpgsqlDbContext instances.
	/// Implementations must be thread-safe and create new contexts per call.
	/// DbContext instances are short-lived and should not be pooled.
	/// </summary>
	/// <remarks>
	/// This interface follows the Interface Segregation Principle by providing
	/// only the essential context creation functionality. For monitoring capabilities,
	/// see <see cref="IDbContextFactoryMonitoring"/>.
	/// </remarks>
	public interface IDbContextFactory : IDisposable, IAsyncDisposable
	{
		/// <summary>
		/// Creates a new DbContext instance.
		/// Each call creates a fresh context - safe for concurrent use.
		/// </summary>
		/// <returns>A new NpgsqlDbContext instance.</returns>
		NpgsqlDbContext CreateDbContext();

		/// <summary>
		/// Asynchronously creates a new DbContext instance.
		/// Each call creates a fresh context - safe for concurrent use.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A new NpgsqlDbContext instance.</returns>
		Task<NpgsqlDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default);

		/// <summary>
		/// Shuts down the factory and rejects new DbContext creation.
		/// Unity-friendly synchronous shutdown (safe to call from main thread).
		/// </summary>
		void Shutdown();

		/// <summary>
		/// Asynchronously shuts down the factory and rejects new DbContext creation.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A task representing the asynchronous shutdown operation.</returns>
		Task ShutdownAsync(CancellationToken cancellationToken = default);
	}
}