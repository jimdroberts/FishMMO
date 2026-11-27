using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Database.Data;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>A resting mercenary's row round-trips through the controller: kit, name, look, stance, health and buffs.</summary>
	[TestFixture]
	public class MercenaryRecordsTests
	{
		private GameObject host;

		[TearDown]
		public void TearDown()
		{
			if (host != null)
			{
				Object.DestroyImmediate(host);
			}
		}

		private MercenaryController NewController()
		{
			host = new GameObject("MercenaryRecordsTests");
			return host.AddComponent<MercenaryController>();
		}

		[Test]
		public void ARestingMercenary_ComesBackAsItWasSaved()
		{
			MercenaryController controller = NewController();
			controller.AppearanceSeed = 1234;
			controller.NameState = new SceneObjectNamer.NameState() { Seed = 99, Gender = CharacterGender.Female, BiomeID = 7, Variant = 2 };
			controller.RaceTemplateID = 4321;
			controller.SavedResources = new List<MercenaryPersistedResource> { new MercenaryPersistedResource() { TemplateID = 5, CurrentValue = 42.5f } };
			controller.SavedBuffs = new List<MercenaryPersistedBuff>
			{
				new MercenaryPersistedBuff() { TemplateID = 8, RemainingTime = 30f, TickTime = 1f, Stacks = 2, TickCount = 3 },
			};
			controller.SetState(77, "Brannoc", true, PetStance.Aggressive);

			CharacterMercenaryData row = MercenaryRecords.Capture(10, controller, null, null);
			LogAssert.IsNotNull(row);
			LogAssert.AreEqual(77, row.KitID);
			LogAssert.IsTrue(row.Resting);
			LogAssert.AreEqual((byte)PetStance.Aggressive, row.Stance);
			LogAssert.AreEqual(1, row.ResourceTemplateIDs.Length);
			LogAssert.AreEqual(1, row.BuffTemplateIDs.Length);

			MercenaryController restored = new GameObject("restored").AddComponent<MercenaryController>();
			try
			{
				MercenaryRecords.Apply(row, restored);
				LogAssert.AreEqual(77, restored.KitID);
				LogAssert.AreEqual("Brannoc", restored.MercenaryName);
				LogAssert.IsTrue(restored.Resting);
				LogAssert.AreEqual(PetStance.Aggressive, restored.Stance);
				LogAssert.AreEqual(1234, restored.AppearanceSeed, "the face it was hired with");
				LogAssert.AreEqual(99, restored.NameState.Seed, "the name it was hired under");
				LogAssert.AreEqual(CharacterGender.Female, restored.NameState.Gender);
				LogAssert.AreEqual(4321, restored.RaceTemplateID, "the race it was hired as (its name and face depend on it)");
				LogAssert.AreEqual(42.5f, restored.SavedResources[0].CurrentValue, "its health as it left");
				LogAssert.AreEqual(2, restored.SavedBuffs[0].Stacks);
				LogAssert.IsFalse(restored.PersistenceDirty, "a loaded mercenary has nothing to write");
			}
			finally
			{
				Object.DestroyImmediate(restored.gameObject);
			}
		}

		[Test]
		public void NoMercenary_HasNoRow()
		{
			MercenaryController controller = NewController();
			LogAssert.IsNull(MercenaryRecords.Capture(10, controller, null, null));
		}

		[Test]
		public void AChange_KeepsTheDirtyMark_UntilASaveOfItLands()
		{
			MercenaryController controller = NewController();
			controller.MarkDirty();
			int captured = controller.ChangeCount;
			controller.MarkDirty();
			LogAssert.AreNotEqual(captured, controller.ChangeCount, "a later change is a new count, so an earlier save cannot clear it");
		}
	}
}
