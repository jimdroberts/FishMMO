using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("characters", Schema = "fish_mmo_postgresql")]
	[Index(nameof(Name))]
	[Index(nameof(Account))]
	public class CharacterEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		//[Collation("NOCASE")] // [COLLATE NOCASE for case insensitive compare. this way we can't both create 'Archer' and 'archer' as characters]
		// note: the collation has been added in the DbContext OnModelCreating function since the Postgres provider
		// doesn't support collations via attributes
		public string Name { get; set; }
		public string NameLowercase { get; set; }
		public string Account { get; set; }
		public bool Selected { get; set; }
		public long WorldServerID { get; set; }
		public int SceneHandle { get; set; }
		public string BindScene { get; set; }
		public float BindX { get; set; }
		public float BindY { get; set; }
		public float BindZ { get; set; }
		public string SceneName { get; set; }
		public int RaceID { get; set; }
		public float X { get; set; }
		public float Y { get; set; }
		public float Z { get; set; }
		public float RotX { get; set; }
		public float RotY { get; set; }
		public float RotZ { get; set; }
		public float RotW { get; set; }
		public byte AccessLevel { get; set; }
		// online status can be checked from external programs with either just
		// just 'online', or 'online && (DateTime.UtcNow - lastsaved) <= 1min)
		// which is robust to server crashes too.
		public bool Online { get; set; }
		public DateTime TimeCreated { get; set; }
		public DateTime LastSaved { get; set; }
		public DateTime TimeDeleted { get; set; }
		public bool Deleted { get; set; }

		// foreign keys
		public ICollection<CharacterAbilityEntity> Abilities { get; set; }
		public ICollection<CharacterKnownAbilityEntity> KnownAbilities { get; set; }
		public ICollection<CharacterAttributeEntity> Attributes { get; set; }
		public ICollection<CharacterAchievementEntity> Achievements { get; set; }
		public ICollection<CharacterBuffEntity> Buffs { get; set; }
		public ICollection<CharacterInventoryEntity> Inventory { get; set; }
		public ICollection<CharacterEquipmentEntity> Equipment { get; set; }
		public ICollection<CharacterBankEntity> Bank { get; set; }
		public CharacterGuildEntity Guild { get; set; }
		public CharacterPartyEntity Party { get; set; }
		public ICollection<CharacterFriendEntity> Friends { get; set; }
		public ICollection<CharacterItemCooldownEntity> ItemCooldowns { get; set; }
		public ICollection<CharacterQuestEntity> Quests { get; set; }
		public ICollection<CharacterSkillEntity> Skills { get; set; }
	}
}