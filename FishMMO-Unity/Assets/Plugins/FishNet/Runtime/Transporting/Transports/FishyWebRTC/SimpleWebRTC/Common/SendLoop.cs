#if !UNITY_WEBGL || UNITY_EDITOR

using System;
using System.Threading;
using UnityEngine.Profiling;
using Unity.WebRTC;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace cakeslice.SimpleWebRTC
{
	public static class SendLoopConfig
	{
		public static volatile bool sleepBeforeSend = false;
	}
	internal static class SendLoop
	{
		public struct Config
		{
			public readonly Connection conn;
			public readonly int bufferSize;
			public readonly Common.DeliveryMethod dm;
			public readonly ManualResetEventSlim sendPending;
			public readonly ConcurrentQueue<ArrayBuffer> sendQueue;

			public Config(Connection conn, int bufferSize, Common.DeliveryMethod dm)
			{
				this.conn = conn ?? throw new ArgumentNullException(nameof(conn));
				this.dm = dm;
				this.bufferSize = bufferSize;
				this.sendPending = dm == Common.DeliveryMethod.ReliableOrdered ? conn.reliableSendPending : conn.unreliableSendPending;
				this.sendQueue = dm == Common.DeliveryMethod.ReliableOrdered ? conn.reliableSendQueue : conn.unreliableSendQueue;
			}

			public void Deconstruct(out Connection conn, out int bufferSize, out Common.DeliveryMethod dm, out ManualResetEventSlim sendPending, out ConcurrentQueue<ArrayBuffer> sendQueue)
			{
				conn = this.conn;
				bufferSize = this.bufferSize;
				dm = this.dm;
				sendPending = this.sendPending;
				sendQueue = this.sendQueue;
			}
		}

		public static void Loop(Config config)
		{
			(Connection conn, int bufferSize, Common.DeliveryMethod dm, ManualResetEventSlim sendPending, ConcurrentQueue<ArrayBuffer> sendQueue) = config;

			Profiler.BeginThreadProfiling("SimpleWebRTC", $"SendLoop {conn.connId}");

			bool dispose = false;

			// create write buffer for this thread
			byte[] writeBuffer = new byte[bufferSize];
			try
			{
				RTCPeerConnection client = conn.client;
				var stream = dm == Common.DeliveryMethod.ReliableOrdered ? conn.reliableDataChannel : conn.unreliableDataChannel;

				// null check incase disconnect while send thread is starting
				if (client == null)
					return;

				while (client.ConnectionState == RTCPeerConnectionState.Connected)
				{
					// wait for message
					sendPending.Wait();
					// wait for 1ms for mirror to send other messages
					if (SendLoopConfig.sleepBeforeSend)
					{
						Thread.Sleep(1);
					}
					sendPending.Reset();

					if (stream.ReadyState == RTCDataChannelState.Open)
					{
						while (sendQueue.TryDequeue(out ArrayBuffer msg))
						{
							// check if connected before sending message
							if (client.ConnectionState != RTCPeerConnectionState.Connected)
							{
								Log.Info($"SendLoop {conn} not connected"); return;
							}

							int length = SendMessage(writeBuffer, 0, msg);

							GCHandle pinned = GCHandle.Alloc(writeBuffer, GCHandleType.Pinned);
							IntPtr pointer = pinned.AddrOfPinnedObject();

							stream.Send(pointer, length);

							pinned.Free();

							msg.Release();
						}
					}
				}

				Log.Info($"{conn} Not Connected");
			}
			catch (ThreadInterruptedException e)
			{
				dispose = true;
				Log.InfoException(e);
			}
			catch (ThreadAbortException e)
			{
				dispose = true;
				Log.InfoException(e);
			}
			catch (Exception e)
			{
				dispose = true;
				Log.Exception(e);
			}

			if (dispose)
			{
				Profiler.EndThreadProfiling();
				Log.Warn("Closing connection due to exception");
				conn.Dispose();
			}
		}

		static int SendMessage(byte[] buffer, int offset, ArrayBuffer msg)
		{
			msg.CopyTo(buffer, offset);
			Log.DumpBuffer("Send", buffer, offset, buffer.Length);

			offset += msg.count;
			return offset;
		}
	}
}

#endif
