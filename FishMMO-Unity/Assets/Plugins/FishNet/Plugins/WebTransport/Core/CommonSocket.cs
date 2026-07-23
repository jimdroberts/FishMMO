using FishNet.Managing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FishNet.Transporting.WebTransport
{
	/// <summary>
	/// Abstract base for ClientSocket and ServerSocket.
	/// Manages connection state tracking and shared packet queuing.
	///
	/// Key difference from Bayou: WebTransport natively separates reliable
	/// (stream) and unreliable (datagram) channels, so NO channel suffix byte
	/// is appended to packets. The channel is determined at send time by
	/// which native function is called (wt_send_stream vs wt_send_datagram).
	/// </summary>
	public abstract class CommonSocket
	{
		/// <summary>
		/// Maximum allowed packet size for incoming data (streams).
		/// Packets exceeding this size are rejected for security reasons.
		/// 65536 bytes (64 KB) is generous for game data while preventing
		/// runaway allocations from a malicious or buggy peer.
		/// </summary>
		protected const int MaxPacketSize = 65536;

		#region Public
		/// <summary>
		/// Current ConnectionState.
		/// </summary>
		private volatile LocalConnectionState connectionState = LocalConnectionState.Stopped;

		/// <summary>
		/// Returns the current ConnectionState.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal LocalConnectionState GetConnectionState()
		{
			return connectionState;
		}

		/// <summary>
		/// Sets a new connection state and fires the appropriate transport event.
		/// </summary>
		protected void SetConnectionState(LocalConnectionState connectionState, bool asServer)
		{
			if (this.connectionState == connectionState)
				return;

			/* Guard: transport may be null if Initialize() was not called before SetConnectionState.
			 * This can happen during error-recovery paths where the socket is torn down before
			 * it was fully initialized. Silently return instead of throwing NullReferenceException. */
			if (transport == null)
			{
				LogTransportWarning($"[WebTransport] SetConnectionState({connectionState}, {asServer}) skipped: transport is null. Was Initialize() called?");
				return;
			}
			// transport is guaranteed non-null after this guard

			this.connectionState = connectionState;
			if (asServer)
				transport.HandleServerConnectionState(new ServerConnectionStateArgs(connectionState, transport.Index));
			else
				transport.HandleClientConnectionState(new ClientConnectionStateArgs(connectionState, transport.Index));
		}
		#endregion

		#region Protected
		/// <summary>
		/// Transport controlling this socket.
		/// </summary>
		protected Transport transport = null;
		#endregion

		#region Logging helpers
		/// <summary>
		/// Logs a warning message, routing through FishNet's NetworkManager when
		/// the transport is initialized, falling back to UnityEngine.Debug.
		/// This keeps WebTransport logging controllable through FishNet's
		/// <c>NetworkManager.LogLevel</c> configuration.
		/// </summary>
		protected void LogTransportWarning(string message)
		{
			if (transport?.NetworkManager != null)
				transport.NetworkManager.LogWarning(message);
			else
				UnityEngine.Debug.LogWarning(message);
		}

		/// <summary>
		/// Logs an error message, routing through FishNet's NetworkManager when
		/// the transport is initialized, falling back to UnityEngine.Debug.
		/// </summary>
		protected void LogTransportError(string message)
		{
			if (transport?.NetworkManager != null)
				transport.NetworkManager.LogError(message);
			else
				UnityEngine.Debug.LogError(message);
		}
		#endregion

		/// <summary>
		/// Queues a packet for deferred sending during the next <c>IterateOutgoing</c> call.
		/// The packet's data is copied into a buffer from <see cref="ByteArrayPool"/>.
		/// Backpressure is enforced at <see cref="MaxOutgoingQueueSize"/> — reliable
		/// packets exceeding the limit are dropped with a warning; unreliable packets
		/// are silently dropped.
		/// </summary>
		/// <param name="queue">The outgoing packet queue to enqueue to.</param>
		/// <param name="channelId">The channel: 0 = reliable (stream), 1 = unreliable (datagram).</param>
		/// <param name="segment">The data segment to send.</param>
		/// <param name="connectionId">The target connection ID, or -1 for broadcast on the server.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal void Send(ConcurrentQueue<Packet> queue, byte channelId, ArraySegment<byte> segment, int connectionId)
		{
			if (GetConnectionState() != LocalConnectionState.Started)
				return;

			Packet outgoing = new Packet(connectionId, segment, channelId);
			queue.Enqueue(outgoing);

			// Backpressure: if queue exceeds limit, drop the oldest packet.
			// ConcurrentQueue is unbounded; we enforce the limit by dequeuing
			// after enqueue rather than checking-before-enqueue (which would
			// have a TOCTOU race with concurrent enqueues).
			if (queue.Count > MaxOutgoingQueueSize)
			{
				if (queue.TryDequeue(out Packet dropped))
					dropped.Dispose();
				if (channelId == 0)
					LogTransportWarning($"[WebTransport] Outgoing queue full ({MaxOutgoingQueueSize}); dropping reliable packet.");
			}
		}

		/// <summary>
		/// Maximum packets in the outgoing queue before backpressure kicks in.
		/// Prevents unbounded memory growth under network congestion.
		/// </summary>
		protected const int MaxOutgoingQueueSize = 10000;

		/// <summary>
		/// Dequeues and disposes all packets in the given queue, returning their
		/// backing buffers to <see cref="ByteArrayPool"/>.  Safe to call when the
		/// connection is stopped — stale packets from a previous session are
		/// cleaned up without sending.
		/// </summary>
		/// <param name="queue">The outgoing packet queue to drain.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal void ClearPacketQueue(ConcurrentQueue<Packet> queue)
		{
			while (queue.TryDequeue(out Packet p))
			{
				p.Dispose();
			}
		}
	}
}