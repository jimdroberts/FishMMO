using System;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Monitoring.Metrics;

namespace FishMMO.Database.Npgsql
{
	public class NpgsqlDbContext : DbContext
	{
		private readonly ConnectionPoolMetrics poolMetrics;
		private int disposed = 0;

		/// <summary>
		/// Gets the database schema name for this context.
		/// </summary>
		public string Schema { get; }

		public NpgsqlDbContext(DbContextOptions options, string schema, ConnectionPoolMetrics poolMetrics = null) : base(options)
		{
			schema = schema ?? "public";

			// Validate schema name to prevent SQL injection
			if (!IsValidSchemaName(schema))
			{
				throw new ArgumentException(
					$"Invalid schema name '{schema}'. Schema names must start with a letter or underscore " +
					"and contain only letters, digits, and underscores.",
					nameof(schema));
			}

			Schema = schema;
			this.poolMetrics = poolMetrics;
		}

		/// <summary>
		/// Validates that a schema name contains only safe characters to prevent SQL injection.
		/// </summary>
		/// <param name="schemaName">The schema name to validate.</param>
		/// <returns>True if the schema name is valid, false otherwise.</returns>
		private static bool IsValidSchemaName(string schemaName)
		{
			if (string.IsNullOrWhiteSpace(schemaName))
				return false;

			// PostgreSQL identifier rules: must start with letter or underscore,
			// followed by letters, digits, underscores, or dollar signs
			// We're being more restrictive and disallowing dollar signs for security
			return Regex.IsMatch(schemaName, @"^[a-zA-Z_][a-zA-Z0-9_]*$");
		}

		//protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		//    => optionsBuilder.LogTo(Console.WriteLine);

		public DbSet<PatchServerEntity> PatchServers { get; set; }
		public DbSet<LoginServerEntity> LoginServers { get; set; }
		public DbSet<WorldServerEntity> WorldServers { get; set; }
		public DbSet<SceneServerEntity> SceneServers { get; set; }
		public DbSet<SceneEntity> Scenes { get; set; }
		public DbSet<AccountEntity> Accounts { get; set; }
		public DbSet<KickRequestEntity> KickRequests { get; set; }

		// character tables
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
		public DbSet<CharacterGuildEntity> CharacterGuilds { get; set; }
		public DbSet<GuildEntity> Guilds { get; set; }
		public DbSet<GuildUpdateEntity> GuildUpdates { get; set; }
		public DbSet<CharacterPartyEntity> CharacterParties { get; set; }
		public DbSet<PartyEntity> Parties { get; set; }
		public DbSet<PartyUpdateEntity> PartyUpdates { get; set; }
		public DbSet<ChatEntity> Chat { get; set; }

		// game data (?)
		//public DbSet<QuestEntity> Quests { get; set; }

		/// <summary>
		/// Disposes the context and records disposal in pool metrics.
		/// Thread-safe disposal using Interlocked.Exchange to prevent race conditions.
		/// Ensures base disposal completes before recording metrics to prevent
		/// incorrect counts if base.Dispose() throws an exception.
		/// </summary>
		public override void Dispose()
		{
			if (Interlocked.Exchange(ref disposed, 1) == 0)
			{
				Exception? disposalException = null;
				try
				{
					base.Dispose();
				}
				catch (Exception ex)
				{
					disposalException = ex;
					throw;
				}
				finally
				{
					if (disposalException == null)
					{
						poolMetrics?.RecordConnectionDisposed();
					}
				}
			}
		}

