using System;
using System.Collections.Generic;

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
		#region Public
		/// <summary>
		/// Current ConnectionState.
		/// </summary>
		private LocalConnectionState connectionState = LocalConnectionState.Stopped;

		/// <summary>
		/// Returns the current ConnectionState.
		/// </summary>
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

			this.connectionState = connectionState;
			if (asServer)
				Transport.HandleServerConnectionState(new ServerConnectionStateArgs(connectionState, Transport.Index));
			else
				Transport.HandleClientConnectionState(new ClientConnectionStateArgs(connectionState, Transport.Index));
		}
		#endregion

		#region Protected
		/// <summary>
		/// Transport controlling this socket.
		/// </summary>
		protected Transport Transport = null;
		#endregion

		/// <summary>
		/// Sends data to the given connection.  Queues for deferred send during IterateOutgoing.
		/// connectionId of -1 on server means broadcast to all.
		/// </summary>
		internal void Send(Queue<Packet> queue, byte channelId, ArraySegment<byte> segment, int connectionId)
		{
			if (GetConnectionState() != LocalConnectionState.Started)
				return;

			Packet outgoing = new Packet(connectionId, segment, channelId);
			queue.Enqueue(outgoing);
		}

		/// <summary>
		/// Clears a queue of Packets, returning their backing buffers to the pool.
		/// </summary>
		internal void ClearPacketQueue(Queue<Packet> queue)
		{
			int count = queue.Count;
			for (int i = 0; i < count; i++)
			{
				Packet p = queue.Dequeue();
				p.Dispose();
			}
		}
	}
}