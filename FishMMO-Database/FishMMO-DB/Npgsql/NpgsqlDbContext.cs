using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Database.Npgsql
{
	/// <summary>
	/// Entity Framework Core DbContext for the FishMMO PostgreSQL database.
	/// </summary>
	/// <remarks>
	/// This DbContext is created as a short-lived instance via <see cref="INpgsqlDbContextFactory"/>
	/// and is not pooled.
	/// </remarks>
	public class NpgsqlDbContext : DbContext
	{
		/// <summary>
		/// Default schema name used when no schema is specified.
		/// </summary>
		public const string DefaultSchema = "public";

		private int disposed;

		/// <summary>
		/// Raised when this context is disposed.
		/// Used by <see cref="NpgsqlDbContextFactory"/> to track active context count.
		/// </summary>
		public event EventHandler? Disposed;

		/// <summary>
		/// Gets the database schema name for this context.
		/// </summary>
		public string Schema { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="NpgsqlDbContext"/> class.
		/// </summary>
		/// <param name="options">The DbContext options.</param>
		/// <param name="schema">The database schema to use; defaults to <see cref="DefaultSchema"/> when empty.</param>
		public NpgsqlDbContext(DbContextOptions options, string schema) : base(options)
		{
			schema = string.IsNullOrWhiteSpace(schema) ? DefaultSchema : schema;

			Schema = schema;
		}

		/// <summary>Login server registry.</summary>
		public DbSet<LoginServerEntity> LoginServers { get; set; }
		/// <summary>World server registry.</summary>
		public DbSet<WorldServerEntity> WorldServers { get; set; }
		/// <summary>Scene server registry.</summary>
		public DbSet<SceneServerEntity> SceneServers { get; set; }
		/// <summary>Scene instances.</summary>
		public DbSet<SceneEntity> Scenes { get; set; }
		/// <summary>Player accounts.</summary>
		public DbSet<AccountEntity> Accounts { get; set; }
		/// <summary>Kick request records.</summary>
		public DbSet<KickRequestEntity> KickRequests { get; set; }
		/// <summary>Login server signing keys.</summary>
		public DbSet<LoginServerSigningKeyEntity> LoginServerSigningKeys { get; set; }
		/// <summary>Authentication tokens.</summary>
		public DbSet<AuthTokenEntity> AuthTokens { get; set; }
		/// <summary>Two-factor recovery codes.</summary>
		public DbSet<TwoFactorRecoveryCodeEntity> TwoFactorRecoveryCodes { get; set; }
		/// <summary>Email queue.</summary>
		public DbSet<EmailQueueEntity> EmailQueue { get; set; }
		/// <summary>Connection token verification keys (per-region HMAC keys).</summary>
		public DbSet<ConnectionTokenKeyEntity> ConnectionTokenKeys { get; set; }
		/// <summary>Deployment-global secrets (loaded at startup instead of env files).</summary>
		public DbSet<DeploymentSecretEntity> DeploymentSecrets { get; set; }
		/// <summary>Player characters.</summary>
		public DbSet<CharacterEntity> Characters { get; set; }
		/// <summary>Character abilities.</summary>
		public DbSet<CharacterAbilityEntity> CharacterAbilities { get; set; }
		/// <summary>Character known abilities.</summary>
		public DbSet<CharacterKnownAbilityEntity> CharacterKnownAbilities { get; set; }
		/// <summary>Character attributes.</summary>
		public DbSet<CharacterAttributeEntity> CharacterAttributes { get; set; }
		/// <summary>Character achievements.</summary>
		public DbSet<CharacterAchievementEntity> CharacterAchievements { get; set; }
		/// <summary>Character inventory items.</summary>
		public DbSet<CharacterInventoryEntity> CharacterInventoryItems { get; set; }
		/// <summary>Character equipped items.</summary>
		public DbSet<CharacterEquipmentEntity> CharacterEquippedItems { get; set; }
		/// <summary>Character bank items.</summary>
		public DbSet<CharacterBankEntity> CharacterBankItems { get; set; }
		/// <summary>Character hotkeys.</summary>
		public DbSet<CharacterHotkeyEntity> CharacterHotkeys { get; set; }
		/// <summary>Character mail.</summary>
		public DbSet<CharacterMailEntity> CharacterMail { get; set; }
		/// <summary>Character item cooldowns.</summary>
		public DbSet<CharacterItemCooldownEntity> CharacterItemCooldowns { get; set; }
		/// <summary>Character skills.</summary>
		public DbSet<CharacterSkillEntity> CharacterSkills { get; set; }
		/// <summary>Character buffs.</summary>
		public DbSet<CharacterBuffEntity> CharacterBuffs { get; set; }
		/// <summary>Character pets.</summary>
		public DbSet<CharacterPetEntity> CharacterPets { get; set; }
		/// <summary>Character pet attributes.</summary>
		public DbSet<CharacterPetAttributeEntity> CharacterPetAttributes { get; set; }
		/// <summary>Character pet buffs.</summary>
		public DbSet<CharacterPetBuffEntity> CharacterPetBuffs { get; set; }
		/// <summary>Character faction standings.</summary>
		public DbSet<CharacterFactionEntity> CharacterFactions { get; set; }
		/// <summary>Character quests.</summary>
		public DbSet<CharacterQuestEntity> CharacterQuests { get; set; }
		/// <summary>Character friends.</summary>
		public DbSet<CharacterFriendEntity> CharacterFriends { get; set; }
		/// <summary>Character archetypes.</summary>
		public DbSet<CharacterArchetypeEntity> CharacterArchetypes { get; set; }
		/// <summary>Character guild memberships.</summary>
		public DbSet<CharacterGuildEntity> CharacterGuilds { get; set; }
		/// <summary>Guilds.</summary>
		public DbSet<GuildEntity> Guilds { get; set; }
		/// <summary>Guild update records.</summary>
		public DbSet<GuildUpdateEntity> GuildUpdates { get; set; }
		/// <summary>Character party memberships.</summary>
		public DbSet<CharacterPartyEntity> CharacterParties { get; set; }
		/// <summary>Parties.</summary>
		public DbSet<PartyEntity> Parties { get; set; }
		/// <summary>Party update records.</summary>
		public DbSet<PartyUpdateEntity> PartyUpdates { get; set; }
		/// <summary>Chat messages.</summary>
		public DbSet<ChatEntity> Chat { get; set; }

		/// <summary>
		/// Keyless entity set for mapping scalar <c>integer</c> results from raw SQL queries via <c>FromSqlRaw</c>.
		/// </summary>
		public DbSet<SqlIntValue> SqlIntValues { get; set; }

		/// <summary>
		/// Keyless entity set for mapping scalar <c>bigint</c> results from raw SQL queries via <c>FromSqlRaw</c>.
		/// </summary>
		public DbSet<SqlLongValue> SqlLongValues { get; set; }

		public override void Dispose()
		{
			if (Interlocked.Exchange(ref disposed, 1) == 0)
			{
				Disposed?.Invoke(this, EventArgs.Empty);
				base.Dispose();
			}
		}

		public override async ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref disposed, 1) == 0)
			{
				Disposed?.Invoke(this, EventArgs.Empty);
				await base.DisposeAsync().ConfigureAwait(false);
			}
		}

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			base.OnModelCreating(modelBuilder);

			// Set default schema for all entities
			modelBuilder.HasDefaultSchema(Schema);

			// Apply all configurations in the assembly
			modelBuilder.ApplyConfigurationsFromAssembly(typeof(NpgsqlDbContext).Assembly);


			ApplyLogicalVersionConventions(modelBuilder);
			ApplyTimeCreatedConventions(modelBuilder);
		}

		private static void ApplyLogicalVersionConventions(ModelBuilder modelBuilder)
		{
			foreach (var entityType in modelBuilder.Model.GetEntityTypes())
			{
				var clrType = entityType.ClrType;
				if (clrType == null)
					continue;
				if (!typeof(IVersionedEntity).IsAssignableFrom(clrType))
					continue;

				// Skip keyless/owned types.
				if (entityType.FindOwnership() != null)
					continue;
				if (entityType.FindPrimaryKey() == null)
					continue;

				var versionProperty = entityType.FindProperty(nameof(IVersionedEntity.Version));
				if (versionProperty == null)
				{
					continue;
				}

				var hasExplicitDefault = versionProperty.GetDefaultValue() != null
					|| !string.IsNullOrWhiteSpace(versionProperty.GetDefaultValueSql());

				var versionBuilder = modelBuilder.Entity(clrType)
					.Property<long>(nameof(IVersionedEntity.Version))
					.IsRequired()
					.ValueGeneratedNever()
					.IsConcurrencyToken();

				if (!hasExplicitDefault)
				{
					versionBuilder.HasDefaultValue(1L);
				}
			}
		}

		private static void ApplyTimeCreatedConventions(ModelBuilder modelBuilder)
		{
			foreach (var entityType in modelBuilder.Model.GetEntityTypes())
			{
				var clrType = entityType.ClrType;
				if (clrType == null)
					continue;

				var timeCreatedProperty = entityType.FindProperty("TimeCreated");
				if (timeCreatedProperty?.ClrType != typeof(DateTime))
					continue;

				// Skip properties that already have an explicit default value
				// configured (e.g., QuestEntity sets DateTime.UnixEpoch).
				// Conventions must not silently override explicit configuration.
				if (timeCreatedProperty.GetDefaultValue() != null ||
				    !string.IsNullOrWhiteSpace(timeCreatedProperty.GetDefaultValueSql()))
					continue;

				modelBuilder.Entity(clrType)
					.Property<DateTime>("TimeCreated")
					.IsRequired()
					.ValueGeneratedOnAdd()
					.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");
			}
		}
	}
}