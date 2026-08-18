using System;

namespace FishMMO.Client
{
	/// <summary>
	/// A snapshot of an in-progress download, passed to the active
	/// <see cref="ILauncherView"/> so it can present transfer state.
	/// </summary>
	/// <remarks>
	/// Structured rather than a preformatted string. The download path used to hand the UI
	/// <c>"42% (12.1 MB)"</c>, which forced every view to display exactly what the service had
	/// decided to say and made anything richer — a rate, a remaining time, a total — impossible
	/// without changing the service.
	/// <para>
	/// <see cref="TotalBytes"/> and <see cref="BytesPerSecond"/> are both optional, and callers
	/// must treat them as such: the total is only known when the server supplied one, and no
	/// rate exists until enough samples have accumulated to measure it.
	/// </para>
	/// </remarks>
	public readonly struct DownloadStats
	{
		/// <summary>
		/// Bytes received so far.
		/// </summary>
		public readonly ulong BytesDownloaded;

		/// <summary>
		/// Total bytes expected, or 0 when the server did not report a size.
		/// </summary>
		/// <remarks>
		/// Sourced from the version manifest's <see cref="PatchInfo.Size"/> rather than from
		/// the response, so it is known before the first byte arrives and the UI can open on
		/// "0 B of 240 MB" instead of a bare "0%".
		/// </remarks>
		public readonly long TotalBytes;

		/// <summary>
		/// Current transfer rate in bytes per second, or 0 when not yet measurable.
		/// </summary>
		public readonly double BytesPerSecond;

		/// <summary>
		/// Estimated seconds until completion, or null when it cannot be estimated —
		/// no known total, or no measurable rate.
		/// </summary>
		public readonly double? EstimatedSecondsRemaining;

		/// <summary>
		/// Progress from 0 to 1 as reported by the transfer.
		/// </summary>
		public readonly float NormalizedProgress;

		/// <summary>
		/// True when the transfer has finished and the remaining work is verification.
		/// </summary>
		public readonly bool IsComplete;

		public DownloadStats(
			ulong bytesDownloaded,
			long totalBytes,
			double bytesPerSecond,
			double? estimatedSecondsRemaining,
			float normalizedProgress,
			bool isComplete = false)
		{
			this.BytesDownloaded = bytesDownloaded;
			this.TotalBytes = totalBytes;
			this.BytesPerSecond = bytesPerSecond;
			this.EstimatedSecondsRemaining = estimatedSecondsRemaining;
			this.NormalizedProgress = normalizedProgress;
			this.IsComplete = isComplete;
		}

		/// <summary>
		/// True when a total size is known, and therefore when "x of y" and a remaining time
		/// are meaningful.
		/// </summary>
		public bool HasTotal => this.TotalBytes > 0;

		/// <summary>
		/// Formats a byte count as a human-readable string (for example "1.2 MB").
		/// </summary>
		public static string FormatBytes(ulong bytes)
		{
			if (bytes < 1024UL) return $"{bytes} B";
			if (bytes < 1024UL * 1024UL) return $"{bytes / 1024.0:F1} KB";
			if (bytes < 1024UL * 1024UL * 1024UL) return $"{bytes / (1024.0 * 1024.0):F1} MB";
			return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
		}

		/// <summary>
		/// Formats a duration as a compact human-readable string (for example "3m 20s").
		/// </summary>
		/// <remarks>
		/// Deliberately coarse. A remaining time derived from a fluctuating transfer rate is an
		/// estimate, and rendering it to the second invites the player to watch it jitter.
		/// </remarks>
		public static string FormatDuration(double seconds)
		{
			if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
			{
				return "--";
			}
			if (seconds < 60)
			{
				return $"{Math.Max(1, (int)Math.Round(seconds))}s";
			}
			if (seconds < 3600)
			{
				int minutes = (int)(seconds / 60);
				int remainder = (int)(seconds % 60);
				return $"{minutes}m {remainder}s";
			}

			int hours = (int)(seconds / 3600);
			int mins = (int)((seconds % 3600) / 60);
			return $"{hours}h {mins}m";
		}

		/// <summary>
		/// Builds the player-facing progress line, including only the parts that are actually
		/// known.
		/// </summary>
		/// <remarks>
		/// Shared by both views so they cannot drift into describing the same download
		/// differently. Anything unknown is omitted rather than shown as a zero — a rate of
		/// "0 B/s" on a transfer that simply has not been measured yet reads as a stall.
		/// </remarks>
		public string ToDisplayString()
		{
			if (this.IsComplete)
			{
				return this.HasTotal
					? $"{FormatBytes((ulong)this.TotalBytes)} — verifying"
					: "Verifying";
			}

			string transferred = this.HasTotal
				? $"{FormatBytes(this.BytesDownloaded)} of {FormatBytes((ulong)this.TotalBytes)}"
				: FormatBytes(this.BytesDownloaded);

			string rate = this.BytesPerSecond > 0
				? $"  •  {FormatBytes((ulong)this.BytesPerSecond)}/s"
				: string.Empty;

			string eta = this.EstimatedSecondsRemaining.HasValue
				? $"  •  {FormatDuration(this.EstimatedSecondsRemaining.Value)} left"
				: string.Empty;

			return transferred + rate + eta;
		}
	}
}
