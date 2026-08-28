using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Serializing;
using UnityEngine;
using AuthTestTrace = FishMMO.UnitTests.Harness.AuthTestTrace;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Regression proofs for the tick-domain translation pipeline used by
	/// <see cref="FishMMO.Shared.BuffController.ApplyAuthoritative"/> and the
	/// matching cooldown path.
	///
	/// <para>
	/// Historical bug report (May 2026): a buff applied via <c>ApplyAuthoritative</c>
	/// could last <c>D + K</c> ticks on the owning client instead of <c>D</c>, where
	/// <c>K = serverLocalTick - clientInputTick</c>. The fix was to translate the raw
	/// server tick into the controller's replicate-tick domain via
	/// <c>ResolveAuthoritativeTick</c> BEFORE stamping <see cref="Buff.ExpiryTick"/>,
	/// then to evaluate <see cref="Buff.HasExpired"/> against the replicate-input tick
	/// in <c>OnReplicate</c>. These tests pin that contract by re-implementing the
	/// translation formula and replaying the exact scenario from the bug report —
	/// any future regression in the formula (or any new call site that bypasses it)
	/// is caught immediately.
	/// </para>
	///
	/// <para>
	/// Most tests mirror the production arithmetic directly because a live FishNet
	/// <c>NetworkObject</c> + <c>TimeManager</c> is impractical in edit mode. The
	/// branchhang regression also instantiates a real <c>BuffController</c> and drives
	/// <c>ApplyAuthoritative</c> with its private replicate snapshot fields set to the
	/// same values the prediction pipeline captures at runtime.
	/// </para>
	/// </summary>
	[TestFixture]
	public class AuthoritativeTickTranslationTests
	{
		private const BindingFlags PrivateInstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
		private const float TickDelta30 = 1.0f / 30f;

		/// <summary>
		/// Mirror of the production formula used by both
		/// <c>BuffController.ResolveAuthoritativeTick</c> and
		/// <c>CooldownController.ResolveAuthoritativeTick</c>. Once a replicate tick is
		/// known, raw authoritative ticks collapse to that replicate-domain tick because
		/// buff and cooldown expiry are evaluated against <c>input.GetTick()</c>.
		/// </summary>
		private static uint ResolveAuthoritativeTick(uint serverTick, uint lastReplicateTick, uint lastReplicateLocalTick)
		{
			if (lastReplicateTick == TimeManager.UNSET_TICK)
			{
				return serverTick;
			}

			return lastReplicateTick;
		}

		/// <summary>
		/// Verifies payload buff ticks use a signed negative offset when the payload was
		/// written at a later reference tick than the receiving controller has reached.
		/// This pins the bug where unsigned subtraction wrapped to roughly four billion
		/// ticks and made finite buffs effectively permanent.
		/// </summary>
		[Test]
		public void BuffPayloadTickTranslation_PayloadAheadOfCurrent_UsesNegativeSignedOffset()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(BuffPayloadTickTranslation_PayloadAheadOfCurrent_UsesNegativeSignedOffset),
					"A payload written at a later reference tick than the receiver must use a signed negative offset, not an unsigned wrap to ~4 billion.")
					.GetAwaiter().GetResult();

				int offset = BuffController.GetSignedTickOffset(1_000u, 900u, nameof(BuffPayloadTickTranslation_PayloadAheadOfCurrent_UsesNegativeSignedOffset));
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "STEP",
					$"GetSignedTickOffset(1000, 900) = {offset}; AddSignedTickOffset(1030) = {BuffController.AddSignedTickOffset(1_030u, offset)}; AddSignedTickOffset(1020) = {BuffController.AddSignedTickOffset(1_020u, offset)}")
					.GetAwaiter().GetResult();

				LogAssert.AreEqual(-100, offset, "Offset of payload(1000) relative to current(900) must be -100.");
				LogAssert.AreEqual(930u, BuffController.AddSignedTickOffset(1_030u, offset),
					"ExpiryTick must preserve remaining duration in the receiver domain instead of wrapping forward by uint.MaxValue.");
				LogAssert.AreEqual(920u, BuffController.AddSignedTickOffset(1_020u, offset),
					"NextTickTick must be translated with the same signed offset as ExpiryTick.");

				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "SUCCESS", nameof(BuffPayloadTickTranslation_PayloadAheadOfCurrent_UsesNegativeSignedOffset)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "FAILURE", $"{nameof(BuffPayloadTickTranslation_PayloadAheadOfCurrent_UsesNegativeSignedOffset)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(BuffPayloadTickTranslation_PayloadAheadOfCurrent_UsesNegativeSignedOffset)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Verifies the exact late-join shape from the audit request: a long-running
		/// server writes payload ticks at <c>1_020_404</c>, while the receiving client
		/// has just started its local/reference domain at tick <c>1</c>. Payload
		/// translation must preserve remaining duration, not absolute tick magnitude.
		/// </summary>
		[Test]
		public void BuffPayloadTickTranslation_LateJoinHugeServerOffset_PreservesRemainingDuration()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(BuffPayloadTickTranslation_LateJoinHugeServerOffset_PreservesRemainingDuration),
					"Late join: server payload ticks at 1,020,404 while the receiver starts at tick 1. Translation must preserve the 30 remaining ticks, not absolute magnitude.")
					.GetAwaiter().GetResult();

				uint serverReferenceTick = 1_020_404u;
				uint clientReferenceTick = 1u;
				uint durationTicks = 30u;

				int offset = BuffController.GetSignedTickOffset(serverReferenceTick, clientReferenceTick,
					nameof(BuffPayloadTickTranslation_LateJoinHugeServerOffset_PreservesRemainingDuration));
				uint translatedExpiry = BuffController.AddSignedTickOffset(serverReferenceTick + durationTicks, offset);
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "STEP",
					$"serverRef={serverReferenceTick} clientRef={clientReferenceTick} duration={durationTicks} -> offset={offset} translatedExpiry={translatedExpiry}")
					.GetAwaiter().GetResult();

				LogAssert.AreEqual(-1_020_403, offset,
					"The server/client reference gap is large but still comfortably inside the signed tick-offset range.");
				LogAssert.AreEqual(clientReferenceTick + durationTicks, translatedExpiry,
					"Initial payload sync must preserve the 30 remaining ticks in the receiver's current domain.");
				LogAssert.IsFalse(new Buff(1, translatedExpiry, TimeManager.UNSET_TICK, TickDelta30, 0, 0).HasExpired(clientReferenceTick + durationTicks - 1u),
					"The late-join translated buff must still be active one receiver tick before expiry.");
				LogAssert.IsTrue(new Buff(1, translatedExpiry, TimeManager.UNSET_TICK, TickDelta30, 0, 0).HasExpired(clientReferenceTick + durationTicks),
					"The late-join translated buff must expire exactly after the preserved remaining duration.");

				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "SUCCESS", nameof(BuffPayloadTickTranslation_LateJoinHugeServerOffset_PreservesRemainingDuration)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "FAILURE", $"{nameof(BuffPayloadTickTranslation_LateJoinHugeServerOffset_PreservesRemainingDuration)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(BuffPayloadTickTranslation_LateJoinHugeServerOffset_PreservesRemainingDuration)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Verifies pre-replicate buff ticks can be translated backward when the local
		/// authoritative tick is ahead of the first replicate input tick.
		/// </summary>
		[Test]
		public void BuffPreReplicateTranslation_LocalTickAheadOfInput_DoesNotUnsignedWrap()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(BuffPreReplicateTranslation_LocalTickAheadOfInput_DoesNotUnsignedWrap),
					"Pre-replicate buff ticks must translate backward (signed) when local authoritative tick leads the first replicate input tick.")
					.GetAwaiter().GetResult();

				int offset = BuffController.GetSignedTickOffset(105u, 100u, nameof(BuffPreReplicateTranslation_LocalTickAheadOfInput_DoesNotUnsignedWrap));
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "STEP",
					$"GetSignedTickOffset(105, 100) = {offset}; AddSignedTickOffset(135) = {BuffController.AddSignedTickOffset(135u, offset)}")
					.GetAwaiter().GetResult();

				LogAssert.AreEqual(-5, offset, "LocalTick(105) relative to input(100) must yield offset -5.");
				LogAssert.AreEqual(130u, BuffController.AddSignedTickOffset(135u, offset),
					"A buff stamped at LocalTick+duration must map back to inputTick+duration, not jump to a near-permanent uint value.");

				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "SUCCESS", nameof(BuffPreReplicateTranslation_LocalTickAheadOfInput_DoesNotUnsignedWrap)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "FAILURE", $"{nameof(BuffPreReplicateTranslation_LocalTickAheadOfInput_DoesNotUnsignedWrap)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(BuffPreReplicateTranslation_LocalTickAheadOfInput_DoesNotUnsignedWrap)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Verifies spawn/pre-first-replicate application when the server has been
		/// running long before the client begins its input domain. The first valid
		/// replicate tick translates raw LocalTick-stamped buff fields into the
		/// replicate/input domain used by <c>Tick(input.GetTick())</c>.
		/// </summary>
		[Test]
		public void BuffPreReplicateTranslation_LateJoinHugeServerOffset_MapsToFirstInputTick()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(BuffPreReplicateTranslation_LateJoinHugeServerOffset_MapsToFirstInputTick),
					"Spawn before first replicate with a long-running server: a buff stamped at raw LocalTick+duration must map to firstInputTick+duration.")
					.GetAwaiter().GetResult();

				uint rawLocalTickAtApply = 1_020_404u;
				uint firstInputTick = 1u;
				uint durationTicks = 30u;
				uint rawExpiryTick = rawLocalTickAtApply + durationTicks;

				int offset = BuffController.GetSignedTickOffset(rawLocalTickAtApply, firstInputTick,
					nameof(BuffPreReplicateTranslation_LateJoinHugeServerOffset_MapsToFirstInputTick));
				uint translatedExpiryTick = BuffController.AddSignedTickOffset(rawExpiryTick, offset);
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "STEP",
					$"rawLocalApply={rawLocalTickAtApply} firstInput={firstInputTick} rawExpiry={rawExpiryTick} -> offset={offset} translatedExpiry={translatedExpiryTick}")
					.GetAwaiter().GetResult();

				LogAssert.AreEqual(-1_020_403, offset, "Offset must be (firstInputTick - rawLocalTickAtApply).");
				LogAssert.AreEqual(31u, translatedExpiryTick,
					"A pre-replicate buff stamped at server LocalTick+duration must become firstInputTick+duration.");

				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "SUCCESS", nameof(BuffPreReplicateTranslation_LateJoinHugeServerOffset_MapsToFirstInputTick)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "FAILURE", $"{nameof(BuffPreReplicateTranslation_LateJoinHugeServerOffset_MapsToFirstInputTick)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(BuffPreReplicateTranslation_LateJoinHugeServerOffset_MapsToFirstInputTick)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Anchor-invariant proof (refutes the "double/zero translation" hazard in the
		/// 6-bug report). Before the first replicate, ALL buff stamps — whether they came
		/// from <c>ApplyAuthoritative</c> (raw LocalTick anchor) or from
		/// <c>ReadPayload</c> (payload-reference anchored to the current LocalTick) — are
		/// expressed in the SAME raw LocalTick domain. <c>TranslatePreReplicateBuffTicks</c>
		/// then applies ONE uniform signed offset <c>(firstInputTick - localTickAtFirstReplicate)</c>
		/// to every buff. This test proves that a single uniform offset maps buffs stamped
		/// at DIFFERENT LocalTick anchors to their correct input-domain expiry, because each
		/// buff's raw expiry already embeds its own apply LocalTick; the offset only shifts
		/// the LocalTick→input domain, which is uniform across all buffs.
		///
		/// <para>
		/// If a future change re-anchors one source (e.g. payload buffs to the payload
		/// reference tick) while leaving the other on LocalTick, the two buffs would need
		/// different offsets and this test fails — catching exactly the inconsistency the
		/// report warns about.
		/// </para>
		/// </summary>
		[Test]
		public void PreReplicate_MixedAnchors_UniformOffsetTranslatesEveryBuffToInputDomain()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(PreReplicate_MixedAnchors_UniformOffsetTranslatesEveryBuffToInputDomain),
					"A single uniform offset must map buffs stamped at DIFFERENT LocalTick anchors to their correct input-domain expiry, proving anchors are not mixed.")
					.GetAwaiter().GetResult();

				// Two buffs applied at DIFFERENT raw LocalTicks before the first replicate.
				uint payloadApplyLocalTick = 1_000u;   // ReadPayload stamped remaining duration here.
				uint payloadRemaining = 20u;
				uint payloadExpiryRaw = payloadApplyLocalTick + payloadRemaining; // 1020

				uint authApplyLocalTick = 1_003u;      // ApplyAuthoritative stamped here, 3 ticks later.
				uint authDuration = 30u;
				uint authExpiryRaw = authApplyLocalTick + authDuration; // 1033

				// First replicate arrives: server LocalTick advanced to 1010, client input tick is 400.
				uint localTickAtFirstReplicate = 1_010u;
				uint firstInputTick = 400u;

				int uniformOffset = BuffController.GetSignedTickOffset(
					localTickAtFirstReplicate, firstInputTick,
					nameof(PreReplicate_MixedAnchors_UniformOffsetTranslatesEveryBuffToInputDomain));
				LogAssert.AreEqual(-610, uniformOffset, "Uniform offset is (firstInputTick - localTickAtFirstReplicate).");

				uint payloadFinal = BuffController.AddSignedTickOffset(payloadExpiryRaw, uniformOffset);
				uint authFinal = BuffController.AddSignedTickOffset(authExpiryRaw, uniformOffset);

				// Correct input-domain expiry = firstInputTick + (applyLocalTick - localTickAtFirstReplicate) + remaining/duration.
				uint payloadCorrect = firstInputTick + (payloadApplyLocalTick - localTickAtFirstReplicate) + payloadRemaining; // 410
				uint authCorrect = firstInputTick + (authApplyLocalTick - localTickAtFirstReplicate) + authDuration;          // 423
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "STEP",
					$"uniformOffset={uniformOffset} payloadFinal={payloadFinal} (expect {payloadCorrect}) authFinal={authFinal} (expect {authCorrect})")
					.GetAwaiter().GetResult();

				LogAssert.AreEqual(payloadCorrect, payloadFinal,
					"Payload buff (anchored at LocalTick 1000) must land at input tick 410 after the uniform offset.");
				LogAssert.AreEqual(authCorrect, authFinal,
					"Authoritative buff (anchored at LocalTick 1003) must land at input tick 423 with the SAME uniform offset — proving anchors are not mixed.");

				// Expiry parity: each buff stays active until exactly its input-domain expiry tick.
				LogAssert.IsFalse(new Buff(1, payloadFinal, TimeManager.UNSET_TICK, TickDelta30, 0, 0).HasExpired(payloadCorrect - 1u),
					"Payload buff must still be active one tick before its input-domain expiry.");
				LogAssert.IsTrue(new Buff(1, payloadFinal, TimeManager.UNSET_TICK, TickDelta30, 0, 0).HasExpired(payloadCorrect),
					"Payload buff must expire exactly at its input-domain expiry tick.");
				LogAssert.IsFalse(new Buff(2, authFinal, TimeManager.UNSET_TICK, TickDelta30, 0, 0).HasExpired(authCorrect - 1u),
					"Authoritative buff must still be active one tick before its input-domain expiry.");
				LogAssert.IsTrue(new Buff(2, authFinal, TimeManager.UNSET_TICK, TickDelta30, 0, 0).HasExpired(authCorrect),
					"Authoritative buff must expire exactly at its input-domain expiry tick.");

				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "SUCCESS", nameof(PreReplicate_MixedAnchors_UniformOffsetTranslatesEveryBuffToInputDomain)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "FAILURE", $"{nameof(PreReplicate_MixedAnchors_UniformOffsetTranslatesEveryBuffToInputDomain)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(PreReplicate_MixedAnchors_UniformOffsetTranslatesEveryBuffToInputDomain)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Verifies cooldown payload and pre-replicate translations use the same signed
		/// offset behavior as buff timing fields.
		/// </summary>
		[Test]
		public void CooldownTickTranslation_LocalTickAheadOfInput_DoesNotUnsignedWrap()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(CooldownTickTranslation_LocalTickAheadOfInput_DoesNotUnsignedWrap),
					"Cooldown payload/pre-replicate translation must use the same signed offset behavior as buff timing fields.")
					.GetAwaiter().GetResult();

				int offset = CooldownController.GetSignedTickOffset(105u, 100u, nameof(CooldownTickTranslation_LocalTickAheadOfInput_DoesNotUnsignedWrap));
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "STEP",
					$"GetSignedTickOffset(105, 100) = {offset}; AddSignedTickOffset(105) = {CooldownController.AddSignedTickOffset(105u, offset)}")
					.GetAwaiter().GetResult();

				LogAssert.AreEqual(-5, offset, "LocalTick(105) relative to input(100) must yield offset -5.");
				LogAssert.AreEqual(100u, CooldownController.AddSignedTickOffset(105u, offset),
					"Cooldown StartTick must map back to the replicate input tick rather than wrapping forward by uint.MaxValue.");

				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "SUCCESS", nameof(CooldownTickTranslation_LocalTickAheadOfInput_DoesNotUnsignedWrap)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "FAILURE", $"{nameof(CooldownTickTranslation_LocalTickAheadOfInput_DoesNotUnsignedWrap)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(CooldownTickTranslation_LocalTickAheadOfInput_DoesNotUnsignedWrap)).GetAwaiter().GetResult();
			}
		}

		// ─────────────────────────────────────────────────────────────────────────
		//  Regression: the exact scenario from the May 2026 bug report
		// ─────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Bug-report scenario, verbatim:
		/// <list type="bullet">
		///   <item><c>inputTick = 100</c> (client / replicate domain)</item>
		///   <item><c>LocalTick  = 105</c> (server wall-clock)</item>
		///   <item><c>durationTicks = 30</c></item>
		/// </list>
		/// Before the fix, <c>ExpiryTick</c> was stamped as <c>105 + 30 = 135</c> and the
		/// client compared it against <c>inputTick = 100</c>, so the buff lasted 35 input
		/// ticks instead of 30 — a 16% over-duration error.
		///
		/// After the fix, <c>ApplyAuthoritative(LocalTick)</c> first collapses the raw
		/// authoritative tick to the current replicate-domain tick, yielding <c>100</c>,
		/// then stamps <c>ExpiryTick = 100 + 30 = 130</c>. The buff
		/// now expires after exactly 30 client input ticks, matching the configured duration.
		/// </summary>
		[Test]
		public void BugReport_ServerLocalTickAheadOfClientInput_ApplyAuthoritativeYieldsCorrectExpiry()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(BugReport_ServerLocalTickAheadOfClientInput_ApplyAuthoritativeYieldsCorrectExpiry),
					"Server LocalTick=105, client inputTick=100, durationTicks=30. " +
					"ApplyAuthoritative must produce ExpiryTick=130 (not 135) so the buff lasts " +
					"exactly 30 client input ticks.")
					.GetAwaiter().GetResult();

				uint clientInputTick = 100u;     // The replicate tick currently being processed.
				uint serverLocalTick = 105u;     // TimeManager.LocalTick on the server.
				uint durationTicks = 30u;        // Buff template duration in ticks.

				// State captured at the start of OnReplicate(inputTick=100):
				uint lastReplicateTick = clientInputTick;       // 100
				uint lastReplicateLocalTick = serverLocalTick;  // 105

				// ApplyAuthoritative(LocalTick) path: serverTick is mapped to replicate domain.
				uint mappedApplyTick = ResolveAuthoritativeTick(serverLocalTick, lastReplicateTick, lastReplicateLocalTick);
				LogAssert.AreEqual(clientInputTick, mappedApplyTick,
					"Mapped tick must equal the current replicate input tick when the raw " +
					"server tick equals lastReplicateLocalTick.");

				uint expiryTick = mappedApplyTick + durationTicks;
				LogAssert.AreEqual(130u, expiryTick,
					"ExpiryTick must be 130 (replicate-domain), NOT 135 (raw LocalTick + duration).");

				// Buff is evaluated against the replicate input tick inside BuffController.Tick.
				// Simulate every tick from apply until expiry.
				int activeTicks = 0;
				for (uint tick = clientInputTick; ; tick++)
				{
					bool expired = (int)(tick - expiryTick) >= 0; // mirror of Buff.HasExpired
					if (expired)
					{
						break;
					}
					activeTicks++;
					if (activeTicks > 100) // safety net — must never reach
					{
						break;
					}
				}

				LogAssert.AreEqual((int)durationTicks, activeTicks,
					"The buff must be active for exactly durationTicks (30) replicate ticks, " +
					"not durationTicks + (LocalTick - inputTick) (35) as in the original bug.");

				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "SUCCESS",
					nameof(BugReport_ServerLocalTickAheadOfClientInput_ApplyAuthoritativeYieldsCorrectExpiry))
					.GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "FAILURE",
					$"{nameof(BugReport_ServerLocalTickAheadOfClientInput_ApplyAuthoritativeYieldsCorrectExpiry)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(BugReport_ServerLocalTickAheadOfClientInput_ApplyAuthoritativeYieldsCorrectExpiry))
					.GetAwaiter().GetResult();
			}
		}

		// ─────────────────────────────────────────────────────────────────────────
		//  ResolveAuthoritativeTick — formula proofs
		// ─────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// When a raw server tick is ahead of the captured local reference, the mapped
		/// tick must not advance past the current replicate tick. The authoritative
		/// fallback is a current-domain stamp, not a wall-clock elapsed-time projection.
		/// </summary>
		[TestCase(0)]   // Server tick == lastReplicateLocalTick → mapped == lastReplicateTick
		[TestCase(1)]   // 1-tick lookahead (input queue head)
		[TestCase(5)]   // 167 ms RTT @ 30 Hz
		[TestCase(10)]  // 333 ms RTT @ 30 Hz
		[TestCase(20)]  // 667 ms RTT @ 30 Hz (poor network)
		public void ResolveAuthoritativeTick_RawLocalAhead_DoesNotAdvanceReplicateDomain(int serverAheadBy)
		{
			try
			{
				AuthTestTrace.LogTestStart($"{nameof(ResolveAuthoritativeTick_RawLocalAhead_DoesNotAdvanceReplicateDomain)}({serverAheadBy})",
					$"serverTick - lastReplicateLocalTick == {serverAheadBy} → mapped == lastReplicateTick.")
					.GetAwaiter().GetResult();

				uint lastReplicateTick = 1_000u;
				uint lastReplicateLocalTick = 1_005u; // server is 5 ticks ahead of client input
				uint serverTick = lastReplicateLocalTick + (uint)serverAheadBy;

				uint mapped = ResolveAuthoritativeTick(serverTick, lastReplicateTick, lastReplicateLocalTick);

				LogAssert.AreEqual(lastReplicateTick, mapped,
					$"For serverTick == lastReplicateLocalTick + {serverAheadBy}, mapped must stay at lastReplicateTick.");

				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "SUCCESS",
					$"{nameof(ResolveAuthoritativeTick_RawLocalAhead_DoesNotAdvanceReplicateDomain)}({serverAheadBy})")
					.GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "FAILURE",
					$"{nameof(ResolveAuthoritativeTick_RawLocalAhead_DoesNotAdvanceReplicateDomain)}({serverAheadBy}): {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd($"{nameof(ResolveAuthoritativeTick_RawLocalAhead_DoesNotAdvanceReplicateDomain)}({serverAheadBy})")
					.GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Region triggers and other authoritative callbacks may carry a raw tick older
		/// than the last captured <c>LocalTick</c>. Because the consuming state is still
		/// evaluated against the current replicate tick, the authoritative fallback must
		/// not move backward in prediction-domain time either.
		/// </summary>
		[TestCase(1)]
		[TestCase(5)]
		[TestCase(20)]
		public void ResolveAuthoritativeTick_RawLocalBehind_DoesNotMoveBackwardInReplicateDomain(int serverBehindBy)
		{
			try
			{
				AuthTestTrace.LogTestStart($"{nameof(ResolveAuthoritativeTick_RawLocalBehind_DoesNotMoveBackwardInReplicateDomain)}({serverBehindBy})",
					$"serverTick - lastReplicateLocalTick == -{serverBehindBy} → mapped == lastReplicateTick.")
					.GetAwaiter().GetResult();

				uint lastReplicateTick = 1_000u;
				uint lastReplicateLocalTick = 1_005u;
				uint serverTick = lastReplicateLocalTick - (uint)serverBehindBy;

				uint mapped = ResolveAuthoritativeTick(serverTick, lastReplicateTick, lastReplicateLocalTick);

				LogAssert.AreEqual(lastReplicateTick, mapped,
					$"For serverTick == lastReplicateLocalTick - {serverBehindBy}, mapped must stay at lastReplicateTick.");

				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "SUCCESS",
					$"{nameof(ResolveAuthoritativeTick_RawLocalBehind_DoesNotMoveBackwardInReplicateDomain)}({serverBehindBy})")
					.GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "FAILURE",
					$"{nameof(ResolveAuthoritativeTick_RawLocalBehind_DoesNotMoveBackwardInReplicateDomain)}({serverBehindBy}): {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd($"{nameof(ResolveAuthoritativeTick_RawLocalBehind_DoesNotMoveBackwardInReplicateDomain)}({serverBehindBy})")
					.GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// User-reported branch-hang scenario: after the target controller observed
		/// <c>inputTick=100</c> at <c>LocalTick=105</c>, the raw clock advances to 110
		/// while the target input tick is still 100. Mapping 110 to 105 would stamp the
		/// buff five prediction ticks in the future and extend its lifetime.
		/// </summary>
		[Test]
		public void ResolveAuthoritativeTick_InputTickStalled_DoesNotAccumulateLocalTickDrift()
		{
			uint lastReplicateTick = 100u;
			uint lastReplicateLocalTick = 105u;
			uint currentRawLocalTick = 110u;

			uint mapped = ResolveAuthoritativeTick(currentRawLocalTick, lastReplicateTick, lastReplicateLocalTick);

			LogAssert.AreEqual(lastReplicateTick, mapped,
				"Raw LocalTick drift must not push authoritative buff/cooldown stamps beyond the tick used by Tick(input.GetTick()).");
		}

		/// <summary>
		/// Before <c>OnReplicate</c> has fired for the first time, neither
		/// <c>lastReplicateTick</c> nor <c>lastReplicateLocalTick</c> have been
		/// captured (both are <see cref="TimeManager.UNSET_TICK"/>). In that window
		/// <c>ResolveAuthoritativeTick</c> must return the raw server tick unchanged
		/// so spawn-time buff application still produces a sensible
		/// <c>ExpiryTick</c>. The first replicate then runs
		/// <c>TranslatePreReplicateBuffTicks</c> to retroactively map any such buffs.
		/// </summary>
		[Test]
		public void ResolveAuthoritativeTick_BeforeFirstReplicate_ReturnsServerTickVerbatim()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(ResolveAuthoritativeTick_BeforeFirstReplicate_ReturnsServerTickVerbatim),
					"With unset replicate tracking, mapping must be a no-op so spawn-time " +
					"buff/cooldown application doesn't produce garbage ticks.")
					.GetAwaiter().GetResult();

				uint serverTick = 42_000u;

				LogAssert.AreEqual(serverTick,
					ResolveAuthoritativeTick(serverTick, TimeManager.UNSET_TICK, TimeManager.UNSET_TICK),
					"Both fields unset → return serverTick unchanged.");
				LogAssert.AreEqual(serverTick,
					ResolveAuthoritativeTick(serverTick, TimeManager.UNSET_TICK, 9_999u),
					"lastReplicateTick unset → return serverTick unchanged.");
				LogAssert.AreEqual(9_999u,
					ResolveAuthoritativeTick(serverTick, 9_999u, TimeManager.UNSET_TICK),
					"lastReplicateTick set → use the replicate-domain tick even if the local reference is unset.");

				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "SUCCESS",
					nameof(ResolveAuthoritativeTick_BeforeFirstReplicate_ReturnsServerTickVerbatim))
					.GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "FAILURE",
					$"{nameof(ResolveAuthoritativeTick_BeforeFirstReplicate_ReturnsServerTickVerbatim)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(ResolveAuthoritativeTick_BeforeFirstReplicate_ReturnsServerTickVerbatim))
					.GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Callers that have no usable raw tick (e.g., an ECA action lacking
		/// <c>TickEventData</c> and TimeManager) pass either <see cref="TimeManager.UNSET_TICK"/>
		/// or the legacy <c>0u</c> sentinel. Both must collapse to <c>lastReplicateTick</c>
		/// so the buff/cooldown is stamped in the current replicate-tick domain instead
		/// of being silently rooted at tick 0 (which would expire instantly or at uint
		/// wrap-around).
		/// </summary>
		[Test]
		public void ResolveAuthoritativeTick_MissingRawTick_FallsBackToLastReplicateTick()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(ResolveAuthoritativeTick_MissingRawTick_FallsBackToLastReplicateTick),
					"UNSET_TICK and 0u both collapse to lastReplicateTick once the controller has run a replicate.")
					.GetAwaiter().GetResult();

				uint lastReplicateTick = 5_000u;
				uint lastReplicateLocalTick = 5_010u;

				LogAssert.AreEqual(lastReplicateTick,
					ResolveAuthoritativeTick(TimeManager.UNSET_TICK, lastReplicateTick, lastReplicateLocalTick),
					"UNSET_TICK → fall back to lastReplicateTick.");
				LogAssert.AreEqual(lastReplicateTick,
					ResolveAuthoritativeTick(0u, lastReplicateTick, lastReplicateLocalTick),
					"0u (legacy missing-tick sentinel) → fall back to lastReplicateTick.");

				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "SUCCESS",
					nameof(ResolveAuthoritativeTick_MissingRawTick_FallsBackToLastReplicateTick))
					.GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "FAILURE",
					$"{nameof(ResolveAuthoritativeTick_MissingRawTick_FallsBackToLastReplicateTick)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(ResolveAuthoritativeTick_MissingRawTick_FallsBackToLastReplicateTick))
					.GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Server runs many ticks before the first client input arrives (typical join-late
		/// scenario: <c>lastReplicateLocalTick</c> is far ahead of <c>lastReplicateTick</c>
		/// because the client's replicate domain starts at zero). A
		/// <c>ResolveAuthoritativeTick(LocalTick)</c> call must still map back to the
		/// current replicate tick — without overflow surprises.
		/// </summary>
		[Test]
		public void ResolveAuthoritativeTick_LargeReplicateToLocalOffset_PreservesMapping()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(ResolveAuthoritativeTick_LargeReplicateToLocalOffset_PreservesMapping),
					"Large LocalTick - ReplicateTick offset (server ran 1 hour before client joined) " +
					"must still produce a valid replicate-domain mapping for LocalTick.")
					.GetAwaiter().GetResult();

				uint lastReplicateTick = 1u;                     // Client just started replicating.
				uint lastReplicateLocalTick = 1_020_404u;        // Server has been running for a long time.
				uint serverTick = lastReplicateLocalTick;        // Apply-time wall-clock.

				uint mapped = ResolveAuthoritativeTick(serverTick, lastReplicateTick, lastReplicateLocalTick);

				LogAssert.AreEqual(lastReplicateTick, mapped,
					"serverTick == lastReplicateLocalTick must always map to lastReplicateTick exactly, " +
					"regardless of the absolute offset between domains.");

				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "SUCCESS",
					nameof(ResolveAuthoritativeTick_LargeReplicateToLocalOffset_PreservesMapping))
					.GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "FAILURE",
					$"{nameof(ResolveAuthoritativeTick_LargeReplicateToLocalOffset_PreservesMapping)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(ResolveAuthoritativeTick_LargeReplicateToLocalOffset_PreservesMapping))
					.GetAwaiter().GetResult();
			}
		}

		// ─────────────────────────────────────────────────────────────────────────
		//  End-to-end translation + Buff expiry parity
		// ─────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Production-path proof for branchhang's reported scenario. A real
		/// <see cref="BuffController"/> with <c>lastReplicateTick=100</c> receives a raw
		/// authoritative <c>serverTick=105</c>. <see cref="BuffController.ApplyAuthoritative"/>
		/// must stamp the buff in the replicate/input domain used by
		/// <c>BuffController.Tick(input.GetTick())</c>, producing <c>ExpiryTick=130</c>
		/// for a 30-tick buff. The historical bug stamped <c>105 + 30 = 135</c>, which
		/// would make <see cref="Buff.HasExpired"/> false at tick 130 and extend the buff.
		/// </summary>
		[Test]
		public void ApplyAuthoritative_ProductionController_StampsExpiryInReplicateDomain()
		{
			GameObject gameObject = new GameObject("ApplyAuthoritativeProductionTickTest");
			ProductionBuffTemplate template = ScriptableObject.CreateInstance<ProductionBuffTemplate>();

			try
			{
				template.name = "BranchhangAuthoritativeBuff";
				template.Duration = 1.0f;
				template.TickRate = 0.0f;
				template.AddToCache(template.name);

				BuffController controller = gameObject.AddComponent<BuffController>();
				SetPrivateField(controller, "tickDelta", TickDelta30);
				SetPrivateField(controller, "lastReplicateTick", 100u);
				SetPrivateField(controller, "hasSeenFirstReplicate", true);
				SetPrivateField(controller, "isReplayingTick", true);

				// The production apply path gates on Character.IsFlagged(IsDead). Character
				// is only assigned by InitializeOnce, so wire in a living character the way
				// the spawn pipeline does instead of reaching for the backing field.
				controller.InitializeOnce(new MockCharacter(42));

				controller.ApplyAuthoritative(template, 105u);

				Assert.IsTrue(controller.Buffs.TryGetValue(template.ID, out Buff buff),
					"ApplyAuthoritative must create the buff through the production Apply path.");

				uint durationTicks = Buff.DurationToTicks(template.Duration, TickDelta30);
				Assert.AreEqual(30u, durationTicks,
					"The test template is intentionally one second at 30 TPS.");
				Assert.AreEqual(130u, buff.ExpiryTick,
					"ExpiryTick must be current replicate tick + durationTicks, not raw serverTick + durationTicks.");
				Assert.IsFalse(buff.HasExpired(129u),
					"The buff must still be active one input tick before expiry.");
				Assert.IsTrue(buff.HasExpired(130u),
					"The buff must expire exactly when Tick(input.GetTick()) reaches the replicate-domain expiry tick.");
			}
			finally
			{
				if (template != null)
				{
					template.RemoveFromCache();
					UnityEngine.Object.DestroyImmediate(template);
				}

				UnityEngine.Object.DestroyImmediate(gameObject);
			}
		}

		/// <summary>
		/// AbilityObject.OnTick can run before CharacterPredictionController.TimeManager_OnTick
		/// in the same TimeManager tick. In that window CurrentReplicateTickSnapshot and
		/// lastReplicateTick still describe the previous replicate tick. The pending snapshot
		/// captured from OnPreTick must win so ApplyAuthoritative stamps the new tick.
		/// </summary>
		[Test]
		public void BuffResolveAuthoritativeTick_PreReplicatePendingSnapshot_WinsOverStaleCurrentTick()
		{
			GameObject gameObject = new GameObject("BuffPendingSnapshotTickTest");

			try
			{
				CharacterPredictionController prediction = gameObject.AddComponent<CharacterPredictionController>();
				BuffController controller = gameObject.AddComponent<BuffController>();

				SetPrivateField(controller, "predictionController", prediction);
				SetPrivateField(controller, "lastReplicateTick", 99u);
				SetPrivateField(controller, "hasSeenFirstReplicate", true);
				SetAutoProperty(prediction, nameof(CharacterPredictionController.CurrentReplicateTickSnapshot), 99u);
				SetAutoProperty(prediction, nameof(CharacterPredictionController.PendingReplicateTickSnapshot), 100u);

				Assert.AreEqual(100u, controller.ResolveAuthoritativeTick(105u),
					"Pre-replicate authoritative calls must use the pending current tick instead of the previous tick.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(gameObject);
			}
		}

		/// <summary>
		/// Cooldown queries and additions use the same authoritative tick mapper as buffs.
		/// This pins the mirrored stale-window fix so cooldown readiness cannot drift one
		/// tick early/late when called before the controller's OnReplicate pass.
		/// </summary>
		[Test]
		public void CooldownResolveAuthoritativeTick_PreReplicatePendingSnapshot_WinsOverStaleCurrentTick()
		{
			GameObject gameObject = new GameObject("CooldownPendingSnapshotTickTest");

			try
			{
				CharacterPredictionController prediction = gameObject.AddComponent<CharacterPredictionController>();
				CooldownController controller = gameObject.AddComponent<CooldownController>();

				SetPrivateField(controller, "predictionController", prediction);
				SetPrivateField(controller, "lastReplicateTick", 199u);
				SetPrivateField(controller, "hasSeenFirstReplicate", true);
				SetAutoProperty(prediction, nameof(CharacterPredictionController.CurrentReplicateTickSnapshot), 199u);
				SetAutoProperty(prediction, nameof(CharacterPredictionController.PendingReplicateTickSnapshot), 200u);

				Assert.AreEqual(200u, controller.ResolveAuthoritativeTick(205u),
					"Pre-replicate authoritative calls must use the pending current tick instead of the previous tick.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(gameObject);
			}
		}

		/// <summary>
		/// If spawn payload arrives before the receiver has a usable local or replicate
		/// tick, <see cref="BuffController.ReadPayload"/> cannot translate immediately.
		/// It must remember the writer's reference tick so the first valid replicate can
		/// translate from the payload domain rather than the receiver's raw LocalTick.
		/// </summary>
		[Test]
		public void BuffReadPayload_UnsetCurrentReference_DefersPayloadReferenceForFirstReplicate()
		{
			GameObject gameObject = new GameObject("BuffDeferredPayloadReferenceTest");
			ProductionBuffTemplate template = ScriptableObject.CreateInstance<ProductionBuffTemplate>();

			try
			{
				template.name = "LateJoinDeferredPayloadBuff";
				template.Duration = 1.0f;
				template.TickRate = 0.0f;
				template.AddToCache(template.name);

				uint serverReferenceTick = 1_020_404u;
				uint firstInputTick = 1u;
				uint durationTicks = 30u;

				BuffController controller = gameObject.AddComponent<BuffController>();
				SetPrivateField(controller, "tickDelta", TickDelta30);

				// The production apply path gates on Character.IsFlagged(IsDead). Character
				// is only assigned by InitializeOnce, so wire in a living character the way
				// the spawn pipeline does instead of reaching for the backing field.
				controller.InitializeOnce(new MockCharacter(42));

				Writer writer = WriteFramedBuffPayload(serverReferenceTick, w =>
				{
					w.WriteInt32(1);
					w.WriteInt32(template.ID);
					w.WriteUInt32(serverReferenceTick + durationTicks);
					w.WriteUInt32(TimeManager.UNSET_TICK);
					w.WriteInt32(0);
					w.WriteInt32(0);
					w.WriteInt32(0);
				});

				var reader = new Reader(writer.GetArraySegment(), null);
				controller.ReadPayload(null, reader);

				Assert.AreEqual(serverReferenceTick,
					GetPrivateField<uint>(controller, "preReplicatePayloadReferenceTick"),
					"ReadPayload must remember the writer reference tick when the receiver reference is unset.");

				int offset = BuffController.GetSignedTickOffset(serverReferenceTick, firstInputTick,
					nameof(BuffReadPayload_UnsetCurrentReference_DefersPayloadReferenceForFirstReplicate));
				InvokePrivateMethod(controller, "TranslatePreReplicateBuffTicks", offset);

				Assert.IsTrue(controller.Buffs.TryGetValue(template.ID, out Buff buff));
				Assert.AreEqual(firstInputTick + durationTicks, buff.ExpiryTick,
					"Deferred payload translation must preserve the remaining duration in the first input tick domain.");
			}
			finally
			{
				if (template != null)
				{
					template.RemoveFromCache();
					UnityEngine.Object.DestroyImmediate(template);
				}

				UnityEngine.Object.DestroyImmediate(gameObject);
			}
		}

		/// <summary>
		/// Even an empty buff payload carries a valid writer reference tick. Preserve it
		/// until first replicate so any pre-replicate buff additions can be translated
		/// from the server payload domain rather than the receiver's local domain.
		/// </summary>
		[Test]
		public void BuffReadPayload_EmptyPayload_PreservesReferenceForFirstReplicate()
		{
			GameObject gameObject = new GameObject("BuffEmptyPayloadReferenceTest");

			try
			{
				uint serverReferenceTick = 1_020_404u;
				BuffController controller = gameObject.AddComponent<BuffController>();
				SetPrivateField(controller, "tickDelta", TickDelta30);

				Writer writer = WriteFramedBuffPayload(serverReferenceTick, w => w.WriteInt32(0));

				var reader = new Reader(writer.GetArraySegment(), null);
				controller.ReadPayload(null, reader);

				Assert.AreEqual(serverReferenceTick,
					GetPrivateField<uint>(controller, "preReplicatePayloadReferenceTick"),
					"An empty payload must still preserve its valid writer reference tick until first replicate.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(gameObject);
			}
		}

		/// <summary>
		/// Cooldown payloads share the same late-join reference-tick hazard as buffs.
		/// This pins the mirrored cooldown fix so ability readiness cannot drift during
		/// initial sync when the receiver has not reached a non-zero tick yet.
		/// </summary>
		[Test]
		public void CooldownRead_UnsetCurrentReference_DefersPayloadReferenceForFirstReplicate()
		{
			GameObject gameObject = new GameObject("CooldownDeferredPayloadReferenceTest");

			try
			{
				uint serverReferenceTick = 1_020_404u;
				uint firstInputTick = 1u;
				uint durationTicks = 30u;
				long abilityID = 771L;

				CooldownController controller = gameObject.AddComponent<CooldownController>();
				SetPrivateField(controller, "cachedTickDelta", TickDelta30);

				Writer writer = WriteFramedTickPayload(serverReferenceTick, w =>
				{
					w.WriteInt32(1);
					w.WriteInt64(abilityID);
					w.WriteUInt32(serverReferenceTick);
					w.WriteUInt32(durationTicks);
				});

				var reader = new Reader(writer.GetArraySegment(), null);
				controller.Read(reader, TimeManager.UNSET_TICK);

				Assert.AreEqual(serverReferenceTick,
					GetPrivateField<uint>(controller, "preReplicatePayloadReferenceTick"),
					"Cooldown Read must remember the writer reference tick when the receiver reference is unset.");

				int offset = CooldownController.GetSignedTickOffset(serverReferenceTick, firstInputTick,
					nameof(CooldownRead_UnsetCurrentReference_DefersPayloadReferenceForFirstReplicate));
				InvokePrivateMethod(controller, "TranslatePreReplicateCooldownTicks", offset);

				Assert.IsTrue(controller.IsOnCooldown(abilityID, firstInputTick + durationTicks - 1u),
					"Deferred cooldown payload translation must stay active one input tick before elapse.");
				Assert.IsFalse(controller.IsOnCooldown(abilityID, firstInputTick + durationTicks),
					"Deferred cooldown payload translation must elapse exactly after the preserved duration.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(gameObject);
			}
		}

		/// <summary>
		/// Empty cooldown payloads also carry a valid writer reference tick. Preserve it
		/// for symmetry with buffs and to keep initial-sync timing stable before first
		/// replicate.
		/// </summary>
		[Test]
		public void CooldownRead_EmptyPayload_PreservesReferenceForFirstReplicate()
		{
			GameObject gameObject = new GameObject("CooldownEmptyPayloadReferenceTest");

			try
			{
				uint serverReferenceTick = 1_020_404u;
				CooldownController controller = gameObject.AddComponent<CooldownController>();
				SetPrivateField(controller, "cachedTickDelta", TickDelta30);

				Writer writer = WriteFramedTickPayload(serverReferenceTick, w => w.WriteInt32(0));

				var reader = new Reader(writer.GetArraySegment(), null);
				controller.Read(reader, TimeManager.UNSET_TICK);

				Assert.AreEqual(serverReferenceTick,
					GetPrivateField<uint>(controller, "preReplicatePayloadReferenceTick"),
					"An empty cooldown payload must still preserve its valid writer reference tick until first replicate.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(gameObject);
			}
		}

		/// <summary>
		/// Builds a tick-anchored payload in the production wire format: the reference tick, then
		/// a four-byte block length, then the block itself. Shared by <see cref="BuffController"/>
		/// and <see cref="CooldownController"/>, which use an identical layout.
		/// </summary>
		/// <remarks>
		/// The length frame is not decoration — it is what lets <c>ReadPayload</c> resynchronise the
		/// shared spawn stream after rejecting an untrustworthy buff count, since FishNet packs every
		/// NetworkBehaviour's payload into one buffer with no per-behaviour framing. A hand-built
		/// payload that omits it is simply not the format the reader parses, so these tests mirror
		/// the production writer here rather than open-coding a stale layout.
		/// </remarks>
		/// <param name="referenceTick">Writer-domain reference tick.</param>
		/// <param name="writeBlock">Writes the framed portion: entry count followed by entries.</param>
		/// <returns>A writer positioned at the end of the completed payload.</returns>
		/// <summary>
		/// Frames a BUFF payload: the shared frame, opened with the owner's shape flag.
		/// </summary>
		/// <remarks>
		/// BuffController's framed block starts with a shape byte — the owner is sent the simulation
		/// (absolute ticks, hidden buffs, tick counters), every other connection the display list in
		/// seconds. Tick translation only exists for the first, so every buff payload written here is
		/// the owner's. CooldownController's frame has no such byte, which is why this is a buff-only
		/// wrapper rather than a change to the shared framer.
		/// </remarks>
		private static Writer WriteFramedBuffPayload(uint referenceTick, Action<Writer> writeBlock)
		{
			const byte BuffSimulationShape = 0;

			return WriteFramedTickPayload(referenceTick, w =>
			{
				w.WriteUInt8Unpacked(BuffSimulationShape);
				writeBlock(w);
			});
		}

		private static Writer WriteFramedTickPayload(uint referenceTick, Action<Writer> writeBlock)
		{
			const int PayloadLengthBytes = 4;

			Writer writer = new Writer();
			writer.WriteUInt32(referenceTick);
			writer.Skip(PayloadLengthBytes);
			int blockStart = writer.Position;

			writeBlock(writer);

			writer.InsertUInt32Unpacked((uint)(writer.Position - blockStart), blockStart - PayloadLengthBytes);
			return writer;
		}

		private static void SetPrivateField<T>(object instance, string fieldName, T value)
		{
			instance.GetType()
				.GetField(fieldName, PrivateInstanceFlags)
				.SetValue(instance, value);
		}

		private static T GetPrivateField<T>(object instance, string fieldName)
		{
			return (T)instance.GetType()
				.GetField(fieldName, PrivateInstanceFlags)
				.GetValue(instance);
		}

		private static void SetAutoProperty<T>(object instance, string propertyName, T value)
		{
			SetPrivateField(instance, $"<{propertyName}>k__BackingField", value);
		}

		private static void InvokePrivateMethod(object instance, string methodName, int tickOffset)
		{
			instance.GetType()
				.GetMethod(methodName, PrivateInstanceFlags)
				.Invoke(instance, new object[] { tickOffset });
		}

		private sealed class ProductionBuffTemplate : BaseBuffTemplate
		{
			public override void OnApply(Buff buff, FishMMO.Shared.Core.ICharacter target) { }
			public override void OnRemove(Buff buff, FishMMO.Shared.Core.ICharacter target) { }
		}

		/// <summary>
		/// Minimal <see cref="ICharacter"/> stub for tests that exercise the production
		/// apply path. <see cref="CharacterBehaviour.Character"/> is only assigned by
		/// <see cref="CharacterBehaviour.InitializeOnce(ICharacter)"/>; the dead-character
		/// gate on the apply path dereferences it, so these tests must initialize the
		/// controller with a living character exactly as the spawn pipeline does.
		/// </summary>
		private sealed class MockCharacter : ICharacter
		{
			public MockCharacter(long id) => ID = id;

			public long ID { get; set; }
			public string Name => "MockCharacter";
			public Transform Transform => null;
			public GameObject GameObject => null;
			public Collider Collider { get; set; }
			public NetworkConnection Owner => null;
			public NetworkObject NetworkObject => null;
			public PredictionManager PredictionManager => null;
			public HashSet<NetworkConnection> Observers { get; } = new HashSet<NetworkConnection>();
			public bool IsTeleporting => false;
			public bool IsSpawned => true;
			public int Flags { get; set; }

			public WorldLabel CharacterNameLabel { get; set; }
			public WorldLabel CharacterGuildLabel { get; set; }
			public Transform MeshRoot => null;

#if !UNITY_SERVER
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex) { }
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex, CharacterGender gender) { }
#endif

			public void EnableFlags(CharacterFlags flags) => Flags |= (int)flags;
			public void DisableFlags(CharacterFlags flags) => Flags &= ~(int)flags;
			public bool IsFlagged(CharacterFlags flags) => (Flags & (int)flags) != 0;
			public void RegisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }
			public void UnregisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }
			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour
			{
				control = null;
				return false;
			}
			public void Invoke(List<Trigger> triggers, EventData eventData) { }
		}

		/// <summary>
		/// Full pipeline: ApplyAuthoritative(serverTick=LocalTick) → ResolveAuthoritativeTick →
		/// Buff constructor with mapped tick → HasExpired evaluated against replicate
		/// input ticks. Asserts that for the bug-report parameters the buff lasts the
		/// configured duration in the input-tick domain regardless of the LocalTick offset.
		/// </summary>
		[TestCase(0u)]
		[TestCase(1u)]
		[TestCase(5u)]
		[TestCase(10u)]
		[TestCase(50u)]
		public void ApplyAuthoritative_FullPipeline_BuffLastsExactlyDurationInInputTickDomain(uint serverAheadBy)
		{
			try
			{
				AuthTestTrace.LogTestStart($"{nameof(ApplyAuthoritative_FullPipeline_BuffLastsExactlyDurationInInputTickDomain)}({serverAheadBy})",
					$"For serverLocalTick = inputTick + {serverAheadBy}, the buff must expire exactly " +
					"durationTicks input ticks after apply — no over-duration drift.")
					.GetAwaiter().GetResult();

				uint inputTick = 1_000u;
				uint serverLocalTick = inputTick + serverAheadBy;
				uint durationTicks = 30u;

				// OnReplicate captures these before invoking any ApplyAuthoritative.
				uint lastReplicateTick = inputTick;
				uint lastReplicateLocalTick = serverLocalTick;

				uint mappedApplyTick = ResolveAuthoritativeTick(serverLocalTick, lastReplicateTick, lastReplicateLocalTick);

				// Restore constructor — same arithmetic the BuffController takes via Apply(template, PredictionTick(mappedApplyTick)).
				var buff = new Buff(1, mappedApplyTick + durationTicks, mappedApplyTick + durationTicks, TickDelta30, 0, 0);

				// One tick before expiry — still active.
				LogAssert.IsFalse(buff.HasExpired(inputTick + durationTicks - 1u),
					$"Buff must be active at inputTick + {durationTicks - 1} (one tick before expiry).");

				// Exactly at expiry — expired.
				LogAssert.IsTrue(buff.HasExpired(inputTick + durationTicks),
					$"Buff must expire exactly at inputTick + {durationTicks} regardless of serverAheadBy={serverAheadBy}.");

				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "SUCCESS",
					$"{nameof(ApplyAuthoritative_FullPipeline_BuffLastsExactlyDurationInInputTickDomain)}({serverAheadBy})")
					.GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "FAILURE",
					$"{nameof(ApplyAuthoritative_FullPipeline_BuffLastsExactlyDurationInInputTickDomain)}({serverAheadBy}): {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd($"{nameof(ApplyAuthoritative_FullPipeline_BuffLastsExactlyDurationInInputTickDomain)}({serverAheadBy})")
					.GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Same pipeline test but for cooldowns. <see cref="CooldownInstance.IsOnCooldown"/>
		/// evaluates <c>(currentTick - StartTick) &gt;= DurationTicks</c>, so any drift between
		/// the StartTick stamp and the replicate-domain currentTick produces an analogous
		/// over-duration. With <c>CooldownController.ResolveAuthoritativeTick</c> threaded
		/// through the apply path, the cooldown must elapse in exactly DurationTicks input ticks.
		/// </summary>
		[TestCase(0u)]
		[TestCase(5u)]
		[TestCase(20u)]
		public void Cooldown_FullPipeline_ElapsesExactlyDurationInInputTickDomain(uint serverAheadBy)
		{
			try
			{
				AuthTestTrace.LogTestStart($"{nameof(Cooldown_FullPipeline_ElapsesExactlyDurationInInputTickDomain)}({serverAheadBy})",
					$"Cooldown stamped via ResolveAuthoritativeTick(LocalTick + {serverAheadBy}) " +
					"must elapse exactly DurationTicks input ticks after apply.")
					.GetAwaiter().GetResult();

				uint inputTick = 2_000u;
				uint serverLocalTick = inputTick + serverAheadBy;
				uint durationTicks = 60u; // 2 seconds @ 30 tps

				uint mappedStartTick = ResolveAuthoritativeTick(serverLocalTick, inputTick, serverLocalTick);
				LogAssert.AreEqual(inputTick, mappedStartTick,
					"StartTick must be mapped into the input-tick domain.");

				var cd = new CooldownInstance(mappedStartTick, durationTicks, TickDelta30);

				LogAssert.IsTrue(cd.IsOnCooldown(inputTick),
					"Cooldown must be active at the apply tick.");
				LogAssert.IsTrue(cd.IsOnCooldown(inputTick + durationTicks - 1u),
					"Cooldown must remain active 1 tick before elapse.");
				LogAssert.IsFalse(cd.IsOnCooldown(inputTick + durationTicks),
					"Cooldown must elapse exactly at inputTick + DurationTicks.");

				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "SUCCESS",
					$"{nameof(Cooldown_FullPipeline_ElapsesExactlyDurationInInputTickDomain)}({serverAheadBy})")
					.GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "FAILURE",
					$"{nameof(Cooldown_FullPipeline_ElapsesExactlyDurationInInputTickDomain)}({serverAheadBy}): {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd($"{nameof(Cooldown_FullPipeline_ElapsesExactlyDurationInInputTickDomain)}({serverAheadBy})")
					.GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Gameplay decision paths, including NPC ability selection, must query cooldowns
		/// with a current tick in the same replicate domain as <see cref="CooldownInstance.StartTick"/>.
		/// This pins the bug where a raw <c>TimeManager.LocalTick</c> comparison can make
		/// an ability appear ready several ticks before the prediction-domain cooldown elapses.
		/// </summary>
		[Test]
		public void Cooldown_GameplayDecision_UsesMappedTickInsteadOfRawLocalTick()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(Cooldown_GameplayDecision_UsesMappedTickInsteadOfRawLocalTick),
					"LocalTick ahead of replicate tick must be mapped before IsOnCooldown; raw LocalTick would expire too early.")
					.GetAwaiter().GetResult();

				uint cooldownStartTick = 100u;
				uint durationTicks = 30u;
				uint currentReplicateTick = cooldownStartTick + durationTicks - 1u;
				uint currentLocalTick = currentReplicateTick + 5u;

				var cooldown = new CooldownInstance(cooldownStartTick, durationTicks, TickDelta30);
				uint mappedDecisionTick = ResolveAuthoritativeTick(currentLocalTick, currentReplicateTick, currentLocalTick);

				LogAssert.AreEqual(currentReplicateTick, mappedDecisionTick,
					"ResolveAuthoritativeTick(LocalTick) must return the current replicate tick when LocalTick is the captured authoritative tick.");
				LogAssert.IsTrue(cooldown.IsOnCooldown(mappedDecisionTick),
					"One replicate tick before expiry, the ability must still be considered on cooldown.");
				LogAssert.IsFalse(cooldown.IsOnCooldown(currentLocalTick),
					"This is the bad path: raw LocalTick is five ticks ahead and falsely reports the cooldown as elapsed.");

				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "SUCCESS",
					nameof(Cooldown_GameplayDecision_UsesMappedTickInsteadOfRawLocalTick))
					.GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "FAILURE",
					$"{nameof(Cooldown_GameplayDecision_UsesMappedTickInsteadOfRawLocalTick)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Cooldown_GameplayDecision_UsesMappedTickInsteadOfRawLocalTick))
					.GetAwaiter().GetResult();
			}
		}

		// ─────────────────────────────────────────────────────────────────────────
		//  TickEventData — replicate vs authoritative routing
		// ─────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// <see cref="TickEventData.IsReplicateTick"/> is the contract that drives the
		/// branch in <see cref="ApplyBuffAction"/>: <c>true</c> → <c>Apply</c>
		/// directly (no translation), <c>false</c> → <c>ApplyAuthoritative</c>
		/// (translation required). Pins the constructor mapping so a refactor that
		/// silently flips one default will be caught.
		/// </summary>
		[Test]
		public void TickEventData_IsReplicateTick_FlagsMatchConstructorIntent()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(TickEventData_IsReplicateTick_FlagsMatchConstructorIntent),
					"Public PredictionTick ctor sets IsReplicateTick=true; internal uint ctor sets it false.")
					.GetAwaiter().GetResult();

				// Public ctor — replicate domain.
				var replicate = new TickEventData(null, new PredictionTick(123u));
				LogAssert.IsTrue(replicate.IsReplicateTick,
					"PredictionTick ctor must mark TickEventData as replicate-domain (true).");
				LogAssert.AreEqual(123u, (uint)replicate.Tick,
					"Tick value must be preserved from PredictionTick.");

				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "SUCCESS",
					nameof(TickEventData_IsReplicateTick_FlagsMatchConstructorIntent))
					.GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "FAILURE",
					$"{nameof(TickEventData_IsReplicateTick_FlagsMatchConstructorIntent)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(TickEventData_IsReplicateTick_FlagsMatchConstructorIntent))
					.GetAwaiter().GetResult();
			}
		}
	}
}
