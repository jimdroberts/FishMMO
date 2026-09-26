using System;
using FishMMO.Logging;

namespace FishMMO.Server.Core
{
	/// <summary>
	/// Logs a fault that may repeat every frame or tick without flooding the log.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The server isolates each unit of repeating work (a behaviour's update, a periodic callback,
	/// an NPC brain, a weather scene) in its own try/catch so one failure cannot stop the rest.
	/// Isolation alone turns a deterministic throw into a full stack trace on every pass: 60 a
	/// second from a behaviour, 1,500 a second from fifty NPCs sharing a broken archetype. That
	/// drowns the first trace, which is the only one anyone needs.
	/// </para>
	/// <para>
	/// One instance per unit of work. The first failure, and any failure whose exception type or
	/// message differs from the last, is logged in full. Identical repeats are counted and
	/// summarised at most once per <see cref="SummaryIntervalSeconds"/>. A success after failures
	/// logs one recovery line with the count, so a transient fault reads as transient.
	/// </para>
	/// <para>
	/// Not thread-safe: each instance belongs to the thread that runs its unit of work, which
	/// for every current caller is the main thread.
	/// </para>
	/// </remarks>
	public sealed class RepeatingFaultLog
	{
		/// <summary>
		/// Default seconds between summaries of an identical repeating fault.
		/// </summary>
		public const double DefaultSummaryIntervalSeconds = 10.0;

		private readonly string category;
		private readonly string subject;
		private string lastSignature;
		private double nextSummaryAt;
		private int suppressed;

		/// <summary>
		/// Seconds between summaries of an identical repeating fault.
		/// </summary>
		public double SummaryIntervalSeconds { get; }

		/// <summary>
		/// Failures since the last success. Zero while the unit of work is healthy.
		/// </summary>
		public int ConsecutiveFailures { get; private set; }

		/// <summary>
		/// Creates a fault log for one unit of work.
		/// </summary>
		/// <param name="category">Log category, usually the owning system's name.</param>
		/// <param name="subject">What failed, e.g. a behaviour or callback name. Included in every line.</param>
		/// <param name="summaryIntervalSeconds">Seconds between summaries of an identical fault.</param>
		public RepeatingFaultLog(string category, string subject, double summaryIntervalSeconds = DefaultSummaryIntervalSeconds)
		{
			this.category = category ?? "Server";
			this.subject = subject ?? "unknown";
			SummaryIntervalSeconds = summaryIntervalSeconds > 0.0 ? summaryIntervalSeconds : DefaultSummaryIntervalSeconds;
		}

		/// <summary>
		/// What <see cref="Record"/> decided to do with one failure.
		/// </summary>
		public enum Decision
		{
			/// <summary>Log the exception in full: the first failure, or a different one.</summary>
			LogFull,
			/// <summary>Log a one-line summary of the identical repeats since the last line.</summary>
			LogSummary,
			/// <summary>Count it and stay quiet.</summary>
			Suppress,
		}

		/// <summary>
		/// Records a failure and decides what to log. Pure bookkeeping, so the rule is testable
		/// without a logger; <see cref="Report"/> is the logging wrapper.
		/// </summary>
		/// <param name="ex">The exception.</param>
		/// <param name="now">Monotonic time in seconds.</param>
		/// <param name="repeats">For <see cref="Decision.LogSummary"/>, the repeats being summarised
		/// (this one included); otherwise 0.</param>
		public Decision Record(Exception ex, double now, out int repeats)
		{
			ConsecutiveFailures++;
			string signature = ex == null ? string.Empty : ex.GetType().FullName + ": " + ex.Message;
			repeats = 0;

			if (ConsecutiveFailures == 1 || !string.Equals(signature, lastSignature, StringComparison.Ordinal))
			{
				lastSignature = signature;
				suppressed = 0;
				nextSummaryAt = now + SummaryIntervalSeconds;
				return Decision.LogFull;
			}

			suppressed++;
			if (now >= nextSummaryAt)
			{
				repeats = suppressed;
				suppressed = 0;
				nextSummaryAt = now + SummaryIntervalSeconds;
				return Decision.LogSummary;
			}
			return Decision.Suppress;
		}

		/// <summary>
		/// Records a success. Returns the failure count it ends, or 0 if the unit was already healthy.
		/// </summary>
		public int RecordSuccess()
		{
			int ended = ConsecutiveFailures;
			ConsecutiveFailures = 0;
			lastSignature = null;
			suppressed = 0;
			return ended;
		}

		/// <summary>
		/// Records a failure and logs it according to <see cref="Record"/>.
		/// </summary>
		/// <param name="ex">The exception.</param>
		/// <param name="now">Monotonic time in seconds.</param>
		public void Report(Exception ex, double now)
		{
			switch (Record(ex, now, out int repeats))
			{
				case Decision.LogFull:
					Log.Error(category, $"{subject} threw: {ex}");
					break;
				case Decision.LogSummary:
					Log.Error(category, $"{subject} threw the same exception {repeats} more time(s) " +
						$"({ConsecutiveFailures} in a row): {ex?.GetType().Name}: {ex?.Message}");
					break;
			}
		}

		/// <summary>
		/// Records a success and logs a recovery line if it ends a run of failures.
		/// </summary>
		public void ReportSuccess()
		{
			// Field read first: the healthy path is the per-frame path and must stay free.
			if (ConsecutiveFailures == 0)
			{
				return;
			}
			int ended = RecordSuccess();
			Log.Warning(category, $"{subject} recovered after {ended} consecutive failure(s).");
		}
	}
}
