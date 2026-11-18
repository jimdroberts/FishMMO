using System;
using FishNet.Transporting.WebTransport;

namespace FishMMO.Client
{
	/// <summary>
	/// What the network statistics overlay knows at one moment: the newest snapshot (for the
	/// totals and the connection figures) and the rates smoothed over the last few seconds.
	/// </summary>
	/// <remarks>
	/// A value, taken once per refresh, so every label and every tooltip written in that refresh
	/// describes the same instant. <see cref="HasRates"/> is false until two snapshots of one
	/// averaging window exist; every rate then reads as unknown, never as zero.
	/// </remarks>
	public struct NetworkStatsReadout
	{
		/// <summary>True once at least one snapshot has been taken since the overlay opened.</summary>
		public bool HasSnapshot;

		/// <summary>The newest snapshot. Meaningful only when <see cref="HasSnapshot"/> is true.</summary>
		public TransportTrafficSnapshot Latest;

		/// <summary>True when <see cref="Rates"/> spans a real interval.</summary>
		public bool HasRates;

		/// <summary>Traffic over the smoothing window. Meaningful only when <see cref="HasRates"/> is true.</summary>
		public TransportTrafficRates Rates;

		/// <summary>A readout with nothing in it: the state before the first sample.</summary>
		public static NetworkStatsReadout Empty => default;
	}

	/// <summary>
	/// The overlay's memory: the last few snapshots, for rates smoothed over about five seconds,
	/// and a minute of one-second wire rates, for the graph.
	/// </summary>
	/// <remarks>
	/// <para><b>Smoothing is a longer interval, not an average of short ones.</b> The rate shown is
	/// the difference between the newest snapshot and the oldest one still inside
	/// <see cref="SmoothingWindowSeconds"/>, divided by the time between them — the transport's own
	/// advice (<see cref="TransportTrafficMath.TryComputeRates"/>). An average of one-second rates
	/// would come to the same number with the rounding of every step in it.</para>
	///
	/// <para><b>A window never spans a change of definition.</b> When a layer's measure changes
	/// between two snapshots — the native library starting on the first connect, or a browser's
	/// first <c>getStats()</c> answer turning an estimate into a reported figure — the two ends
	/// count different things, and the transport refuses to difference them (the layer comes back
	/// unavailable). Keeping the older snapshots would leave that layer unknown for the whole
	/// window; the window restarts at the new snapshot instead, so the layer is unknown for one
	/// sample and then correct. The same happens when two snapshots cannot belong to one process
	/// lifetime at all (a backend change, a counter that went backwards).</para>
	///
	/// <para><b>The graph keeps its gaps.</b> A second whose wire rate could not be computed is
	/// stored as NaN and drawn as a break in the line, never as a dip to zero.</para>
	///
	/// <para>Pure: no clock, no Unity object. The overlay feeds it one snapshot a second; tests and
	/// the render harness feed it fabricated ones.</para>
	/// </remarks>
	public sealed class NetworkStatsSampler
	{
		/// <summary>Snapshots kept: enough for a five-second window at one a second.</summary>
		public const int SnapshotCapacity = 6;

		/// <summary>How far back the smoothed rates reach, seconds.</summary>
		public const double SmoothingWindowSeconds = 5.0;

		/// <summary>
		/// Slack allowed on <see cref="SmoothingWindowSeconds"/>. Samples are taken on the first
		/// frame after each second, so five intervals come to a little over five seconds; without
		/// slack the oldest snapshot would fall out of the window by a frame and the average would
		/// quietly shorten to four seconds.
		/// </summary>
		public const double WindowSlackSeconds = 0.5;

		/// <summary>One-second graph points kept: a minute.</summary>
		public const int HistoryCapacity = 60;

		private readonly TransportTrafficSnapshot[] snapshots = new TransportTrafficSnapshot[SnapshotCapacity];
		private int snapshotCount;
		private int newestIndex = -1;

		private readonly double[] historyDown = new double[HistoryCapacity];
		private readonly double[] historyUp = new double[HistoryCapacity];
		private int historyCount;
		private int historyNext;

		/// <summary>Snapshots in the current averaging window.</summary>
		public int Count => snapshotCount;

		/// <summary>Graph points held, at most <see cref="HistoryCapacity"/>.</summary>
		public int HistoryCount => historyCount;

		/// <summary>Forgets everything: the window and the graph. The overlay does this each time it opens.</summary>
		/// <remarks>
		/// Nothing is sampled while the overlay is hidden, so a window or a graph carried across a
		/// hidden spell would join two moments minutes apart as if they were adjacent seconds.
		/// </remarks>
		public void Clear()
		{
			snapshotCount = 0;
			newestIndex = -1;
			historyCount = 0;
			historyNext = 0;
		}

		/// <summary>The newest snapshot, if there is one.</summary>
		public bool TryGetLatest(out TransportTrafficSnapshot latest)
		{
			if (snapshotCount == 0)
			{
				latest = default;
				return false;
			}
			latest = snapshots[newestIndex];
			return true;
		}

