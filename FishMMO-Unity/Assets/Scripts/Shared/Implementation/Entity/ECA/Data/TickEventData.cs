using FishNet.Object.Prediction;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Lightweight EventData subtype carrying a network tick for tick-aware triggers.
	/// </summary>
	public class TickEventData : EventData
	{
		/// <summary>
		/// The tick carried by this event.
		/// </summary>
		public PredictionTick Tick { get; }

		/// <summary>
		/// True when <see cref="Tick"/> was sourced from a replicate input tick and may be used
		/// directly with prediction-domain state such as buff expiry or cooldown start ticks.
		/// False when the tick is a raw authoritative wall-clock tick such as TimeManager.LocalTick.
		/// </summary>
		public bool IsReplicateTick { get; }

		/// <summary>
		/// True when this execution is a reconcile REPLAY of a tick that already ran once.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Deliberately separate from <see cref="IsReplicateTick"/>, which says which CLOCK the
		/// tick is counted on and nothing about how many times it has been executed. Conflating
		/// the two was a real defect: every ability spawn and every self-target dispatch carries a
		/// replicate-domain tick, on the server as much as on the owner, and each of them is
		/// skipped outright on a replayed tick — so a guard that read the domain flag as "this is a
		/// replay" answered true for exactly the dispatches that are never replays. The visible
		/// cost was that <c>PlayFXAction</c> refused every self-buff and self-heal impact effect on
		/// every peer, permanently; the quieter one was that the owner drew no predicted number for
		/// a self-heal.
		/// </para>
		/// <para>
		/// Set it only where a dispatch genuinely re-runs during a reconcile. Every current dispatch
		/// site gates replays before it builds this payload — <c>ResolveTargetAndSpawn</c> returns
		/// before it spawns, <c>BuffController</c> tests its own <c>isReplayingTick</c>, the
		/// activation triggers test <c>state.ContainsReplayed()</c>, and the hit, tick and destroy
		/// dispatches run off <c>TimeManager.OnTick</c>, which is not replayed — so nothing sets it
		/// today. That is the right place for the gate, not a gap to be plugged here: skipping the
		/// whole dispatch also skips the RNG draws and the selector fan-out inside it, which a flag
		/// read by four actions cannot.
		/// </para>
		/// <para>
		/// <b>How a site that CANNOT gate says so.</b> By handing over the <see cref="ReplicateState"/>
		/// it is running under, not a hand-written bool. The bool overload exists for the sites that
		/// have no replicate state to offer and can therefore prove they are not replays; a
		/// prediction dispatch that has one has no excuse to answer the question from memory, and
		/// the state overload is what makes the answer follow the dispatch instead of being
		/// remembered. <c>BaseAction.IsReplayTick</c> is the consumer.
		/// </para>
		/// </remarks>
		public bool IsReplay { get; }

		/// <summary>
		/// Creates tick event data from a replicate input tick.
		/// </summary>
		/// <param name="character">The event initiator.</param>
		/// <param name="tick">The replicate-domain tick.</param>
		/// <param name="isReplay">True when this execution is a reconcile replay of the tick.</param>
		public TickEventData(ICharacter character, PredictionTick tick, bool isReplay = false) : base(character)
		{
			Tick = tick;
			IsReplicateTick = true;
			IsReplay = isReplay;
		}

		/// <summary>
		/// Creates tick event data from a replicate input tick, taking its replay answer from the
		/// <see cref="ReplicateState"/> the dispatch is running under.
		/// </summary>
		/// <remarks>
		/// The preferred form for any dispatch reached from a replicate. <see cref="IsReplay"/> is
		/// then a fact about the execution rather than a claim the caller had to remember to make,
		/// which is the whole difference between a guard that protects something and one that reads
		/// as though it does. See <see cref="IsReplay"/>.
		/// </remarks>
		/// <param name="character">The event initiator.</param>
		/// <param name="tick">The replicate-domain tick.</param>
		/// <param name="state">The replicate state this dispatch is executing under.</param>
		public TickEventData(ICharacter character, PredictionTick tick, ReplicateState state)
			: this(character, tick, state.ContainsReplayed())
		{
		}

		/// <summary>
		/// Returns true when this tick was sourced from the same character whose
		/// prediction-domain state is about to consume it.
		/// </summary>
		/// <param name="character">The character whose prediction-domain state will consume the tick.</param>
		/// <returns>True when the tick belongs to <paramref name="character"/>.</returns>
		public bool IsForCharacter(ICharacter character)
		{
			if (character == null || Initiator == null)
			{
				return false;
			}

			return ReferenceEquals(Initiator, character) ||
				(Initiator.ID != 0 && Initiator.ID == character.ID);
		}

		/// <summary>
		/// Creates tick event data from a raw authoritative tick.
		/// </summary>
		/// <param name="character">The event initiator.</param>
		/// <param name="serverTick">The raw authoritative tick.</param>
		internal TickEventData(ICharacter character, uint serverTick) : base(character)
		{
			Tick = new PredictionTick(serverTick);
			IsReplicateTick = false;
		}
	}
}