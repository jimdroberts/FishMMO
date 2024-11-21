using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("character_pet", Schema = "fish_mmo_postgresql")]
	[Index(nameof(CharacterID))]
	public class CharacterPetEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public int TemplateID { get; set; }
		public List<int> Abilities { get; set; }
		public bool Spawned { get; set; }
	}
}