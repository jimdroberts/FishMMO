using FishNet.Object;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// The single answer to "may this ECA step change authoritative state, here, on this peer?".
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why a helper and not a field test.</b> Every ECA action used to decide this for itself, and
	/// the two things they reached for were both wrong. <c>#if UNITY_SERVER</c> is a build-target
	/// define, so it is undefined in the editor the scene server is developed in. Gating on
	/// <c>TickEventData.IsReplicateTick</c> is worse, because the <i>server's own</i> ability spawn
	/// and self-target dispatches carry replicate ticks — so a guard meant to suppress a client
	/// suppressed the server as well and the effect never happened anywhere. Both mistakes are
	/// invisible in a build and only show up as "the ability does nothing".
	/// </para>
	/// <para>
	/// <b>What it asks.</b> The identities the event carries, in the order they are most likely to be
	/// networked: the explicit initiator, the event's initiator, then the event's target character.
	/// The first one with a live <see cref="NetworkObject"/> decides, by way of
	/// <see cref="NetworkObject.IsServerInitialized"/> — the same question
	/// <c>BaseAction.IsServer</c> asks, so a client-hosted process answers for the peer the
	/// character actually belongs to rather than for the process.
	/// </para>
	/// <para>
	/// <b>What it does when nothing is networked.</b> It allows. A trigger fired by a scene object,
	/// an edit-mode test, or a character that has not spawned yet has no peer to be wrong about;
	/// refusing there would silently disable every scene-authored trigger in the project. On a real
	/// client the characters involved always carry a NetworkObject, and that object reports
	/// <c>IsServerInitialized == false</c>, so the case this gate exists for is always decided by
	/// evidence rather than by the fallback.
	/// </para>
	/// </remarks>
	public static class EcaAuthority
	{
		/// <summary>What the event was able to prove about the peer it is executing on.</summary>
		public enum PeerEvidence
		{
			/// <summary>Nothing networked was reachable from the event.</summary>
			None = 0,
			/// <summary>A networked identity reported the local peer's server is initialized.</summary>
			Server = 1,
			/// <summary>A networked identity reported the local peer is not a server.</summary>
			Client = 2,
		}

		/// <summary>
		/// The gate's decision, as a pure function of the evidence. Split out so the rule can be
		/// tested without a NetworkManager, and so every caller shares one interpretation of
		/// "undecidable".
		/// </summary>
		/// <param name="evidence">What the event proved about the executing peer.</param>
		/// <returns>True when authoritative mutation is permitted.</returns>
		public static bool Allows(PeerEvidence evidence) => evidence != PeerEvidence.Client;

		/// <summary>
		/// Resolves what the event can prove about the peer it is running on.
		/// </summary>
		/// <param name="initiator">The action's explicit initiator, or null.</param>
		/// <param name="eventData">The event being executed, or null.</param>
		/// <returns>The strongest evidence available.</returns>
		public static PeerEvidence Evidence(ICharacter initiator, EventData eventData)
		{
			PeerEvidence evidence = FromCharacter(initiator);
			if (evidence != PeerEvidence.None)
			{
				return evidence;
			}

			if (eventData == null)
			{
				return PeerEvidence.None;
			}

			evidence = FromCharacter(eventData.Initiator);
			if (evidence != PeerEvidence.None)
			{
				return evidence;
			}

			return FromCharacter(eventData.TargetCharacter);
		}

		/// <summary>
		/// True when this peer may mutate authoritative state for the supplied event.
		/// </summary>
		/// <param name="initiator">The action's explicit initiator, or null.</param>
		/// <param name="eventData">The event being executed, or null.</param>
		public static bool IsServer(ICharacter initiator, EventData eventData)
			=> Allows(Evidence(initiator, eventData));

		/// <summary>
		/// True when this peer may mutate authoritative state for the supplied event.
		/// </summary>
		/// <param name="eventData">The event being executed, or null.</param>
		public static bool IsServer(EventData eventData) => IsServer(null, eventData);

		/// <summary>
		/// True when this peer may apply an effect for FEEDBACK, ahead of the server agreeing.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Why this is separate from <see cref="IsServer(ICharacter, EventData)"/>.</b> That gate
		/// asks "may I change authoritative state", and its answer has to stay no on a client. But
		/// using it for everything meant a player's own hit produced nothing locally until the
		/// server's report arrived — hit, pause, then the world reacts. In a predicted client that
		/// is not acceptable, and it is not what the gate was protecting against: the risk was a
		/// client inventing effects for characters it has no business simulating, not a caster
		/// showing the result of its own cast.
		/// </para>
		/// <para>
		/// <b>Only the initiator's owner predicts.</b> The server always may. A client may only when
		/// it owns the character that CAUSED the effect — it has that character's input, it
		/// predicted the cast, and it is the only peer with anything to predict from. A mere
		/// observer answers false: it has no input stream for the caster, holds every peer
		/// interpolated, and the server is going to tell it what happened anyway.
		/// </para>
		/// <para>
		/// <b>What makes this safe.</b> The authoritative consequences of a predicted effect are
		/// already gated one level down, inside the controllers: <c>CharacterDamageController.Kill</c>,
		/// <c>QueueCombatEvent</c> and <c>RecordCombatContribution</c> each return early unless
		/// <c>IsServerStarted</c>. So a predicted hit moves a bar and raises local events; it cannot
		/// kill anybody, award loot rights, or emit a combat report. Death in particular stays
		/// server-only and unpredicted by deliberate decision — a corpse that stands back up is far
		/// worse than one that arrives an RTT late.
		/// </para>
		/// <para>
		/// An action whose effect is not player-visible — threat, taunt, revive — has nothing to gain
		/// here and must keep using <see cref="IsServer(ICharacter, EventData)"/>. Predicting those
		/// buys no feel and adds a way to be wrong.
		/// </para>
		/// </remarks>
		/// <param name="initiator">The action's explicit initiator, or null.</param>
		/// <param name="eventData">The event being executed, or null.</param>
		public static bool MayPredict(ICharacter initiator, EventData eventData)
		{
			// The server is always allowed; this is a widening of IsServer, never a replacement.
			if (IsServer(initiator, eventData))
			{
				return true;
			}

			ICharacter cause = initiator ?? eventData?.Initiator;
			if (cause == null)
			{
				/* No initiator to own. Matches the undecidable case in Allows: a scene-authored
				 * trigger or an edit-mode test has no peer to be wrong about. */
				return true;
			}

			NetworkObject networkObject = cause.NetworkObject;
			if (networkObject == null)
			{
				return true;
			}

			/* IsOwner, not "is a client". An observer watching somebody else's fireball must not
			 * apply its effects locally — it would be guessing, and the server's broadcast is
			 * already on its way. */
			return networkObject.IsOwner;
		}

		/// <summary>
		/// True when this peer may apply an effect for feedback. See
		/// <see cref="MayPredict(ICharacter, EventData)"/>.
		/// </summary>
		/// <param name="eventData">The event being executed, or null.</param>
		public static bool MayPredict(EventData eventData) => MayPredict(null, eventData);

		/// <summary>Reads one character's networked view of the local peer.</summary>
		private static PeerEvidence FromCharacter(ICharacter character)
		{
			if (character == null)
			{
				return PeerEvidence.None;
			}

			NetworkObject networkObject = character.NetworkObject;
			if (networkObject == null)
			{
				return PeerEvidence.None;
			}

			return networkObject.IsServerInitialized ? PeerEvidence.Server : PeerEvidence.Client;
		}
	}
}
