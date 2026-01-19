using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("patch_servers")]
	public class PatchServerEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public string Address { get; set; }
		public ushort Port { get; set; }
		public DateTime TimeCreated { get; set; }
		public DateTime LastPulse { get; set; }
	}
}