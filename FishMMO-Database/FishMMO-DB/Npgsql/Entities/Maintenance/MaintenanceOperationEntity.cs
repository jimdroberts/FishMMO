using System;
using System.Collections.Generic;
using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One planned maintenance window across a set of servers.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>This row is the plan, and the plan is the whole point.</b> Taking a shard down is a
	/// sequence — lock so nobody new joins, wait for players to drain, then stop — and done by
	/// hand across a dozen servers somebody misses one. Writing it down means the operator can
	/// close their laptop: the drain does not live in a browser tab.
	/// </para>
	/// <para>
	/// <b>It carries no steps to execute.</b> Everything that acts on a server is written the
	/// moment the operation starts, into the servers' own <c>locked</c> and
	/// <c>shutdown_at_utc</c> columns, which each server reads back on its next pulse and
	/// adopts. The deadline is absolute, so the drain counts down inside every server process
	/// whether or not anything else is running — no panel, no scheduler, no tab. What is left
	/// here afterwards is a record and an observation, which is why <see cref="Status"/> is
	/// derived from the target rows rather than advanced by anybody.
	/// </para>
	/// <para>
	/// No foreign key from <see cref="StartedBy"/> to accounts, for the audit log's reason: who
	/// took a shard down must outlive the account that did it.
	/// </para>
	/// </remarks>
	public class MaintenanceOperationEntity
	{
		/// <summary>Surrogate key.</summary>
		public long ID { get; set; }

		/// <summary>What the operator called this window.</summary>
		public string Name { get; set; }

		/// <summary>The operator account that planned it. Copied, with no foreign key.</summary>
		public string StartedBy { get; set; }

		/// <summary>Why. Mandatory at the API, and recorded in the audit log as well.</summary>
		public string Reason { get; set; }

		/// <summary>Where the window has got to. Derived from the targets; see the remarks.</summary>
		public MaintenanceStatus Status { get; set; }

		/// <summary>How long players were given, in seconds.</summary>
		public int DrainSeconds { get; set; }

		/// <summary>When the plan was written, which is when the servers were locked.</summary>
		public DateTime StartedUtc { get; set; }

		/// <summary>
		/// The absolute instant every target is due to stop.
		/// </summary>
		/// <remarks>
		/// Stored on the operation as well as on each target so a cancelled or half-written
		/// window still says what it was going to do, and so the drain's end can be read without
		/// loading every target row.
		/// </remarks>
		public DateTime DeadlineUtc { get; set; }

		/// <summary>When every target reached a terminal state.</summary>
		public DateTime? CompletedUtc { get; set; }

		/// <summary>The operator account that cancelled it.</summary>
		public string CancelledBy { get; set; }

		/// <summary>When it was cancelled.</summary>
		public DateTime? CancelledUtc { get; set; }

		/// <summary>Why it was cancelled.</summary>
		public string CancelReason { get; set; }

		/// <summary>What happened in the end, in words.</summary>
		public string Outcome { get; set; }

		/// <summary>The servers in this window.</summary>
		public ICollection<MaintenanceTargetEntity> Targets { get; set; }
	}
}
