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
		public Sprite SceneTransitionImage;

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