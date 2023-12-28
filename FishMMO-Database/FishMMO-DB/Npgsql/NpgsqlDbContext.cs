using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Database.Npgsql
{
    public class NpgsqlDbContext : DbContext
    {
        public NpgsqlDbContext(DbContextOptions options) : base(options)
        {
        }

		//protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        //    => optionsBuilder.LogTo(Console.WriteLine);
		
        public DbSet<WorldServerEntity> WorldServers { get; set; }
        public DbSet<SceneServerEntity> SceneServers { get; set; }
        public DbSet<LoadedSceneEntity> LoadedScenes { get; set; }
        public DbSet<PendingSceneEntity> PendingScenes { get; set; }
        public DbSet<AccountEntity> Accounts { get; set; }
        
        // character tables
        public DbSet<CharacterEntity> Characters { get; set; }
        public DbSet<CharacterAbilityEntity> CharacterAbilities { get; set; }
        public DbSet<CharacterKnownAbilityEntity> CharacterKnownAbilities { get; set; }
        public DbSet<CharacterAttributeEntity> CharacterAttributes { get; set; }
        public DbSet<CharacterAchievementEntity> CharacterAchievements { get; set; }
        public DbSet<CharacterInventoryEntity> CharacterInventoryItems { get; set; }
        public DbSet<CharacterEquipmentEntity> CharacterEquippedItems { get; set; }
        public DbSet<CharacterItemCooldownEntity> CharacterItemCooldowns { get; set; }
        public DbSet<CharacterSkillEntity> CharacterSkills { get; set; }
        public DbSet<CharacterBuffEntity> CharacterBuffs { get; set; }
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

            // add collation to character entity name
            modelBuilder.ApplyConfiguration(new CharacterEntityConfiguration());
            
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