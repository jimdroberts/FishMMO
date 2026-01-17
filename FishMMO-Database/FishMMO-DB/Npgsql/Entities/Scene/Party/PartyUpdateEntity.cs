using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("party_updates")]
	[Index(nameof(PartyID), IsUnique = true)]
	public class PartyUpdateEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public long PartyID { get; set; }
		public DateTime LastUpdate { get; set; }
	}
}