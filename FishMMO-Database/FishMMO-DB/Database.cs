using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Monitoring.Health;
using FishMMO.Database.Npgsql.Monitoring.Metrics;
using FishMMO.Database.Npgsql.Monitoring.Diagnostics;
using FishMMO.Database.Exceptions;

namespace FishMMO.Database
{
	/// <summary>
	/// Main orchestrator and facade for the FishMMO database layer.
	/// Provides centralized access to database services, health monitoring, and metrics.
	/// This class initializes the database context factory, discovers and registers concrete service
	/// implementations, and exposes monitoring points to the host application.
	/// </summary>
	public sealed class Database : IDatabase, IAsyncDisposable
	{
		/// <inheritdoc/>
		/// <remarks>
		/// The service registry is populated during construction by discovering implementations
		/// in the `FishMMO.Database.Npgsql.Services` assembly. Consumers should call
		/// <see cref="IDatabaseServiceRegistry.TryGet{TService}(out TService)"/> to obtain services.
		/// </remarks>
		public IDatabaseServiceRegistry ServiceRegistry { get; private set; }

		/// <inheritdoc/>
		/// <remarks>
		/// The <see cref="DatabaseHealthMonitor"/> performs lightweight connectivity and response-time checks
		/// against the underlying database using <see cref="INpgsqlDbContextFactory"/>.
		/// </remarks>
		public DatabaseHealthMonitor HealthMonitor { get; private set; }

		/// <inheritdoc/>
		public DatabaseMetricsTracker MetricsTracker { get; private set; }

		/// <inheritdoc/>
		/// <remarks>
		/// Exposes the configured <see cref="INpgsqlDbContextFactory"/> used to create short-lived
		/// <see cref="NpgsqlDbContext"/> instances. Prefer service interfaces from <see cref="ServiceRegistry"/>
		/// for application operations.
		/// </remarks>
		public INpgsqlDbContextFactory DbContextFactory { get; private set; }

		/// <summary>
		/// Initializes a new instance of the <see cref="Database"/> class with a pre-built <see cref="IConfiguration"/>.
		/// Creates the DbContext factory, discovers and registers all services, and sets up monitoring.
		/// </summary>
		/// <param name="configuration">Application configuration containing an <c>Npgsql</c> section. Cannot be <c>null</c>.</param>
		/// <param name="enableLogging">Enable sensitive data logging for development (default: false).</param>
		/// <param name="commandTimeout">Optional database command timeout override in seconds.</param>
		/// <param name="healthCheckWarningMs">Health check warning threshold in milliseconds (default: 100).</param>
		/// <param name="healthCheckCriticalMs">Health check critical threshold in milliseconds (default: 500).</param>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <c>null</c>.</exception>
		/// <exception cref="DatabaseException">Thrown when initialization fails due to configuration or registration issues.</exception>
		public Database(
			IConfiguration configuration,
			bool enableLogging = false,
			int? commandTimeout = null,
			int healthCheckWarningMs = 100,
			int healthCheckCriticalMs = 500)
		{
			if (configuration == null)
				throw new ArgumentNullException(nameof(configuration));

			var dbConfiguration = new NpgsqlDbConfiguration(
				configuration,
				enableLogging,
				commandTimeout);

			Initialize(
				new NpgsqlDbContextFactory(dbConfiguration),
				healthCheckWarningMs,
				healthCheckCriticalMs);
		}

