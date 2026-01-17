using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Database.Npgsql
{
	public class NpgsqlDbContext : DbContext
	{
		/// <summary>
		/// Gets the database schema name for this context.
		/// </summary>
		public string Schema { get; }

		public NpgsqlDbContext(DbContextOptions options, string schema) : base(options)
		{
			Schema = schema ?? "public";
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

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			base.OnModelCreating(modelBuilder);

			// Set default schema for all entities
			modelBuilder.HasDefaultSchema(Schema);

			// Apply all entity configurations for explicit control over indexes, constraints, and defaults
			// Benefits: Explicit index definitions, collation settings, computed columns, FK relationships
			modelBuilder.ApplyConfiguration(new CharacterEntityConfiguration());
			modelBuilder.ApplyConfiguration(new AccountEntityConfiguration());
			modelBuilder.ApplyConfiguration(new GuildEntityConfiguration());
			modelBuilder.ApplyConfiguration(new PartyEntityConfiguration());
			modelBuilder.ApplyConfiguration(new SceneEntityConfiguration());
			modelBuilder.ApplyConfiguration(new LoginServerEntityConfiguration());
			modelBuilder.ApplyConfiguration(new PatchServerEntityConfiguration());
			modelBuilder.ApplyConfiguration(new WorldServerEntityConfiguration());
			modelBuilder.ApplyConfiguration(new SceneServerEntityConfiguration());
			modelBuilder.ApplyConfiguration(new ChatEntityConfiguration());
			modelBuilder.ApplyConfiguration(new KickRequestEntityConfiguration());
			modelBuilder.ApplyConfiguration(new CharacterMailEntityConfiguration());
			modelBuilder.ApplyConfiguration(new CharacterGuildEntityConfiguration());
			modelBuilder.ApplyConfiguration(new CharacterPartyEntityConfiguration());
			modelBuilder.ApplyConfiguration(new CharacterInventoryEntityConfiguration());
			modelBuilder.ApplyConfiguration(new CharacterAbilityEntityConfiguration());
			modelBuilder.ApplyConfiguration(new CharacterAttributeEntityConfiguration());
			modelBuilder.ApplyConfiguration(new CharacterEquipmentEntityConfiguration());

			// other settings
			/*modelBuilder.Entity<CharacterBuffEntity>()
                .HasKey(cb => new { cb.character, cb.name });
            modelBuilder.Entity<CharacterEquipmentEntity>()
                .HasKey(ce => new { ce.character, ce.slot });
            modelBuilder.Entity<CharacterGuildEntity>()
                .HasKey(cg => new { cg.character, cg.guild });
            modelBuilder.Entity<CharacterInventoryEntity>()
                .HasKey(ci => new { ci.character, ci.slot });
            modelBuilder.Entity<CharacterItemCooldownEntity>()
                .HasKey(ic => new { ic.character, ic.category });
            modelBuilder.Entity<CharacterQuestEntity>()
                .HasKey(cq => new { cq.character, cq.name });
            modelBuilder.Entity<CharacterSkillEntity>()
                .HasKey(cs => new { cs.character, cs.hash });*/
		}
	}
}