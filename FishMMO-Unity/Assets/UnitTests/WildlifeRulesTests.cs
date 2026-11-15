using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Wildlife lives outside the faction system: anyone may harm it and it may harm anyone, except a pack its own members;
	/// predators hunt everything but their pack, prey flee from everything but prey and territorial animals.
	/// </summary>
	public class WildlifeRulesTests
	{
		private const WildlifeRole None = WildlifeRole.None;
		private const WildlifeRole Prey = WildlifeRole.Prey;
		private const WildlifeRole Predator = WildlifeRole.Predator;
		private const WildlifeRole Territorial = WildlifeRole.Territorial;

		[Test]
		public void WildlifeDecidesOnlyWhenEitherSideIsWild()
		{
			LogAssert.IsFalse(WildlifeRules.Decides(None, None), "two ordinary characters: the faction rules decide");
			LogAssert.IsTrue(WildlifeRules.Decides(None, Prey), "a player hitting a deer");
			LogAssert.IsTrue(WildlifeRules.Decides(Predator, None), "a wolf biting a player or an orc");
		}

		[Test]
		public void AnyoneMayHarmWildlifeAndWildlifeAnyone()
		{
			LogAssert.IsTrue(WildlifeRules.MayHarm(None, 0, Prey, 0), "a player may hunt a deer");
			LogAssert.IsTrue(WildlifeRules.MayHarm(None, 0, Predator, 5), "a player may kill a wolf");
			LogAssert.IsTrue(WildlifeRules.MayHarm(Predator, 5, None, 0), "a wolf may bite a player or any NPC");
			LogAssert.IsTrue(WildlifeRules.MayHarm(Predator, 5, Prey, 9), "a wolf may kill a deer");
			LogAssert.IsTrue(WildlifeRules.MayHarm(Predator, 5, Predator, 6), "two packs fight it out");
		}

		[Test]
		public void APackNeverHarmsItsOwn()
		{
			LogAssert.IsFalse(WildlifeRules.MayHarm(Predator, 5, Predator, 5), "wolves of one spawner never bite each other");
			LogAssert.IsFalse(WildlifeRules.MayHarm(Prey, 7, Prey, 7), "nor does a herd");
			LogAssert.IsTrue(WildlifeRules.MayHarm(Predator, 0, Predator, 0), "pack 0 is no pack: two loose animals may fight");
		}

		[Test]
		public void PredatorsNoticeEverythingButTheirPack()
		{
			LogAssert.IsTrue(WildlifeRules.Notices(Predator, 5, None, 0, out bool decided) && decided, "players and NPCs");
			LogAssert.IsTrue(WildlifeRules.Notices(Predator, 5, Prey, 2, out _), "prey");
			LogAssert.IsTrue(WildlifeRules.Notices(Predator, 5, Territorial, 3, out _), "a boar");
			LogAssert.IsTrue(WildlifeRules.Notices(Predator, 5, Predator, 6, out _), "another pack");
			LogAssert.IsFalse(WildlifeRules.Notices(Predator, 5, Predator, 5, out _), "not its own pack");
		}

		[Test]
		public void PreyFleesFromAllButPreyAndTerritorial()
		{
			LogAssert.IsTrue(WildlifeRules.Notices(Prey, 2, None, 0, out bool decided) && decided, "players and NPCs scare it");
			LogAssert.IsTrue(WildlifeRules.Notices(Prey, 2, Predator, 5, out _), "predators scare it");
			LogAssert.IsFalse(WildlifeRules.Notices(Prey, 2, Prey, 9, out _), "other prey does not");
			LogAssert.IsFalse(WildlifeRules.Notices(Prey, 2, Territorial, 3, out _), "a grazing boar does not");
		}

		[Test]
		public void TerritorialChargesIntrudersOnly()
		{
			LogAssert.IsTrue(WildlifeRules.Notices(Territorial, 3, None, 0, out _), "a player who comes too close");
			LogAssert.IsTrue(WildlifeRules.Notices(Territorial, 3, Predator, 5, out _), "a wolf");
			LogAssert.IsFalse(WildlifeRules.Notices(Territorial, 3, Prey, 2, out _), "not deer");
			LogAssert.IsFalse(WildlifeRules.Notices(Territorial, 3, Territorial, 4, out _), "not its own kind");
		}

		[Test]
		public void OrdinaryNpcsLeaveTheDecisionToFactions()
		{
			LogAssert.IsFalse(WildlifeRules.Notices(None, 0, Prey, 2, out bool decided), "an orc does not hunt deer by these rules");
			LogAssert.IsFalse(decided, "its faction check decides instead");
		}

		[Test]
		public void PreyRunsAwayFromEveryThreatAtOnce()
		{
			Vector3 heading = WildlifeRules.FleeHeading(Vector3.zero, new List<Vector3> { new Vector3(-10f, 0f, 0f) }, Vector3.forward);
			LogAssert.IsTrue(Vector3.Dot(heading, Vector3.right) > 0.999f, "one threat to the west: run east");

			// Wolves west and east, equally close: neither way, so it breaks out sideways (the fallback axis).
			heading = WildlifeRules.FleeHeading(Vector3.zero,
				new List<Vector3> { new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f) }, Vector3.forward);
			LogAssert.IsTrue(Mathf.Abs(heading.x) < 1e-3f, "two opposite threats cancel along their line");

			// A near wolf west and a far one north: mostly east, a little south.
			heading = WildlifeRules.FleeHeading(Vector3.zero,
				new List<Vector3> { new Vector3(-5f, 0f, 0f), new Vector3(0f, 0f, 30f) }, Vector3.forward);
			LogAssert.IsTrue(heading.x > 0.9f && heading.z < 0f, "the nearer threat weighs more");
			LogAssert.IsTrue(Mathf.Abs(heading.y) < 1e-6f && Mathf.Abs(heading.magnitude - 1f) < 1e-4f, "flat and unit length");
		}
	}
}
