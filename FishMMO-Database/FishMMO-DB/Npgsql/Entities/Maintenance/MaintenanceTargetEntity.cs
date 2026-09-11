using System;
using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One server inside a maintenance window, with what was written to it and what has been
	/// seen of it since.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>There is no foreign key to <c>world_servers</c> or <c>scene_servers</c>, and there
	/// must not be.</b> A world server deletes its registration as it exits — that deletion is
	/// precisely how this row knows the shutdown worked — so a foreign key would either block
	/// the server from stopping or cascade away the record of the window that stopped it. The
	/// server id and the name are copied for the same reason.
	/// </para>
	/// <para>
	/// The columns split into two groups, and keeping them apart is what makes this row
	/// readable: <c>*_written_utc</c> is what this operation did, and <c>observed_*</c> is what
	/// the server's row said the last time anybody looked. A disagreement between them is not a
	/// bug, it is the interesting case — somebody rescheduled the shutdown by hand, or the
	/// server never read it.
	/// </para>
	/// </remarks>
	public class MaintenanceTargetEntity
	{
		/// <summary>Surrogate key of this target row, not of the server.</summary>
		public long ID { get; set; }

		/// <summary>The window this belongs to.</summary>
		public long OperationID { get; set; }

		/// <summary>The window this belongs to.</summary>
		public MaintenanceOperationEntity Operation { get; set; }

		/// <summary><c>world</c> or <c>scene</c>. The same vocabulary as the per-server routes.</summary>
		public string Kind { get; set; }

		/// <summary>The server's primary key, within its tier. Not a foreign key; see the remarks.</summary>
		public long ServerID { get; set; }

		/// <summary>The server's name as it read when the window was planned.</summary>
		public string ServerName { get; set; }

		/// <summary>Where this target has got to.</summary>
		public MaintenanceStatus Status { get; set; }

		/// <summary>The deadline this operation wrote to the server's row.</summary>
		public DateTime? ScheduledShutdownUtc { get; set; }

		/// <summary>When the lock was written. The lock outlives the shutdown; see the data type.</summary>
		public DateTime? LockWrittenUtc { get; set; }

		/// <summary>
		/// When the deadline was written to the server's row.
		/// </summary>
		/// <remarks>
		/// Null means the write has not happened or did not succeed, which is a state this row
		/// is allowed to be in: the plan is committed before the servers are touched, so a panel
		/// that dies between the two leaves a visible half-written window rather than a locked
		/// shard nobody has a record of. The advance pass finishes the writing.
		/// </remarks>
		public DateTime? ShutdownWrittenUtc { get; set; }

		/// <summary>When a cancellation cleared the deadline again. The lock stays set.</summary>
		public DateTime? ShutdownClearedUtc { get; set; }

		/// <summary>Whether the server was pulsing when the window was planned.</summary>
		public bool PulsingAtStart { get; set; }

		/// <summary>When this target's server row was last read.</summary>
		public DateTime? ObservedUtc { get; set; }

		/// <summary>Players on it at that moment. The drain, in one number.</summary>
		public int ObservedCharacterCount { get; set; }

		/// <summary>Whether its row still said locked.</summary>
		public bool ObservedLocked { get; set; }

		/// <summary>The deadline its row carried, which need not be the one written here.</summary>
		public DateTime? ObservedShutdownUtc { get; set; }

		/// <summary>When it last pulsed. Null once its registration is gone.</summary>
		public DateTime? ObservedLastPulseUtc { get; set; }

		/// <summary>Whether its registration row still exists.</summary>
		public bool ObservedRegistered { get; set; }

		/// <summary>What was concluded, and why, in words.</summary>
		public string Note { get; set; }
	}
}
