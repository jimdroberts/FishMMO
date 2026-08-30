using System.IO;
using System.Text.RegularExpressions;
using FishMMO.Shared;
using FishNet.Serializing;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Regressions for the defects the third audit found and this pass fixed.
	/// </summary>
	/// <remarks>
	/// Each covers a case whose failure produced a plausible wrong outcome rather than an error:
	/// a charge that cancels itself early, a stun that reads as rubber-banding, an enemy that
	/// stops reporting its damage, and a query buffer shared between two casts.
	/// </remarks>
	[TestFixture]
	public class AuditFollowUpFixTests
	{
		#region F1 — the charged hold counter survives a reconcile.

		/// <summary>
		/// <c>ChargedHoldTicks</c> round-trips through both wire forms.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The counter is incremented once per invocation of the replicate body, and a reconcile
		/// replays every tick since the correction. Without an authoritative value the owner counts
		/// each replayed tick a second time and reaches the hold ceiling in a fraction of the real
		/// time — the charge drops on the player's screen and the ability then fires by itself when
		/// the server's own counter finally expires.
		/// </para>
		/// <para>
		/// Both forms are asserted because they are written by different code: the delta carries the
		/// field only when it changed, and the absolute snapshot always carries it. A field added to
		/// one and not the other misaligns every field after it in that form only, which is the
		/// failure mode the framing exists to contain rather than prevent.
		/// </para>
		/// </remarks>
		[Test]
		public void ChargedHoldTicks_RoundTripsThroughBothWireForms()
		{
			CharacterReconcileData previous = default;
			CharacterReconcileData next = default;
			next.ChargedHoldTicks = 17u;
			next.Sequence = 1;

			// Absolute form.
			Writer absoluteWriter = new Writer();
			absoluteWriter.WriteCharacterReconcileData(next);
			Reader absoluteReader = new Reader(absoluteWriter.GetArraySegment(), null);
			CharacterReconcileData fromAbsolute = absoluteReader.ReadCharacterReconcileData();
			LogAssert.AreEqual(17u, fromAbsolute.ChargedHoldTicks,
				"The absolute snapshot must carry the charged-hold counter. It is the form a peer " +
				"with no baseline decodes, and the periodic resync.");

			// Delta form.
			Writer deltaWriter = new Writer();
			deltaWriter.WriteDelta(previous, next, DeltaSerializerOption.RootSerialize);
			Reader deltaReader = new Reader(deltaWriter.GetArraySegment(), null);
			CharacterReconcileData fromDelta = deltaReader.ReadDelta(previous);
			LogAssert.AreEqual(17u, fromDelta.ChargedHoldTicks,
				"The delta must carry the counter when it moved, or the owner's copy is never corrected.");
		}

		/// <summary>
		/// A delta that does not move the counter leaves the reader's value alone.
		/// </summary>
		/// <remarks>
		/// The delta writer omits an unchanged field, and the reader has to carry the previous value
		/// forward rather than zeroing it. Zeroing would reset the hold on every tick the counter
		/// happened not to move, which is most of them.
		/// </remarks>
		[Test]
		public void ChargedHoldTicks_SurvivesADeltaThatDoesNotMoveIt()
		{
			CharacterReconcileData previous = default;
			previous.ChargedHoldTicks = 9u;
			previous.Sequence = 4;

			CharacterReconcileData next = previous;
			next.Sequence = 5;
			next.RemainingTicks = 3u;

			Writer writer = new Writer();
			writer.WriteDelta(previous, next, DeltaSerializerOption.RootSerialize);
			Reader reader = new Reader(writer.GetArraySegment(), null);
			CharacterReconcileData read = reader.ReadDelta(previous);

			LogAssert.AreEqual(9u, read.ChargedHoldTicks,
				"An unchanged counter must survive the delta, not be reset to zero.");
			LogAssert.AreEqual(3u, read.RemainingTicks, "And the field that did move must land.");
		}

		#endregion

		#region F2 — crowd control is predicted, death is not gated on a stale flag.

		/// <summary>
		/// The owner applies the crowd-control half of the movement gate.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Asserted on the source, because reproducing it needs a spawned NetworkObject with an
		/// owning connection, a motor and a live buff simulation — none of which an EditMode test
		/// can build. The same approach <c>PositionConditionTests</c> takes for the rewind rule.
		/// </para>
		/// <para>
		/// The whole gate used to sit behind <c>IsServerStarted</c>, so a stunned owner carried on
		/// predicting movement and the reconcile snapped it back on every tick. The correction was
		/// real; what the player saw was rubber-banding rather than a stun.
		/// </para>
		/// </remarks>
		[Test]
		public void MovementGate_PredictsCrowdControl_AndDoesNotGateDeathOnTheFlag()
		{
			string source = ReadSource("Assets/Scripts/Shared/Implementation/Entity/Prediction/KCC/KCCPlayer.cs");

			// The incapacitation test must not sit inside an IsServerStarted branch.
			Match gate = Regex.Match(source,
				@"if \(CharacterIncapacitation\.IsIncapacitated\(character\) \|\|\s*IsHealthDepleted\(character\)\)");
			LogAssert.IsTrue(gate.Success,
				"The owner must evaluate crowd control and death itself. Those come from the predicted " +
				"buff simulation and from reconciled resource state, so both peers reach the same answer " +
				"on the same tick.");

			/* Matched on the CALL, not the identifier: the remarks above the gate name the flag in
			 * order to say why it is not used, and a bare name match reads that as the defect. */
			LogAssert.IsFalse(Regex.IsMatch(source, @"IsFlagged\(\s*CharacterFlags\.IsDead\s*\)"),
				"Death must NOT be gated on CharacterFlags.IsDead here. Flags ride the spawn payload and " +
				"are never re-synced, so a client's copy is stale from its first death onward — gating on " +
				"it would freeze the owner permanently after one death.");

			// The server-only half must still exist for the predicates a client cannot evaluate.
			LogAssert.IsTrue(source.Contains("character.IsTeleporting"),
				"IsTeleporting is server bookkeeping and must stay in the server-only half.");
			LogAssert.IsTrue(Regex.IsMatch(source, @"base\.IsServerStarted &&\s*\(character\.IsTeleporting"),
				"...inside an IsServerStarted branch, because a client holds IsLoaded as true for its " +
				"whole session and would gate movement on a value that never changes there.");
		}

		#endregion

		#region F4 — the characters you are fighting cannot be evicted.

		/// <summary>
		/// The visibility budget pins characters in mutual combat with the viewer.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>ObserverBudgetCondition</c> despawns, it does not merely slow down — so an evicted
		/// enemy takes its combat reports with it, because <c>FlushCombatEvents</c> broadcasts to the
		/// VICTIM's observers. The player sees no damage numbers, and their own predicted numbers
		/// grey out as unconfirmed after a second, while every hit is landing.
		/// </para>
		/// <para>
		/// Pins previously covered party members and the viewer's current target only, so an enemy
		/// that was neither — one of several in a pull, or anything attacking you that you have not
		/// clicked — could be evicted mid-fight.
		/// </para>
		/// </remarks>
		[Test]
		public void VisibilityBudget_PinsCharactersTheViewerIsFighting()
		{
			string source = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/ObserverStreaming/ObserverStreamingRegistry.cs");

			LogAssert.IsTrue(source.Contains("engagedWith"),
				"The pin rule must include characters the viewer is actually fighting.");

			LogAssert.IsTrue(source.Contains("observed.IsFoughtBy(viewer.Character.ID)"),
				"One direction: the viewer has damaged the observed character.");
			LogAssert.IsTrue(source.Contains("viewer.IsFoughtBy(observed.Character.ID)"),
				"And the other: the observed character has damaged the viewer. Either one is a fight, " +
				"and an enemy attacking a player who has not hit back yet is the case that matters most.");

			LogAssert.IsTrue(Regex.IsMatch(source, @"engagedWith\s*=\s*observed\.InCombat"),
				"Gated on the observed character being in combat, so a stale contributor entry cannot " +
				"pin something the fight has long since left.");
		}

		/// <summary>
		/// The contributor query used by the pin does not consume the loot-rights ledger.
		/// </summary>
		/// <remarks>
		/// The only pre-existing way to read contributors was <c>TryConsumeContributors</c>, which
		/// hands the list out and clears it — it is how loot rights are awarded on death. Calling it
		/// once per viewer per scheduling pass would have handed out loot rights several times a
		/// second and left every real death with none.
		/// </remarks>
		[Test]
		public void CombatContributorQuery_IsAPeekAndNotAConsume()
		{
			string source = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/CharacterAttribute/CharacterDamageController.cs");

			Match method = Regex.Match(source,
				@"public bool HasCombatContributor\(long characterID\)\s*\{(?<body>[^}]*)\}");
			LogAssert.IsTrue(method.Success, "HasCombatContributor must exist.");

			string body = method.Groups["body"].Value;
			LogAssert.IsFalse(body.Contains("Clear") || body.Contains("Remove"),
				"The query must not mutate the contributor ledger. It is read once per viewer per " +
				"observer pass, and the same ledger awards loot rights exactly once, on death.");
		}

		#endregion

		#region F6 — a selector's query buffer cannot be shared between two casts.

		/// <summary>
		/// No spatial selector holds its query buffer in a field.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Selectors are serialized inline on shared assets through <c>[SerializeReference]</c>, so
		/// one instance serves every character that casts the ability. A candidate's authored
		/// conditions can fire nested triggers that reach that same instance again, and the
		/// re-entrant gather re-ran the query into the shared array while the outer loop was still
		/// walking it — so the outer cast resolved against another cast's colliders.
		/// </para>
		/// <para>
		/// The scratch LISTS were made local for exactly this reason and the buffer was missed,
		/// which is why this is asserted for the whole set rather than for the one selector that
		/// prompted it.
		/// </para>
		/// </remarks>
		[Test]
		public void SpatialSelectors_DoNotShareAQueryBufferBetweenCasts()
		{
			string[] selectors =
			{
				"AreaTargetSelector.cs",
				"ConeTargetSelector.cs",
				"ChainTargetSelector.cs",
				"RandomTargetSelector.cs",
				"NearestTargetSelector.cs",
				"FurthestTargetSelector.cs",
				"LineTargetSelector.cs",
			};

			foreach (string selector in selectors)
			{
				string source = ReadSource(
					"Assets/Scripts/Shared/Implementation/Entity/ECA/Target/" + selector);

				LogAssert.IsFalse(
					Regex.IsMatch(source, @"private\s+(Collider|RaycastHit)\[\]\s+hits\s*;"),
					$"{selector} holds its query buffer in a field. A nested trigger reaching the same " +
					"serialized instance re-runs the query into it while the outer gather is still " +
					"walking it. Allocate it inside the gather instead.");
			}
		}

		#endregion

		// ── Helpers ──────────────────────────────────────────────────────────────────

		private static string ReadSource(string projectRelativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), projectRelativePath);
			LogAssert.IsTrue(File.Exists(path), $"Source not found at {path}.");
			return File.ReadAllText(path);
		}
	}
}
