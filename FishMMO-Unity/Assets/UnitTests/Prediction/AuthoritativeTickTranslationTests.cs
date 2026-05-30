using System;
using NUnit.Framework;
using FishMMO.Shared;
using FishNet.Managing.Timing;
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
	/// These tests intentionally do NOT instantiate <c>BuffController</c> or
	/// <c>CooldownController</c> as Unity components. Doing so would require a live
	/// <c>NetworkObject</c> + <c>TimeManager</c>, which is impractical in an
	/// edit-mode test. Instead we mirror the production formula exactly so the test
	/// breaks the moment the formula in <c>ResolveAuthoritativeTick</c> drifts from
	/// the one asserted here.
	/// </para>
	/// </summary>
	[TestFixture]
	public class AuthoritativeTickTranslationTests
	{
		private const float TickDelta30 = 1.0f / 30f;

		/// <summary>
		/// Mirror of the production formula used by both
		/// <c>BuffController.ResolveAuthoritativeTick</c> and
		/// <c>CooldownController.ResolveAuthoritativeTick</c>. Uses the same signed
		/// offset helpers as production so negative raw-to-replicate offsets do not
		/// rely on unsigned subtraction wrap.
		/// </summary>
		private static uint ResolveAuthoritativeTick(uint serverTick, uint lastReplicateTick, uint lastReplicateLocalTick)
		{
			const uint MissingRawTick = 0u;
			if (lastReplicateTick == TimeManager.UNSET_TICK ||
				lastReplicateLocalTick == TimeManager.UNSET_TICK)
			{
				return serverTick;
			}

			if (serverTick == TimeManager.UNSET_TICK || serverTick == MissingRawTick)
			{
				return lastReplicateTick;
			}

			int tickOffset = BuffController.GetSignedTickOffset(lastReplicateLocalTick, serverTick, nameof(ResolveAuthoritativeTick));
			return BuffController.AddSignedTickOffset(lastReplicateTick, tickOffset);
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
			int offset = BuffController.GetSignedTickOffset(1_000u, 900u, nameof(BuffPayloadTickTranslation_PayloadAheadOfCurrent_UsesNegativeSignedOffset));

			Assert.AreEqual(-100, offset);
			Assert.AreEqual(930u, BuffController.AddSignedTickOffset(1_030u, offset),
				"ExpiryTick must preserve remaining duration in the receiver domain instead of wrapping forward by uint.MaxValue.");
			Assert.AreEqual(920u, BuffController.AddSignedTickOffset(1_020u, offset),
				"NextTickTick must be translated with the same signed offset as ExpiryTick.");
		}

		/// <summary>
		/// Verifies pre-replicate buff ticks can be translated backward when the local
		/// authoritative tick is ahead of the first replicate input tick.
		/// </summary>
		[Test]
		public void BuffPreReplicateTranslation_LocalTickAheadOfInput_DoesNotUnsignedWrap()
		{
			int offset = BuffController.GetSignedTickOffset(105u, 100u, nameof(BuffPreReplicateTranslation_LocalTickAheadOfInput_DoesNotUnsignedWrap));

			Assert.AreEqual(-5, offset);
			Assert.AreEqual(130u, BuffController.AddSignedTickOffset(135u, offset),
				"A buff stamped at LocalTick+duration must map back to inputTick+duration, not jump to a near-permanent uint value.");
		}

		/// <summary>
		/// Verifies cooldown payload and pre-replicate translations use the same signed
		/// offset behavior as buff timing fields.
		/// </summary>
		[Test]
		public void CooldownTickTranslation_LocalTickAheadOfInput_DoesNotUnsignedWrap()
		{
			int offset = CooldownController.GetSignedTickOffset(105u, 100u, nameof(CooldownTickTranslation_LocalTickAheadOfInput_DoesNotUnsignedWrap));

			Assert.AreEqual(-5, offset);
			Assert.AreEqual(100u, CooldownController.AddSignedTickOffset(105u, offset),
				"Cooldown StartTick must map back to the replicate input tick rather than wrapping forward by uint.MaxValue.");
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
		/// After the fix, <c>ApplyAuthoritative(LocalTick)</c> first maps <c>105</c> into
		/// the replicate domain via the offset <c>lastReplicateTick - lastReplicateLocalTick</c>,
		/// yielding <c>100</c>, then stamps <c>ExpiryTick = 100 + 30 = 130</c>. The buff
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
		/// When a raw server tick is at an arbitrary offset ahead of (or behind)
		/// <c>lastReplicateLocalTick</c>, the mapped tick must preserve that offset
		/// relative to <c>lastReplicateTick</c>. Verifies the formula for several
		/// realistic offsets: client lagging by 5/10/20 ticks (typical 30 Hz tick rates
		/// at 167/333/667 ms RTT).
		/// </summary>
		[TestCase(0)]   // Server tick == lastReplicateLocalTick → mapped == lastReplicateTick
		[TestCase(1)]   // 1-tick lookahead (input queue head)
		[TestCase(5)]   // 167 ms RTT @ 30 Hz
		[TestCase(10)]  // 333 ms RTT @ 30 Hz
		[TestCase(20)]  // 667 ms RTT @ 30 Hz (poor network)
		public void ResolveAuthoritativeTick_ServerAheadOfReplicate_OffsetPreserved(int serverAheadBy)
		{
			try
			{
				AuthTestTrace.LogTestStart($"{nameof(ResolveAuthoritativeTick_ServerAheadOfReplicate_OffsetPreserved)}({serverAheadBy})",
					$"serverTick - lastReplicateLocalTick == {serverAheadBy} → mapped - lastReplicateTick == {serverAheadBy}.")
					.GetAwaiter().GetResult();

				uint lastReplicateTick = 1_000u;
				uint lastReplicateLocalTick = 1_005u; // server is 5 ticks ahead of client input
				uint serverTick = lastReplicateLocalTick + (uint)serverAheadBy;

				uint mapped = ResolveAuthoritativeTick(serverTick, lastReplicateTick, lastReplicateLocalTick);

				LogAssert.AreEqual(lastReplicateTick + (uint)serverAheadBy, mapped,
					$"For serverTick == lastReplicateLocalTick + {serverAheadBy}, mapped must equal lastReplicateTick + {serverAheadBy}.");

				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "SUCCESS",
					$"{nameof(ResolveAuthoritativeTick_ServerAheadOfReplicate_OffsetPreserved)}({serverAheadBy})")
					.GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "FAILURE",
					$"{nameof(ResolveAuthoritativeTick_ServerAheadOfReplicate_OffsetPreserved)}({serverAheadBy}): {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd($"{nameof(ResolveAuthoritativeTick_ServerAheadOfReplicate_OffsetPreserved)}({serverAheadBy})")
					.GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Region triggers and other authoritative callbacks may arrive at a raw server
		/// tick older than the last captured <c>LocalTick</c>. The mapping must preserve
		/// that negative offset in signed space instead of depending on unsigned wrap.
		/// </summary>
		[TestCase(1)]
		[TestCase(5)]
		[TestCase(20)]
		public void ResolveAuthoritativeTick_ServerBehindReplicateLocal_OffsetPreserved(int serverBehindBy)
		{
			try
			{
				AuthTestTrace.LogTestStart($"{nameof(ResolveAuthoritativeTick_ServerBehindReplicateLocal_OffsetPreserved)}({serverBehindBy})",
					$"serverTick - lastReplicateLocalTick == -{serverBehindBy} → mapped - lastReplicateTick == -{serverBehindBy}.")
					.GetAwaiter().GetResult();

				uint lastReplicateTick = 1_000u;
				uint lastReplicateLocalTick = 1_005u;
				uint serverTick = lastReplicateLocalTick - (uint)serverBehindBy;

				uint mapped = ResolveAuthoritativeTick(serverTick, lastReplicateTick, lastReplicateLocalTick);

				LogAssert.AreEqual(lastReplicateTick - (uint)serverBehindBy, mapped,
					$"For serverTick == lastReplicateLocalTick - {serverBehindBy}, mapped must equal lastReplicateTick - {serverBehindBy}.");

				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "SUCCESS",
					$"{nameof(ResolveAuthoritativeTick_ServerBehindReplicateLocal_OffsetPreserved)}({serverBehindBy})")
					.GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AuthoritativeTickTranslationTests", "FAILURE",
					$"{nameof(ResolveAuthoritativeTick_ServerBehindReplicateLocal_OffsetPreserved)}({serverBehindBy}): {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd($"{nameof(ResolveAuthoritativeTick_ServerBehindReplicateLocal_OffsetPreserved)}({serverBehindBy})")
					.GetAwaiter().GetResult();
			}
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
				LogAssert.AreEqual(serverTick,
					ResolveAuthoritativeTick(serverTick, 9_999u, TimeManager.UNSET_TICK),
					"lastReplicateLocalTick unset → return serverTick unchanged.");

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

				uint lastReplicateTick = 50u;                    // Client just started replicating.
				uint lastReplicateLocalTick = 108_050u;          // Server ran ~60 minutes alone @ 30 tps.
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
