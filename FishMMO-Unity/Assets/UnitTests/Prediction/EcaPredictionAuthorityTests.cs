using System.IO;
using System.Reflection;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the split between "may change authoritative state" and "may apply an effect for
	/// feedback", and the properties that make the second one safe.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every player-visible effect used to be gated on <see cref="EcaAuthority.IsServer(EventData)"/>,
	/// so a player's own hit produced nothing locally until the server's report arrived — hit,
	/// pause, then the world reacts. <c>MayPredict</c> widens that for the peer owning the
	/// initiator only.
	/// </para>
	/// <para>
	/// The reflection tests below are the load-bearing ones. Prediction is only safe because the
	/// authoritative consequences self-gate a level down in the controllers; if any of those guards
	/// is removed, a predicted hit starts killing characters on clients and these fail.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class EcaPredictionAuthorityTests
	{
		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"Expected source at {path}.");
			return File.ReadAllText(path);
		}

		// ── The predicate ────────────────────────────────────────────────────────────

		/// <summary>
		/// With nothing networked to judge, both gates allow — the documented undecidable case.
		/// </summary>
		/// <remarks>
		/// A scene-authored trigger or an edit-mode test has no peer to be wrong about. Refusing
		/// here would silently disable every scene trigger in the project.
		/// </remarks>
		[Test]
		public void MayPredict_AllowsWhenNothingIsNetworked()
		{
			LogAssert.IsTrue(EcaAuthority.MayPredict(null, null),
				"An event with no networked identity must be allowed, matching IsServer's fallback.");
			LogAssert.IsTrue(EcaAuthority.IsServer(null, null),
				"Sanity: IsServer has the same fallback, so MayPredict is a widening of it and never a narrowing.");
		}

		/// <summary>
		/// <c>MayPredict</c> must never be narrower than <c>IsServer</c>.
		/// </summary>
		/// <remarks>
		/// It is a widening by construction — it returns true whenever IsServer does, then adds the
		/// owning client. An implementation that stopped delegating would silently disable effects
		/// on the server, which is the failure that produced "the ability does nothing" before
		/// EcaAuthority existed.
		/// </remarks>
		[Test]
		public void MayPredict_DelegatesToIsServerFirst()
		{
			string source = ReadSource("Assets/Scripts/Shared/Core/Entity/ECA/Core/EcaAuthority.cs");

			int mayPredict = source.IndexOf("public static bool MayPredict(ICharacter initiator, EventData eventData)");
			LogAssert.IsTrue(mayPredict > 0, "MayPredict(ICharacter, EventData) must exist.");

			string body = source.Substring(mayPredict);
			int end = body.IndexOf("public static bool MayPredict(EventData");
			body = end > 0 ? body.Substring(0, end) : body;

			LogAssert.IsTrue(body.Contains("IsServer(initiator, eventData)"),
				"MayPredict must return true whenever IsServer does. It is a widening, not a replacement.");
			LogAssert.IsTrue(body.Contains("IsOwner"),
				"The client branch must be keyed on ownership of the INITIATOR. Allowing any client to " +
				"predict would let an observer invent effects for a cast it has no input for.");
		}

		// ── What makes prediction safe ───────────────────────────────────────────────

		/// <summary>
		/// Death must stay server-only, whatever the caller.
		/// </summary>
		/// <remarks>
		/// This is the guard that lets <c>ApplyDamageAction</c> predict at all. <c>Damage()</c> ends
		/// in <c>Kill()</c> when the resource reaches zero, so without this a predicted hit would
		/// predict a death — and a corpse that stands back up is far worse than one that arrives an
		/// RTT late. Death was deliberately excluded from prediction.
		/// </remarks>
		[Test]
		public void Kill_IsServerOnly()
		{
			string source = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/CharacterAttribute/CharacterDamageController.cs");

			int kill = source.IndexOf("public void Kill(ICharacter killer)");
			LogAssert.IsTrue(kill > 0, "CharacterDamageController.Kill must exist.");

			string body = source.Substring(kill, 400);
			LogAssert.IsTrue(body.Contains("IsServerStarted"),
				"Kill must refuse to run off the server. ApplyDamageAction now predicts on the owning " +
				"client, and Damage() calls Kill when the resource hits zero — this guard is the only " +
				"thing stopping a client from predicting a death.");
		}

		/// <summary>
		/// The combat report and loot-rights bookkeeping must stay server-only.
		/// </summary>
		/// <remarks>
		/// A predicted hit must not emit a combat report (every observer would draw a number the
		/// server never agreed to) nor record a contribution (loot rights are authoritative).
		/// </remarks>
		[Test]
		public void CombatReportAndContribution_AreServerOnly()
		{
			string source = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/CharacterAttribute/CharacterDamageController.cs");

			foreach (string method in new[] { "QueueCombatEvent", "RecordCombatContribution" })
			{
				int index = source.IndexOf(method + "(ICharacter");
				LogAssert.IsTrue(index > 0, $"CharacterDamageController.{method} must exist.");
				LogAssert.IsTrue(source.Substring(index, 400).Contains("IsServerStarted"),
					$"{method} must be server-only; a predicted hit would otherwise broadcast or " +
					"award something the server never agreed to.");
			}
		}

		// ── Which actions predict ────────────────────────────────────────────────────

		/// <summary>Actions whose effect the caster can see must predict.</summary>
		[Test]
		public void FeedbackActions_UseMayPredict()
		{
			foreach (string action in new[] { "ApplyDamageAction", "ApplyHealAction", "ConsumeResourceAction" })
			{
				string source = ReadSource(
					$"Assets/Scripts/Shared/Implementation/Entity/ECA/Actions/Character/{action}.cs");

				LogAssert.IsTrue(source.Contains("EcaAuthority.MayPredict"),
					$"{action} produces feedback the caster should see immediately and must gate on MayPredict.");

				/* The AUTHORITY gate — the early return at the top — is what must have widened. A
				 * bare mention of IsServer elsewhere is fine and expected: these actions ask it a
				 * second, different question ("am I the peer that should DRAW this number?"), since
				 * the server has no display and its report is what every other client draws from.
				 * Matching on the early-return shape tests the gate rather than the vocabulary. */
				LogAssert.IsFalse(
					source.Contains("if (!EcaAuthority.IsServer(initiator, eventData))\n\t\t\t{\n\t\t\t\treturn;"),
					$"{action} must not still refuse to run off the server. That early return is the " +
					"gate this change widened; leaving it would make MayPredict decorative.");
			}
		}

		/// <summary>
		/// Actions with no player-visible effect must stay server-only.
		/// </summary>
		/// <remarks>
		/// Threat and taunt move NPC AI state and revive is deliberate and rare. Predicting them
		/// buys no feel and adds a way to be wrong, so they keep the narrow gate. This test exists
		/// to stop a sweeping find-and-replace widening them along with the rest.
		/// </remarks>
		[Test]
		public void NonVisibleActions_StayServerOnly()
		{
			foreach (string action in new[] { "ApplyThreatAction", "ApplyTauntAction", "ApplyReviveAction" })
			{
				string source = ReadSource(
					$"Assets/Scripts/Shared/Implementation/Entity/ECA/Actions/Character/{action}.cs");

				LogAssert.IsTrue(source.Contains("EcaAuthority.IsServer"),
					$"{action} has no player-visible effect and must keep the authoritative gate.");
				LogAssert.IsFalse(source.Contains("MayPredict"),
					$"{action} must not predict. Widening it buys no feedback and adds a way to diverge.");
			}
		}

		// ── Hit dispatch ─────────────────────────────────────────────────────────────

		/// <summary>
		/// An ability object's hit events must dispatch on every peer, not only the server.
		/// </summary>
		/// <remarks>
		/// The server-only dispatch broke two things at once and they had the same cause: an impact
		/// VFX authored on an OnHit trigger instantiated on the headless server and was seen by
		/// nobody, and the caster could not predict its own hit, so widening ApplyDamageAction to
		/// MayPredict had no effect on the projectile path at all. An AbilityObject is a local,
		/// deterministic object rather than a networked one, so every peer can resolve its own hit;
		/// what each action then does about it is the action's own gate.
		/// </remarks>
		[Test]
		public void AbilityObject_DispatchesHitEventsOnEveryPeer()
		{
			string source = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityObject.cs");

			LogAssert.IsFalse(source.Contains("if (isServer && Caster != null && Caster.IsSpawned)"),
				"OnHit must not be gated on isServer. That gate made authored impact effects invisible " +
				"to every player and stopped the caster predicting its own hit.");
			LogAssert.IsTrue(source.Contains("if (Caster != null && Caster.IsSpawned)"),
				"The dispatch must still require a live caster — a despawned one has nothing to attribute " +
				"the hit to and Trigger.Execute rejects a null initiator.");
		}

		/// <summary>
		/// Purely presentational actions must run on clients and not on the dedicated server.
		/// </summary>
		/// <remarks>
		/// Now that OnHit dispatches everywhere, an ungated visual action would allocate a particle
		/// prefab on a server with no screen. The client gate is the mirror of the authority gate and
		/// must never be used in its place.
		/// </remarks>
		[Test]
		public void VisualActions_AreGatedToClients()
		{
			string source = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/ECA/Actions/PlayFXAction.cs");

			LogAssert.IsTrue(source.Contains("IsClientPeer(initiator, eventData)"),
				"PlayFXAction is presentational and must run only where there is a screen.");
			LogAssert.IsFalse(source.Contains("EcaAuthority.IsServer"),
				"A visual must not carry an authority gate — that is what made it server-only and " +
				"therefore invisible in the first place.");
		}

		// ── The ability set merge ────────────────────────────────────────────────────

		/// <summary>
		/// There must be exactly one ability container.
		/// </summary>
		/// <remarks>
		/// A local client is required to hold an observed character's real state — Inspect and
		/// faction/aggro evaluation read it, not just the renderer — so the observer-only parallel
		/// dictionary was the wrong shape. The invariant it protected (a forged learn message must
		/// never reach the set an activation is gated on) is preserved by refusing the message on
		/// our own character instead.
		/// </remarks>
		[Test]
		public void AbilityKnowledge_HasNoParallelObservedSet()
		{
			string source = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityController.Knowledge.cs");

			LogAssert.IsFalse(source.Contains("observedAbilities"),
				"observedAbilities must be gone; observer-learned abilities belong in KnownAbilities.");

			int register = source.IndexOf("public void RegisterObservedAbility");
			LogAssert.IsTrue(register > 0, "RegisterObservedAbility must exist.");

			string body = source.Substring(register, 1200);
			LogAssert.IsTrue(body.Contains("KnownAbilities["),
				"An observer-learned ability must be filed into KnownAbilities.");
			LogAssert.IsTrue(body.Contains("base.IsOwner"),
				"It must refuse on our own character. The owner learns through the authoritative " +
				"KnownAbilityAdd family, so this message only ever describes somebody else — and that " +
				"refusal is what keeps a forged learn out of the dictionary CanActivate gates on.");
		}
	}
}