		/// <summary>
		/// Asynchronously disposes the context and records disposal in pool metrics.
		/// Thread-safe disposal using Interlocked.Exchange to prevent race conditions.
		/// Ensures base disposal completes before recording metrics to prevent
		/// incorrect counts if base.DisposeAsync() throws an exception.
		/// </summary>
		public override async ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref disposed, 1) == 0)
			{
				Exception? disposalException = null;
				try
				{
					await base.DisposeAsync().ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					disposalException = ex;
					throw;
				}
				finally
				{
					if (disposalException == null)
					{
						poolMetrics?.RecordConnectionDisposed();
					}
				}
			}
		}

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			base.OnModelCreating(modelBuilder);

			// Set default schema for all entities
			modelBuilder.HasDefaultSchema(Schema);

			// Apply all configurations in the assembly
			modelBuilder.ApplyConfigurationsFromAssembly(typeof(NpgsqlDbContext).Assembly);

			ApplyXminConcurrencyConventions(modelBuilder);
			ApplyLogicalVersionConventions(modelBuilder);
			ApplySoftDeleteConventions(modelBuilder);
			ApplyTimeCreatedConventions(modelBuilder);
		}

		private static void ApplyXminConcurrencyConventions(ModelBuilder modelBuilder)
		{
			foreach (var entityType in modelBuilder.Model.GetEntityTypes())
			{
				var clrType = entityType.ClrType;
				if (clrType == null)
					continue;

				// Avoid adding duplicate shadow properties for derived types (TPH/TPT).
				if (entityType.BaseType != null)
					continue;

				// Skip owned/keyless entity types.
				if (entityType.FindOwnership() != null)
					continue;
				if (entityType.FindPrimaryKey() == null)
					continue;

				// If a shadow property already exists, don't override it.
				if (entityType.FindProperty("xmin") != null)
					continue;

				// Map PostgreSQL system column xmin as a shadow property used for optimistic concurrency.
				modelBuilder.Entity(clrType)
					.Property<uint>("xmin")
					.HasColumnName("xmin")
					.HasColumnType("xid")
					.ValueGeneratedOnAddOrUpdate()
					.IsConcurrencyToken();
			}
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

				modelBuilder.Entity(clrType)
					.Property<long>(nameof(IVersionedEntity.Version))
					.IsRequired()
					.HasDefaultValue(0L);
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

		private static void ApplySoftDeleteConventions(ModelBuilder modelBuilder)
		{
			var efPropertyMethod = typeof(EF).GetMethod(nameof(EF.Property))!;

			foreach (var entityType in modelBuilder.Model.GetEntityTypes())
			{
				var clrType = entityType.ClrType;
				if (clrType == null)
					continue;

				var deletedProperty = entityType.FindProperty("Deleted");
				if (deletedProperty?.ClrType != typeof(bool))
					continue;

				// If this is a character-owned table that uses soft delete, ensure we have an index
				// that supports common predicates like: WHERE character_id = ? AND deleted = FALSE.
				var characterIdProperty = entityType.FindProperty("CharacterID") ?? entityType.FindProperty("CharacterId");
				if (characterIdProperty != null)
				{
					bool hasCharacterIdDeletedIndex = entityType.GetIndexes().Any(i =>
						i.Properties.Count == 2 &&
						i.Properties[0].Name == characterIdProperty.Name &&
						i.Properties[1].Name == "Deleted");

					if (!hasCharacterIdDeletedIndex)
					{
						modelBuilder.Entity(clrType).HasIndex(characterIdProperty.Name, "Deleted");
					}
				}

				modelBuilder.Entity(clrType)
					.Property<bool>("Deleted")
					.IsRequired()
					.HasDefaultValue(false);

				var timeDeletedProperty = entityType.FindProperty("TimeDeleted");
				if (timeDeletedProperty?.ClrType == typeof(DateTime?))
				{
					modelBuilder.Entity(clrType)
						.Property<DateTime?>("TimeDeleted")
						.IsRequired(false);
				}

				// Global query filter: only return non-deleted rows by default.
				var parameter = Expression.Parameter(clrType, "e");
				var deletedEfProperty = Expression.Call(
					efPropertyMethod.MakeGenericMethod(typeof(bool)),
					parameter,
					Expression.Constant("Deleted"));
				var filterBody = Expression.Equal(deletedEfProperty, Expression.Constant(false));
				var filterLambda = Expression.Lambda(filterBody, parameter);

				modelBuilder.Entity(clrType).HasQueryFilter(filterLambda);
			}
		}
	}
}