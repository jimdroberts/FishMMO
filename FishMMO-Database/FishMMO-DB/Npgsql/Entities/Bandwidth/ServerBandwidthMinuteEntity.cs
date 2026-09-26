using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// What one server process sent and received in one minute, as its own transport counted it.
	/// Table <c>server_bandwidth_minute</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Written by the process it describes, once a minute, as a set.</b> Each login, world and
	/// scene server samples its transport's cumulative counters on a 60 second timer and writes
	/// the difference since the minute began (<c>ServerBandwidthLedger</c> in the Unity project).
	/// The write is <c>INSERT … ON CONFLICT DO UPDATE SET col = EXCLUDED.col</c>, never an
	/// addition: a retry after a lost commit reply rewrites the same numbers, and a second sample
	/// that lands in the same minute rewrites the minute's running total. An additive upsert
	/// would count a retried write twice, which is the failure the idempotency work of
	/// 2026-09-25 exists to prevent.
	/// </para>
	/// <para>
	/// <b>Keyed by <see cref="InstanceID"/> as well as the server's name.</b> A restart starts
	/// its counters from zero in a new process; the instance id keeps the two processes' rows
	/// apart, so a restart inside one minute neither overwrites the old process's last minute nor
	/// mixes the new one's first minute into it. Summing over instances is the reader's job.
	/// </para>
	/// <para>
	/// <b>Measured values only.</b> Every counter here was counted by the process: the
	/// application bytes by the managed WebTransport socket, the UDP payload bytes and datagram
	/// counts by msquic. The bytes on the wire (payload plus 28 bytes of IPv4 and UDP header per
	/// datagram) are an estimate and are computed when read, never stored, so a change to the
	/// estimate never leaves history wrong. See <c>ServerBandwidthMath</c>.
	/// </para>
	/// <para>
	/// <b>No foreign key</b> to <c>login_servers</c>, <c>world_servers</c> or
	/// <c>scene_servers</c>, and the name is copied as text. Those rows are deleted when a
	/// server deregisters; the bandwidth it used must outlive it, or a month's egress total would
	/// shrink every time a server was retired. Append-mostly (a minute's row is rewritten only by
	/// its own process), so there is no xmin token either, as with <c>admin_audit_log</c>.
	/// </para>
	/// <para>
	/// Kept 14 days, then rolled into <see cref="ServerBandwidthHourEntity"/> and deleted, one
	/// whole hour at a time, by the Control Panel's rollup job.
	/// </para>
	/// </remarks>
	public class ServerBandwidthMinuteEntity
	{
		/// <summary>
		/// The UTC minute this row covers, from the DATABASE clock: the minute the process's sample
		/// was taken in, read from the database and truncated before the write.
		/// </summary>
		/// <remarks>
		/// Not the process's own clock. Rows from every host are compared and summed per minute,
		/// and a host whose clock ran a minute fast would file its traffic under the wrong minute.
		/// The traffic is the interval that ENDED in this minute; <see cref="IntervalMs"/> says
		/// how long that interval was.
		/// </remarks>
		public DateTime BucketStart { get; set; }

		/// <summary>Which tier: 1 login, 2 world, 3 scene (<c>ServerBandwidthKind</c>).</summary>
		public int ServerKind { get; set; }

		/// <summary>
		/// The server's configured <c>ServerName</c>, copied as text. With <see cref="ServerKind"/>
		/// it identifies the server across restarts.
		/// </summary>
		public string ServerName { get; set; }

		/// <summary>A random id the process took when it started. See the remarks on the class.</summary>
		public Guid InstanceID { get; set; }

		/// <summary>
		/// How long the counted interval was, in milliseconds on the process's monotonic clock.
		/// </summary>
		/// <remarks>
		/// About 60 000. More when a sample was deferred (the database was unreachable, so the
		/// counters kept accumulating and the next sample carried the whole stretch), and about
		/// double when two samples landed in one minute. A rate is always
		/// <c>bytes / interval</c>, never <c>bytes / 60</c>.
		/// </remarks>
		public long IntervalMs { get; set; }

		/// <summary>FishNet bytes handed to the transport, both channels. Measured.</summary>
		public long AppSentBytes { get; set; }

		/// <summary>FishNet bytes the transport delivered, both channels. Measured.</summary>
		public long AppRecvBytes { get; set; }

		/// <summary>
		/// UDP payload bytes sent: QUIC headers, AEAD tags, padding, ACKs, retransmissions and the
		/// handshake included. Measured by msquic, process-wide.
		/// </summary>
		public long UdpSentBytes { get; set; }

		/// <summary>UDP payload bytes received. Measured by msquic, process-wide.</summary>
		public long UdpRecvBytes { get; set; }

		/// <summary>
		/// UDP datagrams sent. Measured by msquic, which over-counts a flush sent in several batches
		/// (handshakes, bursts), so the header estimate built from it is an upper bound.
		/// </summary>
		public long UdpSentDatagrams { get; set; }

		/// <summary>UDP datagrams received. Measured, exact.</summary>
		public long UdpRecvDatagrams { get; set; }

		/// <summary>
		/// WebTransport sessions open when the minute was sampled (a gauge, not a total); the
		/// higher reading when two samples landed in the minute.
		/// </summary>
		public long SessionsActive { get; set; }

		/// <summary>Sessions accepted in the interval.</summary>
		public long SessionsOpened { get; set; }

		/// <summary>
		/// Connections refused before becoming a session in the interval: by the transport's own
		/// listener limits, by msquic under load, or for no matching ALPN.
		/// </summary>
		public long ConnectionsRefused { get; set; }

		/// <summary>Connections that started and failed their handshake in the interval.</summary>
		public long HandshakeFailures { get; set; }
	}
}
