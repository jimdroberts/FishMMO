using NUnit.Framework;
using FishMMO.Shared;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Proofs for the parts of the threat system that exist to be driven from outside the AI —
	/// taunts and scripted aggro grabs.
	/// </summary>
	/// <remarks>
	/// These cover the arithmetic that <see cref="ApplyTauntAction"/> performs on a threat table.
	/// The action itself needs a live scene to test end-to-end; the calculation that decides
	/// whether a taunt actually lands does not, and it is the part that was wrong before — a flat
	/// bonus that a long fight had already outgrown.
	/// </remarks>
	[TestFixture]
	public class AIThreatIntegrationTests
	{
		private const long TANK = 2001L;
		private const long DPS = 2002L;
		private const long HEALER = 2003L;

		private AggressionController controller;

		/// <summary>
		/// Builds a fresh table with decay disabled.
		/// </summary>
		[SetUp]
		public void SetUp()
		{
			controller = new AggressionController
			{
				DamageWeight = 1.0f,
				HitBonusPoints = 0f,
				DecayRate = 0f,
				ResourceWeight = 0.4f,
				TargetVarietyChance = 0f,
			};
		}

		[Test]
		public void GetHighestPoints_EmptyTable_ReturnsZero()
		{
			Assert.AreEqual(0f, controller.GetHighestPoints(), 0.001f);
		}

		[Test]
		public void GetHighestPoints_ReturnsTheLeader()
		{
			controller.RecordDamage(TANK, 100);
			controller.RecordDamage(DPS, 400);
			controller.RecordDamage(HEALER, 50);

			Assert.AreEqual(400f, controller.GetHighestPoints(), 0.001f);
		}

		[Test]
		public void GetHighestPoints_CanExcludeTheTaunter()
		{
			controller.RecordDamage(TANK, 900);
			controller.RecordDamage(DPS, 400);

			Assert.AreEqual(400f, controller.GetHighestPoints(TANK), 0.001f,
				"A taunter must compare against everyone else, not against itself.");
		}

		[Test]
		public void GetHighestPoints_DoesNotCreateEntries()
		{
			controller.GetHighestPoints(DPS);

			Assert.AreEqual(0, controller.Count,
				"Querying the table must never mutate it.");
		}

		[Test]
		public void TauntMath_FlatBonusAloneIsNotEnoughInALongFight()
		{
			/* The reason ApplyTauntAction has a top-threat guarantee at all. A tank taunting with
			 * a fixed 500 points against a DPS sitting on 5000 does not take the mob. */
			controller.RecordDamage(TANK, 100);
			controller.RecordDamage(DPS, 5000);

			const float flatTaunt = 500f;
			controller.AddPoints(TANK, flatTaunt);

			Assert.Less(controller.GetPoints(TANK), controller.GetPoints(DPS),
				"A flat taunt bonus is outgrown by sustained damage.");
		}

		[Test]
		public void TauntMath_TopThreatGuaranteePutsTheTaunterOnTop()
		{
			controller.RecordDamage(TANK, 100);
			controller.RecordDamage(DPS, 5000);

			// The same arithmetic ApplyTauntAction runs.
			const float leadOverHighest = 100f;
			float highest = controller.GetHighestPoints(TANK);
			float current = controller.GetPoints(TANK);
			float required = (highest + leadOverHighest) - current;

			controller.AddPoints(TANK, required);

			Assert.Greater(controller.GetPoints(TANK), controller.GetPoints(DPS),
				"The guarantee must place the taunter above the previous leader.");
			Assert.AreEqual(highest + leadOverHighest, controller.GetPoints(TANK), 0.01f);
		}

		[Test]
		public void TauntMath_GuaranteeIsStillCorrectWhenTheTaunterIsAlreadyLeading()
		{
			controller.RecordDamage(TANK, 5000);
			controller.RecordDamage(DPS, 100);

			float highest = controller.GetHighestPoints(TANK);
			float current = controller.GetPoints(TANK);
			float required = (highest + 100f) - current;

			// Negative: the taunter is already far ahead, so the guarantee asks for nothing.
			Assert.Less(required, 0f);
			Assert.Greater(controller.GetPoints(TANK), controller.GetPoints(DPS),
				"An already-leading taunter needs no top-up and must not lose threat.");
		}

		[Test]
		public void ResourceThreat_IsWeightedByTheControllersResourceWeight()
		{
			/* ResourceWeight was serialized, documented, and had no caller anywhere in the
			 * project — tuning it did nothing. ApplyThreatAction is now that caller. */
			controller.RecordDamage(DPS, 10);
			float before = controller.GetPoints(DPS);

			controller.RecordResourceSpent(DPS, 100);

			Assert.AreEqual(before + (100f * controller.ResourceWeight), controller.GetPoints(DPS), 0.01f);
		}

		[Test]
		public void ResourceThreat_RespectsAZeroWeight()
		{
			controller.ResourceWeight = 0f;
			controller.RecordDamage(DPS, 10);
			float before = controller.GetPoints(DPS);

			controller.RecordResourceSpent(DPS, 1000);

			Assert.AreEqual(before, controller.GetPoints(DPS), 0.01f,
				"A zero resource weight must mean casters draw no extra attention.");
		}
	}
}
