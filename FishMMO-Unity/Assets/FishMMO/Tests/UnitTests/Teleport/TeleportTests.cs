using System.Collections.Generic;
using NUnit.Framework;
using FishMMO.Shared.Core;
using FishMMO.Server.Implementation.World.SceneServer;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the teleport-system rules that were fixed on 2026-09-08: the one spelling of a
	/// teleporter's lookup key, and the live-vs-baked waypoint comparison.
	/// </summary>
	[TestFixture]
	public class TeleportTests
	{
		[Test]
		public void TeleporterKey_TrimsAndStripsEveryCloneSuffix()
		{
			Assert.AreEqual("Teleporter", TeleporterKey.Normalize("Teleporter"));
			Assert.AreEqual("Teleporter", TeleporterKey.Normalize("  Teleporter "));
			Assert.AreEqual("Teleporter", TeleporterKey.Normalize("Teleporter(Clone)"));
			Assert.AreEqual("Teleporter", TeleporterKey.Normalize("Teleporter(Clone)(Clone)"));
			Assert.AreEqual("Teleporter", TeleporterKey.Normalize(" Teleporter(Clone) "));
		}

		[Test]
		public void TeleporterKey_NullAndEmptyAreEmpty_AndInnerTextIsKept()
		{
			Assert.AreEqual(string.Empty, TeleporterKey.Normalize(null));
			Assert.AreEqual(string.Empty, TeleporterKey.Normalize(""));
			Assert.AreEqual(string.Empty, TeleporterKey.Normalize("   "));
			// Only a trailing suffix is Unity's; the same text elsewhere is part of the name.
			Assert.AreEqual("(Clone) Gate", TeleporterKey.Normalize("(Clone) Gate"));
			Assert.AreEqual("From Start Scene A 1 Teleporter", TeleporterKey.Normalize("From Start Scene A 1 Teleporter"));
		}

		[Test]
		public void TeleporterKey_BakeAndRuntimeAgreeOnTheSpellingTheyUsedToDisagreeOn()
		{
			/* The bake trimmed; the runtime did not. A server build kept "(Clone)"; the editor
			 * stripped it. Both producers now normalise, so these four spellings are one key. */
			string baked = TeleporterKey.Normalize("Dungeon Exit ");
			string serverRuntime = TeleporterKey.Normalize("Dungeon Exit (Clone)");
			string editorRuntime = TeleporterKey.Normalize("Dungeon Exit ");
			Assert.AreEqual(baked, serverRuntime);
			Assert.AreEqual(baked, editorRuntime);
		}

		[Test]
		public void WaypointSceneAudit_AgreesWhenTheSetsMatch_InAnyOrder()
		{
			List<int> liveOnly = new List<int>(), bakedOnly = new List<int>();
			Assert.IsTrue(WaypointSceneAudit.Compare(new[] { 2, 0, 1 }, new[] { 0, 1, 2 }, liveOnly, bakedOnly));
			Assert.IsEmpty(liveOnly);
			Assert.IsEmpty(bakedOnly);
			Assert.IsTrue(WaypointSceneAudit.Compare(null, null, liveOnly, bakedOnly), "no waypoints on either side is agreement");
		}

		[Test]
		public void WaypointSceneAudit_ReportsEachDirectionOfDrift_Sorted()
		{
			List<int> liveOnly = new List<int>(), bakedOnly = new List<int>();
			// Live 0,3,5. Baked 0,1,5. Index 3 was added to the scene without a rebuild;
			// index 1 was removed from the scene but the cache still carries it.
			Assert.IsFalse(WaypointSceneAudit.Compare(new[] { 5, 3, 0 }, new[] { 0, 1, 5 }, liveOnly, bakedOnly));
			CollectionAssert.AreEqual(new[] { 3 }, liveOnly);
			CollectionAssert.AreEqual(new[] { 1 }, bakedOnly);
		}

		[Test]
		public void WaypointSceneAudit_ClearsTheOutputListsBeforeUse()
		{
			List<int> liveOnly = new List<int> { 99 }, bakedOnly = new List<int> { 98 };
			Assert.IsTrue(WaypointSceneAudit.Compare(new[] { 1 }, new[] { 1 }, liveOnly, bakedOnly));
			Assert.IsEmpty(liveOnly);
			Assert.IsEmpty(bakedOnly);
		}
	}
}
