using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("chat")]
	public class ChatEntity
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public string CharacterName { get; set; }
		public string AccountName { get; set; }
		public long WorldServerID { get; set; }
		public long SceneServerID { get; set; }
		public DateTime ServerReceivedTime { get; set; }
		public DateTime TimeCreated { get; set; }
		public byte Channel { get; set; }
		public string Message { get; set; }
	}
}