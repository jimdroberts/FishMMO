using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using FishNet.Transporting.WebTransport.Native;
#if UNITY_WEBGL && !UNITY_EDITOR
using FishNet.Transporting.WebTransport.WebGL;
#endif

namespace FishNet.Transporting.WebTransport
{
	/// <summary>
	/// What this process has sent and received through the WebTransport transport, and the one
	/// place game code reads it: <see cref="Capture"/>.
	/// </summary>
	/// <remarks>
	/// <para>Every total is process-lifetime. A client's server hop tears its socket down and builds
	/// a new one; nothing here is reset by that, so a rate that spans a hop is still right. The
	/// native counters also survive a deinitialise of the library (they are carried), and a Unity
	/// domain reload starts the managed and native totals together from zero.</para>
	/// <para>This is the transport's own accounting, not FishNet's <c>StatisticsManager</c>, which is
	/// disabled in release builds and on servers and double-counts in development builds.</para>
	/// <para>The sockets feed it through <see cref="TransportTrafficCounters"/>: two or three
	/// uncontended <see cref="Interlocked"/> adds per message and nothing else.</para>
	/// </remarks>
	public static class TransportTraffic
	{
		private static readonly double SecondsPerTick = 1.0 / Stopwatch.Frequency;

		/// <summary>The monotonic clock every snapshot is stamped with (Stopwatch seconds).</summary>
		public static double NowSeconds => Stopwatch.GetTimestamp() * SecondsPerTick;

		#region Native (msquic) counters
		/* msquic's counters live in the library and start from zero at wt_init. Two things break
		 * "process lifetime" if they are read raw: a Unity domain reload resets the managed totals
		 * while the loaded library keeps counting, and a deinitialise followed by a new
		 * initialise resets the library's. So a baseline is taken at every initialise and the
		 * running total is folded into `carried` before every deinitialise; the reported total is
		 * carried + (current - baseline). */
		private static readonly object nativeLock = new object();
		private static bool nativeLive;
		private static bool nativeEverLive;
		private static WebTransportNative.WtGlobalCounters nativeBaseline;
		private static WebTransportNative.WtGlobalCounters nativeCarried;
		private static bool nativeReadFailureLogged;

		/// <summary>Called by <see cref="WebTransportNative.EnsureInitialized"/> after a successful <c>wt_init</c>.</summary>
		internal static void OnNativeInitialized()
		{
			lock (nativeLock)
			{
				if (TryReadNative(out WebTransportNative.WtGlobalCounters current))
				{
					nativeBaseline = current;
					nativeLive = true;
					nativeEverLive = true;
				}
				else
				{
					nativeLive = false;
				}
			}
		}

		/// <summary>Called by <see cref="WebTransportNative.Deinitialize"/> just before <c>wt_deinit</c>.</summary>
		internal static void OnNativeDeinitializing()
		{
			lock (nativeLock)
			{
				if (nativeLive && TryReadNative(out WebTransportNative.WtGlobalCounters current))
					nativeCarried = Combine(in nativeCarried, in current, in nativeBaseline, keepGauges: false);
				nativeLive = false;
			}
		}

		private static bool TryReadNative(out WebTransportNative.WtGlobalCounters counters)
		{
			counters = default;
#if UNITY_WEBGL && !UNITY_EDITOR
			return false;
#else
			counters.StructSize = WebTransportNative.WtGlobalCounters.NativeSize;
			try
			{
				int rc = WebTransportNative.wt_get_global_counters(ref counters);
				return rc == 0 && counters.StructSize >= WebTransportNative.WtGlobalCounters.NativeSize;
			}
			catch (Exception ex)
			{
				/* Unreachable after the ABI check in EnsureInitialized; logged once in case a
				 * library is swapped underneath a running process. */
				if (!nativeReadFailureLogged)
				{
					nativeReadFailureLogged = true;
					UnityEngine.Debug.LogError($"[WebTransport] Reading the native traffic counters failed; the QUIC layer will show as unavailable. {ex}");
				}
				return false;
			}
#endif
		}

