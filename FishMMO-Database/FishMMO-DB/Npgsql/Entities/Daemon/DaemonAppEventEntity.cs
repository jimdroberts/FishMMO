using System;
using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One observed change in a supervised application, appended when a heartbeat differs from
	/// what was already stored.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>This table exists because <see cref="DaemonAppEntity"/> has no memory.</b> That row is
	/// overwritten on every heartbeat, so it says only what is true now. A game server that
	/// restart-looped at three in the morning and came back before anybody looked leaves no
	/// trace anywhere: the status reads Healthy, the restart counter has been reset, and the
	/// event is gone. This is the record of what the supervisor saw while nobody was watching.
	/// </para>
	/// <para>
	/// <b>It is not process output, and the daemon writes none of it.</b> Every column is either
	/// a value from a closed enumeration, an integer, or a name the daemon already reported on
	/// its own row — there is no free-text column, and nothing here is a string the daemon
	/// chose. The rows are derived server-side by comparing an incoming report against the
	/// stored one, which is what keeps a compromised daemon from putting attacker-chosen text
	/// on an operator screen. A description of the transition is composed from these values at
	/// read time; it is never stored and never supplied. <b>Adding a column the daemon fills
	/// with text would undo that in one commit.</b>
	/// </para>
	/// <para>
	/// <b>Append-only, and free of foreign keys</b>, for the reasons that govern
	/// <c>admin_audit_log</c>: nothing updates or deletes a row, there is no xmin token because
	/// there is no second writer to mediate, and the host and application are copied as text so
	/// that decommissioning a host does not take the history of what it did with it. A cascade
	/// here would delete the evidence exactly when a machine is removed for having misbehaved.
	/// </para>
	/// </remarks>
	public class DaemonAppEventEntity
	{
		/// <summary>Surrogate key. Also the tie-break for rows inside one timestamp.</summary>
		public long ID { get; set; }

		/// <summary>The host that reported it, copied as text with no foreign key.</summary>
		public string HostName { get; set; }

		/// <summary>The supervised application, by the name the daemon knows it as.</summary>
		public string AppName { get; set; }

		/// <summary>
		/// What was stored before this observation, or null when this is the first sighting.
		/// </summary>
		/// <remarks>
		/// Null is a fact rather than a gap: it means no row existed for this application, which
		/// is what a newly configured application — or one the panel has never seen a heartbeat
		/// for — looks like.
		/// </remarks>
		public DaemonAppStatus? PreviousStatus { get; set; }

		/// <summary>What the daemon observed this time.</summary>
		public DaemonAppStatus Status { get; set; }

		/// <summary>The process id now, when it is running.</summary>
		public int? ProcessID { get; set; }

		/// <summary>
		/// The process id before, when there was one.
		/// </summary>
		/// <remarks>
		/// The whole story on its own in one case: a status that reads Healthy on both sides
		/// with a different process id is a crash and a relaunch that finished inside one poll
		/// interval. Without this column that beat is indistinguishable from nothing happening.
		/// </remarks>
		public int? PreviousProcessID { get; set; }

		/// <summary>Restarts in the current failure cycle, as reported.</summary>
		public int RestartAttempts { get; set; }

		/// <summary>
		/// The restart count before, or null on a first sighting.
		/// </summary>
		/// <remarks>
		/// Not in the original sketch for this table, and added because a decrease is meaningful:
		/// the daemon zeroes its counter the moment an application checks out healthy again, so
		/// a fall from three to zero <em>is</em> the recovery. A row that carried only the new
		/// value would show a zero indistinguishable from an application that never failed.
		/// </remarks>
		public int? PreviousRestartAttempts { get; set; }

		/// <summary>When the heartbeat carrying this observation was recorded.</summary>
		/// <remarks>
		/// The moment the database saw it, not a clock reading from the reporting host. A
		/// timestamp chosen by the daemon would let a host with a wrong clock — or a lying one —
		/// reorder the history of every other machine on the page.
		/// </remarks>
		public DateTime ObservedUtc { get; set; }
	}
}
