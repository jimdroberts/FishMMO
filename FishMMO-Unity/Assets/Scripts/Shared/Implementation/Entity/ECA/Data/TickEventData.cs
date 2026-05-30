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
		/// Creates tick event data from a replicate input tick.
		/// </summary>
		/// <param name="character">The event initiator.</param>
		/// <param name="tick">The replicate-domain tick.</param>
		public TickEventData(ICharacter character, PredictionTick tick) : base(character)
		{
			Tick = tick;
			IsReplicateTick = true;
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