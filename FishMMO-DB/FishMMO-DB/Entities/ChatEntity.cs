using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO_DB.Entities
{
	[Table("chat", Schema = "fish_mmo_postgresql")]
	[Index(nameof(WorldServerID))]
	public class ChatEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public int WorldServerID { get; set; }
		public int SceneServerID { get; set; }
		public DateTime TimeCreated { get; set; }
		public byte Channel { get; set; }
		public string Message { get; set; }
	}
}