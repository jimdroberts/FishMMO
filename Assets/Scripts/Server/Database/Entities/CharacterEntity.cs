using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Server.Entities
{
    [Table("characters", Schema = "fishMMO")]
    [Index(nameof(Name))]
    [Index(nameof(Account))]
    public class CharacterEntity
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }
        //[Collation("NOCASE")] // [COLLATE NOCASE for case insensitive compare. this way we can't both create 'Archer' and 'archer' as characters]
        // note: the collation has been added in the DbContext OnModelCreating function since the Postgres provider
        // doesn't support collations via attributes
        public string Name { get; set; }
        public string NameLowercase { get; set; }
        public string Account { get; set; }
        public int RaceID { get; set; }
        public string SceneName { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float RotX { get; set; }
        public float RotY { get; set; }
        public float RotZ { get; set; }
        public float RotW { get; set; }
        public bool IsGameMaster { get; set; }
        public bool Selected { get; set; }
        // online status can be checked from external programs with either just
        // just 'online', or 'online && (DateTime.UtcNow - lastsaved) <= 1min)
        // which is robust to server crashes too.
        public bool Online { get; set; }
        public DateTime LastSaved { get; set; }
        public DateTime TimeDeleted { get; set; }
        public bool Deleted { get; set; }
        
        // foreign keys
        public ICollection<CharacterBuffEntity> Buffs { get; set; }
        public ICollection<CharacterEquipmentEntity> Equipment { get; set; }
        public CharacterGuildEntity Guild { get; set; }
        public ICollection<CharacterInventoryEntity> Inventory { get; set; }
        public ICollection<CharacterItemCooldownEntity> ItemCooldowns { get; set; }
        public ICollection<CharacterQuestEntity> Quests { get; set; }
        public ICollection<CharacterSkillEntity> Skills { get; set; }
    }
}