using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One server's traffic in one UTC hour, rolled up from <see cref="ServerBandwidthMinuteEntity"/>.
	/// Table <c>server_bandwidth_hour</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A pure function of the minute rows.</b> Every column is recomputed from the minutes of
	/// its hour by the Control Panel's rollup job and written with
	/// <c>ON CONFLICT DO UPDATE SET col = EXCLUDED.col</c>, so running the rollup twice, from two
	/// panels, or after a minute row was rewritten, always leaves the same row. Nothing is ever
	/// added to a stored value.
	/// </para>
	/// <para>
	/// Summed over process instances: a server that restarted inside the hour has one row here,
	/// and <see cref="Instances"/> says it restarted. Kept 13 months, so a year-on-year egress
	/// comparison is always possible. No foreign key and no xmin, for the reasons on the minute
	/// table.
	/// </para>
	/// </remarks>
	public class ServerBandwidthHourEntity
	{
		/// <summary>The UTC hour this row covers (database clock, as the minutes it came from).</summary>
		public DateTime BucketStart { get; set; }

		/// <summary>Which tier: 1 login, 2 world, 3 scene.</summary>
		public int ServerKind { get; set; }

		/// <summary>The server's configured name, copied as text.</summary>
		public string ServerName { get; set; }

		/// <summary>
		/// The counted intervals summed, in milliseconds: about 3 600 000 for a server that ran the
		/// whole hour, less for one that started or stopped inside it.
		/// </summary>
		public long IntervalMs { get; set; }

		/// <summary>FishNet bytes handed to the transport. Measured.</summary>
		public long AppSentBytes { get; set; }

		/// <summary>FishNet bytes delivered by the transport. Measured.</summary>
		public long AppRecvBytes { get; set; }

		/// <summary>UDP payload bytes sent. Measured by msquic.</summary>
		public long UdpSentBytes { get; set; }

		/// <summary>UDP payload bytes received. Measured by msquic.</summary>
		public long UdpRecvBytes { get; set; }

		/// <summary>UDP datagrams sent. Measured; an upper bound under bursts (see the minute table).</summary>
		public long UdpSentDatagrams { get; set; }

		/// <summary>UDP datagrams received. Measured.</summary>
		public long UdpRecvDatagrams { get; set; }

		/// <summary>The highest minute's session gauge in the hour.</summary>
		public long SessionsActiveMax { get; set; }

		/// <summary>
		/// The minutes' session gauges summed. Divided by <see cref="MinutesSampled"/> it is the
		/// average number of sessions open while the server was sampled; stored as a sum so that
		/// the column stays an exact integer and the division happens when read.
		/// </summary>
		public long SessionsActiveSum { get; set; }

		/// <summary>Sessions accepted in the hour.</summary>
		public long SessionsOpened { get; set; }

		/// <summary>Connections refused before becoming a session in the hour.</summary>
		public long ConnectionsRefused { get; set; }

		/// <summary>Connections that failed their handshake in the hour.</summary>
		public long HandshakeFailures { get; set; }

		/// <summary>How many minute rows the hour was rolled from.</summary>
		public long MinutesSampled { get; set; }

		/// <summary>How many process instances wrote those minutes. More than one means a restart.</summary>
		public long Instances { get; set; }
	}
}
