using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("quest")]
	public class QuestEntity
	{
		public DateTime TimeCreated { get; set; }
	}
}