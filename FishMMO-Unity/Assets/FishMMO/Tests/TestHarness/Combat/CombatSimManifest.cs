using System.Collections.Generic;
using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.TestHarness
{
	/// <summary>
	/// Direct references to everything the CombatSim needs: its fighter, its network prefabs, and the ability roster with
	/// the events and buffs it is built from, cached by hand so the sim does not wait on the addressable labels. Populated
	/// by the scene generator (FishMMO Dashboard → Maintenance, Test Scenes: Generate Combat Sim, or Refresh Combat Sim
	/// Roster) from the shipped test ability suite under <c>Modules/Abilities/Templates/Types</c> (the Mock
	/// set it was built from is gone).
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

		/// <summary>A channel marker event for the fighters' ChanneledTemplate, or null (none ships; held casts then end on release).</summary>
		public AbilityEvent ChannelMarker;

		/// <summary>A charge marker event for the fighters' ChargedTemplate, or null (none ships; staged charges need none).</summary>
		public AbilityEvent ChargeMarker;
	}
}
