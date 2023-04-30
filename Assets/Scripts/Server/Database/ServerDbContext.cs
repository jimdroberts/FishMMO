﻿using Microsoft.EntityFrameworkCore;
using Server.Entities;
using Server.EntityConfigurations;

namespace Server
{
    public class ServerDbContext : DbContext
	{
		public ServerDbContext(DbContextOptions options) : base(options)
        {
        }
        
        public DbSet<WorldServerEntity> WorldServers { get; set; }
        public DbSet<AccountEntity> Accounts { get; set; }
        
        // character tables
        public DbSet<CharacterEntity> Characters { get; set; }
        public DbSet<CharacterInventoryEntity> CharacterInventories { get; set; }
        public DbSet<CharacterEquipmentEntity> CharacterEquipments { get; set; }
        public DbSet<CharacterItemCooldownEntity> CharacterItemCooldowns { get; set; }
        public DbSet<CharacterSkillEntity> CharacterSkills { get; set; }
        public DbSet<CharacterBuffEntity> CharacterBuffs { get; set; }
        public DbSet<CharacterQuestEntity> CharacterQuests { get; set; }
        public DbSet<CharacterGuildEntity> CharacterGuilds { get; set; }
        public DbSet<GuildInfoEntity> GuildInfos { get; set; }
        
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