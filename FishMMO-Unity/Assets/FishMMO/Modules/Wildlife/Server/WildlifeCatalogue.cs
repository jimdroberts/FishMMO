using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer.Spawner
{
	/// <summary>
	/// Every wildlife species the server places, and the limits on how many: read by <see cref="WildlifePlanner"/> when a
	/// scene is cut (WildlifeSpawnerCut writes its plan into the scene as ObjectSpawners). Built by the wildlife builder (FishMMO Dashboard → Maintenance → Wildlife).
	/// </summary>
	[CreateAssetMenu(fileName = "Wildlife Catalogue", menuName = "FishMMO/Server/Wildlife Catalogue", order = 0)]
	public class WildlifeCatalogue : ScriptableObject
	{
		[Tooltip("The species' NPC prefabs (each with NPC.Wildlife set).")]
		public List<NetworkObject> Species = new List<NetworkObject>();

		[Tooltip("Side of one wildlife spawner's area, metres. Bigger than the stride, so the areas overlap.")]
		[Min(64f)]
		public float AreaMetres = 768f;

		[Tooltip("Metres between neighbouring areas' centres.")]
		[Min(32f)]
		public float AreaStrideMetres = 512f;

		[Tooltip("Smallest share of an area a species' ground must cover for the area to hold it.")]
		[Range(0f, 1f)]
		public float MinimumCoverage = 0.1f;

		[Tooltip("Fewest animals one wildlife spawner keeps (its MaxSpawnCount is rolled between these).")]
		[Min(1)]
		public int SpawnCountMin = 2;

		[Tooltip("Most animals one wildlife spawner keeps.")]
		[Min(1)]
		public int SpawnCountMax = 6;

		[Tooltip("Metres every spawner's area stays inside the scene boundary, so nothing spawns past the edge and falls.")]
		[Min(0f)]
		public float BoundaryMargin = 16f;

		[Tooltip("Most wildlife spawners in one scene, dens included: the AI's cost cap.")]
		[Min(0)]
		public int MaxSpawnersPerScene = 240;

		[Tooltip("Chance a cave mouth holds a den, for species that den in caves.")]
		[Range(0f, 1f)]
		public float CaveDenChance = 0.35f;

		[Tooltip("Chance a cave system holds colonies, for species that live deep in caves (giant bats).")]
		[Range(0f, 1f)]
		public float CaveColonyChance = 0.75f;

		[Tooltip("Most colonies of one species in one cave system.")]
		[Min(1)]
		public int ColoniesPerCave = 2;

		[Tooltip("Metres between two colonies in one cave system.")]
		[Min(0f)]
		public float ColonySpacing = 60f;

		[Tooltip("Metres kept clear around a settlement point of interest beyond its own radius.")]
		[Min(0f)]
		public float SettlementClearance = 60f;
	}
}
