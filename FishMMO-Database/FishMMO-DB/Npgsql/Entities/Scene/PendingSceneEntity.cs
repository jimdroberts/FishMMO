using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("pending_scenes", Schema = "fish_mmo_postgresql")]
	[Index(nameof(WorldServerID))]
	public class PendingSceneEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public long WorldServerID { get; set; }
		public string SceneName { get; set; }
	}
}