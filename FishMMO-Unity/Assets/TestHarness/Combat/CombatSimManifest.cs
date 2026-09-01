using System.Collections.Generic;
using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.TestHarness
{
	/// <summary>
	/// Direct references to everything the CombatSim needs that the addressable labels do NOT
	/// carry: the mock content is deliberately unregistered (inert in builds), so the harness
	/// caches it by hand from these references instead. Populated by the scene generator
	/// (FishMMO → Test Scenes) scanning <c>Assets/Templates/**/Mock/</c>; regenerating the scene
	/// refreshes it.
	/// </summary>
	public sealed class CombatSimManifest : ScriptableObject
	{
		/// <summary>The fighter prefab — a shipped monster with the full character stack.</summary>
		public GameObject NpcPrefab;

		/// <summary>FishNet's generated DefaultPrefabObjects — a NetworkManager refuses to
		/// initialize without a spawnable prefabs collection.</summary>
		public FishNet.Managing.Object.PrefabObjects SpawnablePrefabs;

		/// <summary>
		/// A pre-configured, saved-inactive NetworkManager prefab (Tugboat + TimeManager in
		/// TimeManager physics mode + spawnable prefabs assigned). Authored by the generator
		/// because NetworkManager's editor OnValidate fires the moment the component is added —
		/// building one at runtime logs a spurious SpawnablePrefabs error before any field can
		/// be assigned.
		/// </summary>
		public GameObject NetworkPrefab;

		/// <summary>
		/// Every mock asset to feed through <c>ICachedObject.AddToCache</c> before any spawn:
		/// templates AND events, because ability IDs come from the cache and are 0 without it.
		/// </summary>
		public List<ScriptableObject> CacheAssets = new List<ScriptableObject>();

		/// <summary>The mock ability templates each fighter learns (a subset of CacheAssets).</summary>
		public List<AbilityTemplate> Roster = new List<AbilityTemplate>();

		/// <summary>Mock Channel Marker Event — every prefab ships ChanneledTemplate null.</summary>
		public AbilityEvent ChannelMarker;

		/// <summary>Mock Charge Marker Event — every prefab ships ChargedTemplate null.</summary>
		public AbilityEvent ChargeMarker;
	}
}
