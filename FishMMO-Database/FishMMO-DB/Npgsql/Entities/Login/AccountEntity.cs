using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("accounts", Schema = "fish_mmo_postgresql")]
	public class AccountEntity
	{
		[Key]
		public string Name { get; set; }
		public string Salt { get; set; }
		public string Verifier { get; set; }
		public byte AccessLevel { get; set; }
		public DateTime Created { get; set; }
		public DateTime Lastlogin { get; set; }
	}
}