using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// The owner's memory of the rush stops its predicted hits made, by the movement tick each followed, so a replay can make
	/// them again (see <see cref="KCCController.EndMotionFromHit"/>). Pure bookkeeping, no motor.
	/// </summary>
	public sealed class PredictedRushStops
	{
		/// <summary>How long a stop waits for the server to confirm it, in ticks (two seconds at 30 Hz).</summary>
		public const uint ExpiryTicks = 60;

		/// <summary>The most stops remembered at once; the oldest goes first.</summary>
		public const int Capacity = 8;

		private readonly List<uint> stops = new List<uint>(Capacity);

		/// <summary>How many stops are remembered.</summary>
		public int Count => stops.Count;

		/// <summary>Remembers a stop made after the movement step of <paramref name="tick"/>.</summary>
		public void Record(uint tick)
		{
			if (stops.Contains(tick))
			{
				return;
			}
			if (stops.Count >= Capacity)
			{
				stops.RemoveAt(0);
			}
			stops.Add(tick);
		}

		/// <summary>True when a stop was made after the movement step of <paramref name="tick"/>.</summary>
		public bool StopsAfter(uint tick)
		{
			return stops.Contains(tick);
		}

		/// <summary>
		/// After a correction for <paramref name="reconciledTick"/> has been applied: forgets the stops the server has confirmed
		/// (its state shows no rush) or never made (expired), and answers whether a stop the correction has carried past must be
		/// made now, because the server is still rushing a tick or two behind the owner's hit.
		/// </summary>
		/// <param name="reconciledTick">The tick the correction is for.</param>
		/// <param name="serverRushing">Whether the corrected state is still rushing.</param>
		/// <returns>True to end the rush now, before the replay.</returns>
		public bool OnReconciled(uint reconciledTick, bool serverRushing)
		{
			bool endNow = false;
			for (int i = stops.Count - 1; i >= 0; --i)
			{
				uint stop = stops[i];
				if (reconciledTick < stop)
				{
					continue; // the replay will pass it
				}
				if (!serverRushing || reconciledTick - stop > ExpiryTicks)
				{
					stops.RemoveAt(i);
				}
				else
				{
					endNow = true;
				}
			}
			return endNow;
		}

		/// <summary>Forgets everything.</summary>
		public void Clear()
		{
			stops.Clear();
		}
	}
}