		/// <summary>
		/// carried + (current - baseline) for every cumulative counter. Gauges (connections active and
		/// connected now) are taken from <paramref name="current"/>, or zeroed when folding for a
		/// deinitialise.
		/// </summary>
		private static WebTransportNative.WtGlobalCounters Combine(
			in WebTransportNative.WtGlobalCounters carried,
			in WebTransportNative.WtGlobalCounters current,
			in WebTransportNative.WtGlobalCounters baseline,
			bool keepGauges)
		{
			return new WebTransportNative.WtGlobalCounters
			{
				StructSize = WebTransportNative.WtGlobalCounters.NativeSize,
				Version = current.Version,
				UdpSendDatagrams = Add(carried.UdpSendDatagrams, current.UdpSendDatagrams, baseline.UdpSendDatagrams),
				UdpRecvDatagrams = Add(carried.UdpRecvDatagrams, current.UdpRecvDatagrams, baseline.UdpRecvDatagrams),
				UdpSendBytes = Add(carried.UdpSendBytes, current.UdpSendBytes, baseline.UdpSendBytes),
				UdpRecvBytes = Add(carried.UdpRecvBytes, current.UdpRecvBytes, baseline.UdpRecvBytes),
				AppSendBytes = Add(carried.AppSendBytes, current.AppSendBytes, baseline.AppSendBytes),
				AppRecvBytes = Add(carried.AppRecvBytes, current.AppRecvBytes, baseline.AppRecvBytes),
				ConnCreated = Add(carried.ConnCreated, current.ConnCreated, baseline.ConnCreated),
				ConnActive = keepGauges ? current.ConnActive : 0,
				ConnConnected = keepGauges ? current.ConnConnected : 0,
				ConnHandshakeFail = Add(carried.ConnHandshakeFail, current.ConnHandshakeFail, baseline.ConnHandshakeFail),
				ConnAppReject = Add(carried.ConnAppReject, current.ConnAppReject, baseline.ConnAppReject),
				ConnLoadReject = Add(carried.ConnLoadReject, current.ConnLoadReject, baseline.ConnLoadReject),
				ConnNoAlpn = Add(carried.ConnNoAlpn, current.ConnNoAlpn, baseline.ConnNoAlpn),
				ConnProtocolErrors = Add(carried.ConnProtocolErrors, current.ConnProtocolErrors, baseline.ConnProtocolErrors),
				PktsSuspectedLost = Add(carried.PktsSuspectedLost, current.PktsSuspectedLost, baseline.PktsSuspectedLost),
				PktsDropped = Add(carried.PktsDropped, current.PktsDropped, baseline.PktsDropped),
				PktsDecryptionFail = Add(carried.PktsDecryptionFail, current.PktsDecryptionFail, baseline.PktsDecryptionFail),
				StatelessRetrySent = Add(carried.StatelessRetrySent, current.StatelessRetrySent, baseline.StatelessRetrySent),
				StatelessResetSent = Add(carried.StatelessResetSent, current.StatelessResetSent, baseline.StatelessResetSent),
			};
		}

		/// <summary>carried + (current - baseline), treating a current below its baseline as no traffic.</summary>
		private static ulong Add(ulong carried, ulong current, ulong baseline)
		{
			return carried + (current > baseline ? current - baseline : 0UL);
		}
		#endregion

		#region Connection statistics
		/* A connection-level read blocks until that connection's msquic worker answers, so it is
		 * made by the client socket itself, on its own polling thread, at most once a second, and
		 * only while somebody has captured a snapshot recently (a hidden panel costs nothing).
		 * Capture only copies the last published figures. */
		private static readonly object connectionLock = new object();
		private static TransportConnectionStats publishedConnection = TransportConnectionStats.Unknown;
		private static object publishedConnectionOwner;
		private static long connectionDemandTicks;

		/// <summary>How long a <see cref="Capture"/> keeps the client refreshing its connection statistics.</summary>
		private static readonly long ConnectionDemandWindowTicks = 5 * Stopwatch.Frequency;

		/// <summary>True while a snapshot has been captured within the last few seconds.</summary>
		internal static bool ConnectionStatsWanted(long nowTicks)
		{
			long demand = Interlocked.Read(ref connectionDemandTicks);
			return demand != 0 && nowTicks - demand < ConnectionDemandWindowTicks;
		}

		/// <summary>Publishes a client socket's latest connection statistics.</summary>
		internal static void PublishConnectionStats(object owner, in TransportConnectionStats stats)
		{
			lock (connectionLock)
			{
				publishedConnection = stats;
				publishedConnectionOwner = owner;
			}
		}

		/// <summary>
		/// Withdraws a socket's connection statistics (it disconnected, or the read failed), unless a
		/// newer socket has published since.
		/// </summary>
		internal static void ClearConnectionStats(object owner)
		{
			lock (connectionLock)
			{
				if (publishedConnectionOwner != null && !ReferenceEquals(publishedConnectionOwner, owner))
					return;
				publishedConnection = TransportConnectionStats.Unknown;
				publishedConnectionOwner = null;
			}
		}
		#endregion

