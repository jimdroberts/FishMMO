using NUnit.Framework;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Validates the 32-bit packing of <see cref="CharacterReconcileData.PackedFlagsAndSlot"/>:
	/// the lower 16 bits hold activation flags and the upper 16 bits hold the consumable
	/// inventory slot as a signed short (with -1 indicating "no consumable"). Off-by-one or
	/// sign-extension bugs here silently desync persistent ability state (IsHeld, IsConsumable,
	/// IsMount, etc.) across the entire reconcile pipeline.
	///
	/// Tests exercise the real <see cref="CharacterReconcileData.Pack"/>,
	/// <see cref="CharacterReconcileData.UnpackFlags"/> and
	/// <see cref="CharacterReconcileData.UnpackConsumableSlot"/> — no reimplementation.
	/// </summary>
	[TestFixture]
	public class CharacterReconcileDataPackTests
	{
		[Test]
		public void Pack_NoSlot_RoundTrips_NegativeOne()
		{
			int flags = (int)(AbilityActivationFlags.IsActualData | AbilityActivationFlags.IsHeld);
			int packed = CharacterReconcileData.Pack(flags, -1);
			var rd = new CharacterReconcileData { PackedFlagsAndSlot = packed };

			LogAssert.AreEqual(flags, rd.UnpackFlags, "UnpackFlags must restore the original flags.");
			LogAssert.AreEqual((short)-1, rd.UnpackConsumableSlot,
				"UnpackConsumableSlot must return -1 when no consumable is active (sign-extension).");
		}

		[Test]
		public void Pack_PositiveSlot_RoundTrips()
		{
			int flags = (int)AbilityActivationFlags.IsConsumable;
			int packed = CharacterReconcileData.Pack(flags, 5);
			var rd = new CharacterReconcileData { PackedFlagsAndSlot = packed };

			LogAssert.AreEqual(flags, rd.UnpackFlags, "Flags must round-trip even with a positive slot.");
			LogAssert.AreEqual((short)5, rd.UnpackConsumableSlot, "Positive slot must round-trip.");
		}

		[Test]
		public void Pack_MaxFlags_RoundTrips_AllSixteenBits()
		{
			int flags = 0xFFFF;
			int packed = CharacterReconcileData.Pack(flags, 0);
			var rd = new CharacterReconcileData { PackedFlagsAndSlot = packed };

			LogAssert.AreEqual(flags, rd.UnpackFlags, "All 16 flag bits must round-trip without bleeding into slot bits.");
			LogAssert.AreEqual((short)0, rd.UnpackConsumableSlot, "Slot must remain 0 when only flags are set.");
		}

		[Test]
		public void Pack_NegativeSlotAndAllFlags_DoNotInterfere()
		{
			int flags = 0xFFFF;
			int packed = CharacterReconcileData.Pack(flags, -1);
			var rd = new CharacterReconcileData { PackedFlagsAndSlot = packed };

			LogAssert.AreEqual(flags, rd.UnpackFlags,
				"All flags set together with slot = -1 (0xFFFF in upper 16) must not corrupt the flag bits.");
			LogAssert.AreEqual((short)-1, rd.UnpackConsumableSlot,
				"Slot = -1 must be recovered as a signed short.");
		}

		[Test]
		public void AbilityActivationFlags_AllValues_FitWithinSixteenBits()
		{
			// Pack's contract requires every defined flag to fit in 16 bits.
			// Any new flag added past bit 15 will silently desync at runtime — this guard
			// catches it at test time.
			foreach (AbilityActivationFlags f in System.Enum.GetValues(typeof(AbilityActivationFlags)))
			{
				int value = (int)f;
				LogAssert.IsTrue((value & ~0xFFFF) == 0,
					$"AbilityActivationFlags.{f} (0x{value:X}) does not fit in 16 bits. " +
					"Pack would corrupt the consumable slot.");
			}
		}
	}
}