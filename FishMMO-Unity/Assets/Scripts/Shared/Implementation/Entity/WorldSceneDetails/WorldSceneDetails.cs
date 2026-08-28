using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable data structure containing configuration details for a game scene.
	/// Includes client limits, transition visuals, spawn/respawn positions, teleporters, and boundaries.
	/// </summary>
	[Serializable]
	public class WorldSceneDetails
	{
		/// <summary>
		/// The maximum number of clients allowed in this scene.
		/// </summary>
		public int MaxClients;

		/// <summary>
		/// The image displayed during scene transitions.
		/// </summary>
		/// <remarks>
		/// Resolved at cache-rebuild time from <see cref="MapDefinition"/>, falling back to the
		/// legacy field on <c>WorldSceneSettings</c> for a scene that has not been baked since the
		/// image moved. Kept as its own field rather than read through the definition so the
		/// loading screen — which runs before any map subsystem exists — does not have to
		/// dereference an asset that may legitimately be absent.
		/// </remarks>
		public Sprite SceneTransitionImage;

		/// <summary>
		/// The scene's map, player-facing name and authored landmarks.
		/// </summary>
		/// <remarks>
		/// Null for a scene with no map definition, which is normal: the world map derives its
		/// extents from <see cref="Boundaries"/> in that case and draws without a background
		/// image. Nothing on the server reads this, and the definition holds its map texture as an
		/// addressable reference precisely so that a dedicated server build does not pull
		/// megabytes of map art in through this field.
		/// </remarks>
		public WorldMapDefinition MapDefinition;

		/// <summary>
		/// Dictionary of initial spawn positions for characters entering the scene.
		/// </summary>
		public CharacterInitialSpawnPositionDictionary InitialSpawnPositions = new CharacterInitialSpawnPositionDictionary();

		/// <summary>
		/// Dictionary of respawn positions for characters after death or re-entry.
		/// </summary>
		public CharacterRespawnPositionDictionary RespawnPositions = new CharacterRespawnPositionDictionary();

		/// <summary>
		/// Dictionary of teleporters available in the scene, mapping teleporter IDs to their details.
		/// </summary>
		public SceneTeleporterDictionary Teleporters = new SceneTeleporterDictionary();

		/// <summary>
		/// Dictionary of boundaries that define the playable area of the scene.
		/// </summary>
		public SceneBoundaryDictionary Boundaries = new SceneBoundaryDictionary();
	}
}