		#region Capture
		/// <summary>
		/// Takes a snapshot of everything this process has sent and received. Allocation-free and
		/// non-blocking; safe from any thread on native backends (WebGL has only one thread).
		/// </summary>
		/// <remarks>
		/// <para>Sample at a steady cadence (once a second is plenty) and turn pairs of snapshots
		/// into rates with <see cref="TransportTrafficMath.TryComputeRates"/>.</para>
		/// <para>A capture also keeps the client's connection statistics fresh for the next few
		/// seconds (see <see cref="TransportTrafficSnapshot.Connection"/>); the first capture after a
		/// quiet spell may therefore carry none.</para>
		/// </remarks>
		public static void Capture(out TransportTrafficSnapshot snapshot)
		{
			snapshot = default;
			long now = Stopwatch.GetTimestamp();
			snapshot.TimestampSeconds = now * SecondsPerTick;

			snapshot.AppSentReliableBytes = Interlocked.Read(ref TransportTrafficCounters.Sent.ReliableBytes);
			snapshot.AppSentReliableMessages = Interlocked.Read(ref TransportTrafficCounters.Sent.ReliableMessages);
			snapshot.AppSentUnreliableBytes = Interlocked.Read(ref TransportTrafficCounters.Sent.UnreliableBytes);
			snapshot.AppSentUnreliableMessages = Interlocked.Read(ref TransportTrafficCounters.Sent.UnreliableMessages);
			snapshot.FramingSentBytes = Interlocked.Read(ref TransportTrafficCounters.Sent.FramingBytes);
			snapshot.AppRecvReliableBytes = Interlocked.Read(ref TransportTrafficCounters.Received.ReliableBytes);
			snapshot.AppRecvReliableMessages = Interlocked.Read(ref TransportTrafficCounters.Received.ReliableMessages);
			snapshot.AppRecvUnreliableBytes = Interlocked.Read(ref TransportTrafficCounters.Received.UnreliableBytes);
			snapshot.AppRecvUnreliableMessages = Interlocked.Read(ref TransportTrafficCounters.Received.UnreliableMessages);
			snapshot.FramingRecvBytes = Interlocked.Read(ref TransportTrafficCounters.Received.FramingBytes);

			snapshot.SessionsOpened = Interlocked.Read(ref TransportTrafficCounters.SessionsOpened);
			snapshot.SessionsActive = Math.Max(0, Volatile.Read(ref TransportTrafficCounters.ClientSessionsActive)) +
				Math.Max(0, Volatile.Read(ref TransportTrafficCounters.ServerSessionsActive));

			Interlocked.Exchange(ref connectionDemandTicks, now);

#if UNITY_WEBGL && !UNITY_EDITOR
			snapshot.Backend = TransportTrafficBackend.Browser;
			CaptureBrowser(ref snapshot);
#else
			snapshot.Backend = TransportTrafficBackend.Native;
			CaptureNative(ref snapshot);
			lock (connectionLock)
			{
				snapshot.Connection = publishedConnection;
			}
#endif
		}

		private static void CaptureNative(ref TransportTrafficSnapshot snapshot)
		{
			WebTransportNative.WtGlobalCounters total;
			lock (nativeLock)
			{
				if (!nativeEverLive)
				{
					/* The library has never been initialised in this process (no connection yet,
					 * or it failed to load): the QUIC layer is unknown, not zero. */
					SetQuicUnavailable(ref snapshot);
					return;
				}
				if (nativeLive)
				{
					if (!TryReadNative(out WebTransportNative.WtGlobalCounters current))
					{
						SetQuicUnavailable(ref snapshot);
						return;
					}
					total = Combine(in nativeCarried, in current, in nativeBaseline, keepGauges: true);
				}
				else
				{
					/* Deinitialised: the totals up to that moment still stand; nothing has moved since. */
					total = nativeCarried;
				}
			}

			snapshot.QuicMeasure = TrafficMeasure.Measured;
			snapshot.QuicSentBytes = ToLong(total.UdpSendBytes);
			snapshot.QuicRecvBytes = ToLong(total.UdpRecvBytes);
			snapshot.DatagramMeasure = TrafficMeasure.Measured;
			snapshot.UdpSentDatagrams = ToLong(total.UdpSendDatagrams);
			snapshot.UdpRecvDatagrams = ToLong(total.UdpRecvDatagrams);
			snapshot.Process = TransportTrafficMath.FromNative(in total);
		}

		private static void SetQuicUnavailable(ref TransportTrafficSnapshot snapshot)
		{
			snapshot.QuicMeasure = TrafficMeasure.Unavailable;
			snapshot.QuicSentBytes = -1;
			snapshot.QuicRecvBytes = -1;
			snapshot.DatagramMeasure = TrafficMeasure.Unavailable;
			snapshot.UdpSentDatagrams = -1;
			snapshot.UdpRecvDatagrams = -1;
			snapshot.Process = TransportProcessCounters.Unknown;
		}

