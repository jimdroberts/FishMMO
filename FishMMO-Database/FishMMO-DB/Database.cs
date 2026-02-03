using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Monitoring.Health;
using FishMMO.Database.Npgsql.Monitoring.Metrics;
using FishMMO.Database.Npgsql.Monitoring.Diagnostics;

namespace FishMMO.Database
{
	/// <summary>
	/// Main orchestrator and facade for the FishMMO database layer.
	/// Provides centralized access to database services, health monitoring, and metrics.
	/// Follows Facade Pattern: simplifies complex subsystem interactions.
	/// Follows Single Responsibility Principle: coordinates database infrastructure components.
	/// Designed to be instantiated and managed by the server orchestrator (e.g., Server.cs).
	/// </summary>
	public sealed class Database : IDatabase
	{
		/// <inheritdoc/>
		public IDatabaseServiceRegistry ServiceRegistry { get; private set; }

		/// <inheritdoc/>
		public DatabaseHealthMonitor HealthMonitor { get; private set; }

		/// <inheritdoc/>
		public DatabaseMetricsTracker MetricsTracker { get; private set; }

		/// <inheritdoc/>
		public INpgsqlDbContextFactory DbContextFactory { get; private set; }

		/// <summary>
		/// Initializes a new instance of the <see cref="Database"/> class with the specified configuration path.
		/// Creates the DbContext factory from appsettings.json, discovers and registers all services, and sets up monitoring.
		/// </summary>
		/// <param name="configPath">Path to configuration directory containing appsettings.json.</param>
		/// <param name="enableLogging">Enable sensitive data logging for development (default: false).</param>
		/// <param name="commandTimeout">Database command timeout in seconds (default: 10).</param>
		/// <param name="healthCheckWarningMs">Health check warning threshold in milliseconds (default: 100).</param>
		/// <param name="healthCheckCriticalMs">Health check critical threshold in milliseconds (default: 500).</param>
		/// <exception cref="ArgumentNullException">Thrown when configPath is null or empty.</exception>
		/// <exception cref="InvalidOperationException">Thrown when initialization fails.</exception>
		public Database(
			string configPath,
			bool enableLogging = false,
			int commandTimeout = 10,
			int healthCheckWarningMs = 100,
			int healthCheckCriticalMs = 500)
		{
			if (string.IsNullOrWhiteSpace(configPath))
				throw new ArgumentNullException(nameof(configPath));

			try
			{
				// Create the database context factory from appsettings.json
				DbContextFactory = new NpgsqlDbContextFactory(
					configPath,
					enableLogging,
					commandTimeout);

				// Initialize service registry (composition root)
				ServiceRegistry = CreateNpgsqlServiceRegistry(DbContextFactory);

				// Initialize health monitoring
				HealthMonitor = new DatabaseHealthMonitor(
					DbContextFactory,
					healthCheckWarningMs,
					healthCheckCriticalMs);

				// Initialize metrics tracking
				MetricsTracker = new DatabaseMetricsTracker();
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException(
					$"Failed to initialize database orchestrator: {ex.Message}",
					ex);
			}
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="Database"/> class with default configuration path.
		/// Uses the parent directory of the current AppDomain base directory to locate appsettings.json.
		/// </summary>
		/// <param name="enableLogging">Enable sensitive data logging for development (default: false).</param>
		/// <param name="commandTimeout">Database command timeout in seconds (default: 10).</param>
		/// <param name="healthCheckWarningMs">Health check warning threshold in milliseconds (default: 100).</param>
		/// <param name="healthCheckCriticalMs">Health check critical threshold in milliseconds (default: 500).</param>
		/// <exception cref="InvalidOperationException">Thrown when initialization fails.</exception>
		public Database(
			bool enableLogging = false,
			int commandTimeout = 10,
			int healthCheckWarningMs = 100,
			int healthCheckCriticalMs = 500)
		{
			try
			{
				// Create the database context factory with default configuration path
				DbContextFactory = new NpgsqlDbContextFactory();

				// Initialize service registry (composition root)
				ServiceRegistry = CreateNpgsqlServiceRegistry(DbContextFactory);

				// Initialize health monitoring
				HealthMonitor = new DatabaseHealthMonitor(
					DbContextFactory,
					healthCheckWarningMs,
					healthCheckCriticalMs);

				// Initialize metrics tracking
				MetricsTracker = new DatabaseMetricsTracker();
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException(
					$"Failed to initialize database orchestrator: {ex.Message}",
					ex);
			}
		}

		private IDatabaseServiceRegistry CreateNpgsqlServiceRegistry(INpgsqlDbContextFactory dbContextFactory)
		{
			if (dbContextFactory == null)
				throw new ArgumentNullException(nameof(dbContextFactory));

			var registry = new NpgsqlServiceRegistry();
			RegisterNpgsqlServicesByReflection(registry, dbContextFactory);
			return registry;
		}

		private static void RegisterNpgsqlServicesByReflection(NpgsqlServiceRegistry registry, INpgsqlDbContextFactory dbContextFactory)
		{
			if (registry == null)
				throw new ArgumentNullException(nameof(registry));
			if (dbContextFactory == null)
				throw new ArgumentNullException(nameof(dbContextFactory));

			// Discover all service interfaces and their concrete implementations, then register them.
			// This avoids a growing manual registration wall as new tables/services are added.
			const string interfaceNamespace = "FishMMO.Database.Npgsql.Services.Interfaces";

			var assembly = typeof(NpgsqlDbContextFactory).Assembly;
			var serviceInterfaces = assembly.GetTypes()
				.Where(t => t.IsInterface
					&& string.Equals(t.Namespace, interfaceNamespace, StringComparison.Ordinal)
					&& t.Name.EndsWith("Service", StringComparison.Ordinal))
				.OrderBy(t => t.FullName, StringComparer.Ordinal)
				.ToArray();

			var candidates = assembly.GetTypes()
				.Where(t => t.IsClass
					&& !t.IsAbstract
					&& t.Namespace != null
					&& t.Namespace.StartsWith("FishMMO.Database.Npgsql.Services", StringComparison.Ordinal))
				.ToArray();

			var registerOpenMethod = typeof(NpgsqlServiceRegistry).GetMethod(
				"Register",
				BindingFlags.Instance | BindingFlags.NonPublic);
			if (registerOpenMethod == null)
			{
				throw new InvalidOperationException(
					"Failed to locate NpgsqlServiceRegistry.Register<TService>(TService) method for dynamic registration.");
			}

			var interfaceToImplementation = serviceInterfaces
				.Select(serviceInterface =>
				{
					var implementations = candidates
						.Where(t => serviceInterface.IsAssignableFrom(t))
						.ToArray();

					if (implementations.Length == 0)
					{
						throw new InvalidOperationException(
							$"No implementation found for service interface '{serviceInterface.FullName}'.");
					}

					if (implementations.Length > 1)
					{
						var implList = string.Join(", ", implementations.Select(t => t.FullName).OrderBy(n => n, StringComparer.Ordinal));
						throw new InvalidOperationException(
							$"Multiple implementations found for service interface '{serviceInterface.FullName}': {implList}." +
							" Please keep exactly one implementation per service interface.");
					}

					return new { ServiceInterface = serviceInterface, Implementation = implementations[0] };
				})
				.ToArray();

			var groupedByImplementation = interfaceToImplementation
				.GroupBy(x => x.Implementation, x => x.ServiceInterface)
				.OrderBy(g => g.Key.FullName, StringComparer.Ordinal)
				.ToArray();

			foreach (var group in groupedByImplementation)
			{
				var implementation = group.Key;
				object instance;

				try
				{
					instance = Activator.CreateInstance(implementation, dbContextFactory)!;
				}
				catch (TargetInvocationException tie)
				{
					var inner = tie.InnerException ?? tie;
					var serviceInterfacesList = string.Join(", ", group.Select(t => t.FullName).OrderBy(n => n, StringComparer.Ordinal));
					throw new InvalidOperationException(
						$"Failed to construct '{implementation.FullName}' for: {serviceInterfacesList}. {inner.Message}",
						inner);
				}
				catch (Exception ex)
				{
					var serviceInterfacesList = string.Join(", ", group.Select(t => t.FullName).OrderBy(n => n, StringComparer.Ordinal));
					throw new InvalidOperationException(
						$"Failed to construct '{implementation.FullName}' for: {serviceInterfacesList}. " +
						$"Ensure it has a public constructor accepting '{nameof(INpgsqlDbContextFactory)}'.",
						ex);
				}

				foreach (var serviceInterface in group.OrderBy(t => t.FullName, StringComparer.Ordinal))
				{
					var registerMethod = registerOpenMethod.MakeGenericMethod(serviceInterface);
					registerMethod.Invoke(registry, new[] { instance });
				}
			}
		}

		/// <inheritdoc/>
		public void Shutdown()
		{
			DbContextFactory.Shutdown();
		}

		/// <inheritdoc/>
		public Task ShutdownAsync(CancellationToken cancellationToken = default)
		{
			return DbContextFactory.ShutdownAsync(cancellationToken);
		}
	}
}