using System;
using FishNet.Broadcast;
using FishNet.Connection;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Opt-in interface for actions that can meaningfully fail at runtime (e.g. insufficient
	/// resources, missing controllers, validation rejection). When combined with
	/// <see cref="BaseAction.StopChainOnFailure"/>, returning <c>false</c> aborts the rest of the
	/// action chain for the current event. Most actions don't need this — their <see cref="IAction.Execute"/>
	/// implementation simply runs and returns; only implement <see cref="IAbortableAction"/> when
	/// there is a genuine fail-and-bail outcome.
	/// </summary>
	public interface IAbortableAction
	{
		/// <summary>
		/// Attempts to perform the action. Returns false to signal that the action could not be
		/// performed (e.g. preconditions failed) so callers can short-circuit chains.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">Event data for the action.</param>
		/// <returns>True on success; false when the action could not be performed.</returns>
		bool TryExecute(ICharacter initiator, EventData eventData);
	}

	/// <summary>
	/// Abstract base class for all ECA actions. Serialized inline via [SerializeReference] on Trigger assets.
	/// Derive from this class and add [Serializable] to create concrete actions.
	/// </summary>
	[Serializable]
	public abstract class BaseAction : IAction
	{
		/// <summary>
		/// Optional selector that picks one or more targets for this action. When set, the
		/// action runs once per selected target. When unset, the action runs once against the
		/// current event data (reading <see cref="EventData.TargetCharacter"/> or falling back
		/// to the initiator).
		/// </summary>
		[Tooltip("Optional selector for this action. When unset the action runs once against the current event target.")]
		[SerializeReference, SubclassSelector]
		public TargetSelector TargetSelector;

		/// <summary>
		/// When true and this action implements <see cref="IAbortableAction"/>, returning
		/// <c>false</c> from <see cref="IAbortableAction.TryExecute"/> aborts the remainder of the
		/// current action list (e.g. stop applying damage after a resource consume fails).
		/// Has no effect on actions that do not implement <see cref="IAbortableAction"/>.
		/// </summary>
		[Tooltip("If this action implements IAbortableAction, abort the rest of the action chain when TryExecute returns false.")]
		public bool StopChainOnFailure;

		/// <summary>
		/// True when this action's ENTIRE effect is something rendered — particles, sound, floating
		/// text, camera shake — and it changes no state that any other peer can observe.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>What reads it.</b> <see cref="TriggerExecution"/>'s selector fan-out. A spatial selector
		/// correctly declines to resolve on a third-party OBSERVER
		/// (<c>TargetSelector.ResolvesTargetsLocally</c>), and the fan-out runs an action once per
		/// selected target — so an empty selection ran the whole action list zero times and an authored
		/// OnHit event with a Chain, Area, Line or Cone selector produced NOTHING on any screen but the
		/// caster's. Observers saw damage numbers pop over three characters with no arc, impact or sound
		/// linking them. This flag is how the fan-out knows which of those actions it may still run
		/// once, against the event's own scope, on a peer that is not allowed to select for itself.
		/// </para>
		/// <para>
		/// <b>The bar is deliberately high, and it is not "harmless".</b> Say true only when the action
		/// would be correct to run on a peer that has resolved no targets and has no authority for the
		/// event. Anything that moves a resource, installs a buff, draws a PREDICTED number, spends a
		/// hit count, grants an item or touches persistence must stay false — those belong to the peer
		/// that resolved the hit, and an observer running them is the failure the selector gate exists
		/// to prevent. A type list in the fan-out was the alternative and is worse: it puts the
		/// classification somewhere the action's author never looks.
		/// </para>
		/// <para>
		/// Exactly one action answers true today (<c>PlayFXAction</c>), which is also the only action in
		/// the project that gates on <see cref="IsClientPeer"/> — the two questions are close relatives:
		/// this one says "I am only a picture", that one says "and only a screen needs it".
		/// </para>
		/// </remarks>
		public virtual bool IsPresentation => false;

		/// <summary>
		/// Executes the action. Must be implemented by derived classes.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">Event data for the action.</param>
		public abstract void Execute(ICharacter initiator, EventData eventData);

		/// <summary>
		/// True when this action is running on a peer that is actually hosting a server.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The runtime replacement for <c>#if UNITY_SERVER</c>, which every server-only action body
		/// used to be wrapped in. <c>UNITY_SERVER</c> is a build-target define, so it is undefined
		/// in the editor — and the scene server runs from the editor. Every one of those bodies
		/// therefore compiled to nothing there, which meant a bindstone bound nobody, a merchant
		/// opened no shop, and a teleporter moved no one, in the configuration the project is
		/// developed in. It is the same compile-time gate already found and removed from
		/// <c>NPC.OnStartServer</c> and from scene object registration.
		/// </para>
		/// <para>
		/// Asks the initiator's own <see cref="FishNet.Object.NetworkObject"/> rather than a global
		/// singleton, so a client-hosted process answers for the peer the character belongs to.
		/// </para>
		/// </remarks>
		/// <param name="initiator">The character the action is running for.</param>
		/// <returns>True when the local peer's server is initialized.</returns>
		protected static bool IsServer(ICharacter initiator)
		{
			return initiator != null &&
				   initiator.NetworkObject != null &&
				   initiator.NetworkObject.IsServerInitialized;
		}

		/// <summary>
		/// True when this execution is a prediction REPLAY rather than a first run.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A reconcile replays every tick between the corrected one and now, so anything an action
		/// does that is visible or one-shot — a floating number, a VFX instance, a sound — fires
		/// once per replayed tick unless it is suppressed here. That is the visual spam
		/// <c>PlayFXAction</c> already guards against; this is the same test with one definition
		/// instead of an open-coded copy per action.
		/// </para>
		/// <para>
		/// <b>It is the SECOND line, and today it is the one holding nothing.</b> Every production
		/// dispatch refuses to build an ECA payload at all on a replayed tick — <c>ResolveTargetAndSpawn</c>
		/// returns before it spawns, <c>BuffController</c> tests its own <c>isReplayingTick</c>, the
		/// activation triggers test <c>state.ContainsReplayed()</c>, and the hit, tick and destroy
		/// dispatches run off <c>TimeManager.OnTick</c>, which is not replayed — so
		/// <see cref="TickEventData.IsReplay"/> is false at every one of them and this returns false in
		/// production. That is the correct end state rather than a hole: skipping the whole dispatch is
		/// strictly better than skipping one action inside it, because ECA actions are not idempotent
		/// and a replayed dispatch would also re-roll RNG and re-enter selectors. This guard is for the
		/// dispatch that CANNOT gate itself, and such a site declares itself by handing
		/// <see cref="TickEventData"/> the <c>ReplicateState</c> it is running under rather than by
		/// remembering a bare bool — see <see cref="TickEventData.IsReplay"/>.
		/// </para>
		/// <para>
		/// <b>Never use this as an authority gate.</b> The server's own ability dispatches carry
		/// replicate ticks too, so gating state changes on it suppresses the server and the effect
		/// happens nowhere — the exact mistake <see cref="EcaAuthority"/> exists to prevent. This
		/// answers "have I already done this once", not "am I allowed to do this".
		/// </para>
		/// <para>
		/// It reads <see cref="TickEventData.IsReplay"/> and not <c>IsReplicateTick</c>. The two
		/// were the same test until the domain flag was found to be true for dispatches that
		/// cannot be replayed — a spawn chain and a self-target chain are both skipped outright on
		/// a replayed tick, and both carry a replicate-domain tick on every peer, so this returned
		/// true for them and false for everything else. Reading the domain flag here suppressed
		/// every self-buff and self-heal impact effect in the game, on all peers, for good.
		/// </para>
		/// </remarks>
		/// <param name="eventData">The event being executed, or null.</param>
		/// <returns>True when the event carries a replayed tick.</returns>
		protected static bool IsReplayTick(EventData eventData)
		{
			return eventData != null &&
				   eventData.TryGet(out TickEventData tickData) &&
				   tickData.IsReplay;
		}

		/// <summary>
		/// True when this peer has a screen — i.e. it is a client rather than the dedicated server.
		/// </summary>
		/// <remarks>
		/// <para>
		/// For actions whose entire effect is presentational: particles, sound, floating text,
		/// camera shake. Those must run on every client that can see the event and on NONE of the
		/// server, which has nothing to render and would spend the allocation for no viewer.
		/// </para>
		/// <para>
		/// <b>The mirror of <see cref="IsServer"/>, and not a substitute for it.</b> This answers
		/// "should I draw something", never "may I change state". An action that mutates anything
		/// must still gate on <see cref="EcaAuthority"/>; gating a state change on this instead
		/// would let every client change state and the server change none.
		/// </para>
		/// <para>
		/// There is no host mode in this project, so a peer is a client or a server and never both.
		/// A character with no networked identity — a scene-authored trigger, an edit-mode test —
		/// answers true, matching the "allow when undecidable" stance the authority gate takes.
		/// </para>
		/// </remarks>
		/// <param name="initiator">The character the action is running for.</param>
		/// <param name="eventData">The event being executed, or null.</param>
		/// <returns>True when the local peer is a client.</returns>
		protected static bool IsClientPeer(ICharacter initiator, EventData eventData)
		{
			return !EcaAuthority.IsServer(initiator, eventData);
		}

		/// <summary>
		/// Sends a broadcast to the one player an action is acting for.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Not</b> <c>initiator.NetworkObject.Broadcast(...)</c>, which is what these actions
		/// used to call. That sends to the <em>observers</em> of the initiator's NetworkObject —
		/// every client that can see the player, not the player. One person opening a merchant
		/// therefore opened the shop on every screen within observer range, because the client
		/// handlers have no reason to filter a message they were sent directly.
		/// </para>
		/// <para>
		/// Anything that opens a window, offers a quest, or reports a personal result belongs here.
		/// Genuine world state — a switch throwing, a door opening — should go to the observers of
		/// the <em>object that changed</em>, which is a different set again.
		/// </para>
		/// </remarks>
		/// <typeparam name="T">The broadcast type.</typeparam>
		/// <param name="character">The character whose owning connection should receive it.</param>
		/// <param name="message">The broadcast to send.</param>
		protected static void SendToOwner<T>(ICharacter character, T message) where T : struct, IBroadcast
		{
			NetworkConnection owner = character?.Owner;
			if (owner == null || !owner.IsActive)
			{
				return;
			}
			owner.Broadcast(message);
		}

		/// <summary>
		/// Strict target resolution: returns the explicit <see cref="EventData.TargetCharacter"/>
		/// only. Does <b>not</b> fall back to the initiator — outward-effecting actions
		/// (damage, dispel, interrupt, knockback) should use this so a misconfigured selector
		/// can't silently make a caster attack itself.
		/// </summary>
		/// <param name="eventData">The action's event data (may be null).</param>
		/// <param name="target">Resolved target character, or null when none is present.</param>
		/// <returns>True when an explicit target was resolved.</returns>
		protected static bool TryResolveTarget(EventData eventData, out ICharacter target)
		{
			target = eventData?.TargetCharacter;
			return target != null;
		}

		/// <summary>
		/// Forgiving target resolution: prefers <see cref="EventData.TargetCharacter"/>, then
		/// falls back to the initiator. Use for self-effecting actions (resource costs,
		/// self-buffs, cooldown starts) where "no target" naturally means "act on self".
		/// Outward-effecting actions should use <see cref="TryResolveTarget"/> instead so a
		/// missing target produces a no-op rather than a self-hit.
		/// </summary>
		/// <param name="initiator">The action's initiator.</param>
		/// <param name="eventData">The action's event data (may be null).</param>
		/// <param name="target">Resolved character, or null when neither is available.</param>
		/// <returns>True when a non-null target was resolved.</returns>
		protected static bool TryResolveTargetOrInitiator(ICharacter initiator, EventData eventData, out ICharacter target)
		{
			target = (eventData?.TargetCharacter ?? initiator);
			return target != null;
		}

		/// <summary>
		/// Returns a short, designer-facing tooltip line describing this action's effect, or
		/// <c>null</c> when the action has nothing to contribute. Override on actions that
		/// produce a player-visible outcome (damage, heal, buff apply, knockback, etc.) so
		/// ability tooltips can list effects without designer authoring text twice.
		/// </summary>
		/// <remarks>
		/// The base implementation returns <c>null</c>. Aggregators
		/// (<see cref="BaseAbilityTemplate"/>) skip null/whitespace contributions, so most
		/// actions need not override.
		/// </remarks>
		public virtual string GetTooltipContribution() => null;
	}
}