		private static long ToLong(ulong value)
		{
			return value > long.MaxValue ? long.MaxValue : (long)value;
		}

#if UNITY_WEBGL && !UNITY_EDITOR
		/// <summary>Reused for every WTGetStats call; WebGL is single-threaded.</summary>
		private static readonly double[] browserStats = new double[TransportTrafficMath.BrowserStats.Count];

		private static void CaptureBrowser(ref TransportTrafficSnapshot snapshot)
		{
			int mask;
			try
			{
				/* Returns the figures of the last getStats() answers and asks each live session for
				 * fresh ones, so what a capture sees is at most one capture old. */
				mask = WebTransportJSLib.WTGetStats(browserStats, browserStats.Length);
			}
			catch (Exception)
			{
				mask = 0;
			}
			TransportTrafficMath.ApplyBrowserStats(ref snapshot, browserStats, mask, snapshot.TimestampSeconds);
		}
#endif
		#endregion
	}

	/// <summary>
	/// The application-layer counters the sockets add to on every message, kept apart from
	/// <see cref="TransportTraffic"/> so that touching them never runs a type initializer that
	/// allocates.
	/// </summary>
	/// <remarks>
	/// Receives are counted on msquic worker threads, and the first message can reach a worker
	/// before anything on the main thread has touched these statics. A managed allocation on
	/// those threads is what the sockets copy into unmanaged memory to avoid (IL2CPP); this class
	/// holds only value-type statics, so its initializer allocates nothing wherever it first runs.
	/// Read them through <see cref="TransportTraffic.Capture"/>.
	/// </remarks>
	internal static class TransportTrafficCounters
	{
		/// <summary>
		/// One direction's counters. Sends run on the main thread and receives on msquic worker
		/// threads; the padding keeps the two directions off each other's cache lines, so a busy
		/// server's receive adds do not keep invalidating the line its sends are adding to.
		/// </summary>
		[StructLayout(LayoutKind.Explicit, Size = 192)]
		internal struct DirectionCounters
		{
			[FieldOffset(64)] public long ReliableBytes;
			[FieldOffset(72)] public long ReliableMessages;
			[FieldOffset(80)] public long UnreliableBytes;
			[FieldOffset(88)] public long UnreliableMessages;
			[FieldOffset(96)] public long FramingBytes;
		}

		internal static DirectionCounters Sent;
		internal static DirectionCounters Received;
		internal static long SessionsOpened;
		internal static int ClientSessionsActive;
		internal static int ServerSessionsActive;

		/// <summary>Counts one message handed to the transport. Hot path: adds only.</summary>
		/// <param name="channel">0 reliable (stream), 1 unreliable (datagram).</param>
		/// <param name="length">Application bytes, without the transport's framing.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static void CountSent(byte channel, int length)
		{
			Count(ref Sent, channel, length);
		}

		/// <summary>Counts one message the transport delivered. Hot path, any thread: adds only.</summary>
		/// <param name="channel">0 reliable (stream), 1 unreliable (datagram).</param>
		/// <param name="length">Application bytes, the framing already stripped.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static void CountReceived(byte channel, int length)
		{
			Count(ref Received, channel, length);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static void Count(ref DirectionCounters counters, byte channel, int length)
		{
			if (channel == 1)
			{
				Interlocked.Add(ref counters.UnreliableBytes, length);
				Interlocked.Increment(ref counters.UnreliableMessages);
			}
			else
			{
				/* The reliable channel is one byte stream, so every message carries a length
				 * prefix; its size is a pure function of the length, counted exactly here rather
				 * than estimated later. */
				Interlocked.Add(ref counters.ReliableBytes, length);
				Interlocked.Increment(ref counters.ReliableMessages);
				Interlocked.Add(ref counters.FramingBytes, TransportTrafficMath.VarintLength(length));
			}
		}

		/// <summary>A client socket reached Started (one per successful connect, so one per hop).</summary>
		internal static void OnClientSessionStarted()
		{
			Interlocked.Increment(ref SessionsOpened);
			Interlocked.Increment(ref ClientSessionsActive);
		}

		/// <summary>A client socket left Started.</summary>
		internal static void OnClientSessionEnded()
		{
			Interlocked.Decrement(ref ClientSessionsActive);
		}

		/// <summary>A server accepted a session (the client joined its connection set).</summary>
		internal static void OnServerSessionOpened()
		{
			Interlocked.Increment(ref SessionsOpened);
			Interlocked.Increment(ref ServerSessionsActive);
		}

		/// <summary><paramref name="count"/> sessions left a server's connection set.</summary>
		internal static void OnServerSessionsClosed(int count)
		{
			if (count > 0)
				Interlocked.Add(ref ServerSessionsActive, -count);
		}
	}
}
