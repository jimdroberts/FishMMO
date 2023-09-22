using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO_DB.Entities
{
	[Table("pending_scenes", Schema = "fish_mmo_postgresql")]
	[Index(nameof(WorldServerID))]
	public class PendingSceneEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public int WorldServerID { get; set; }
		public string SceneName { get; set; }
	}
}