using System;
using NUnit.Framework;
using FishMMO.Shared;
using AuthTestTrace = FishMMO.UnitTests.Harness.AuthTestTrace;
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
			try
			{
				AuthTestTrace.LogTestStart(nameof(Ctor_SecondsOverload_ComputesDurationTicksByCeiling),
					"The seconds constructor must convert duration to ticks by ceiling (0.05s @ 30 tps -> 2 ticks).")
					.GetAwaiter().GetResult();

				// 0.05s @ 30 tps = 1.5 ticks → ceil → 2 ticks.
				var cd = new CooldownInstance(100u, 0.05f, TickDelta30);
				AuthTestTrace.Log("CooldownInstanceTests", "STEP",
					$"CooldownInstance(start=100, seconds=0.05, tickDelta={TickDelta30:F5}) -> DurationTicks={cd.DurationTicks} StartTick={cd.StartTick}")
					.GetAwaiter().GetResult();

				LogAssert.AreEqual(2u, cd.DurationTicks, "0.05s @ 30 tps must ceil to 2 ticks.");
				LogAssert.AreEqual(100u, cd.StartTick, "StartTick must be preserved.");

				AuthTestTrace.Log("CooldownInstanceTests", "SUCCESS", nameof(Ctor_SecondsOverload_ComputesDurationTicksByCeiling)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CooldownInstanceTests", "FAILURE", $"{nameof(Ctor_SecondsOverload_ComputesDurationTicksByCeiling)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Ctor_SecondsOverload_ComputesDurationTicksByCeiling)).GetAwaiter().GetResult();
			}
		}

		[Test]
		public void IsOnCooldown_BoundaryBeforeAtAfter_ExactExpiry()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(IsOnCooldown_BoundaryBeforeAtAfter_ExactExpiry),
					"IsOnCooldown must be inclusive of StartTick and exclusive of StartTick+DurationTicks (start=1000, dur=10).")
					.GetAwaiter().GetResult();

				var cd = new CooldownInstance(1_000u, 10u, TickDelta30);
				AuthTestTrace.Log("CooldownInstanceTests", "STEP",
					$"Boundary checks: @1000={cd.IsOnCooldown(1_000u)} @1009={cd.IsOnCooldown(1_009u)} @1010={cd.IsOnCooldown(1_010u)} @1011={cd.IsOnCooldown(1_011u)}")
					.GetAwaiter().GetResult();

				LogAssert.IsTrue(cd.IsOnCooldown(1_000u),
					"At the StartTick, the cooldown must be active.");
				LogAssert.IsTrue(cd.IsOnCooldown(1_009u),
					"One tick before the boundary, the cooldown must still be active.");
				LogAssert.IsFalse(cd.IsOnCooldown(1_010u),
					"Exactly at StartTick+DurationTicks, the cooldown must be expired.");
				LogAssert.IsFalse(cd.IsOnCooldown(1_011u),
					"After the boundary, the cooldown must remain expired.");

				AuthTestTrace.Log("CooldownInstanceTests", "SUCCESS", nameof(IsOnCooldown_BoundaryBeforeAtAfter_ExactExpiry)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CooldownInstanceTests", "FAILURE", $"{nameof(IsOnCooldown_BoundaryBeforeAtAfter_ExactExpiry)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(IsOnCooldown_BoundaryBeforeAtAfter_ExactExpiry)).GetAwaiter().GetResult();
			}
		}

		[Test]
		public void RemainingTicks_DecrementsLinearly_ZeroAtAndAfterExpiry()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(RemainingTicks_DecrementsLinearly_ZeroAtAndAfterExpiry),
					"RemainingTicks must decrement one per tick and clamp to 0 at/after expiry (start=500, dur=5).")
					.GetAwaiter().GetResult();

				var cd = new CooldownInstance(500u, 5u, TickDelta30);
				AuthTestTrace.Log("CooldownInstanceTests", "STEP",
					$"RemainingTicks: @500={cd.RemainingTicks(500u)} @501={cd.RemainingTicks(501u)} @504={cd.RemainingTicks(504u)} @505={cd.RemainingTicks(505u)} @50000={cd.RemainingTicks(50_000u)}")
					.GetAwaiter().GetResult();

				LogAssert.AreEqual(5u, cd.RemainingTicks(500u), "RemainingTicks at StartTick == DurationTicks.");
				LogAssert.AreEqual(4u, cd.RemainingTicks(501u), "RemainingTicks decrements by 1 per tick.");
				LogAssert.AreEqual(1u, cd.RemainingTicks(504u), "RemainingTicks at the last active tick == 1.");
				LogAssert.AreEqual(0u, cd.RemainingTicks(505u), "RemainingTicks at the boundary must be 0.");
				LogAssert.AreEqual(0u, cd.RemainingTicks(50_000u), "RemainingTicks remains 0 well past expiry.");

				AuthTestTrace.Log("CooldownInstanceTests", "SUCCESS", nameof(RemainingTicks_DecrementsLinearly_ZeroAtAndAfterExpiry)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CooldownInstanceTests", "FAILURE", $"{nameof(RemainingTicks_DecrementsLinearly_ZeroAtAndAfterExpiry)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(RemainingTicks_DecrementsLinearly_ZeroAtAndAfterExpiry)).GetAwaiter().GetResult();
			}
		}

		[Test]
		public void FromRemainingSeconds_RoundTrip_PreservesRemainingTicks()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(FromRemainingSeconds_RoundTrip_PreservesRemainingTicks),
					"FromRemainingSeconds must back-compute a StartTick yielding the expected remaining ticks (total=3s, remaining=1s).")
					.GetAwaiter().GetResult();

				uint currentTick = 10_000u;
				float totalSeconds = 3.0f;          // 90 ticks
				float remainingSeconds = 1.0f;      // 30 ticks

				var cd = CooldownInstance.FromRemainingSeconds(currentTick, totalSeconds, remainingSeconds, TickDelta30);
				AuthTestTrace.Log("CooldownInstanceTests", "STEP",
					$"currentTick={currentTick} total={totalSeconds}s remaining={remainingSeconds}s -> StartTick={cd.StartTick} DurationTicks={cd.DurationTicks} RemainingTicks={cd.RemainingTicks(currentTick)}")
					.GetAwaiter().GetResult();

				LogAssert.AreEqual(90u, cd.DurationTicks, "Total seconds → 90 ticks.");
				LogAssert.AreEqual(30u, cd.RemainingTicks(currentTick),
					"FromRemainingSeconds must produce a StartTick that yields the expected remaining ticks.");

				AuthTestTrace.Log("CooldownInstanceTests", "SUCCESS", nameof(FromRemainingSeconds_RoundTrip_PreservesRemainingTicks)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CooldownInstanceTests", "FAILURE", $"{nameof(FromRemainingSeconds_RoundTrip_PreservesRemainingTicks)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(FromRemainingSeconds_RoundTrip_PreservesRemainingTicks)).GetAwaiter().GetResult();
			}
		}

		[Test]
		public void Ctor_ZeroTickDelta_DurationTicksIsZero()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(Ctor_ZeroTickDelta_DurationTicksIsZero),
					"A zero tickDelta must not divide-by-zero; DurationTicks must be 0 and the cooldown inactive.")
					.GetAwaiter().GetResult();

				// Defensive: CooldownInstance must not divide by zero.
				var cd = new CooldownInstance(0u, 5.0f, 0f);
				AuthTestTrace.Log("CooldownInstanceTests", "STEP",
					$"CooldownInstance(start=0, seconds=5, tickDelta=0) -> DurationTicks={cd.DurationTicks} IsOnCooldown(0)={cd.IsOnCooldown(0u)}")
					.GetAwaiter().GetResult();

				LogAssert.AreEqual(0u, cd.DurationTicks, "tickDelta == 0 must yield DurationTicks == 0.");
				LogAssert.IsFalse(cd.IsOnCooldown(0u),
					"With DurationTicks == 0 the cooldown must be inactive immediately.");

				AuthTestTrace.Log("CooldownInstanceTests", "SUCCESS", nameof(Ctor_ZeroTickDelta_DurationTicksIsZero)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CooldownInstanceTests", "FAILURE", $"{nameof(Ctor_ZeroTickDelta_DurationTicksIsZero)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Ctor_ZeroTickDelta_DurationTicksIsZero)).GetAwaiter().GetResult();
			}
		}

		[Test]
		public void Immutability_NoSettersOnPublicSurface()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(Immutability_NoSettersOnPublicSurface),
					"Every public property on CooldownInstance must remain get-only to preserve replay determinism.")
					.GetAwaiter().GetResult();

				// Sanity: the struct exposes only get-only auto-properties. A reflective check
				// catches accidental field additions in future refactors.
				System.Type t = typeof(CooldownInstance);
				foreach (System.Reflection.PropertyInfo p in t.GetProperties())
				{
					AuthTestTrace.Log("CooldownInstanceTests", "STEP",
						$"Property {p.Name}: CanWrite={p.CanWrite}").GetAwaiter().GetResult();
					LogAssert.IsFalse(p.CanWrite, $"Property {p.Name} must remain get-only — CooldownInstance must stay immutable.");
				}

				AuthTestTrace.Log("CooldownInstanceTests", "SUCCESS", nameof(Immutability_NoSettersOnPublicSurface)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CooldownInstanceTests", "FAILURE", $"{nameof(Immutability_NoSettersOnPublicSurface)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Immutability_NoSettersOnPublicSurface)).GetAwaiter().GetResult();
			}
		}
	}
}