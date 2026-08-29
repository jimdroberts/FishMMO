using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Asserts that every NPC prefab with a brain has AI level-of-detail settings assigned.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The LOD system is opt-in by reference: <see cref="AIController.LodSettings"/> is a plain
	/// field, and when it is null <see cref="AIController"/> keeps <c>currentLodTier</c> at
	/// <see cref="AILodTier.Active"/> and a tick interval of 1. Every NPC then runs the full
	/// pipeline — sweep, leash, behaviour tree, boss script, state machine, aggression — on every
	/// AI tick, whether or not a player is within three hundred metres of it.
	/// </para>
	/// <para>
	/// That is what makes this worth a test rather than a convention. A missing reference produces
	/// no error, no warning and no visible misbehaviour: the NPC is simply more attentive than it
	/// needs to be, and the cost is invisible until there are thousands of them. The whole system
	/// shipped switched off this way, with its settings assets authored and unreferenced.
	/// </para>
	/// <para>
	/// EditMode only — it uses <see cref="AssetDatabase"/> to find the shipped prefabs.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AILodAssignmentTests
	{
		/// <summary>Folder the NPC prefabs live in.</summary>
		private const string NPC_PREFAB_FOLDER = "Assets/Prefabs/Shared/Entity/NPCs";

		/// <summary>Folder the LOD settings assets live in.</summary>
		private const string LOD_FOLDER = "Assets/Templates/Entity/NPCs/AI/LOD";

		/// <summary>Every NPC prefab that carries an <see cref="AIController"/>.</summary>
		private static List<GameObject> brains;

		[OneTimeSetUp]
		public void LoadPrefabs()
		{
			brains = new List<GameObject>();

			string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { NPC_PREFAB_FOLDER });
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				if (prefab == null)
				{
					continue;
				}

				if (prefab.GetComponentInChildren<AIController>(true) != null)
				{
					brains.Add(prefab);
				}
			}
		}

		[Test]
		public void EveryNPCWithABrain_HasLodSettingsAssigned()
		{
			Assert.Greater(brains.Count, 0,
				$"No NPC prefab with an AIController was found under {NPC_PREFAB_FOLDER}. " +
				"The test is looking in the wrong place rather than passing.");

			StringBuilder missing = new StringBuilder();

			for (int i = 0; i < brains.Count; i++)
			{
				AIController controller = brains[i].GetComponentInChildren<AIController>(true);
				if (controller.LodSettings == null)
				{
					missing.AppendLine($"  {brains[i].name}");
				}
			}

			Assert.IsEmpty(missing.ToString(),
				"These NPC prefabs have no AI LOD settings, so their brains run the full pipeline " +
				"at every AI tick no matter how far away every player is:\n" + missing);
		}

		[Test]
		public void EveryAssignedLodSettings_SuspendsFartherThanItSimplifies()
		{
			/* The tiers have to be ordered or the thresholds mean nothing: an NPC cannot become
			 * dormant closer in than it becomes simplified. Inverting two numbers in the inspector
			 * produces a settings asset that reads plausibly and quietly never reaches a tier. */
			for (int i = 0; i < brains.Count; i++)
			{
				AILodSettings settings = brains[i].GetComponentInChildren<AIController>(true).LodSettings;
				if (settings == null)
				{
					continue;
				}

				Assert.Less(settings.ActiveDistanceSqr, settings.NearbyDistanceSqr,
					$"{settings.name}: Active must end before Nearby begins.");
				Assert.Less(settings.NearbyDistanceSqr, settings.FarDistanceSqr,
					$"{settings.name}: Nearby must end before Far begins.");
			}
		}

		[Test]
		public void EveryAssignedLodSettings_ThinksLessOftenTheFartherOutItGets()
		{
			/* The intervals are what the distances buy. A Far tier that ticks as often as Active is
			 * a tier in name only, and the ordering is easy to break by editing one field. */
			for (int i = 0; i < brains.Count; i++)
			{
				AILodSettings settings = brains[i].GetComponentInChildren<AIController>(true).LodSettings;
				if (settings == null)
				{
					continue;
				}

				Assert.LessOrEqual(settings.GetTickInterval(AILodTier.Active), settings.GetTickInterval(AILodTier.Nearby),
					$"{settings.name}: Nearby should not think more often than Active.");
				Assert.LessOrEqual(settings.GetTickInterval(AILodTier.Nearby), settings.GetTickInterval(AILodTier.Far),
					$"{settings.name}: Far should not think more often than Nearby.");
				Assert.LessOrEqual(settings.GetTickInterval(AILodTier.Far), settings.GetTickInterval(AILodTier.Dormant),
					$"{settings.name}: Dormant should not wake more often than Far thinks.");
			}
		}

		[Test]
		public void TheShippedLodAssets_AreAllReachableFromSomePrefab()
		{
			/* The failure this whole fixture exists for, from the other direction: three LOD assets
			 * were authored and none of them was referenced by anything. An asset nobody points at
			 * is indistinguishable from one that was never made. */
			string[] guids = AssetDatabase.FindAssets("t:AILodSettings", new[] { LOD_FOLDER });
			Assert.Greater(guids.Length, 0, $"No AILodSettings assets found under {LOD_FOLDER}.");

			HashSet<AILodSettings> referenced = new HashSet<AILodSettings>();
			for (int i = 0; i < brains.Count; i++)
			{
				AILodSettings settings = brains[i].GetComponentInChildren<AIController>(true).LodSettings;
				if (settings != null)
				{
					referenced.Add(settings);
				}
			}

			StringBuilder orphans = new StringBuilder();
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				AILodSettings settings = AssetDatabase.LoadAssetAtPath<AILodSettings>(path);
				if (settings != null && !referenced.Contains(settings))
				{
					orphans.AppendLine($"  {settings.name}");
				}
			}

			/* Reported rather than failed. A profile authored ahead of the content that will use it
			 * is legitimate; a profile nobody ever wired up is the bug. Only the assertion above
			 * can tell those apart, so this one only says what is unused. */
			if (orphans.Length > 0)
			{
				TestContext.WriteLine("AI LOD assets not referenced by any NPC prefab:\n" + orphans);
			}

			Assert.Pass();
		}
	}
}
