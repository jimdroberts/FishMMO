using System;
using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One application a daemon supervises, as it last reported it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>This row is a report, not a definition.</b> What the name actually launches — the
	/// executable path, the arguments, the working directory — lives only in the daemon's
	/// configuration file on that host and is deliberately absent here. A database row that
	/// carried a path would make the database able to decide what runs on a game server, which
	/// is precisely the property the command queue is designed not to have.
	/// </para>
	/// <para>
	/// The consequence is that this table cannot be used to add an application. A daemon
	/// reports what its own config already told it to supervise; an operator adding one edits
	/// that file.
	/// </para>
	/// </remarks>
	public class DaemonAppEntity
	{
		/// <summary>Surrogate key.</summary>
		public long ID { get; set; }

		/// <summary>The host supervising it.</summary>
		public long HostID { get; set; }

		/// <summary>Navigation to the host.</summary>
		public DaemonHostEntity Host { get; set; }

		/// <summary>The name from the daemon's configuration. Unique within a host.</summary>
		public string Name { get; set; }

		/// <summary>What the daemon last observed.</summary>
		public DaemonAppStatus Status { get; set; }

		/// <summary>The process id, when it is running.</summary>
		public int? ProcessID { get; set; }

		/// <summary>How many times the daemon has restarted it.</summary>
		public int RestartAttempts { get; set; }

		/// <summary>How many it is allowed before giving up.</summary>
		public int MaxRestartAttempts { get; set; }

		/// <summary>The port it is health-checked on.</summary>
		public int MonitoredPort { get; set; }

		/// <summary>When the daemon last reported on it.</summary>
		public DateTime LastReportedUtc { get; set; }
	}
}
