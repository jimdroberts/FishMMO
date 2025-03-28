using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("scenes", Schema = "fish_mmo_postgresql")]
	[Index(nameof(SceneServerID))]
	[Index(nameof(WorldServerID))]
	[Index(nameof(CharacterID))]
	public class SceneEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public long SceneServerID { get; set; }
		public long WorldServerID { get; set; }
		public string SceneName { get; set; }
		public int SceneHandle { get; set; }
		public int SceneStatus { get; set; }
		public int SceneType { get; set; }
		/// <summary>
		/// Character ID of the player than opened this scene if it's instanced.
		/// </summary>
		public long CharacterID { get; set; }
		public int CharacterCount { get; set; }
		public DateTime TimeCreated { get; set; }
	}
}