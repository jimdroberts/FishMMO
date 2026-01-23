using System;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Services;
using FishMMO.Database.Npgsql.Services.Interfaces;
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
			registry.Register<IAccountService>(new AccountService(dbContextFactory));
			registry.Register<ICharacterAbilityService>(new CharacterAbilityService(dbContextFactory));
			registry.Register<ICharacterAchievementService>(new CharacterAchievementService(dbContextFactory));
			registry.Register<ICharacterAttributeService>(new CharacterAttributeService(dbContextFactory));
			registry.Register<ICharacterBankService>(new CharacterBankService(dbContextFactory));
			registry.Register<ICharacterBuffService>(new CharacterBuffService(dbContextFactory));
			registry.Register<ICharacterEquipmentService>(new CharacterEquipmentService(dbContextFactory));
			registry.Register<ICharacterFactionService>(new CharacterFactionService(dbContextFactory));
			registry.Register<ICharacterFriendService>(new CharacterFriendService(dbContextFactory));
			registry.Register<ICharacterGuildService>(new CharacterGuildService(dbContextFactory));
			registry.Register<ICharacterHotkeyService>(new CharacterHotkeyService(dbContextFactory));
			registry.Register<ICharacterInventoryService>(new CharacterInventoryService(dbContextFactory));
			registry.Register<ICharacterKnownAbilityService>(new CharacterKnownAbilityService(dbContextFactory));
			registry.Register<ICharacterPartyService>(new CharacterPartyService(dbContextFactory));
			registry.Register<ICharacterPetService>(new CharacterPetService(dbContextFactory));
			registry.Register<ICharacterService>(new CharacterService(dbContextFactory));
			registry.Register<IChatService>(new ChatService(dbContextFactory));
			registry.Register<IGuildService>(new GuildService(dbContextFactory));
			registry.Register<IGuildUpdateService>(new GuildUpdateService(dbContextFactory));
			registry.Register<IKickRequestService>(new KickRequestService(dbContextFactory));
			registry.Register<ILoginServerService>(new LoginServerService(dbContextFactory));
			registry.Register<IPartyService>(new PartyService(dbContextFactory));
			registry.Register<IPartyUpdateService>(new PartyUpdateService(dbContextFactory));
			registry.Register<ISceneServerService>(new SceneServerService(dbContextFactory));
			registry.Register<ISceneService>(new SceneService(dbContextFactory));
			registry.Register<IWorldServerService>(new WorldServerService(dbContextFactory));
			return registry;
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