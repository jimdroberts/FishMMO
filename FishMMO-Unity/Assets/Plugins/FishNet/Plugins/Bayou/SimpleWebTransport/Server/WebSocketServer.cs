using FishNet.Connection;
using FishNet.Managing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace JamesFrowen.SimpleWeb
{
	public class WebSocketServer
	{
		public readonly ConcurrentQueue<Message> receiveQueue = new ConcurrentQueue<Message>();

		readonly TcpConfig tcpConfig;
		readonly int maximumClients;
		readonly int maxMessageSize;
		readonly bool useProxyProtocol;

		public TcpListener listener;
		Thread acceptThread;
		bool serverStopped;
		readonly ServerHandshake handShake;
		readonly ServerSslHelper sslHelper;
		readonly BufferPool bufferPool;
		readonly ConcurrentDictionary<int, Connection> connections = new ConcurrentDictionary<int, Connection>();

		/// <summary>
		/// Connections that have been accepted but have not yet completed the handshake.
		/// Tracked so that Stop() can clean them up (prevents slowloris-style DoS).
		/// </summary>
		readonly ConcurrentDictionary<Connection, byte> pendingConnections = new ConcurrentDictionary<Connection, byte>();


		private ConcurrentQueue<int> _idCache = new ConcurrentQueue<int>();

		public WebSocketServer(TcpConfig tcpConfig, int maximumClients, int maxMessageSize, int handshakeMaxSize, SslConfig sslConfig, BufferPool bufferPool, bool useProxyProtocol = false)
		{
			this.tcpConfig = tcpConfig;
			this.maximumClients = maximumClients;
			this.maxMessageSize = maxMessageSize;
			this.useProxyProtocol = useProxyProtocol;
			sslHelper = new ServerSslHelper(sslConfig);
			this.bufferPool = bufferPool;
			handShake = new ServerHandshake(this.bufferPool, handshakeMaxSize);

			// Pre-fill exactly the IDs we'll ever need.
			// Pool sized to maximumClients means exhaustion is impossible
			// as long as IDs are returned on disconnect, which AfterConnectionDisposed does.
			for (int i = 0; i < maximumClients; i++)
				_idCache.Enqueue(i);
		}

		private int GetNextId()
		{
			// If the queue is empty we're genuinely at capacity — no growth needed.
			if (_idCache.TryDequeue(out int id))
				return id;

			return NetworkConnection.UNSET_CLIENTID_VALUE;
		}

		public void Listen(string bindAddress, int port)
		{
			// Parse the bind address. Defaults to loopback for co-located NGINX.
			// Use "0.0.0.0" or a specific IP when NGINX is on a separate machine.
			IPAddress address;
			if (string.IsNullOrEmpty(bindAddress) || bindAddress == "localhost")
				address = IPAddress.Loopback;
			else if (!IPAddress.TryParse(bindAddress, out address))
				address = IPAddress.Loopback;

			listener = new TcpListener(address, port);
			listener.Start();
			Log.Info($"Server has started on {address}:{port}");

			acceptThread = new Thread(acceptLoop);
			acceptThread.IsBackground = true;
			acceptThread.Start();
		}

		public void Stop()
		{
			serverStopped = true;

			// Interrupt then stop so that Exception is handled correctly
			acceptThread?.Interrupt();
			listener?.Stop();
			acceptThread = null;


			Log.Info("Server stoped, Closing all connections...");
			// make copy so that foreach doesn't break if values are removed
			Connection[] connectionsCopy = connections.Values.ToArray();
			foreach (Connection conn in connectionsCopy)
			{
				conn.Dispose();
			}

			// Also dispose any connections still in the handshake phase.
			Connection[] pendingCopy = pendingConnections.Keys.ToArray();
			foreach (Connection conn in pendingCopy)
			{
				conn.Dispose();
			}

			connections.Clear();
			pendingConnections.Clear();

			sslHelper.Dispose();
		}

		void acceptLoop()
		{
			try
			{
				try
				{
					while (true)
					{
						TcpClient client = listener.AcceptTcpClient();
						tcpConfig.ApplyTo(client);


						Connection conn = new Connection(client, AfterConnectionDisposed);
						pendingConnections.TryAdd(conn, 0);
						//Log.Info($"A client connected {conn}");

						// handshake needs its own thread as it needs to wait for message from client
						Thread receiveThread = new Thread(() => HandshakeAndReceiveLoop(conn));

						conn.receiveThread = receiveThread;

						receiveThread.IsBackground = true;
						receiveThread.Start();
					}
				}
				catch (SocketException)
				{
					// check for Interrupted/Abort
					Utils.CheckForInterupt();
					throw;
				}
			}
			catch (ThreadInterruptedException e) { Log.InfoException(e); }
			catch (ThreadAbortException e) { Log.InfoException(e); }
			catch (Exception e) { Log.Exception(e); }
		}

		void HandshakeAndReceiveLoop(Connection conn)
		{
			try
			{
				// ── PROXY protocol ──────────────────────────────────────
				// Must be parsed BEFORE SSL/TLS because PROXY protocol is
				// a transport-layer preamble sent in the clear by the proxy.
				if (useProxyProtocol)
				{
					Stream rawStream = conn.client.GetStream();
					if (ProxyProtocol.TryParse(rawStream, out string srcAddr))
					{
						// Overwrite the cached TCP endpoint with the real client IP.
						if (!string.IsNullOrEmpty(srcAddr))
							conn.remoteAddress = srcAddr;
					}
					else
					{
						Log.Warn("Failed to parse PROXY protocol header, disconnecting client.");
						conn.Dispose();
						return;
					}
				}

				bool success = sslHelper.TryCreateStream(conn);
				if (!success)
				{
					//Log.Error($"Failed to create SSL Stream {conn}");
					conn.Dispose();
					return;
				}

				success = handShake.TryHandshake(conn);

				if (success)
				{
					//Log.Info($"Sent Handshake {conn}");
				}
				else
				{
					//Log.Error($"Handshake Failed {conn}");
					conn.Dispose();
					return;
				}

				// check if Stop has been called since accepting this client
				if (serverStopped)
				{
					Log.Info("Server stops after successful handshake");
					return;
				}

				conn.connId = GetNextId();
				if (conn.connId == NetworkConnection.UNSET_CLIENTID_VALUE)
				{
					NetworkManagerExtensions.LogWarning($"At maximum connections. A client attempting to connect to be rejected.");
					conn.Dispose();
					return;
				}

				connections.TryAdd(conn.connId, conn);
				pendingConnections.TryRemove(conn, out _);

				receiveQueue.Enqueue(new Message(conn.connId, EventType.Connected));

				Thread sendThread = new Thread(() =>
				{
					SendLoop.Config sendConfig = new SendLoop.Config(
						conn,
						bufferSize: Constants.HeaderSize + maxMessageSize,
						setMask: false);

					SendLoop.Loop(sendConfig);
				});

				conn.sendThread = sendThread;
				sendThread.IsBackground = true;
				sendThread.Name = $"SendLoop {conn.connId}";
				sendThread.Start();

				ReceiveLoop.Config receiveConfig = new ReceiveLoop.Config(
					conn,
					maxMessageSize,
					expectMask: true,
					receiveQueue,
					bufferPool);

				ReceiveLoop.Loop(receiveConfig);
			}
			catch (ThreadInterruptedException e) { Log.InfoException(e); }
			catch (ThreadAbortException e) { Log.InfoException(e); }
			catch (Exception e) { Log.Exception(e); }
			finally
			{
				// close here incase connect fails
				conn.Dispose();
			}
		}

		void AfterConnectionDisposed(Connection conn)
		{
			pendingConnections.TryRemove(conn, out _);
			if (conn.connId != Connection.IdNotSet)
			{
				receiveQueue.Enqueue(new Message(conn.connId, EventType.Disconnected));
				connections.TryRemove(conn.connId, out Connection _);
				_idCache.Enqueue(conn.connId);
			}
		}

		public void Send(int id, ArrayBuffer buffer)
		{
			if (connections.TryGetValue(id, out Connection conn))
			{
				conn.sendQueue.Enqueue(buffer);
				conn.sendPending.Set();
			}
			else
			{
				Log.Warn($"Cant send message to {id} because connection was not found in dictionary. Maybe it disconnected.");
			}
		}

		public bool CloseConnection(int id)
		{
			if (connections.TryGetValue(id, out Connection conn))
			{
				Log.Info($"Kicking connection {id}");
				conn.Dispose();
				return true;
			}
			else
			{
				Log.Warn($"Failed to kick {id} because id not found");

				return false;
			}
		}

		public string GetClientAddress(int id)
		{
			if (connections.TryGetValue(id, out Connection conn))
				return conn.remoteAddress;

			Log.Error($"Cannot get address for {id}, connection not found.");
			return null;
		}
	}
}
