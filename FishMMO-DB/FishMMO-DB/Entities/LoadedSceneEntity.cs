using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO_DB.Entities
{
	[Table("loaded_scenes", Schema = "fish_mmo_postgresql")]
	[Index(nameof(SceneServerID))]
	[Index(nameof(WorldServerID))]
	public class LoadedSceneEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ID { get; set; }
		public int SceneServerID { get; set; }
		public int WorldServerID { get; set; }
		public string SceneName { get; set; }
		public int SceneHandle { get; set; }
		public int CharacterCount { get; set; }
	}
}