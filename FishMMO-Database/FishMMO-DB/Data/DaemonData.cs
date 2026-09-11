using System;
using System.Collections.Generic;
using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Data
{
	/// <summary>A machine running a daemon, with what it supervises.</summary>
	public sealed class DaemonHostData
	{
		/// <summary>Surrogate key.</summary>
		public long ID { get; set; }

		/// <summary>The machine's name.</summary>
		public string HostName { get; set; }

		/// <summary>The daemon build running there.</summary>
		public string DaemonVersion { get; set; }

		/// <summary>What the operating system calls itself.</summary>
		public string OSDescription { get; set; }

		/// <summary>Logical processors.</summary>
		public int ProcessorCount { get; set; }

		/// <summary>When the daemon started.</summary>
		public DateTime StartedUtc { get; set; }

		/// <summary>Last time it said it was alive.</summary>
		public DateTime LastHeartbeatUtc { get; set; }

		/// <summary>What it supervises.</summary>
		public IReadOnlyList<DaemonAppData> Apps { get; set; } = Array.Empty<DaemonAppData>();
	}

	/// <summary>One supervised application, as the daemon last reported it.</summary>
	public sealed class DaemonAppData
	{
		/// <summary>Surrogate key.</summary>
		public long ID { get; set; }

		/// <summary>The host supervising it.</summary>
		public string HostName { get; set; }

		/// <summary>The name from the daemon's configuration.</summary>
		public string Name { get; set; }

		/// <summary>What the daemon last observed.</summary>
		public DaemonAppStatus Status { get; set; }

		/// <summary>The process id, when running.</summary>
		public int? ProcessID { get; set; }

		/// <summary>Restarts so far.</summary>
		public int RestartAttempts { get; set; }

		/// <summary>Restarts allowed before it gives up.</summary>
		public int MaxRestartAttempts { get; set; }

		/// <summary>The health-checked port.</summary>
		public int MonitoredPort { get; set; }

		/// <summary>When it was last reported on.</summary>
		public DateTime LastReportedUtc { get; set; }
	}

	/// <summary>
	/// What a daemon reports about one application on each cycle.
	/// </summary>
	/// <remarks>
	/// Carries no executable path or arguments, matching the entity: the daemon tells the
	/// database what it observed, never what it was configured to run.
	/// </remarks>
	public sealed class DaemonAppReport
	{
		/// <summary>The name from the daemon's configuration.</summary>
		public string Name { get; set; }

		/// <summary>What was observed.</summary>
		public DaemonAppStatus Status { get; set; }

		/// <summary>The process id, when running.</summary>
		public int? ProcessID { get; set; }

		/// <summary>Restarts so far.</summary>
		public int RestartAttempts { get; set; }

		/// <summary>Restarts allowed.</summary>
		public int MaxRestartAttempts { get; set; }

		/// <summary>The health-checked port.</summary>
		public int MonitoredPort { get; set; }
	}

	/// <summary>One instruction for a daemon.</summary>
	public sealed class DaemonCommandData
	{
		/// <summary>Surrogate key.</summary>
		public long ID { get; set; }

		/// <summary>Which host it is for.</summary>
		public string HostName { get; set; }

		/// <summary>Which supervised application, by name.</summary>
		public string AppName { get; set; }

		/// <summary>Start, stop or restart.</summary>
		public DaemonCommandVerb Verb { get; set; }

		/// <summary>The operator account that asked.</summary>
		public string RequestedBy { get; set; }

		/// <summary>When they asked.</summary>
		public DateTime RequestedUtc { get; set; }

		/// <summary>Why.</summary>
		public string Reason { get; set; }

		/// <summary>After which it must not be executed.</summary>
		public DateTime ExpiresUtc { get; set; }

		/// <summary>When a daemon took it.</summary>
		public DateTime? ClaimedUtc { get; set; }

		/// <summary>Which daemon instance took it.</summary>
		public string ClaimedBy { get; set; }

		/// <summary>When it finished.</summary>
		public DateTime? CompletedUtc { get; set; }

		/// <summary>Whether it worked.</summary>
		public bool? Succeeded { get; set; }

		/// <summary>What happened, in words.</summary>
		public string Outcome { get; set; }
	}

	/// <summary>One page of daemon commands.</summary>
	public sealed class DaemonCommandPage
	{
		/// <summary>The rows on this page.</summary>
		public IReadOnlyList<DaemonCommandData> Items { get; set; } = Array.Empty<DaemonCommandData>();

		/// <summary>1-based page number.</summary>
		public int Page { get; set; }

		/// <summary>Rows per page.</summary>
		public int PageSize { get; set; }

		/// <summary>Total rows matching the filter.</summary>
		public int TotalCount { get; set; }
	}

	/// <summary>
	/// One observed change in a supervised application.
	/// </summary>
	/// <remarks>
	/// Read-only history. Every field is an enum member, an integer or a name the daemon already
	/// reported on its own row; nothing here is free text, and nothing here was written by a
	/// daemon. See <c>DaemonAppEventEntity</c> for why that matters.
	/// </remarks>
	public sealed class DaemonAppEventData
	{
		/// <summary>Surrogate key. Also the tie-break inside one timestamp.</summary>
		public long ID { get; set; }

		/// <summary>The host that reported it.</summary>
		public string HostName { get; set; }

		/// <summary>The supervised application, by name.</summary>
		public string AppName { get; set; }

		/// <summary>What was stored before, or null on a first sighting.</summary>
		public DaemonAppStatus? PreviousStatus { get; set; }

		/// <summary>What was observed.</summary>
		public DaemonAppStatus Status { get; set; }

		/// <summary>The process id now, when it is running.</summary>
		public int? ProcessID { get; set; }

		/// <summary>The process id before, when there was one.</summary>
		public int? PreviousProcessID { get; set; }

		/// <summary>Restarts in the current failure cycle, as reported.</summary>
		public int RestartAttempts { get; set; }

		/// <summary>The restart count before, or null on a first sighting.</summary>
		public int? PreviousRestartAttempts { get; set; }

		/// <summary>When the heartbeat carrying this observation was recorded.</summary>
		public DateTime ObservedUtc { get; set; }

		/// <summary>
		/// One line saying what this transition was, composed here and never stored.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Built entirely from the two status values and the integers beside them. That is the
		/// point: an operator reads a sentence, and no part of that sentence came from a
		/// supervisor on another machine. The vocabulary is closed — a daemon can cause a
		/// transition between known statuses and nothing else — so a fully compromised daemon
		/// cannot put a string of its choosing in front of somebody with an administrative
		/// session.
		/// </para>
		/// <para>
		/// Composed rather than persisted so that improving the wording does not require
		/// rewriting history, which an append-only table must never do.
		/// </para>
		/// </remarks>
		public string Describe()
		{
			var parts = new List<string>(3);

			if (!PreviousStatus.HasValue)
			{
				parts.Add($"First seen {Name(Status)}");
			}
			else if (PreviousStatus.Value != Status)
			{
				parts.Add($"{Name(PreviousStatus.Value)} → {Name(Status)}");
			}
			else
			{
				/* The status is unchanged, so something else is the story — a process id that
				 * moved, or a restart counter that did. Saying "Healthy → Healthy" would read as
				 * a row that should not exist. */
				parts.Add($"Still {Name(Status)}");
			}

			string process = DescribeProcess();
			if (process.Length > 0)
			{
				parts.Add(process);
			}

			string restarts = DescribeRestarts();
			if (restarts.Length > 0)
			{
				parts.Add(restarts);
			}

			return string.Join(", ", parts) + ".";
		}

		/// <summary>
		/// One status, as a person reads it.
		/// </summary>
		/// <remarks>
		/// Written out rather than taken from <c>ToString</c>, so that StoppedByOperator reads as
		/// English in the middle of a sentence. A value outside the enumeration — which only a
		/// daemon newer than this deployment can produce — renders as its integer: honest about
		/// not knowing, and still not a string anybody supplied.
		/// </remarks>
		private static string Name(DaemonAppStatus status)
		{
			switch (status)
			{
				case DaemonAppStatus.Unknown: return "Unknown";
				case DaemonAppStatus.Healthy: return "Healthy";
				case DaemonAppStatus.Starting: return "Starting";
				case DaemonAppStatus.Stopped: return "Stopped";
				case DaemonAppStatus.Exhausted: return "Exhausted";
				case DaemonAppStatus.StoppedByOperator: return "Stopped by operator";
				default: return ((int)status).ToString();
			}
		}

		/// <summary>The process half of the sentence, empty when no process id is involved.</summary>
		private string DescribeProcess()
		{
			if (ProcessID.HasValue && PreviousProcessID.HasValue)
			{
				/* Two different ids across one beat is a relaunch that began and finished between
				 * heartbeats. It is the case the status alone cannot show: both sides can read
				 * Healthy while the process an operator was looking at is gone. */
				return ProcessID.Value == PreviousProcessID.Value
					? $"process {ProcessID.Value}"
					: $"relaunched as process {ProcessID.Value}, replacing {PreviousProcessID.Value}";
			}
			if (ProcessID.HasValue)
			{
				return PreviousStatus.HasValue
					? $"revived as process {ProcessID.Value}"
					: $"running as process {ProcessID.Value}";
			}
			if (PreviousProcessID.HasValue)
			{
				return $"process {PreviousProcessID.Value} ended";
			}
			return string.Empty;
		}

		/// <summary>The restart half of the sentence, empty when the counter did not move.</summary>
		private string DescribeRestarts()
		{
			if (!PreviousRestartAttempts.HasValue)
			{
				// A first sighting of something already mid-cycle, which is worth saying.
				return RestartAttempts > 0 ? $"{RestartAttempts} restarts already attempted" : string.Empty;
			}
			if (RestartAttempts > PreviousRestartAttempts.Value)
			{
				return $"restart attempt {RestartAttempts}";
			}
			if (RestartAttempts < PreviousRestartAttempts.Value)
			{
				/* A fall is a recovery, not a gap in the data: the daemon zeroes its counter the
				 * moment the application checks out healthy again. Reported rather than smoothed
				 * away, because it is the end of a restart loop and the operator is looking for
				 * exactly that. */
				return $"restart counter reset from {PreviousRestartAttempts.Value}";
			}
			return string.Empty;
		}
	}

	/// <summary>One page of observed application events.</summary>
	public sealed class DaemonAppEventPage
	{
		/// <summary>The rows on this page, newest first.</summary>
		public IReadOnlyList<DaemonAppEventData> Items { get; set; } = Array.Empty<DaemonAppEventData>();

		/// <summary>1-based page number.</summary>
		public int Page { get; set; }

		/// <summary>Rows per page.</summary>
		public int PageSize { get; set; }

		/// <summary>Total rows matching the filter.</summary>
		public int TotalCount { get; set; }
	}
}
