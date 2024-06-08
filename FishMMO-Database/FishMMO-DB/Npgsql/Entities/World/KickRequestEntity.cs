using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("kick_requests", Schema = "fish_mmo_postgresql")]
	[Index(nameof(AccountName))]
	public class KickRequestEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public string AccountName { get; set; }
		public DateTime TimeCreated { get; set; }
	}
}