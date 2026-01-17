using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Scene data transfer object.
	/// </summary>
	public struct SceneData
	{
		public long ID { get; set; }
		public long SceneServerID { get; set; }
		public long WorldServerID { get; set; }
		public string SceneName { get; set; }
		public int SceneHandle { get; set; }
		public int SceneStatus { get; set; }
		public int SceneType { get; set; }
		public long CharacterID { get; set; }
		public int CharacterCount { get; set; }
		public DateTime TimeCreated { get; set; }
	}
}