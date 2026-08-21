using System.Collections.Generic;

namespace FishMMO.Client
{
	/// <summary>
	/// Measures transfer rate over a trailing time window and estimates remaining time.
	/// </summary>
	/// <remarks>
	/// A sliding window rather than a cumulative average. Dividing total bytes by total elapsed
	/// time keeps reporting a healthy rate for minutes after a transfer has actually stalled,
	/// because the historical average drags the number up — which is exactly when the player
	/// most needs to be told something is wrong. A trailing window converges on zero within
	/// <see cref="windowSeconds"/> of the last byte arriving.
	/// <para>
	/// Time is supplied by the caller rather than read from <c>UnityEngine.Time</c> so this
	/// class stays independent of the engine clock and can be exercised directly.
	/// </para>
	/// </remarks>
	public class DownloadRateTracker
	{
		/// <summary>
		/// One observation of cumulative bytes at a point in time.
		/// </summary>
		private readonly struct RateSample
		{
			public readonly float Time;
			public readonly ulong Bytes;

			public RateSample(float time, ulong bytes)
			{
				this.Time = time;
				this.Bytes = bytes;
			}
		}

		/// <summary>
		/// Length of the trailing window, in seconds.
		/// </summary>
		/// <remarks>
		/// Long enough to smooth the per-frame jitter of a chunked HTTP transfer, short enough
		/// that a stall shows up while the player is still looking at it.
		/// </remarks>
		private readonly float windowSeconds;

		/// <summary>
		/// Observations inside the window, oldest first.
		/// </summary>
		private readonly Queue<RateSample> samples = new Queue<RateSample>();

		/// <summary>
		/// Most recent rate in bytes per second, or 0 before enough samples exist.
		/// </summary>
		public double BytesPerSecond { get; private set; }

		public DownloadRateTracker(float windowSeconds = 3f)
		{
			this.windowSeconds = windowSeconds > 0f ? windowSeconds : 3f;
		}

		/// <summary>
		/// Discards all history. Call before starting a new transfer so a previous download's
		/// rate cannot be attributed to it.
		/// </summary>
		public void Reset()
		{
			this.samples.Clear();
			this.BytesPerSecond = 0;
		}

		/// <summary>
		/// Records an observation and recomputes the rate.
		/// </summary>
		/// <param name="time">Monotonic timestamp in seconds.</param>
		/// <param name="bytesDownloaded">Cumulative bytes received so far.</param>
		public void Sample(float time, ulong bytesDownloaded)
		{
			this.samples.Enqueue(new RateSample(time, bytesDownloaded));

			// Retain one sample older than the window so the span always covers the full
			// window. Dropping everything outside it would shrink the measured interval to the
			// gap between the two newest samples, which on a fast frame is near zero and
			// produces an absurd rate.
			while (this.samples.Count > 2 && time - this.samples.Peek().Time > this.windowSeconds)
			{
				this.samples.Dequeue();
			}

			if (this.samples.Count < 2)
			{
				this.BytesPerSecond = 0;
				return;
			}

			RateSample oldest = this.samples.Peek();
			float elapsed = time - oldest.Time;
			if (elapsed <= 0f)
			{
				return;
			}

			// Unsigned arithmetic: a server that restarts a transfer can report fewer bytes
			// than a previous sample, and the wraparound would otherwise produce an enormous
			// rate rather than zero.
			ulong delta = bytesDownloaded >= oldest.Bytes ? bytesDownloaded - oldest.Bytes : 0UL;
			this.BytesPerSecond = delta / elapsed;
		}

		/// <summary>
		/// Estimates seconds until completion, or null when it cannot be estimated.
		/// </summary>
		/// <param name="bytesDownloaded">Cumulative bytes received so far.</param>
		/// <param name="totalBytes">Expected total, or 0 when unknown.</param>
		/// <remarks>
		/// Returns null rather than a large number when the rate is zero. An estimate derived
		/// from a stalled transfer is not a long estimate, it is no estimate — and showing one
		/// implies progress that is not happening.
		/// </remarks>
		public double? EstimateSecondsRemaining(ulong bytesDownloaded, long totalBytes)
		{
			if (totalBytes <= 0 || this.BytesPerSecond <= 0)
			{
				return null;
			}

			ulong total = (ulong)totalBytes;
			if (bytesDownloaded >= total)
			{
				return 0;
			}

			return (total - bytesDownloaded) / this.BytesPerSecond;
		}
	}
}
