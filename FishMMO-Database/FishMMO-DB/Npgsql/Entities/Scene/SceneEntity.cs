using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>Scene instance entity representing a currently loaded scene on a scene server.</summary>
	public class SceneEntity : IVersionedEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }
		/// <summary>Concurrency version for optimistic locking.</summary>
		public long Version { get; set; }
		/// <summary>Scene server instance ID that hosts this scene.</summary>
		public long SceneServerID { get; set; }
		/// <summary>World server ID this scene belongs to.</summary>
		public long WorldServerID { get; set; }
		/// <summary>Scene name (e.g. the Unity scene asset name).</summary>
		public string SceneName { get; set; }
		/// <summary>Scene handle assigned by the scene server.</summary>
		/// <summary>
		/// The hosting scene server's own scene-manager handle for this instance.
		/// </summary>
		/// <remarks>
		/// Diagnostic only. A scene-manager handle is assigned from a per-process counter, so it
		/// is meaningful solely inside the process that allocated it — two scene servers running
		/// the same build routinely produce the same value for different scenes. Anything that
		/// needs to identify a scene instance from outside its host uses <see cref="ID"/>.
		/// </remarks>
		public int SceneHandle { get; set; }
		/// <summary>Current status of the scene (e.g. loading, running, unloading).</summary>
		public int SceneStatus { get; set; }
		/// <summary>Scene type (over-world vs instanced).</summary>
		public int SceneType { get; set; }
		/// <summary>
		/// Character ID of the player than opened this scene if it's instanced.
		/// </summary>
		public long CharacterID { get; set; }
		/// <summary>Number of characters currently in this scene.</summary>
		public int CharacterCount { get; set; }
		/// <summary>Row creation timestamp (UTC).</summary>
		public DateTime TimeCreated { get; set; }
	}
}