using System;
using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One instruction for a daemon: start, stop or restart a named application.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The security of the whole control plane is the shape of this row.</b> It carries a
	/// verb from a closed enumeration and the <em>name</em> of an application the daemon
	/// already supervises. It carries no path, no arguments, no shell and nothing else the
	/// daemon executes. Somebody who can write to this table can restart a FishMMO process;
	/// they cannot make a game host run something new. Adding a column that changes that would
	/// turn a database password into remote code execution.
	/// </para>
	/// <para>
	/// <b>Claimed before it is executed.</b> A daemon takes a command atomically and stamps
	/// itself on it, so two daemons cannot both act on one row, and a daemon that dies
	/// mid-command leaves a claimed row that can be seen rather than one that is silently done
	/// twice.
	/// </para>
	/// <para>
	/// <b>And it expires.</b> A command nobody collected is abandoned rather than executed
	/// late: restarting a world server twenty minutes after an operator asked, once they have
	/// moved on and players are back on it, is worse than never doing it at all.
	/// </para>
	/// </remarks>
	public class DaemonCommandEntity
	{
		/// <summary>Surrogate key.</summary>
		public long ID { get; set; }

		/// <summary>Which host it is for. A daemon claims only its own.</summary>
		public string HostName { get; set; }

		/// <summary>Which supervised application, by the name the daemon knows.</summary>
		public string AppName { get; set; }

		/// <summary>Start, stop or restart. Nothing else exists.</summary>
		public DaemonCommandVerb Verb { get; set; }

		/// <summary>The operator account that asked. Copied, with no foreign key.</summary>
		public string RequestedBy { get; set; }

		/// <summary>When they asked.</summary>
		public DateTime RequestedUtc { get; set; }

		/// <summary>Why. Also recorded in the audit log.</summary>
		public string Reason { get; set; }

		/// <summary>After which it must not be executed.</summary>
		public DateTime ExpiresUtc { get; set; }

		/// <summary>When a daemon took it, or null while it is still waiting.</summary>
		public DateTime? ClaimedUtc { get; set; }

		/// <summary>
		/// Which daemon instance took it.
		/// </summary>
		/// <remarks>
		/// The instance, not the host: a daemon restarted mid-command comes back with a new
		/// identity, which is how an abandoned claim is told from one still being worked.
		/// </remarks>
		public string ClaimedBy { get; set; }

		/// <summary>When it finished, either way.</summary>
		public DateTime? CompletedUtc { get; set; }

		/// <summary>Whether it worked. Null until it has finished.</summary>
		public bool? Succeeded { get; set; }

		/// <summary>What happened, in words, for the operator who asked.</summary>
		public string Outcome { get; set; }
	}
}
