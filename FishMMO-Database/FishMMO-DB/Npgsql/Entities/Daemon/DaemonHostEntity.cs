using System;
using System.Collections.Generic;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One machine running an AppHealthMonitor daemon.
	/// </summary>
	/// <remarks>
	/// The daemon writes this row and refreshes its heartbeat; nothing else does. A host that
	/// stops beating is the daemon going quiet, which is a different fault from an application
	/// going down — one needs somebody on that machine, the other does not.
	/// </remarks>
	public class DaemonHostEntity
	{
		/// <summary>Surrogate key.</summary>
		public long ID { get; set; }

		/// <summary>
		/// The machine's name, as the daemon reports it. Unique.
		/// </summary>
		/// <remarks>
		/// The identity a command is addressed to. A daemon claims only commands naming its own
		/// host, so this string is also a scope boundary: get it wrong on two machines and they
		/// will both execute each other's work.
		/// </remarks>
		public string HostName { get; set; }

		/// <summary>The daemon build running there.</summary>
		public string DaemonVersion { get; set; }

		/// <summary>What the operating system calls itself.</summary>
		public string OSDescription { get; set; }

		/// <summary>Logical processors, for judging load.</summary>
		public int ProcessorCount { get; set; }

		/// <summary>When the daemon process started.</summary>
		public DateTime StartedUtc { get; set; }

		/// <summary>Last time the daemon said it was alive.</summary>
		public DateTime LastHeartbeatUtc { get; set; }

		/// <summary>The applications it supervises.</summary>
		public ICollection<DaemonAppEntity> Apps { get; set; }
	}
}
