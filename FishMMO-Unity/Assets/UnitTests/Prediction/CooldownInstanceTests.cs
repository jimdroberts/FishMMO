using NUnit.Framework;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Determinism proofs for <see cref="CooldownInstance"/>. The struct is the foundation
	/// of every ability cooldown and replaces the previous mutable SubtractTick() model.
	/// Because the struct is read-only after construction, replay safety reduces to verifying
	/// that the integer remainder math is identical across every call site.
	///
	/// Tests exercise the real production type — no in-test reimplementation.
	/// </summary>
	[TestFixture]
	public class CooldownInstanceTests
	{
		private const float TickDelta30 = 1f / 30f;

		[Test]
		public void Ctor_SecondsOverload_ComputesDurationTicksByCeiling()
		{
			// 0.05s @ 30 tps = 1.5 ticks → ceil → 2 ticks.
			var cd = new CooldownInstance(100u, 0.05f, TickDelta30);
			LogAssert.AreEqual(2u, cd.DurationTicks, "0.05s @ 30 tps must ceil to 2 ticks.");
			LogAssert.AreEqual(100u, cd.StartTick, "StartTick must be preserved.");
		}

		[Test]
		public void IsOnCooldown_BoundaryBeforeAtAfter_ExactExpiry()
		{
			var cd = new CooldownInstance(1_000u, 10u, TickDelta30);

			LogAssert.IsTrue(cd.IsOnCooldown(1_000u),
				"At the StartTick, the cooldown must be active.");
			LogAssert.IsTrue(cd.IsOnCooldown(1_009u),
				"One tick before the boundary, the cooldown must still be active.");
			LogAssert.IsFalse(cd.IsOnCooldown(1_010u),
				"Exactly at StartTick+DurationTicks, the cooldown must be expired.");
			LogAssert.IsFalse(cd.IsOnCooldown(1_011u),
				"After the boundary, the cooldown must remain expired.");
		}

		[Test]
		public void RemainingTicks_DecrementsLinearly_ZeroAtAndAfterExpiry()
		{
			var cd = new CooldownInstance(500u, 5u, TickDelta30);

			LogAssert.AreEqual(5u, cd.RemainingTicks(500u), "RemainingTicks at StartTick == DurationTicks.");
			LogAssert.AreEqual(4u, cd.RemainingTicks(501u), "RemainingTicks decrements by 1 per tick.");
			LogAssert.AreEqual(1u, cd.RemainingTicks(504u), "RemainingTicks at the last active tick == 1.");
			LogAssert.AreEqual(0u, cd.RemainingTicks(505u), "RemainingTicks at the boundary must be 0.");
			LogAssert.AreEqual(0u, cd.RemainingTicks(50_000u), "RemainingTicks remains 0 well past expiry.");
		}

		[Test]
		public void FromRemainingSeconds_RoundTrip_PreservesRemainingTicks()
		{
			uint currentTick = 10_000u;
			float totalSeconds = 3.0f;          // 90 ticks
			float remainingSeconds = 1.0f;      // 30 ticks

			var cd = CooldownInstance.FromRemainingSeconds(currentTick, totalSeconds, remainingSeconds, TickDelta30);

			LogAssert.AreEqual(90u, cd.DurationTicks, "Total seconds → 90 ticks.");
			LogAssert.AreEqual(30u, cd.RemainingTicks(currentTick),
				"FromRemainingSeconds must produce a StartTick that yields the expected remaining ticks.");
		}

		[Test]
		public void Ctor_ZeroTickDelta_DurationTicksIsZero()
		{
			// Defensive: CooldownInstance must not divide by zero.
			var cd = new CooldownInstance(0u, 5.0f, 0f);
			LogAssert.AreEqual(0u, cd.DurationTicks, "tickDelta == 0 must yield DurationTicks == 0.");
			LogAssert.IsFalse(cd.IsOnCooldown(0u),
				"With DurationTicks == 0 the cooldown must be inactive immediately.");
		}

		[Test]
		public void Immutability_NoSettersOnPublicSurface()
		{
			// Sanity: the struct exposes only get-only auto-properties. A reflective check
			// catches accidental field additions in future refactors.
			System.Type t = typeof(CooldownInstance);
			foreach (System.Reflection.PropertyInfo p in t.GetProperties())
			{
				LogAssert.IsFalse(p.CanWrite, $"Property {p.Name} must remain get-only — CooldownInstance must stay immutable.");
			}
		}
	}
}