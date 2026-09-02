using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for how <see cref="BodyVisibilityManager"/> reports a model that cannot be split into
	/// body regions, which is issue #158.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Discovery used to log one Error per absent region, so every spawn produced six -- and no
	/// model in the project has ever had these regions, so the log said "incorrectly authored"
	/// about every character in the game. A message that fires for everything carries no
	/// information, and worse, it trains people to skip Errors from this system entirely.
	/// </para>
	/// <para>
	/// The distinction these tests exist to hold is between two situations the old code reported
	/// identically: a model that does not use per-region hiding at all (normal, and what every
	/// shipped race is), and a model split only halfway (a real fault, because the missing regions
	/// silently fail to hide and show up as clipping through armour).
	/// </para>
	/// </remarks>
	[TestFixture]
	public class BodyRegionDiscoveryTests
	{
		private static readonly string[] AllRegions =
		{
			"BodyHead", "BodyTorso", "BodyArms", "BodyHands", "BodyLegs", "BodyFeet",
		};

		private GameObject root;

		[TearDown]
		public void TearDown()
		{
			if (root != null)
			{
				UnityEngine.Object.DestroyImmediate(root);
			}
		}

		/// <summary>
		/// Builds a character whose mesh root carries the named region objects, each with a
		/// renderer unless stated otherwise.
		/// </summary>
		private BodyVisibilityManager BuildCharacter(bool withRenderers, params string[] regionNames)
		{
			root = new GameObject("BodyRegionTestCharacter");
			BodyVisibilityManager manager = root.AddComponent<BodyVisibilityManager>();

			GameObject meshRoot = new GameObject("MeshRoot");
			meshRoot.transform.SetParent(root.transform);

			for (int i = 0; i < regionNames.Length; i++)
			{
				GameObject region = new GameObject(regionNames[i]);
				region.transform.SetParent(meshRoot.transform);

				if (withRenderers)
				{
					region.AddComponent<SkinnedMeshRenderer>();
				}
			}

			typeof(CharacterBehaviour)
				.GetProperty("Character", BindingFlags.Instance | BindingFlags.Public)
				.SetValue(manager, new Harness.StubCharacter { MeshRoot = meshRoot.transform });

			return manager;
		}

		/// <summary>Runs discovery, which is private because nothing outside the component calls it.</summary>
		private static void Discover(BodyVisibilityManager manager)
		{
			MethodInfo discover = typeof(BodyVisibilityManager).GetMethod(
				"TryDiscoverRegionRenderers",
				BindingFlags.Instance | BindingFlags.NonPublic);

			LogAssert.IsNotNull(discover, "BodyVisibilityManager must still discover regions.");
			discover.Invoke(manager, null);
		}

		/// <summary>How many renderers discovery actually resolved.</summary>
		private static int DiscoveredCount(BodyVisibilityManager manager)
		{
			FieldInfo field = typeof(BodyVisibilityManager).GetField(
				"regionRenderers",
				BindingFlags.Instance | BindingFlags.NonPublic);

			LogAssert.IsNotNull(field, "the discovered renderers must still be recorded.");
			return ((IDictionary<BodyRegion, SkinnedMeshRenderer>)field.GetValue(manager)).Count;
		}

		[Test]
		public void AModelWithNoRegions_DiscoversNothingAndThatIsNotAFailure()
		{
			/* Every shipped race. The point is that this is a supported configuration: the
			 * character renders normally and equipment simply never hides anything. */
			BodyVisibilityManager manager = BuildCharacter(true);

			Discover(manager);

			LogAssert.AreEqual(0, DiscoveredCount(manager),
				"there is nothing to discover, and that is not a fault");
		}

		[Test]
		public void AModelWithNoRegions_StillHandlesEquipmentWithoutThrowing()
		{
			/* Why the quiet path is safe. Hiding is driven by worn equipment, which does not know
			 * whether the model supports regions, so every hide has to be survivable. */
			BodyVisibilityManager manager = BuildCharacter(true);
			Discover(manager);

			Assert.DoesNotThrow(
				() => manager.HideRegions(new[] { BodyRegion.Head, BodyRegion.Torso }, ItemSlot.Head),
				"equipment must not depend on the model being split");
		}

		[Test]
		public void AFullySplitModel_DiscoversEveryRegion()
		{
			/* The feature working. Without this, the fixture would pass just as happily against a
			 * discovery that found nothing at all, ever. */
			BodyVisibilityManager manager = BuildCharacter(true, AllRegions);

			Discover(manager);

			LogAssert.AreEqual(AllRegions.Length, DiscoveredCount(manager),
				"a properly split model must resolve all six regions");
		}

		[Test]
		public void AFullySplitModel_HidesTheRegionEquipmentCovers()
		{
			BodyVisibilityManager manager = BuildCharacter(true, AllRegions);
			Discover(manager);

			manager.HideRegions(new[] { BodyRegion.Head }, ItemSlot.Head);

			Transform head = root.transform.Find("MeshRoot/BodyHead");
			LogAssert.IsNotNull(head, "the head region must exist in this fixture");
			LogAssert.IsFalse(head.GetComponent<SkinnedMeshRenderer>().enabled,
				"a helmet must hide the head it covers");
		}

		[Test]
		public void APartlySplitModel_DiscoversTheRegionsItHas()
		{
			/* The case that stays loud. Warning rather than Error because nothing is left broken --
			 * the regions present still hide correctly -- but the absent ones fail silently, which
			 * surfaces as armour clipping rather than as anything in a log. */
			BodyVisibilityManager manager = BuildCharacter(true, "BodyHead", "BodyTorso", "BodyArms");

			Discover(manager);

			LogAssert.AreEqual(3, DiscoveredCount(manager),
				"a half-split model still resolves what it has");
		}

		[Test]
		public void ARegionWithoutARenderer_IsNotCountedAsFound()
		{
			/* Present but unusable. It must not count as found, or hiding would silently do nothing
			 * for a region the code believed it had. */
			BodyVisibilityManager manager = BuildCharacter(false, "BodyHead");

			Discover(manager);

			LogAssert.AreEqual(0, DiscoveredCount(manager),
				"an object that cannot render is not a usable region");
		}

		[Test]
		public void DiscoveryDoesNotReportAbsentRegionsAsErrors()
		{
			/* Pinned in source: the level is the whole subject of #158 and is not observable from
			 * the component. Every shipped model takes this path, so an Error here fires for every
			 * character in the game -- which is what made the log unreadable rather than useful. */
			string source = File.ReadAllText(Path.Combine(
				Directory.GetCurrentDirectory(),
				"Assets/Scripts/Shared/Implementation/Entity/Appearance/BodyVisibilityManager.cs"));

			int discovery = source.IndexOf("TryDiscoverRegionRenderers()", StringComparison.Ordinal);
			LogAssert.IsTrue(discovery >= 0, "discovery must still exist");

			int end = source.IndexOf("Finds the skeleton root", discovery, StringComparison.Ordinal);
			LogAssert.IsTrue(end > discovery, "the discovery body must still be locatable");

			LogAssert.IsFalse(
				source.Substring(discovery, end - discovery).Contains("Log.Error"),
				"a model without split regions is not an error -- see #158");
		}
	}
}
