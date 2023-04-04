using Microsoft.EntityFrameworkCore;
using Server.Entities;

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
        public DbSet<QuestEntity> Quests { get; set; }
    }
}