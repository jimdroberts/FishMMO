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
		private int disposed;

		/// <summary>
		/// Gets the database schema name for this context.
		/// </summary>
		public string Schema { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="NpgsqlDbContext"/> class.
		/// </summary>
		/// <param name="options">The DbContext options.</param>
		/// <param name="schema">The database schema to use; defaults to <c>public</c> when empty.</param>
		public NpgsqlDbContext(DbContextOptions options, string schema) : base(options)
		{
			schema = string.IsNullOrWhiteSpace(schema) ? "public" : schema;

			Schema = schema;
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

		public override void Dispose()
		{
			if (Interlocked.Exchange(ref disposed, 1) == 0)
			{
				base.Dispose();
			}
		}

		public override ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref disposed, 1) == 0)
			{
				return base.DisposeAsync();
			}
			return default;
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

			ApplyXminConcurrencyConventions(modelBuilder);
			ApplyLogicalVersionConventions(modelBuilder);
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
	}
}