		/// <summary>
		/// Shared initialization logic used by constructors. Sets up the DbContext factory, service registry,
		/// health monitor and metrics tracker.
		/// </summary>
		/// <param name="dbContextFactory">The pre-configured <see cref="INpgsqlDbContextFactory"/> to use. Cannot be <c>null</c>.</param>
		/// <param name="healthCheckWarningMs">Health check warning threshold in milliseconds.</param>
		/// <param name="healthCheckCriticalMs">Health check critical threshold in milliseconds.</param>
		/// <exception cref="DatabaseException">Thrown when initialization fails.</exception>
		private void Initialize(
			INpgsqlDbContextFactory dbContextFactory,
			int healthCheckWarningMs,
			int healthCheckCriticalMs)
		{
			try
			{
				DbContextFactory = dbContextFactory;
				ServiceRegistry = CreateNpgsqlServiceRegistry(DbContextFactory);
				HealthMonitor = new DatabaseHealthMonitor(
					DbContextFactory,
					healthCheckWarningMs,
					healthCheckCriticalMs);
				MetricsTracker = new DatabaseMetricsTracker();
			}
			catch (Exception ex)
			{
				throw new DatabaseException(
					"Failed to initialize database orchestrator.",
					ex,
					"INVALID_CONFIGURATION");
			}
		}

		/// <summary>
		/// Creates and populates an <see cref="NpgsqlServiceRegistry"/> by reflecting over the
		/// Npgsql services assembly and constructing concrete implementations.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory passed to service constructors.</param>
		/// <returns>An initialized <see cref="IDatabaseServiceRegistry"/> instance.</returns>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="dbContextFactory"/> is <c>null</c>.</exception>
		private IDatabaseServiceRegistry CreateNpgsqlServiceRegistry(INpgsqlDbContextFactory dbContextFactory)
		{
			if (dbContextFactory == null)
				throw new ArgumentNullException(nameof(dbContextFactory));

			var registry = new NpgsqlServiceRegistry();
			RegisterNpgsqlServicesByReflection(registry, dbContextFactory);
			return registry;
		}

		/// <summary>
		/// Discovers and registers Npgsql service implementations by reflection.
		///
		/// CONVENTION
		/// ----------
		/// All service interfaces in <c>FishMMO.Database.Npgsql.Services.Interfaces</c> whose name ends
		/// in "Service" are automatically paired with exactly one concrete implementation in
		/// <c>FishMMO.Database.Npgsql.Services</c> (or a sub-namespace). Each implementation must have a
		/// public constructor accepting <see cref="INpgsqlDbContextFactory"/>. The instance is registered
		/// for every interface it implements.
		///
		/// TRADEOFFS
		/// ---------
		/// + Adding a new table/service requires only creating the interface and implementation files;
		///   no manual wiring in a DI module or builder method.
		/// - Reflection-based construction obscures dependency injection failures until runtime.
		/// - If an interface matches multiple implementations (or none), the system throws at startup,
		///   which can be confusing if the naming convention is not followed precisely.
		/// - The interface namespace filter means interfaces outside <c>Services.Interfaces</c> are
		///   silently skipped.
		///
		/// DEBUGGING REGISTRATION ISSUES
		/// -----------------------------
		/// 1. Check that the service interface's namespace is exactly
		///    <c>FishMMO.Database.Npgsql.Services.Interfaces</c> and its name ends with "Service".
		/// 2. Verify the concrete class is in <c>FishMMO.Database.Npgsql.Services</c> (or a sub-namespace
		///    starting with that prefix) and is not abstract.
		/// 3. Ensure the concrete class has a public constructor that takes a single
		///    <see cref="INpgsqlDbContextFactory"/> parameter.
		/// 4. If a <see cref="ReflectionTypeLoadException"/> occurs, the loader exceptions are written
		///    to <see cref="System.Console.Error"/>; check the server logs.
		/// 5. If the service still isn't registered, temporarily add a breakpoint in this method to
		///    inspect <c>serviceInterfaces</c> and <c>candidates</c> arrays.
		/// </summary>
		/// <param name="registry">The registry to populate. Cannot be <c>null</c>.</param>
		/// <param name="dbContextFactory">Factory instance to pass to service constructors. Cannot be <c>null</c>.</param>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="registry"/> or <paramref name="dbContextFactory"/> is <c>null</c>.</exception>
		/// <exception cref="DatabaseException">Thrown when service discovery or construction fails.</exception>
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

