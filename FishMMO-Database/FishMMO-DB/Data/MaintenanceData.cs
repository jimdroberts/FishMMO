using System;
using System.Collections.Generic;
using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Data
{
	/// <summary>The two server tiers a maintenance operation can target.</summary>
	/// <remarks>
	/// The same vocabulary the per-server controls use — <c>world</c> and <c>scene</c> — so one
	/// string travels from the browser through the route to the row without translation. A login
	/// server is deliberately absent: it has no lock or shutdown column to write.
	/// </remarks>
	public static class MaintenanceTargetKinds
	{
		/// <summary>A world server.</summary>
		public const string World = "world";

		/// <summary>A scene server.</summary>
		public const string Scene = "scene";

		/// <summary>Whether a string names a tier that can be locked and shut down.</summary>
		/// <param name="kind">The candidate, in any case.</param>
		/// <param name="normalized">The canonical lowercase form, when it is one.</param>
		public static bool TryNormalize(string kind, out string normalized)
		{
			if (string.Equals(kind, World, StringComparison.OrdinalIgnoreCase))
			{
				normalized = World;
				return true;
			}
			if (string.Equals(kind, Scene, StringComparison.OrdinalIgnoreCase))
			{
				normalized = Scene;
				return true;
			}
			normalized = null;
			return false;
		}
	}

	/// <summary>One server an operator wants included in a maintenance operation.</summary>
	public sealed class MaintenanceTargetRequest
	{
		/// <summary><c>world</c> or <c>scene</c>.</summary>
		public string Kind { get; set; }

		/// <summary>The server's primary key, within its tier.</summary>
		public long ServerID { get; set; }
	}

	/// <summary>
	/// One planned maintenance window: what it covers, when it lands, and where it has got to.
	/// </summary>
	/// <remarks>
	/// The plan is rows, not a running procedure. Everything that <em>acts</em> on a server was
	/// written when the operation started; this record exists so an operator can see what was
	/// written, what the servers have done about it since, and — when it goes wrong — which
	/// target it went wrong on.
	/// </remarks>
	public sealed class MaintenanceOperationData
	{
		/// <summary>Surrogate key.</summary>
		public long ID { get; set; }

		/// <summary>What the operator called it, for the list.</summary>
		public string Name { get; set; }

		/// <summary>The operator account that planned it. Copied, with no foreign key.</summary>
		public string StartedBy { get; set; }

		/// <summary>Why. Mandatory, and also recorded in the audit log.</summary>
		public string Reason { get; set; }

		/// <summary>Derived from the targets on every read. Never set independently of them.</summary>
		public MaintenanceStatus Status { get; set; }

		/// <summary>How long players were given, in seconds.</summary>
		public int DrainSeconds { get; set; }

		/// <summary>When the operation was planned and the rows were written.</summary>
		public DateTime StartedUtc { get; set; }

		/// <summary>
		/// The absolute instant every target is due to stop.
		/// </summary>
		/// <remarks>
		/// One deadline for the whole operation rather than one per target, because a shard goes
		/// down together: staggering the deadlines would leave a world running with no scene
		/// servers under it, or the reverse.
		/// </remarks>
		public DateTime DeadlineUtc { get; set; }

		/// <summary>When every target reached a terminal state, or null while it is running.</summary>
		public DateTime? CompletedUtc { get; set; }

		/// <summary>The operator account that cancelled it, when one did.</summary>
		public string CancelledBy { get; set; }

		/// <summary>When it was cancelled.</summary>
		public DateTime? CancelledUtc { get; set; }

		/// <summary>Why it was cancelled. Also recorded in the audit log.</summary>
		public string CancelReason { get; set; }

		/// <summary>What happened in the end, in words, for whoever reads this later.</summary>
		public string Outcome { get; set; }

		/// <summary>Every server in the window, with its own progress.</summary>
		public IReadOnlyList<MaintenanceTargetData> Targets { get; set; } = Array.Empty<MaintenanceTargetData>();
	}

	/// <summary>One server inside a maintenance operation, and what has been observed of it.</summary>
	public sealed class MaintenanceTargetData
	{
		/// <summary>Surrogate key of the target row, not of the server.</summary>
		public long ID { get; set; }

		/// <summary>The operation this belongs to.</summary>
		public long OperationID { get; set; }

		/// <summary><c>world</c> or <c>scene</c>.</summary>
		public string Kind { get; set; }

		/// <summary>The server's primary key, within its tier.</summary>
		public long ServerID { get; set; }

		/// <summary>
		/// The server's name as it read when the operation was planned.
		/// </summary>
		/// <remarks>
		/// Copied rather than joined. A world server deletes its registration as it stops, which
		/// is the completion signal — so by the time the operation reads well there is nothing
		/// left to join to, and a window that finished successfully would list a blank name.
		/// </remarks>
		public string ServerName { get; set; }

		/// <summary>Where this target has got to.</summary>
		public MaintenanceStatus Status { get; set; }

		/// <summary>The deadline written on this server's row.</summary>
		public DateTime? ScheduledShutdownUtc { get; set; }

		/// <summary>
		/// When the lock was written.
		/// </summary>
		/// <remarks>
		/// Always the same instant as <see cref="ShutdownWrittenUtc"/>: scheduling a shutdown
		/// sets <c>locked</c> in the same statement. Recorded separately anyway, because the
		/// lock outlives the shutdown — cancelling clears one and not the other.
		/// </remarks>
		public DateTime? LockWrittenUtc { get; set; }

		/// <summary>When the shutdown deadline was written to the server's row.</summary>
		public DateTime? ShutdownWrittenUtc { get; set; }

		/// <summary>When the deadline was cleared again, by a cancellation.</summary>
		public DateTime? ShutdownClearedUtc { get; set; }

		/// <summary>
		/// Whether this server was pulsing when the operation was planned.
		/// </summary>
		/// <remarks>
		/// The difference between "it stopped" and "it was never listening". A row written for a
		/// process that is not running is read by nobody, ever, so a target that was already
		/// silent cannot be reported as having shut down cleanly.
		/// </remarks>
		public bool PulsingAtStart { get; set; }

		/// <summary>When this target was last looked at.</summary>
		public DateTime? ObservedUtc { get; set; }

		/// <summary>Players still on it at that moment.</summary>
		public int ObservedCharacterCount { get; set; }

		/// <summary>Whether its row still said locked.</summary>
		public bool ObservedLocked { get; set; }

		/// <summary>The deadline its row carried, which may not be the one this operation wrote.</summary>
		public DateTime? ObservedShutdownUtc { get; set; }

		/// <summary>When it last pulsed, or null once its registration is gone.</summary>
		public DateTime? ObservedLastPulseUtc { get; set; }

		/// <summary>Whether its registration row still exists at all.</summary>
		public bool ObservedRegistered { get; set; }

		/// <summary>
		/// What was concluded and why, in words.
		/// </summary>
		/// <remarks>
		/// Carries the reasoning, not just the failure: "cleared its own deadline as it exited"
		/// is the difference between a scene server that finished and one somebody cancelled,
		/// and the two look identical in the columns alone.
		/// </remarks>
		public string Note { get; set; }
	}
}
