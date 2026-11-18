using System.Collections.Generic;
﻿using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject template defining a faction's properties, reputation bounds, and default relationships.
	/// </summary>
	[CreateAssetMenu(fileName = "New Faction", menuName = "FishMMO/Character/Faction/Faction", order = 1)]
	public class FactionTemplate : CachedScriptableObject<FactionTemplate>, ICachedObject
	{
		/// <summary>
		/// Serializable hash set of faction templates. Used for storing allied, neutral, and hostile relationships.
		/// </summary>
		[Serializable]
		public class FactionHashSet : SerializableHashSet<FactionTemplate> { }

		/// <summary>
		/// Addressable reference to the icon sprite for this faction.
		/// </summary>
		public AssetReferenceSprite IconReference;

		/// <summary>
		/// The loaded icon sprite. Only available on the client after OnLoad completes.
		/// </summary>
		[System.NonSerialized]
		private Sprite loadedIcon;

		/// <summary>
		/// The icon for this faction (loaded at runtime on client).
		/// </summary>
		public Sprite Icon { get { return this.loadedIcon; } }

		/// <summary>
		/// The minimum possible value for faction reputation or standing.
		/// </summary>
		public const int Minimum = -10000;

		/// <summary>
		/// The maximum possible value for faction reputation or standing.
		/// </summary>
		public const int Maximum = 10000;

		/// <summary>
		/// Description of the faction, used for tooltips and UI.
		/// </summary>
		public string Description;

		/// <summary>
		/// Set of factions that are considered allied by default.
		/// </summary>
		public FactionHashSet DefaultAllied;

		/// <summary>
		/// Set of factions that are considered neutral by default.
		/// </summary>
		public FactionHashSet DefaultNeutral;

		/// <summary>
		/// Set of factions that are considered hostile by default.
		/// </summary>
		public FactionHashSet DefaultHostile;

		/// <summary>
		/// Races whose members start in this faction: its allied, neutral and hostile factions become theirs, at creation and
		/// for every NPC of the race. Was <c>RaceTemplate.InitialFaction</c>; it lives here so a race (core) names nothing of the
		/// Factions module.
		/// </summary>
		[Tooltip("Races whose members start in this faction (its allies, neutrals and enemies become theirs).")]
		public List<RaceTemplate> StartingRaces = new List<RaceTemplate>();

		private static Dictionary<int, FactionTemplate> initialByRace;
		private static int initialByRaceSourceCount = -1;

		/// <summary>Forgets the race lookup <see cref="InitialFor"/> keeps; it is rebuilt on the next question.</summary>
		public static void InvalidateStartingRaces()
		{
			initialByRace = null;
			initialByRaceSourceCount = -1;
		}

		/// <summary>The faction members of <paramref name="race"/> start in (the first that lists it), or null.</summary>
		public static FactionTemplate InitialFor(RaceTemplate race)
		{
			if (race == null)
			{
				return null;
			}
			Dictionary<int, FactionTemplate> all = GetCache<FactionTemplate>();
			int count = all != null ? all.Count : 0;
			if (initialByRace == null || count != initialByRaceSourceCount)
			{
				initialByRace = new Dictionary<int, FactionTemplate>();
				initialByRaceSourceCount = count;
				if (all != null)
				{
					foreach (FactionTemplate faction in all.Values)
					{
						if (faction?.StartingRaces == null)
						{
							continue;
						}
						foreach (RaceTemplate starting in faction.StartingRaces)
						{
							if (starting != null && !initialByRace.ContainsKey(starting.ID))
							{
								initialByRace[starting.ID] = faction;
							}
						}
					}
				}
			}
			return initialByRace.TryGetValue(race.ID, out FactionTemplate initial) ? initial : null;
		}

		/// <summary>
		/// The display name of the faction (from the ScriptableObject's name).
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// Called when the faction template is loaded into cache. Loads the icon on the client.
		/// </summary>
		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			InvalidateStartingRaces();
			base.OnLoad(typeName, resourceName, resourceID);

			if (typeName != nameof(FactionTemplate))
				return;

#if !UNITY_SERVER
			if (IconReference != null && IconReference.RuntimeKeyIsValid())
			{
				IconReference.LoadAssetAsync<Sprite>().Completed += (handle) =>
				{
					if (handle.Status == AsyncOperationStatus.Succeeded)
						loadedIcon = handle.Result;
				};
			}
#endif
		}

		/// <summary>
		/// Called when the faction template is unloaded from cache. Releases the icon on the client.
		/// </summary>
		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			InvalidateStartingRaces();
			if (typeName == nameof(FactionTemplate))
			{
#if !UNITY_SERVER
				if (IconReference != null && IconReference.IsValid())
				{
					IconReference.ReleaseAsset();
				}
				loadedIcon = null;
#endif
			}

			base.OnUnload(typeName, resourceName, resourceID);
		}
	}
}