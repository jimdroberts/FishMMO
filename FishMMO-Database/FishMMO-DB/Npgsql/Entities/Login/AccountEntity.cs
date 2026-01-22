using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("accounts")]
	public class AccountEntity
	{
		public string Name { get; set; }
		public string Salt { get; set; }
		public string Verifier { get; set; }
		public byte AccessLevel { get; set; }
		public DateTime TimeCreated { get; set; }
		public DateTime LastLogin { get; set; }
	}
}