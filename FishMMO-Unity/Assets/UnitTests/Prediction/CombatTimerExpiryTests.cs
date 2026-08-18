using NUnit.Framework;
using FishMMO.Shared;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for the combat window arithmetic in
	/// <see cref="CharacterDamageController.EvaluateCombatTimer"/>.
	///
	/// Two behaviours depend on this being right:
	///   - The teleport gate refuses to move a character between scenes while in combat.
	///   - The combat-logout hold keeps a disconnected character's body in the world, and
	///     removes it as soon as combat ends.
	///
	/// Both read <c>CharacterFlags.IsInCombat</c>, which is set once on the first combat action
	/// and cleared only by this arithmetic. A premature clear is therefore a combat-escape
	/// exploit, and a clear that never happens strands a body in the world.
	/// </summary>
	[TestFixture]
	public class CombatTimerExpiryTests
	{
		private const uint CombatDurationTicks = 600; // 20s at 30 tick/s, the shipped default.

		/// <summary>
		/// The headline guarantee: after a period of not fighting, combat ends.
		/// </summary>
		[Test]
		public void Combat_Expires_After_Duration_Of_No_Further_Actions()
		{
			uint lastCombatTick = 1000;

			// Advance one tick at a time, exactly as TimeManager.OnTick does, with no further
			// combat actions to refresh the window.
			CharacterDamageController.CombatTimerStep step = CharacterDamageController.CombatTimerStep.Continue;
			uint tick = lastCombatTick;
			for (uint elapsed = 1; elapsed <= CombatDurationTicks; elapsed++)
			{
				tick = lastCombatTick + elapsed;
				step = CharacterDamageController.EvaluateCombatTimer(tick, CombatDurationTicks, ref lastCombatTick);
				if (step == CharacterDamageController.CombatTimerStep.Expired)
				{
					break;
				}
			}

			Assert.AreEqual(CharacterDamageController.CombatTimerStep.Expired, step,
				"Combat must end once the duration elapses with no further combat actions.");
			Assert.AreEqual(1000u + CombatDurationTicks, tick,
				"Combat must end exactly on the duration boundary, not before or after.");
		}

		/// <summary>
		/// One tick short of the window is still combat. Guards against an off-by-one that would
		/// let a player teleport or log out a tick early.
		/// </summary>
		[Test]
		public void Combat_Does_Not_Expire_One_Tick_Early()
		{
			uint lastCombatTick = 1000;
			uint tick = lastCombatTick + CombatDurationTicks - 1;

			Assert.AreEqual(CharacterDamageController.CombatTimerStep.Continue,
				CharacterDamageController.EvaluateCombatTimer(tick, CombatDurationTicks, ref lastCombatTick));
		}

		/// <summary>
		/// Refreshing the window (any combat action calls EnterCombat, which rewrites
		/// lastCombatTick) restarts the countdown rather than accumulating toward the old expiry.
		/// </summary>
		[Test]
		public void Refreshing_Combat_Restarts_The_Window()
		{
			uint lastCombatTick = 1000;

			// Almost expired...
			uint tick = lastCombatTick + CombatDurationTicks - 1;
			Assert.AreEqual(CharacterDamageController.CombatTimerStep.Continue,
				CharacterDamageController.EvaluateCombatTimer(tick, CombatDurationTicks, ref lastCombatTick));

			// ...then a fresh combat action lands, as EnterCombat would record it.
			lastCombatTick = tick;

			Assert.AreEqual(CharacterDamageController.CombatTimerStep.Continue,
				CharacterDamageController.EvaluateCombatTimer(tick + 1, CombatDurationTicks, ref lastCombatTick),
				"A refreshed window must not expire on the very next tick.");
			Assert.AreEqual(CharacterDamageController.CombatTimerStep.Expired,
				CharacterDamageController.EvaluateCombatTimer(tick + CombatDurationTicks, CombatDurationTicks, ref lastCombatTick),
				"A refreshed window must expire a full duration after the refresh.");
		}

		/// <summary>
		/// A backwards tick must NOT be read as a colossal elapsed time.
		///
		/// This is the exact shape produced by starting a combat-logout linger: removing
		/// ownership flips IsController true on the server, so the resolver stops using the
		/// owner's replicate tick (which runs ahead under client-side prediction) and falls back
		/// to the server's slower local tick. Unsigned subtraction turns that regression into a
		/// value near uint.MaxValue, which would clear combat instantly.
		/// </summary>
		[Test]
		public void Backwards_Tick_Rebaselines_Instead_Of_Expiring()
		{
			// Owner's replicate tick was well ahead of the server's local tick.
			uint lastCombatTick = 5000;
			const uint serverLocalTick = 4900;

			var step = CharacterDamageController.EvaluateCombatTimer(serverLocalTick, CombatDurationTicks, ref lastCombatTick);

			Assert.AreEqual(CharacterDamageController.CombatTimerStep.Rebaselined, step,
				"A backwards tick must re-baseline, never expire — expiring here is a combat-escape exploit.");
			Assert.AreEqual(serverLocalTick, lastCombatTick,
				"The window must be re-measured from the new tick domain.");
		}

		/// <summary>
		/// After re-baselining, the window still expires — a full duration later, measured in the
		/// new domain. This is what lets a combat-logout body leave combat and be despawned
		/// rather than sitting until its hard deadline.
		/// </summary>
		[Test]
		public void Rebaselined_Window_Still_Expires_A_Full_Duration_Later()
		{
			uint lastCombatTick = 5000;
			const uint serverLocalTick = 4900;

			CharacterDamageController.EvaluateCombatTimer(serverLocalTick, CombatDurationTicks, ref lastCombatTick);
			Assert.AreEqual(serverLocalTick, lastCombatTick);

			Assert.AreEqual(CharacterDamageController.CombatTimerStep.Continue,
				CharacterDamageController.EvaluateCombatTimer(serverLocalTick + CombatDurationTicks - 1, CombatDurationTicks, ref lastCombatTick),
				"Still in combat one tick short of the re-measured window.");

			Assert.AreEqual(CharacterDamageController.CombatTimerStep.Expired,
				CharacterDamageController.EvaluateCombatTimer(serverLocalTick + CombatDurationTicks, CombatDurationTicks, ref lastCombatTick),
				"A re-baselined window must still end, or a combat-logout body would never leave combat.");
		}

		/// <summary>
		/// The re-baseline must happen once and then converge; it must not re-trigger on every
		/// subsequent tick and hold the character in combat forever.
		/// </summary>
		[Test]
		public void Rebaseline_Happens_Once_And_Converges()
		{
			uint lastCombatTick = 5000;
			uint tick = 4900;

			Assert.AreEqual(CharacterDamageController.CombatTimerStep.Rebaselined,
				CharacterDamageController.EvaluateCombatTimer(tick, CombatDurationTicks, ref lastCombatTick));

			int rebaselines = 0;
			for (uint i = 1; i <= CombatDurationTicks; i++)
			{
				var step = CharacterDamageController.EvaluateCombatTimer(tick + i, CombatDurationTicks, ref lastCombatTick);
				if (step == CharacterDamageController.CombatTimerStep.Rebaselined)
				{
					rebaselines++;
				}
			}

			Assert.AreEqual(0, rebaselines,
				"Once re-baselined the tick advances normally; further re-baselines would stall the timer indefinitely.");
		}

		/// <summary>
		/// Sanity check on the underlying hazard: the naive unsigned subtraction really does
		/// produce an immediate expiry for a backwards tick. This pins WHY the guard exists, so a
		/// future simplification back to raw subtraction fails loudly here.
		/// </summary>
		[Test]
		public void Naive_Unsigned_Subtraction_Would_Expire_Immediately()
		{
			// Non-const so the wraparound happens at runtime; as constants the compiler folds
			// this and rejects it as a checked-context overflow.
			uint lastCombatTick = 5000;
			uint serverLocalTick = 4900;

			uint naiveElapsed = unchecked(serverLocalTick - lastCombatTick);

			Assert.Greater(naiveElapsed, CombatDurationTicks,
				"Unsigned wraparound makes a 100-tick regression look like ~4.29 billion ticks elapsed.");
		}
	}
}
