using System;
using System.ComponentModel.DataAnnotations;

namespace FishMMO.Database.Npgsql.Entities
{
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