			Type[] allTypes;
			try
			{
				allTypes = assembly.GetTypes();
			}
			catch (ReflectionTypeLoadException ex)
			{
				foreach (var loaderEx in ex.LoaderExceptions)
				{
					if (loaderEx != null)
					{
						Console.Error.WriteLine(loaderEx.ToString());
					}
				}
				throw new DatabaseException(
					"Failed to load types from assembly via reflection. See LoaderExceptions for details.",
					ex,
					errorCode: "REFLECTION_TYPE_LOAD_FAILURE");
			}

			var serviceInterfaces = allTypes
				.Where(t => t.IsInterface
					&& string.Equals(t.Namespace, interfaceNamespace, StringComparison.Ordinal)
					&& t.Name.EndsWith("Service", StringComparison.Ordinal))
				.OrderBy(t => t.FullName, StringComparer.Ordinal)
				.ToArray();

			var candidates = allTypes
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
				throw new DatabaseException(
					"Failed to locate service registry registration method for dynamic registration.",
					errorCode: "INVALID_CONFIGURATION");
			}

			var interfaceToImplementation = serviceInterfaces
				.Select(serviceInterface =>
				{
					var implementations = candidates
						.Where(t => serviceInterface.IsAssignableFrom(t))
						.ToArray();

					if (implementations.Length == 0)
					{
						throw new DatabaseException(
							$"No implementation found for service interface '{serviceInterface.FullName}'.",
							errorCode: "INVALID_CONFIGURATION");
					}

					if (implementations.Length > 1)
					{
						var implList = string.Join(", ", implementations.Select(t => t.FullName).OrderBy(n => n, StringComparer.Ordinal));
						throw new DatabaseException(
							$"Multiple implementations found for service interface '{serviceInterface.FullName}': {implList}." +
							" Please keep exactly one implementation per service interface.",
							errorCode: "INVALID_CONFIGURATION");
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
					throw new DatabaseException(
						$"Failed to construct '{implementation.FullName}' for: {serviceInterfacesList}.",
						innerException: inner,
						errorCode: "INVALID_CONFIGURATION");
				}
				catch (Exception ex)
				{
					var serviceInterfacesList = string.Join(", ", group.Select(t => t.FullName).OrderBy(n => n, StringComparer.Ordinal));
					throw new DatabaseException(
						$"Failed to construct '{implementation.FullName}' for: {serviceInterfacesList}. " +
						$"Ensure it has a public constructor accepting '{nameof(INpgsqlDbContextFactory)}'.",
						innerException: ex,
						errorCode: "INVALID_CONFIGURATION");
				}

				foreach (var serviceInterface in group.OrderBy(t => t.FullName, StringComparer.Ordinal))
				{
					try
					{
						var registerMethod = registerOpenMethod.MakeGenericMethod(serviceInterface);
						registerMethod.Invoke(registry, new[] { instance });
					}
					catch (Exception ex)
					{
						throw new DatabaseException(
							$"Failed to register '{implementation.FullName}' for interface '{serviceInterface.FullName}'. " +
							$"The naming convention requires '{implementation.Name}' to implement exactly the interface " +
							$"'{serviceInterface.Name}' and the registry method must accept that type. " +
							$"See the XML doc on {nameof(RegisterNpgsqlServicesByReflection)} for debugging steps.",
							innerException: ex,
							errorCode: "INVALID_CONFIGURATION");
					}
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

		/// <summary>
		/// Disposes the database orchestrator asynchronously.
		/// Delegates to <see cref="ShutdownAsync"/> for graceful cleanup.
		/// </summary>
		/// <remarks>
		/// NOTE: GC.SuppressFinalize is intentionally omitted because this class does not have a finalizer.
		/// Calling SuppressFinalize on a non-finalizable object is a no-op but adds unnecessary metadata.
		/// If a finalizer is ever added in the future, add GC.SuppressFinalize(this) back.
		/// </remarks>
		public async ValueTask DisposeAsync()
		{
			await ShutdownAsync().ConfigureAwait(false);
		}
	}
}