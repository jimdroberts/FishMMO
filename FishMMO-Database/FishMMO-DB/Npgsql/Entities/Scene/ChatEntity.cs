using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("chat", Schema = "fish_mmo_postgresql")]
	[Index(nameof(WorldServerID))]
	[Index(nameof(TimeCreated))]
	public class ChatEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public long WorldServerID { get; set; }
		public long SceneServerID { get; set; }
		public DateTime TimeCreated { get; set; }
		public byte Channel { get; set; }
		public string Message { get; set; }
	}
}