		/// <summary>
		/// Adds one snapshot: extends the averaging window, or restarts it when the snapshot cannot
		/// be compared with the previous one, and appends one point to the graph.
		/// </summary>
		/// <param name="snapshot">A snapshot from <see cref="TransportTraffic.Capture"/>.</param>
		/// <returns>
		/// False when the snapshot was refused because it carries no backend at all (a default
		/// struct); true otherwise.
		/// </returns>
		public bool Add(in TransportTrafficSnapshot snapshot)
		{
			if (snapshot.Backend == TransportTrafficBackend.None)
			{
				return false;
			}

			if (snapshotCount > 0)
			{
				ref readonly TransportTrafficSnapshot previous = ref snapshots[newestIndex];
				bool comparable = TransportTrafficMath.TryComputeRates(in previous, in snapshot, out TransportTrafficRates step);

				/* The graph point is this last second, whatever happens to the window: NaN when the
				 * second could not be measured, so the line breaks rather than dips. */
				AppendHistory(
					comparable ? step.WireRecvBytesPerSecond : double.NaN,
					comparable ? step.WireSentBytesPerSecond : double.NaN);

				if (!comparable || DefinitionChanged(in previous, in snapshot))
				{
					snapshotCount = 0;
					newestIndex = -1;
				}
			}

			newestIndex = (newestIndex + 1) % SnapshotCapacity;
			snapshots[newestIndex] = snapshot;
			snapshotCount = Math.Min(snapshotCount + 1, SnapshotCapacity);
			return true;
		}

		/// <summary>
		/// True when a layer counts something different in <paramref name="later"/> than in
		/// <paramref name="earlier"/>, so no interval may span the two.
		/// </summary>
		public static bool DefinitionChanged(in TransportTrafficSnapshot earlier, in TransportTrafficSnapshot later)
		{
			return earlier.Backend != later.Backend ||
				earlier.QuicMeasure != later.QuicMeasure ||
				earlier.DatagramMeasure != later.DatagramMeasure;
		}

		/// <summary>
		/// The traffic between the oldest snapshot inside the smoothing window and the newest.
		/// </summary>
		/// <remarks>
		/// When no older snapshot lies inside the window — a frame hitch longer than the window, so
		/// the previous sample is already too old — the previous snapshot is used anyway: an
		/// average over a slightly longer interval is still the truth about that interval, and
		/// showing nothing would hide a working connection.
		/// </remarks>
		public bool TryGetSmoothedRates(out TransportTrafficRates rates)
		{
			rates = default;
			if (snapshotCount < 2)
			{
				return false;
			}

			ref readonly TransportTrafficSnapshot newest = ref snapshots[newestIndex];

			// Walk back from the previous snapshot to the oldest held; keep the furthest in range.
			int chosen = (newestIndex - 1 + SnapshotCapacity) % SnapshotCapacity;
			for (int back = 2; back < snapshotCount; ++back)
			{
				int index = (newestIndex - back + SnapshotCapacity) % SnapshotCapacity;
				double age = newest.TimestampSeconds - snapshots[index].TimestampSeconds;
				if (age > SmoothingWindowSeconds + WindowSlackSeconds)
				{
					break;
				}
				chosen = index;
			}

			return TransportTrafficMath.TryComputeRates(in snapshots[chosen], in newest, out rates);
		}

		/// <summary>The newest snapshot and the smoothed rates, as one value.</summary>
		public NetworkStatsReadout Readout()
		{
			NetworkStatsReadout readout = default;
			readout.HasSnapshot = TryGetLatest(out readout.Latest);
			readout.HasRates = readout.HasSnapshot && TryGetSmoothedRates(out readout.Rates);
			return readout;
		}

		/// <summary>One graph point: wire bytes per second each way, NaN where unknown.</summary>
		/// <param name="index">0 is the oldest point held, <see cref="HistoryCount"/> - 1 the newest.</param>
		/// <param name="down">Received wire bytes per second.</param>
		/// <param name="up">Sent wire bytes per second.</param>
		public void GetHistory(int index, out double down, out double up)
		{
			if (index < 0 || index >= historyCount)
			{
				down = double.NaN;
				up = double.NaN;
				return;
			}
			int start = historyCount < HistoryCapacity ? 0 : historyNext;
			int slot = (start + index) % HistoryCapacity;
			down = historyDown[slot];
			up = historyUp[slot];
		}

		/// <summary>The highest known point in the graph, either direction, or NaN when none is known.</summary>
		public double HistoryPeak()
		{
			double peak = double.NaN;
			for (int i = 0; i < historyCount; ++i)
			{
				peak = MaxKnown(peak, historyDown[i]);
				peak = MaxKnown(peak, historyUp[i]);
			}
			return peak;
		}

		private static double MaxKnown(double current, double candidate)
		{
			if (double.IsNaN(candidate) || double.IsInfinity(candidate) || candidate < 0)
			{
				return current;
			}
			return double.IsNaN(current) || candidate > current ? candidate : current;
		}

		private void AppendHistory(double down, double up)
		{
			historyDown[historyNext] = down;
			historyUp[historyNext] = up;
			historyNext = (historyNext + 1) % HistoryCapacity;
			historyCount = Math.Min(historyCount + 1, HistoryCapacity);
		}
	}
}
