using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Wildlife stands outside the factions. The wildlife prefabs were cloned from "an orc", race included, so a deer took the
	/// Orc Horde's standing: an Enemy to anyone hostile to orcs, out of reach of Dispel's cleanse (Jim, 11-16: Dispel must
	/// free wildlife) and a target for guards.
	/// </summary>
	[TestFixture]
	public class FactionWildlifeStandingTests
	{
		private const string OrcHordePath = "Assets/Modules/Factions/Templates/Orc Horde.asset";
		private const string OrcRacePath = "Assets/Templates/Entity/Races/Humanoid/Orc.asset";

		private readonly List<GameObject> temporaries = new List<GameObject>();
		private readonly List<Object> created = new List<Object>();
		private readonly List<ICachedObject> cached = new List<ICachedObject>();

		[TearDown]
		public void TearDown()
		{
			foreach (GameObject temporary in temporaries)
			{
				if (temporary != null) Object.DestroyImmediate(temporary);
			}
			temporaries.Clear();
			foreach (Object asset in created)
			{
				if (asset != null) Object.DestroyImmediate(asset);
			}
			created.Clear();
			foreach (ICachedObject entry in cached)
			{
				entry.RemoveFromCache();
			}
			cached.Clear();
			FactionTemplate.InvalidateStartingRaces();
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
		}

		private T NewCharacter<T>(string name, long id) where T : BaseCharacter
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			GameObject host = new GameObject(name);
			host.SetActive(false);
			temporaries.Add(host);
			T character = host.AddComponent<T>();
			character.ID = id;
			return character;
		}

		private static FactionController FactionsOf(ICharacter character)
		{
			FactionController factions = ((Component)character).GetComponent<FactionController>();
			if (factions == null)
			{
				factions = ((Component)character).gameObject.AddComponent<FactionController>();
			}
			factions.InitializeOnce(character);
			return factions;
		}

		private T Cached<T>(string path) where T : Object, ICachedObject
		{
			T asset = AssetDatabase.LoadAssetAtPath<T>(ProjectFiles.Path(path));
			Assert.IsNotNull(asset, $"{path} did not load.");
			// Only what this test put in the cache comes out again; an asset something else cached (ID set) is left alone.
			bool wasCached = asset.ID != 0;
			asset.AddToCache(asset.name);
			if (!wasCached)
			{
				cached.Add(asset);
			}
			return asset;
		}

		[Test]
		public void AnOrcRaceAnimal_IsNeutral_NotTheHordesEnemy()
		{
			FactionTemplate horde = Cached<FactionTemplate>(OrcHordePath);
			RaceTemplate orc = Cached<RaceTemplate>(OrcRacePath);
			FactionTemplate.InvalidateStartingRaces();
			Assert.AreSame(horde, FactionTemplate.InitialFor(orc), "precondition: orcs start in the Orc Horde");

			PlayerCharacter player = NewCharacter<PlayerCharacter>("WildlifeStandingPlayer", 9401);
			FactionController playerFactions = FactionsOf(player);
			playerFactions.SetFaction(horde.ID, FactionTemplate.Minimum, skipEvent: true);

			NPC animal = NewCharacter<NPC>("WildlifeStandingDeer", -9402);
			animal.SetRaceTemplateOnSpawn(orc);
			FactionController animalFactions = FactionsOf(animal);

			Assert.AreEqual(FactionAllianceLevel.Enemy, playerFactions.GetAllianceLevel(animalFactions),
				"precondition: an orc-race NPC is the enemy of a player hostile to the Horde");

			WildlifeTemplate deer = ScriptableObject.CreateInstance<WildlifeTemplate>();
			deer.Role = WildlifeRole.Prey;
			created.Add(deer);
			animal.Wildlife = deer;

			Assert.AreEqual(FactionAllianceLevel.Neutral, playerFactions.GetStandingAllianceLevel(animalFactions),
				"wildlife stands outside the factions, whatever race it was cloned with");
			Assert.AreEqual(FactionAllianceLevel.Neutral, playerFactions.GetAllianceLevel(animalFactions),
				"and is not anyone's enemy until a toggle says so");

			TargetAllianceCondition cleanse = new TargetAllianceCondition
			{
				ApplyToSelf = true, ApplyToEnemy = false, ApplyToNeutral = true, NeutralPlayersOnly = false, ApplyToAllies = true,
			};
			Assert.IsTrue(cleanse.Passes(playerFactions.GetAllianceLevel(animalFactions), playerFactions.GetStandingAllianceLevel(animalFactions), false),
				"Dispel's cleanse reaches it");
		}
	}
}
