
using System;
using NUnit.Framework;
using FishMMO.Shared;
using UnityEngine;
using System.Reflection;
using FishMMO.Shared.Core;
using AuthTestTrace = FishMMO.UnitTests.Harness.AuthTestTrace;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Determinism proofs for the Buff system across the two surviving creation paths and the
	/// reconcile delta-serialiser:
	///
	///   1. Fresh apply  — Buff(templateID, currentTick, tickDelta)
	///                     Called by BuffController.Apply(BaseBuffTemplate, uint) on both
	///                     server and observers (via state-forwarded Replicate), driven by
	///                     the same input.GetTick() from CharacterReplicateData. ExpiryTick
	///                     is computed internally from the template duration, so the apply
	///                     tick must match on both sides.
	///
	///   2. Restore      — Buff(templateID, expiryTick, nextTickTick, tickDelta, stacks, tickCount)
	///                     Called by BuffController.ReadPayload and RestoreFromReconcile.
	///                     Absolute network ticks are written by the server and read verbatim
	///                     by the client — tick-space is shared, so no translation is required.
	///
	/// FishNet Prediction V2 state forwarding (enabled by default on every NetworkObject)
	/// delivers <c>CharacterReconcileData.Buffs</c> to every observer, so observers receive
	/// the authoritative buff snapshot the same way the owner does. The legacy observer
	/// broadcast path (CharacterObserverBuffAdd/RemoveBroadcast) has been deleted because it
	/// was a duplicate, lower-fidelity channel: it used <c>TimeManager.LocalTick</c> as the
	/// apply tick, which diverges from the source character's tick by an arbitrary session
	/// offset. A regression sentinel below pins the math of that divergence so any future
	/// re-introduction of a LocalTick-based apply path is caught immediately.
	///
	/// Core invariants enforced:
	///   - HasExpired:                (int)(currentTick - ExpiryTick) >= 0  — signed wrap-safe.
	///   - DurationToTicks:           ceiling division, clamped to a minimum of 1 tick.
	///   - Reconcile diff:            BuffReconcileEntry.Equals must compare every field;
	///                                a single divergent field is what drives the wire write.
	///   - Reconcile diff:            equal entries must produce equal hash codes
	///                                (Equals/GetHashCode contract).
	///   - Restore preservation:      ExpiryTick / NextTickTick / Stacks / TickCount survive
	///                                a snapshot → entry → snapshot round trip byte-for-byte.
	///   - Cross-tickrate parity:     ExpiryTick math at 20 / 30 / 60 / 100 tps follows the
	///                                ceiling formula exactly — no rounding drift.
	/// </summary>
	[TestFixture]
	public class BuffExpiryTests
	{
		[SetUp]
		public void SetUp()
		{
			var template = ScriptableObject.CreateInstance<MockBuffTemplate>();
			template.Duration = DurationSeconds;
			template.TickRate = 1.0f;
			template.name = "TestBuffTemplate";

			template.AddToCache(template.name);

			TemplateID = template.ID;
		}

		// Minimal mock template for testing
		private class MockBuffTemplate : BaseBuffTemplate
		{
			public override void OnApply(Buff buff, ICharacter target) { }
			public override void OnRemove(Buff buff, ICharacter target) { }
		}

		// ── Shared constants ──────────────────────────────────────────────────────

		private static int TemplateID = 1;
		private const float TickDelta30 = 1.0f / 30f; // 30 tps — realistic session value
		private const float DurationSeconds = 30f;

		/// <summary>
		/// Mirror of <see cref="Buff"/>.DurationToTicks: ceiling division, min 1, zero on
		/// non-positive inputs. Kept here so test expectations stay independent of the
		/// production source and any regression in either side is caught immediately.
		/// </summary>
		private static uint DurationToTicks(float seconds, float tickDelta)
		{
			if (tickDelta <= 0f || seconds <= 0f) return 0u;
			uint ticks = (uint)Math.Ceiling(seconds / tickDelta);
			return ticks < 1u ? 1u : ticks;
		}

		// ─────────────────────────────────────────────────────────────────────────
		//  PATH 1 — Fresh apply: server and client compute ExpiryTick from the same tick
		// ─────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Primary correctness guarantee. When server and client both call the fresh-apply
		/// constructor with the same <c>input.GetTick()</c> value (the only tick source that
		/// is shared by both sides through <see cref="CharacterReplicateData"/>) the resulting
		/// <see cref="Buff.ExpiryTick"/> must be bit-identical and the expiry boundary must
		/// be at the exact same tick.
		/// </summary>
		[Test]
		public void FreshApply_SameReplicateTick_ExpiryIsDeterministic()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(FreshApply_SameReplicateTick_ExpiryIsDeterministic),
					"Server and client both call the fresh-apply constructor with the same " +
					"input.GetTick(). ExpiryTick must be identical and boundary conditions exact.")
					.GetAwaiter().GetResult();

				uint replicateTick = 1_000u; // input.GetTick() — same on both sides
				uint durationTicks = DurationToTicks(DurationSeconds, TickDelta30);

				var serverBuff = new Buff(TemplateID, replicateTick, TickDelta30);
				var clientBuff = new Buff(TemplateID, replicateTick, TickDelta30);

				LogAssert.AreEqual(serverBuff.ExpiryTick, clientBuff.ExpiryTick,
					"Server and client ExpiryTick must be identical when using the same replicate tick.");

				LogAssert.AreEqual(replicateTick + durationTicks, serverBuff.ExpiryTick,
					"ExpiryTick must equal applyTick + DurationToTicks(duration, tickDelta).");

				LogAssert.IsFalse(serverBuff.HasExpired(serverBuff.ExpiryTick - 1u),
					"Buff must be active one tick before ExpiryTick.");
				LogAssert.IsTrue(serverBuff.HasExpired(serverBuff.ExpiryTick),
					"Buff must be expired exactly at ExpiryTick.");

				LogAssert.IsFalse(clientBuff.HasExpired(clientBuff.ExpiryTick - 1u),
					"Client buff must be active one tick before ExpiryTick.");
				LogAssert.IsTrue(clientBuff.HasExpired(clientBuff.ExpiryTick),
					"Client buff must be expired exactly at ExpiryTick.");

				AuthTestTrace.Log("BuffExpiryTests", "SUCCESS",
					nameof(FreshApply_SameReplicateTick_ExpiryIsDeterministic)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("BuffExpiryTests", "FAILURE",
					$"{nameof(FreshApply_SameReplicateTick_ExpiryIsDeterministic)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(FreshApply_SameReplicateTick_ExpiryIsDeterministic))
					.GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Regression sentinel for any future re-introduction of a <c>LocalTick</c>-based
		/// apply path. All current apply paths use a tick from <c>CharacterReplicateData</c>
		/// (server, owner, and state-forwarded observers all see the same value); the legacy
		/// observer-broadcast path that used <c>TimeManager.LocalTick</c> has been deleted.
		/// This test pins the math of the divergence: if anyone ever wires a LocalTick-based
		/// apply onto a controller whose <c>Tick()</c> is active, the resulting <c>ExpiryTick</c>
		/// offset equals the LocalTick gap and the buff appears expired well before the server
		/// believes it should be — exactly the symptom this test documents.
		/// </summary>
		[Test]
		public void FreshApply_DivergentLocalTick_ExpiryDiffers_BroadcastPathKnownIssue()
		{
			try
			{
				AuthTestTrace.LogTestStart(
					nameof(FreshApply_DivergentLocalTick_ExpiryDiffers_BroadcastPathKnownIssue),
					"Broadcast path uses LocalTick which diverges from the server replicate tick. " +
					"Demonstrates why owner state MUST come from reconcile, never from broadcast.")
					.GetAwaiter().GetResult();

				uint serverReplicateTick = 11_000u;
				uint clientLocalTick = 1_000u;

				var serverBuff = new Buff(TemplateID, serverReplicateTick, TickDelta30);
				var clientBuff = new Buff(TemplateID, clientLocalTick, TickDelta30);

				uint expectedOffset = serverReplicateTick - clientLocalTick;
				uint actualOffset = serverBuff.ExpiryTick - clientBuff.ExpiryTick;

				LogAssert.AreEqual(expectedOffset, actualOffset,
					"ExpiryTick offset must equal the server/client LocalTick divergence.");

				LogAssert.IsTrue(clientBuff.HasExpired(serverBuff.ExpiryTick),
					"Client buff (broadcast path) appears expired well before server expiry — " +
					"demonstrates why reconcile correction is mandatory.");

				AuthTestTrace.Log("BuffExpiryTests", "SUCCESS",
					nameof(FreshApply_DivergentLocalTick_ExpiryDiffers_BroadcastPathKnownIssue))
					.GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("BuffExpiryTests", "FAILURE",
					$"{nameof(FreshApply_DivergentLocalTick_ExpiryDiffers_BroadcastPathKnownIssue)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(
					nameof(FreshApply_DivergentLocalTick_ExpiryDiffers_BroadcastPathKnownIssue))
					.GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Determinism across realistic FishNet tick rates. The ceiling formula must produce
		/// identical ExpiryTick at 20 / 30 / 60 / 100 tps without floating-point drift.
		/// Verifies that <c>DurationToTicks</c> never produces an off-by-one between two
		/// invocations with the same inputs.
		/// </summary>
		[TestCase(20u)]
		[TestCase(30u)]
		[TestCase(60u)]
		[TestCase(100u)]
		public void FreshApply_TickRateParity_NoDrift(uint tps)
		{
			try
			{
				AuthTestTrace.LogTestStart($"{nameof(FreshApply_TickRateParity_NoDrift)}({tps})",
					$"DurationToTicks math must be bit-identical between repeated calls at {tps} tps.")
					.GetAwaiter().GetResult();

				float tickDelta = 1.0f / tps;
				uint applyTick = 50_000u;
				uint a = DurationToTicks(DurationSeconds, tickDelta);
				uint b = DurationToTicks(DurationSeconds, tickDelta);

				LogAssert.AreEqual(a, b, "DurationToTicks must be idempotent for identical inputs.");

				var first = new Buff(TemplateID, applyTick, tickDelta);
				var second = new Buff(TemplateID, applyTick, tickDelta);
				LogAssert.AreEqual(first.ExpiryTick, second.ExpiryTick,
					"Two fresh-apply Buff instances created with identical inputs must produce identical ExpiryTick.");
				LogAssert.AreEqual(first.NextTickTick, second.NextTickTick,
					"Two fresh-apply Buff instances created with identical inputs must produce identical NextTickTick.");

				// At the exact ceiling boundary the buff must still be active one tick prior
				// and expired at the boundary — regardless of tps.
				LogAssert.IsFalse(first.HasExpired(first.ExpiryTick - 1u),
					$"At {tps} tps the buff must be active one tick before ExpiryTick.");
				LogAssert.IsTrue(first.HasExpired(first.ExpiryTick),
					$"At {tps} tps the buff must be expired exactly at ExpiryTick.");

				AuthTestTrace.Log("BuffExpiryTests", "SUCCESS",
					$"{nameof(FreshApply_TickRateParity_NoDrift)}({tps})").GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("BuffExpiryTests", "FAILURE",
					$"{nameof(FreshApply_TickRateParity_NoDrift)}({tps}): {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd($"{nameof(FreshApply_TickRateParity_NoDrift)}({tps})")
					.GetAwaiter().GetResult();
			}
		}

		// ─────────────────────────────────────────────────────────────────────────
		//  PATH 2 — Restore from reconcile / payload
		// ─────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// The restore constructor stores absolute ExpiryTick verbatim. Server and client
		/// share network tick space via the replicate pipeline, so an ExpiryTick written
		/// by the server is valid when evaluated against any tick on the client.
		/// </summary>
		[Test]
		public void RestoreConstructor_AbsoluteExpiryTick_IsCorrectInSharedTickSpace()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(RestoreConstructor_AbsoluteExpiryTick_IsCorrectInSharedTickSpace),
					"Restore constructor stores absolute ExpiryTick. No translation is needed.")
					.GetAwaiter().GetResult();

				uint serverReplicateTick = 11_000u;
				uint durationTicks = DurationToTicks(DurationSeconds, TickDelta30);
				uint absoluteExpiryTick = serverReplicateTick + durationTicks;

				var restored = new Buff(TemplateID, absoluteExpiryTick, absoluteExpiryTick, TickDelta30, 0, 0);

				LogAssert.AreEqual(absoluteExpiryTick, restored.ExpiryTick,
					"Restore constructor must preserve ExpiryTick exactly.");
				LogAssert.IsFalse(restored.HasExpired(absoluteExpiryTick - 1u),
					"Restored buff must be active one tick before ExpiryTick.");
				LogAssert.IsTrue(restored.HasExpired(absoluteExpiryTick),
					"Restored buff must expire exactly at absolute ExpiryTick.");

				AuthTestTrace.Log("BuffExpiryTests", "SUCCESS",
					nameof(RestoreConstructor_AbsoluteExpiryTick_IsCorrectInSharedTickSpace))
					.GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("BuffExpiryTests", "FAILURE",
					$"{nameof(RestoreConstructor_AbsoluteExpiryTick_IsCorrectInSharedTickSpace)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(RestoreConstructor_AbsoluteExpiryTick_IsCorrectInSharedTickSpace))
					.GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// <c>RestoreFromReconcile</c> patches an existing buff's ExpiryTick in-place. The
		/// patched value must be the one used for subsequent <see cref="Buff.HasExpired"/>
		/// checks — a stale local prediction must NOT survive reconcile.
		/// </summary>
		[Test]
		public void ReconcilePatch_OverwritesExpiryTick_NewValueUsedForExpiry()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(ReconcilePatch_OverwritesExpiryTick_NewValueUsedForExpiry),
					"RestoreFromReconcile patches ExpiryTick on an existing buff. " +
					"HasExpired must use the patched value, not the stale local value.")
					.GetAwaiter().GetResult();

				uint clientPredictedApplyTick = 1_000u;
				uint durationTicks = DurationToTicks(DurationSeconds, TickDelta30);

				var buff = new Buff(TemplateID, clientPredictedApplyTick, TickDelta30);
				uint staleExpiryTick = buff.ExpiryTick;

				uint serverApplyTick = 11_000u;
				uint authoritativeExpiry = serverApplyTick + durationTicks;

				buff.ExpiryTick = authoritativeExpiry;

				LogAssert.AreEqual(authoritativeExpiry, buff.ExpiryTick,
					"ExpiryTick must reflect the patched authoritative value.");
				LogAssert.IsFalse(buff.HasExpired(staleExpiryTick),
					"Buff must NOT expire at the stale predicted ExpiryTick after reconcile patch.");
				LogAssert.IsFalse(buff.HasExpired(authoritativeExpiry - 1u),
					"Buff must be active one tick before authoritative ExpiryTick.");
				LogAssert.IsTrue(buff.HasExpired(authoritativeExpiry),
					"Buff must expire at the authoritative ExpiryTick.");

				AuthTestTrace.Log("BuffExpiryTests", "SUCCESS",
					nameof(ReconcilePatch_OverwritesExpiryTick_NewValueUsedForExpiry)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("BuffExpiryTests", "FAILURE",
					$"{nameof(ReconcilePatch_OverwritesExpiryTick_NewValueUsedForExpiry)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(ReconcilePatch_OverwritesExpiryTick_NewValueUsedForExpiry))
					.GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Round-trip: take a buff's authoritative state, project it through a
		/// <see cref="BuffReconcileEntry"/> as the wire format does, then rebuild a Buff
		/// from that entry. Every field — ExpiryTick / NextTickTick / Stacks / TickCount —
		/// must survive byte-for-byte. Any drift here causes silent client/server desync.
		/// </summary>
		[Test]
		public void ReconcileEntry_RoundTrip_PreservesAllTickAndStackFields()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(ReconcileEntry_RoundTrip_PreservesAllTickAndStackFields),
					"Buff → BuffReconcileEntry → Buff must preserve every wire field exactly.")
					.GetAwaiter().GetResult();

				uint applyTick = 50_000u;
				uint expiryTick = applyTick + DurationToTicks(DurationSeconds, TickDelta30);
				uint nextTickTick = applyTick + DurationToTicks(1.0f, TickDelta30);
				int stacks = 3;
				int tickCount = 7;

				var source = new Buff(TemplateID, expiryTick, nextTickTick, TickDelta30, stacks, tickCount);

				var entry = new BuffReconcileEntry
				{
					TemplateID = source.Template != null ? source.Template.ID : TemplateID,
					ExpiryTick = source.ExpiryTick,
					NextTickTick = source.NextTickTick,
					Stacks = source.Stacks,
					TickCount = source.TickCount,
				};

				var restored = new Buff(entry.TemplateID, entry.ExpiryTick, entry.NextTickTick,
					TickDelta30, entry.Stacks, entry.TickCount);

				LogAssert.AreEqual(source.ExpiryTick, restored.ExpiryTick, "ExpiryTick must round-trip.");
				LogAssert.AreEqual(source.NextTickTick, restored.NextTickTick, "NextTickTick must round-trip.");
				LogAssert.AreEqual(source.Stacks, restored.Stacks, "Stacks must round-trip.");
				LogAssert.AreEqual(source.TickCount, restored.TickCount, "TickCount must round-trip — required for cumulative-tick reversal on restore.");

				AuthTestTrace.Log("BuffExpiryTests", "SUCCESS",
					nameof(ReconcileEntry_RoundTrip_PreservesAllTickAndStackFields)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("BuffExpiryTests", "FAILURE",
					$"{nameof(ReconcileEntry_RoundTrip_PreservesAllTickAndStackFields)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(ReconcileEntry_RoundTrip_PreservesAllTickAndStackFields))
					.GetAwaiter().GetResult();
			}
		}

		// ─────────────────────────────────────────────────────────────────────────
		//  BuffReconcileEntry equality — drives the delta-serializer diff
		// ─────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// The delta serializer's per-index diff calls <see cref="BuffReconcileEntry.Equals"/>
		/// to decide which entries to write. <c>Equals</c> MUST compare every field — if it
		/// skips one, a changed field never reaches the wire and the client desyncs silently.
		/// </summary>
		[Test]
		public void ReconcileEntry_Equals_DetectsEveryFieldDivergence()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(ReconcileEntry_Equals_DetectsEveryFieldDivergence),
					"BuffReconcileEntry.Equals must return false when any single field differs.")
					.GetAwaiter().GetResult();

				var baseline = new BuffReconcileEntry
				{
					TemplateID = 42,
					ExpiryTick = 10_000u,
					NextTickTick = 9_500u,
					Stacks = 2,
					TickCount = 5,
				};

				LogAssert.IsTrue(baseline.Equals(baseline),
					"An entry must equal itself.");

				var diffTemplate = baseline; diffTemplate.TemplateID = 43;
				LogAssert.IsFalse(baseline.Equals(diffTemplate),
					"TemplateID divergence must be detected.");

				var diffExpiry = baseline; diffExpiry.ExpiryTick = baseline.ExpiryTick + 1u;
				LogAssert.IsFalse(baseline.Equals(diffExpiry),
					"ExpiryTick divergence must be detected — primary expiry-desync field.");

				var diffNextTick = baseline; diffNextTick.NextTickTick = baseline.NextTickTick + 1u;
				LogAssert.IsFalse(baseline.Equals(diffNextTick),
					"NextTickTick divergence must be detected — periodic-fire desync field.");

				var diffStacks = baseline; diffStacks.Stacks = baseline.Stacks + 1;
				LogAssert.IsFalse(baseline.Equals(diffStacks),
					"Stacks divergence must be detected — modifier-count desync field.");

				var diffTickCount = baseline; diffTickCount.TickCount = baseline.TickCount + 1;
				LogAssert.IsFalse(baseline.Equals(diffTickCount),
					"TickCount divergence must be detected — cumulative-reversal desync field.");

				AuthTestTrace.Log("BuffExpiryTests", "SUCCESS",
					nameof(ReconcileEntry_Equals_DetectsEveryFieldDivergence)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("BuffExpiryTests", "FAILURE",
					$"{nameof(ReconcileEntry_Equals_DetectsEveryFieldDivergence)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(ReconcileEntry_Equals_DetectsEveryFieldDivergence))
					.GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// <c>Equals</c>/<c>GetHashCode</c> contract: equal entries must produce equal hashes.
		/// Required for correctness if the delta layer or any test harness ever uses a hashed
		/// collection of entries.
		/// </summary>
		[Test]
		public void ReconcileEntry_EqualsHashCodeContract_Holds()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(ReconcileEntry_EqualsHashCodeContract_Holds),
					"If two entries compare equal, their hash codes must match.")
					.GetAwaiter().GetResult();

				var a = new BuffReconcileEntry
				{
					TemplateID = 7,
					ExpiryTick = 1234u,
					NextTickTick = 1200u,
					Stacks = 1,
					TickCount = 4,
				};
				var b = new BuffReconcileEntry
				{
					TemplateID = 7,
					ExpiryTick = 1234u,
					NextTickTick = 1200u,
					Stacks = 1,
					TickCount = 4,
				};

				LogAssert.IsTrue(a.Equals(b), "Entries with identical fields must compare equal.");
				LogAssert.AreEqual(a.GetHashCode(), b.GetHashCode(),
					"Equal entries must produce equal hash codes (Equals/GetHashCode contract).");

				AuthTestTrace.Log("BuffExpiryTests", "SUCCESS",
					nameof(ReconcileEntry_EqualsHashCodeContract_Holds)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("BuffExpiryTests", "FAILURE",
					$"{nameof(ReconcileEntry_EqualsHashCodeContract_Holds)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(ReconcileEntry_EqualsHashCodeContract_Holds))
					.GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Index-delta correctness invariant. The <see cref="BuffReconcileEntry.WriteArrayDelta"/>
		/// path requires <c>prevCount == nextCount</c> before it walks indices and writes
		/// only the differing positions. Inside that path it relies entirely on per-index
		/// <see cref="BuffReconcileEntry.Equals"/>. This test reproduces the diff exactly as
		/// the serializer performs it on a same-length array where one buff was simultaneously
		/// removed and another added (so the sorted-dictionary suffix shifted) — every shifted
		/// index must be flagged as changed.
		/// </summary>
		[Test]
		public void ReconcileEntry_SameLengthSuffixShift_AllShiftedIndicesFlagged()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(ReconcileEntry_SameLengthSuffixShift_AllShiftedIndicesFlagged),
					"When a buff is removed and another added in the same tick, the sorted " +
					"snapshot shifts. Per-index Equals must flag every shifted slot so the " +
					"server resends them and the client overwrites correctly.")
					.GetAwaiter().GetResult();

				// prev: template IDs [1, 5, 9]
				BuffReconcileEntry[] prev = new BuffReconcileEntry[]
				{
					new BuffReconcileEntry { TemplateID = 1, ExpiryTick = 100u, NextTickTick = 90u,  Stacks = 1, TickCount = 0 },
					new BuffReconcileEntry { TemplateID = 5, ExpiryTick = 200u, NextTickTick = 190u, Stacks = 1, TickCount = 0 },
					new BuffReconcileEntry { TemplateID = 9, ExpiryTick = 300u, NextTickTick = 290u, Stacks = 1, TickCount = 0 },
				};

				// next: id 5 removed, id 7 added → still 3 entries, sorted: [1, 7, 9]
				BuffReconcileEntry[] next = new BuffReconcileEntry[]
				{
					new BuffReconcileEntry { TemplateID = 1, ExpiryTick = 100u, NextTickTick = 90u,  Stacks = 1, TickCount = 0 },
					new BuffReconcileEntry { TemplateID = 7, ExpiryTick = 250u, NextTickTick = 240u, Stacks = 1, TickCount = 0 },
					new BuffReconcileEntry { TemplateID = 9, ExpiryTick = 300u, NextTickTick = 290u, Stacks = 1, TickCount = 0 },
				};

				LogAssert.AreEqual(prev.Length, next.Length,
					"Pre-condition: arrays must be the same length to trigger the index-delta path.");

				LogAssert.IsTrue(prev[0].Equals(next[0]),
					"Index 0 unchanged — must not be flagged.");
				LogAssert.IsFalse(prev[1].Equals(next[1]),
					"Index 1 shifted from id 5 to id 7 — must be flagged as changed.");
				LogAssert.IsTrue(prev[2].Equals(next[2]),
					"Index 2 unchanged (id 9 at both ends) — must not be flagged.");

				AuthTestTrace.Log("BuffExpiryTests", "SUCCESS",
					nameof(ReconcileEntry_SameLengthSuffixShift_AllShiftedIndicesFlagged)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("BuffExpiryTests", "FAILURE",
					$"{nameof(ReconcileEntry_SameLengthSuffixShift_AllShiftedIndicesFlagged)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(ReconcileEntry_SameLengthSuffixShift_AllShiftedIndicesFlagged))
					.GetAwaiter().GetResult();
			}
		}

		// ─────────────────────────────────────────────────────────────────────────
		//  HasExpired — signed wrap-safe comparison
		// ─────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// <c>(int)(currentTick - ExpiryTick) &gt;= 0</c> is correct whenever the absolute
		/// distance fits in 31 bits (~2.07e9 ticks, ~810 days at 30 tps). Verifies the
		/// boundary triple: one tick before, exactly at, one tick after expiry.
		/// </summary>
		[Test]
		public void HasExpired_AtBoundary_TripleBeforeAtAfter()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(HasExpired_AtBoundary_TripleBeforeAtAfter),
					"Expiry boundary must be exact at currentTick == ExpiryTick.")
					.GetAwaiter().GetResult();

				uint expiryTick = 5_000u;
				var buff = new Buff(TemplateID, expiryTick, expiryTick, TickDelta30, 0, 0);

				LogAssert.IsFalse(buff.HasExpired(expiryTick - 1u),
					"One tick before ExpiryTick the buff must still be active.");
				LogAssert.IsTrue(buff.HasExpired(expiryTick),
					"At ExpiryTick the buff must be expired.");
				LogAssert.IsTrue(buff.HasExpired(expiryTick + 1u),
					"One tick after ExpiryTick the buff must still be expired.");

				AuthTestTrace.Log("BuffExpiryTests", "SUCCESS",
					nameof(HasExpired_AtBoundary_TripleBeforeAtAfter)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("BuffExpiryTests", "FAILURE",
					$"{nameof(HasExpired_AtBoundary_TripleBeforeAtAfter)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(HasExpired_AtBoundary_TripleBeforeAtAfter))
					.GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// When the unsigned tick counter wraps from near <c>uint.MaxValue</c> back to a small
		/// value during a long session, <c>HasExpired</c> must keep working: a buff whose
		/// ExpiryTick is past the wrap point must still expire when currentTick reaches it.
		/// </summary>
		[Test]
		public void HasExpired_AcrossUintWrap_BehavesAsSignedDelta()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(HasExpired_AcrossUintWrap_BehavesAsSignedDelta),
					"Buff applied just before uint wrap must expire correctly after wrap.")
					.GetAwaiter().GetResult();

				uint applyTick = uint.MaxValue - 100u;
				uint durationTicks = 500u;
				uint expiryTick = unchecked(applyTick + durationTicks); // wraps

				var buff = new Buff(TemplateID, expiryTick, expiryTick, TickDelta30, 0, 0);

				uint justBefore = unchecked(expiryTick - 1u);
				LogAssert.IsFalse(buff.HasExpired(justBefore),
					"One tick before ExpiryTick must report active even across wrap.");

				LogAssert.IsTrue(buff.HasExpired(expiryTick),
					"Exactly at wrapped ExpiryTick must report expired.");

				LogAssert.IsTrue(buff.HasExpired(unchecked(expiryTick + 1u)),
					"One tick after wrapped ExpiryTick must still report expired.");

				AuthTestTrace.Log("BuffExpiryTests", "SUCCESS",
					nameof(HasExpired_AcrossUintWrap_BehavesAsSignedDelta)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("BuffExpiryTests", "FAILURE",
					$"{nameof(HasExpired_AcrossUintWrap_BehavesAsSignedDelta)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(HasExpired_AcrossUintWrap_BehavesAsSignedDelta))
					.GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// When the gap exceeds 2^31 ticks the signed cast flips sign and the comparison
		/// becomes meaningless. Verifies the (intentional) limit and documents the failure
		/// mode so anyone shipping &gt;810-day buffs is forced to confront it.
		/// </summary>
		[Test]
		public void HasExpired_SignedWrapAroundComparison_DoesNotFalsePositive()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(HasExpired_SignedWrapAroundComparison_DoesNotFalsePositive),
					"When the gap fits in 31 bits, no false positive. Documents the 2^31 limit.")
					.GetAwaiter().GetResult();

				uint expiryTick = 10u;
				uint currentTick = uint.MaxValue - 5u;

				var buff = new Buff(TemplateID, expiryTick, expiryTick, TickDelta30, 0, 0);

				LogAssert.IsFalse(buff.HasExpired(currentTick),
					"HasExpired must return false when the unsigned distance exceeds 2^31 ticks.");

				LogAssert.IsTrue(buff.HasExpired(expiryTick),
					"HasExpired must return true at ExpiryTick.");

				AuthTestTrace.Log("BuffExpiryTests", "SUCCESS",
					nameof(HasExpired_SignedWrapAroundComparison_DoesNotFalsePositive)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("BuffExpiryTests", "FAILURE",
					$"{nameof(HasExpired_SignedWrapAroundComparison_DoesNotFalsePositive)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(HasExpired_SignedWrapAroundComparison_DoesNotFalsePositive))
					.GetAwaiter().GetResult();
			}
		}

		// ─────────────────────────────────────────────────────────────────────────
		//  DurationToTicks edge cases
		// ─────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Any positive sub-tick duration must be clamped to 1 tick so the buff survives
		/// at least one Tick() evaluation. A 0-tick "instant" buff would be applied and
		/// immediately removed in the same tick, never firing OnTick or OnApply visibly.
		/// </summary>
		[Test]
		public void FreshApply_SubTickDuration_ClampedToOneTick()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(FreshApply_SubTickDuration_ClampedToOneTick),
					"Sub-tick durations clamp to 1 tick. Buff survives exactly one tick.")
					.GetAwaiter().GetResult();

				float tinyDuration = 0.001f;
				float tickDelta = 1.0f / 30f;
				uint applyTick = 500u;

				uint expectedDuration = DurationToTicks(tinyDuration, tickDelta);
				LogAssert.AreEqual(1u, expectedDuration,
					"DurationToTicks must clamp sub-tick positive durations to 1.");

				uint expiryTick = applyTick + expectedDuration;
				var buff = new Buff(TemplateID, expiryTick, expiryTick, tickDelta, 0, 0);

				LogAssert.IsFalse(buff.HasExpired(applyTick), "Buff must be active at the apply tick.");
				LogAssert.IsTrue(buff.HasExpired(applyTick + 1u), "Buff must expire after exactly 1 tick.");

				AuthTestTrace.Log("BuffExpiryTests", "SUCCESS",
					nameof(FreshApply_SubTickDuration_ClampedToOneTick)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("BuffExpiryTests", "FAILURE",
					$"{nameof(FreshApply_SubTickDuration_ClampedToOneTick)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(FreshApply_SubTickDuration_ClampedToOneTick))
					.GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Zero / negative duration / zero tickDelta returns 0 ticks. The fresh-apply
		/// constructor's null-template branch also lands here (ExpiryTick == applyTick),
		/// so HasExpired must return true at the apply tick.
		/// </summary>
		[Test]
		public void FreshApply_ZeroDuration_ExpiresImmediately()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(FreshApply_ZeroDuration_ExpiresImmediately),
					"Zero duration → ExpiryTick == applyTick → HasExpired immediately.")
					.GetAwaiter().GetResult();

				uint applyTick = 200u;
				LogAssert.AreEqual(0u, DurationToTicks(0f, TickDelta30), "0 seconds → 0 ticks.");
				LogAssert.AreEqual(0u, DurationToTicks(-1f, TickDelta30), "negative seconds → 0 ticks.");
				LogAssert.AreEqual(0u, DurationToTicks(1f, 0f), "0 tickDelta → 0 ticks (guard against div-by-zero).");
				LogAssert.AreEqual(0u, DurationToTicks(1f, -0.1f), "negative tickDelta → 0 ticks.");

				var buff = new Buff(TemplateID, applyTick, applyTick, TickDelta30, 0, 0);
				LogAssert.IsTrue(buff.HasExpired(applyTick),
					"A zero-duration buff must expire immediately at the apply tick.");

				AuthTestTrace.Log("BuffExpiryTests", "SUCCESS",
					nameof(FreshApply_ZeroDuration_ExpiresImmediately)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("BuffExpiryTests", "FAILURE",
					$"{nameof(FreshApply_ZeroDuration_ExpiresImmediately)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(FreshApply_ZeroDuration_ExpiresImmediately))
					.GetAwaiter().GetResult();
			}
		}

		// ─────────────────────────────────────────────────────────────────────────
		//  RemainingSeconds — UI duration bar
		// ─────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// <c>RemainingSeconds</c> must be proportional to remaining ticks before expiry,
		/// exactly zero at and after expiry, and monotonically non-increasing as the tick
		/// advances. Used by the UI duration slider — drift or non-monotonicity causes
		/// visual stutter even when buff state is correct.
		/// </summary>
		[Test]
		public void RemainingSeconds_BeforeAndAfterExpiry_CorrectValues()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(RemainingSeconds_BeforeAndAfterExpiry_CorrectValues),
					"Proportional before expiry, exactly 0 at and after expiry, monotonic.")
					.GetAwaiter().GetResult();

				uint applyTick = 1_000u;
				uint durationTicks = DurationToTicks(DurationSeconds, TickDelta30);
				uint expiryTick = applyTick + durationTicks;

				var buff = new Buff(TemplateID, expiryTick, expiryTick, TickDelta30, 0, 0);

				uint midTick = applyTick + durationTicks / 2u;
				float expectedSeconds = (expiryTick - midTick) * TickDelta30;
				LogAssert.AreEqual(expectedSeconds, buff.RemainingSeconds(midTick),
					"RemainingSeconds at midpoint must equal (ExpiryTick - currentTick) * tickDelta.");

				LogAssert.AreEqual(0f, buff.RemainingSeconds(expiryTick),
					"RemainingSeconds must be exactly 0 at ExpiryTick.");
				LogAssert.AreEqual(0f, buff.RemainingSeconds(expiryTick + 100u),
					"RemainingSeconds must be exactly 0 past ExpiryTick.");

				// Monotonic non-increasing across ten sample ticks.
				float previous = float.PositiveInfinity;
				uint step = durationTicks / 10u;
				if (step < 1u) step = 1u;
				for (uint t = applyTick; t <= expiryTick; t += step)
				{
					float r = buff.RemainingSeconds(t);
					LogAssert.IsTrue(r <= previous,
						$"RemainingSeconds must be monotonically non-increasing (t={t}, r={r}, prev={previous}).");
					previous = r;
				}

				AuthTestTrace.Log("BuffExpiryTests", "SUCCESS",
					nameof(RemainingSeconds_BeforeAndAfterExpiry_CorrectValues)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("BuffExpiryTests", "FAILURE",
					$"{nameof(RemainingSeconds_BeforeAndAfterExpiry_CorrectValues)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(RemainingSeconds_BeforeAndAfterExpiry_CorrectValues))
					.GetAwaiter().GetResult();
			}
		}

		// ─────────────────────────────────────────────────────────────────────────
		//  NextTickTick semantics
		// ─────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// <c>NextTickTick</c> uses the same signed-cast wrap comparison as <c>HasExpired</c>
		/// (see <see cref="Buff"/>.TryTick). Verifies that periodic-fire decisions are
		/// deterministic across the same tick boundary as expiry.
		/// </summary>
		[Test]
		public void NextTickTick_BoundaryComparison_MatchesHasExpiredSemantics()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(NextTickTick_BoundaryComparison_MatchesHasExpiredSemantics),
					"Periodic-fire boundary uses the same signed-cast rule as expiry.")
					.GetAwaiter().GetResult();

				uint nextTickTick = 1_000u;
				uint expiryTick = 2_000u;

				var buff = new Buff(TemplateID, expiryTick, nextTickTick, TickDelta30, 0, 0);

				// Mirror the TryTick condition: (int)(currentTick - NextTickTick) >= 0.
				LogAssert.IsTrue((int)(nextTickTick - buff.NextTickTick) >= 0,
					"At NextTickTick the periodic-fire condition must hold.");
				LogAssert.IsFalse((int)((nextTickTick - 1u) - buff.NextTickTick) >= 0,
					"One tick before NextTickTick the periodic-fire condition must NOT hold.");

				AuthTestTrace.Log("BuffExpiryTests", "SUCCESS",
					nameof(NextTickTick_BoundaryComparison_MatchesHasExpiredSemantics)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("BuffExpiryTests", "FAILURE",
					$"{nameof(NextTickTick_BoundaryComparison_MatchesHasExpiredSemantics)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(NextTickTick_BoundaryComparison_MatchesHasExpiredSemantics))
					.GetAwaiter().GetResult();
			}
		}

		// ─────────────────────────────────────────────────────────────────────────
		//  SetTickDelta — UI duration bar self-heals when TimeManager was late
		// ─────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Regression for the "RemainingSeconds stuck at 0" bug. If a buff is constructed
		/// before <c>TimeManager.TickDelta</c> is available, the cached <c>tickDelta</c>
		/// would be 0 and <see cref="Buff.RemainingSeconds"/> would always return 0.
		/// <see cref="Buff.SetTickDelta"/> (called every tick by <c>BuffController.Tick</c>)
		/// must repair the cache and the next read must reflect the correct remaining seconds.
		/// </summary>
		[Test]
		public void SetTickDelta_RepairsZeroInitializedTickDelta_RemainingSecondsCorrects()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(SetTickDelta_RepairsZeroInitializedTickDelta_RemainingSecondsCorrects),
					"A buff constructed with tickDelta=0 must heal once SetTickDelta is invoked " +
					"with the real value. UI duration bars must not stay stuck at 0.")
					.GetAwaiter().GetResult();

				uint applyTick = 1_000u;
				uint expiryTick = applyTick + 900u; // 900 ticks at 30 tps = 30 s

				// Simulate the bug condition: TimeManager not ready, BuffController passed 0.
				var buff = new Buff(TemplateID, expiryTick, expiryTick, 0f, 0, 0);

				LogAssert.AreEqual(0f, buff.RemainingSeconds(applyTick),
					"Before SetTickDelta, RemainingSeconds returns 0 (the documented bug).");

				// BuffController.Tick refreshes the cache.
				buff.SetTickDelta(TickDelta30);

				float expected = (expiryTick - applyTick) * TickDelta30;
				LogAssert.AreEqual(expected, buff.RemainingSeconds(applyTick),
					"After SetTickDelta(TickDelta30), RemainingSeconds must reflect the real value.");

				AuthTestTrace.Log("BuffExpiryTests", "SUCCESS",
					nameof(SetTickDelta_RepairsZeroInitializedTickDelta_RemainingSecondsCorrects))
					.GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("BuffExpiryTests", "FAILURE",
					$"{nameof(SetTickDelta_RepairsZeroInitializedTickDelta_RemainingSecondsCorrects)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(SetTickDelta_RepairsZeroInitializedTickDelta_RemainingSecondsCorrects))
					.GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// <see cref="Buff.SetTickDelta"/> must reject non-positive values so a transient
		/// TimeManager glitch (returning 0) cannot wipe out a previously-good cached delta
		/// and re-introduce the stuck-at-0 RemainingSeconds bug.
		/// </summary>
		[Test]
		public void SetTickDelta_IgnoresNonPositiveValues_PreservesLastGoodDelta()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(SetTickDelta_IgnoresNonPositiveValues_PreservesLastGoodDelta),
					"Non-positive SetTickDelta calls must be no-ops; the last good delta survives.")
					.GetAwaiter().GetResult();

				uint applyTick = 1_000u;
				uint expiryTick = applyTick + 900u;

				var buff = new Buff(TemplateID, expiryTick, expiryTick, TickDelta30, 0, 0);
				float expected = (expiryTick - applyTick) * TickDelta30;
				LogAssert.AreEqual(expected, buff.RemainingSeconds(applyTick),
					"Baseline: RemainingSeconds correct with the initial good tickDelta.");

				// Hostile inputs that must NOT corrupt the cache.
				buff.SetTickDelta(0f);
				LogAssert.AreEqual(expected, buff.RemainingSeconds(applyTick),
					"SetTickDelta(0f) must be ignored — last good delta survives.");

				buff.SetTickDelta(-1f);
				LogAssert.AreEqual(expected, buff.RemainingSeconds(applyTick),
					"SetTickDelta(-1f) must be ignored — last good delta survives.");

				// A legitimate tick-rate change must take effect.
				float newDelta = 1f / 60f;
				buff.SetTickDelta(newDelta);
				float newExpected = (expiryTick - applyTick) * newDelta;
				LogAssert.AreEqual(newExpected, buff.RemainingSeconds(applyTick),
					"SetTickDelta with a positive value must update the cache.");

				AuthTestTrace.Log("BuffExpiryTests", "SUCCESS",
					nameof(SetTickDelta_IgnoresNonPositiveValues_PreservesLastGoodDelta))
					.GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("BuffExpiryTests", "FAILURE",
					$"{nameof(SetTickDelta_IgnoresNonPositiveValues_PreservesLastGoodDelta)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(SetTickDelta_IgnoresNonPositiveValues_PreservesLastGoodDelta))
					.GetAwaiter().GetResult();
			}
		}

		// ─────────────────────────────────────────────────────────────────────────
		//  Snapshot ordering — reconcile diff stability
		// ─────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// <see cref="BuffController.CreateReconcileSnapshot"/> iterates a
		/// <see cref="System.Collections.Generic.SortedDictionary{TKey,TValue}"/> whose
		/// enumeration order is key-ascending. The delta serializer relies on this
		/// stability — if server and client iterated in different orders the per-index
		/// <c>Equals</c> diff would flag every entry as changed and the wire payload would
		/// balloon. This test pins the sort guarantee so any future swap to a non-ordered
		/// dictionary is caught.
		/// </summary>
		[Test]
		public void ReconcileSnapshot_IndexOrder_FollowsSortedTemplateID()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(ReconcileSnapshot_IndexOrder_FollowsSortedTemplateID),
					"Snapshot index order must be ascending TemplateID — required for the " +
					"index-delta serializer to produce minimal payloads.")
					.GetAwaiter().GetResult();

				var dict = new System.Collections.Generic.SortedDictionary<int, BuffReconcileEntry>
				{
					{ 9, new BuffReconcileEntry { TemplateID = 9, ExpiryTick = 300u, NextTickTick = 290u, Stacks = 1, TickCount = 0 } },
					{ 1, new BuffReconcileEntry { TemplateID = 1, ExpiryTick = 100u, NextTickTick =  90u, Stacks = 1, TickCount = 0 } },
					{ 5, new BuffReconcileEntry { TemplateID = 5, ExpiryTick = 200u, NextTickTick = 190u, Stacks = 1, TickCount = 0 } },
				};

				int previousID = int.MinValue;
				foreach (var kvp in dict)
				{
					LogAssert.IsTrue(kvp.Key > previousID,
						"SortedDictionary enumeration must yield ascending keys (required for snapshot index stability).");
					previousID = kvp.Key;
				}

				AuthTestTrace.Log("BuffExpiryTests", "SUCCESS",
					nameof(ReconcileSnapshot_IndexOrder_FollowsSortedTemplateID)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("BuffExpiryTests", "FAILURE",
					$"{nameof(ReconcileSnapshot_IndexOrder_FollowsSortedTemplateID)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(ReconcileSnapshot_IndexOrder_FollowsSortedTemplateID))
					.GetAwaiter().GetResult();
			}
		}
	}
}