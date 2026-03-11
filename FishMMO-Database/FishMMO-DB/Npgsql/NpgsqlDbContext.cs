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

		public DbSet<LoginServerEntity> LoginServers { get; set; }
		public DbSet<WorldServerEntity> WorldServers { get; set; }
		public DbSet<SceneServerEntity> SceneServers { get; set; }
		public DbSet<SceneEntity> Scenes { get; set; }
		public DbSet<AccountEntity> Accounts { get; set; }
		public DbSet<KickRequestEntity> KickRequests { get; set; }
		public DbSet<LoginServerSigningKeyEntity> LoginServerSigningKeys { get; set; }
		public DbSet<AuthTokenEntity> AuthTokens { get; set; }

		public DbSet<CharacterEntity> Characters { get; set; }
		public DbSet<CharacterAbilityEntity> CharacterAbilities { get; set; }
		public DbSet<CharacterKnownAbilityEntity> CharacterKnownAbilities { get; set; }
		public DbSet<CharacterAttributeEntity> CharacterAttributes { get; set; }
		public DbSet<CharacterAchievementEntity> CharacterAchievements { get; set; }
		public DbSet<CharacterInventoryEntity> CharacterInventoryItems { get; set; }
		public DbSet<CharacterEquipmentEntity> CharacterEquippedItems { get; set; }
		public DbSet<CharacterBankEntity> CharacterBankItems { get; set; }
		public DbSet<CharacterHotkeyEntity> CharacterHotkeys { get; set; }
		public DbSet<CharacterMailEntity> CharacterMail { get; set; }
		public DbSet<CharacterItemCooldownEntity> CharacterItemCooldowns { get; set; }
		public DbSet<CharacterSkillEntity> CharacterSkills { get; set; }
		public DbSet<CharacterBuffEntity> CharacterBuffs { get; set; }
		public DbSet<CharacterPetEntity> CharacterPets { get; set; }
		public DbSet<CharacterPetAttributeEntity> CharacterPetAttributes { get; set; }
		public DbSet<CharacterPetBuffEntity> CharacterPetBuffs { get; set; }
		public DbSet<CharacterFactionEntity> CharacterFactions { get; set; }
		public DbSet<CharacterQuestEntity> CharacterQuests { get; set; }
		public DbSet<CharacterFriendEntity> CharacterFriends { get; set; }
		public DbSet<CharacterArchetypeEntity> CharacterArchetypes { get; set; }
		public DbSet<CharacterGuildEntity> CharacterGuilds { get; set; }
		public DbSet<GuildEntity> Guilds { get; set; }
		public DbSet<GuildUpdateEntity> GuildUpdates { get; set; }
		public DbSet<CharacterPartyEntity> CharacterParties { get; set; }
		public DbSet<PartyEntity> Parties { get; set; }
		public DbSet<PartyUpdateEntity> PartyUpdates { get; set; }
		public DbSet<ChatEntity> Chat { get; set; }

		public override void Dispose()
		{
			if (Interlocked.Exchange(ref disposed, 1) == 0)
			{
				base.Dispose();
				Disposed?.Invoke(this, EventArgs.Empty);
			}
		}

		public override async ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref disposed, 1) == 0)
			{
				await base.DisposeAsync().ConfigureAwait(false);
				Disposed?.Invoke(this, EventArgs.Empty);
			}
		}

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			base.OnModelCreating(modelBuilder);

			modelBuilder.Entity<SqlIntValue>().HasNoKey();
			modelBuilder.Entity<SqlLongValue>().HasNoKey();

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

				modelBuilder.Entity(clrType)
					.Property<DateTime>("TimeCreated")
					.IsRequired()
					.ValueGeneratedOnAdd()
					.HasDefaultValueSql("CURRENT_TIMESTAMP");
			}
		}
	}
}