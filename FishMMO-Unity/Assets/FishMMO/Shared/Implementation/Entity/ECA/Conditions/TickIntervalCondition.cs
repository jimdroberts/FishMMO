using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Passes on one tick in every <see cref="IntervalTicks"/>, read from the event's <see cref="TickEventData"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// For triggers raised every tick, a region's <c>OnRegionStay</c> above all: an action there runs thirty times a
	/// second, so a "poison mist" doing one point a tick takes thirty a second and nothing between can be authored.
	/// This turns the stay into a pulse.
	/// </para>
	/// <para>
	/// The phase is the tick number itself, so every character in the region pulses on the same tick rather than each on
	/// its own schedule from the moment it walked in. Nothing is remembered between ticks, which is what lets one
	/// condition instance serve every character and every replay alike.
	/// </para>
	/// <para>
	/// An event without tick data fails the condition (when the interval is above one): a pulse with no clock to pace it
	/// would otherwise fire on every call, which is the very thing it exists to stop.
	/// </para>
	/// </remarks>
	[Serializable]
	public class TickIntervalCondition : BaseCondition
	{
		/// <summary>Ticks between passes. One (or less) passes every tick.</summary>
		[Tooltip("Ticks between passes; at 30 ticks a second, 30 is once a second. 1 passes every tick.")]
		[Min(1)]
		public int IntervalTicks = 30;

		/// <inheritdoc />
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			return Passes(IntervalTicks, eventData != null && eventData.TryGet(out TickEventData tick) ? tick.Tick.Value : (uint?)null);
		}

		/// <summary>The rule on its own: whether <paramref name="tick"/> falls on the interval.</summary>
		/// <param name="intervalTicks">Ticks between passes.</param>
		/// <param name="tick">The event's tick, or null when it carried none.</param>
		public static bool Passes(int intervalTicks, uint? tick)
		{
			if (intervalTicks <= 1)
			{
				return true;
			}
			return tick.HasValue && tick.Value % (uint)intervalTicks == 0;
		}

		/// <inheritdoc />
		public override string GetTooltipContribution() => IntervalTicks > 1 ? $"Every {IntervalTicks} ticks" : null;
	}
}
