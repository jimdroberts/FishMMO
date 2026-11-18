using System;
using NUnit.Framework;
using FishMMO.Shared;
using AuthTestTrace = FishMMO.UnitTests.Harness.AuthTestTrace;
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
			try
			{
				AuthTestTrace.LogTestStart(nameof(Pack_NoSlot_RoundTrips_NegativeOne),
					"Packing flags with slot = -1 (no consumable) must round-trip flags and yield a sign-extended -1 slot.")
					.GetAwaiter().GetResult();

				int flags = (int)(AbilityActivationFlags.IsActualData | AbilityActivationFlags.IsHeld);
				int packed = CharacterReconcileData.Pack(flags, -1);
				var rd = new CharacterReconcileData { PackedFlagsAndSlot = packed };
				AuthTestTrace.Log("CharacterReconcileDataPackTests", "STEP",
					$"flags=0x{flags:X} slot=-1 -> packed=0x{packed:X8} | UnpackFlags=0x{rd.UnpackFlags:X} UnpackSlot={rd.UnpackConsumableSlot}")
					.GetAwaiter().GetResult();

				LogAssert.AreEqual(flags, rd.UnpackFlags, "UnpackFlags must restore the original flags.");
				LogAssert.AreEqual((short)-1, rd.UnpackConsumableSlot,
					"UnpackConsumableSlot must return -1 when no consumable is active (sign-extension).");

				AuthTestTrace.Log("CharacterReconcileDataPackTests", "SUCCESS", nameof(Pack_NoSlot_RoundTrips_NegativeOne)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CharacterReconcileDataPackTests", "FAILURE", $"{nameof(Pack_NoSlot_RoundTrips_NegativeOne)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Pack_NoSlot_RoundTrips_NegativeOne)).GetAwaiter().GetResult();
			}
		}

		[Test]
		public void Pack_PositiveSlot_RoundTrips()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(Pack_PositiveSlot_RoundTrips),
					"Packing flags with a positive consumable slot (5) must round-trip both flags and slot.")
					.GetAwaiter().GetResult();

				int flags = (int)AbilityActivationFlags.IsConsumable;
				int packed = CharacterReconcileData.Pack(flags, 5);
				var rd = new CharacterReconcileData { PackedFlagsAndSlot = packed };
				AuthTestTrace.Log("CharacterReconcileDataPackTests", "STEP",
					$"flags=0x{flags:X} slot=5 -> packed=0x{packed:X8} | UnpackFlags=0x{rd.UnpackFlags:X} UnpackSlot={rd.UnpackConsumableSlot}")
					.GetAwaiter().GetResult();

				LogAssert.AreEqual(flags, rd.UnpackFlags, "Flags must round-trip even with a positive slot.");
				LogAssert.AreEqual((short)5, rd.UnpackConsumableSlot, "Positive slot must round-trip.");

				AuthTestTrace.Log("CharacterReconcileDataPackTests", "SUCCESS", nameof(Pack_PositiveSlot_RoundTrips)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CharacterReconcileDataPackTests", "FAILURE", $"{nameof(Pack_PositiveSlot_RoundTrips)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Pack_PositiveSlot_RoundTrips)).GetAwaiter().GetResult();
			}
		}

		[Test]
		public void Pack_MaxFlags_RoundTrips_AllSixteenBits()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(Pack_MaxFlags_RoundTrips_AllSixteenBits),
					"All 16 flag bits set with slot = 0 must round-trip without bleeding into the slot half.")
					.GetAwaiter().GetResult();

				int flags = 0xFFFF;
				int packed = CharacterReconcileData.Pack(flags, 0);
				var rd = new CharacterReconcileData { PackedFlagsAndSlot = packed };
				AuthTestTrace.Log("CharacterReconcileDataPackTests", "STEP",
					$"flags=0xFFFF slot=0 -> packed=0x{packed:X8} | UnpackFlags=0x{rd.UnpackFlags:X} UnpackSlot={rd.UnpackConsumableSlot}")
					.GetAwaiter().GetResult();

				LogAssert.AreEqual(flags, rd.UnpackFlags, "All 16 flag bits must round-trip without bleeding into slot bits.");
				LogAssert.AreEqual((short)0, rd.UnpackConsumableSlot, "Slot must remain 0 when only flags are set.");

				AuthTestTrace.Log("CharacterReconcileDataPackTests", "SUCCESS", nameof(Pack_MaxFlags_RoundTrips_AllSixteenBits)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CharacterReconcileDataPackTests", "FAILURE", $"{nameof(Pack_MaxFlags_RoundTrips_AllSixteenBits)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Pack_MaxFlags_RoundTrips_AllSixteenBits)).GetAwaiter().GetResult();
			}
		}

		[Test]
		public void Pack_NegativeSlotAndAllFlags_DoNotInterfere()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(Pack_NegativeSlotAndAllFlags_DoNotInterfere),
					"All flags set together with slot = -1 (0xFFFF upper half) must keep flag and slot halves independent.")
					.GetAwaiter().GetResult();

				int flags = 0xFFFF;
				int packed = CharacterReconcileData.Pack(flags, -1);
				var rd = new CharacterReconcileData { PackedFlagsAndSlot = packed };
				AuthTestTrace.Log("CharacterReconcileDataPackTests", "STEP",
					$"flags=0xFFFF slot=-1 -> packed=0x{packed:X8} | UnpackFlags=0x{rd.UnpackFlags:X} UnpackSlot={rd.UnpackConsumableSlot}")
					.GetAwaiter().GetResult();

				LogAssert.AreEqual(flags, rd.UnpackFlags,
					"All flags set together with slot = -1 (0xFFFF in upper 16) must not corrupt the flag bits.");
				LogAssert.AreEqual((short)-1, rd.UnpackConsumableSlot,
					"Slot = -1 must be recovered as a signed short.");

				AuthTestTrace.Log("CharacterReconcileDataPackTests", "SUCCESS", nameof(Pack_NegativeSlotAndAllFlags_DoNotInterfere)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CharacterReconcileDataPackTests", "FAILURE", $"{nameof(Pack_NegativeSlotAndAllFlags_DoNotInterfere)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Pack_NegativeSlotAndAllFlags_DoNotInterfere)).GetAwaiter().GetResult();
			}
		}

		[Test]
		public void AbilityActivationFlags_AllValues_FitWithinSixteenBits()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(AbilityActivationFlags_AllValues_FitWithinSixteenBits),
					"Every defined AbilityActivationFlags value must fit within the lower 16 bits used by Pack.")
					.GetAwaiter().GetResult();

				// Pack's contract requires every defined flag to fit in 16 bits.
				// Any new flag added past bit 15 will silently desync at runtime — this guard
				// catches it at test time.
				foreach (AbilityActivationFlags f in System.Enum.GetValues(typeof(AbilityActivationFlags)))
				{
					int value = (int)f;
					AuthTestTrace.Log("CharacterReconcileDataPackTests", "STEP",
						$"Checking flag {f} = 0x{value:X} (upper-bit mask = 0x{value & ~0xFFFF:X}).")
						.GetAwaiter().GetResult();
					LogAssert.IsTrue((value & ~0xFFFF) == 0,
						$"AbilityActivationFlags.{f} (0x{value:X}) does not fit in 16 bits. " +
						"Pack would corrupt the consumable slot.");
				}

				AuthTestTrace.Log("CharacterReconcileDataPackTests", "SUCCESS", nameof(AbilityActivationFlags_AllValues_FitWithinSixteenBits)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CharacterReconcileDataPackTests", "FAILURE", $"{nameof(AbilityActivationFlags_AllValues_FitWithinSixteenBits)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(AbilityActivationFlags_AllValues_FitWithinSixteenBits)).GetAwaiter().GetResult();
			}
		}
	}
}