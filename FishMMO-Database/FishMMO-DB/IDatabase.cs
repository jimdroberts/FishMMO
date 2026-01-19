using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Monitoring.Health;
using FishMMO.Database.Npgsql.Monitoring.Metrics;

namespace FishMMO.Database
{
	/// <summary>
	/// Defines the contract for the database orchestrator and facade.
	/// Provides centralized access to database services, health monitoring, and metrics.
	/// Abstracts the underlying database implementation for testability and flexibility.
	/// </summary>
	/// <remarks>
	/// This interface follows the Facade Pattern and enables:
	/// - Easy mocking/stubbing for unit tests
	/// - Swapping implementations (e.g., in-memory for testing)
	/// - Dependency injection in Unity or server frameworks
	/// - Decoupling client code from concrete database implementations
	/// </remarks>
	public interface IDatabase
	{
		/// <summary>
		/// Gets the service registry for accessing database services.
		/// Provides type-safe access to all registered database service interfaces.
		/// </summary>
		IDatabaseServiceRegistry ServiceRegistry { get; }

		/// <summary>
		/// Gets the database health monitor for connectivity and performance checks.
		/// Use this to verify database availability and response times.
		/// </summary>
		DatabaseHealthMonitor HealthMonitor { get; }

		/// <summary>
		/// Gets the database metrics tracker for operation statistics.
		/// Provides insights into query performance and connection pool usage.
		/// </summary>
		DatabaseMetricsTracker MetricsTracker { get; }

		/// <summary>
		/// Gets the database context factory.
		/// Exposed for direct access when needed (e.g., custom queries, migrations, raw SQL).
		/// In most cases, prefer using services from ServiceRegistry.
		/// </summary>
		INpgsqlDbContextFactory DbContextFactory { get; }
	}
}