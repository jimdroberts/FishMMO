using System;
using System.Collections.Generic;
using System.Text;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Transporting;
using FishNet.Object;
using FishNet.Transporting;
using GameKit.Dependencies.Utilities;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Each connection's outbound game traffic broken down by message type, logged once per interval: which broadcast, which
	/// behaviour's RPCs or sync types, the prediction state updates (reconcile), spawns, and so on, with bytes and messages per
	/// second. A measuring tool for the bandwidth budget, off unless <c>TrafficBreakdownSeconds</c> is set in the server's
	/// configuration.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What it counts.</b> Every message FishNet queues for a client (<see cref="TransportManager.OnServerMessageQueued"/>),
	/// per recipient, at the size FishNet wrote it, before bundling. So the totals are the game data: the transport's own cost
	/// on top (FishNet's bundle header, QUIC, UDP, IP) is what the wire counters measure, and the difference between the two
	/// is the overhead.
	/// </para>
	/// <para>
	/// <b>How a message is named.</b> From its header alone, never its body: the PacketId (two bytes, first); for a broadcast,
	/// its type, from the two-byte key FishNet derives from the type's name; for a message about one behaviour (RPCs, sync
	/// types), the behaviour's class, from the object id and component index that follow the PacketId. An RPC sent through
	/// an RPC link has no PacketId: its first two bytes are the link index FishNet gave that one behaviour's RPC (allocated
	/// down from 65535), so it is resolved through the spawned behaviours' link tables and counted as the RPC type and
	/// behaviour it stands for. A header that does not read cleanly is counted under its PacketId alone.
	/// </para>
	/// <para>
	/// <b>Cost when on.</b> One dictionary update per message per recipient on the main thread, no allocation in steady state.
	/// Off, the hook is null and FishNet pays one branch per message.
	/// </para>
	/// </remarks>
	public sealed class ServerTrafficBreakdown
	{
		private const string LogCategory = "TrafficBreakdown";
		private const string ConfigKey = "TrafficBreakdownSeconds";
		private const int TopRowsPerConnection = 14;

		private readonly struct Category : IEquatable<Category>
		{
			public readonly ushort PacketId;
			public readonly ushort BroadcastKey;
			public readonly Type Behaviour;

			public Category(ushort packetId, ushort broadcastKey, Type behaviour)
			{
				PacketId = packetId;
				BroadcastKey = broadcastKey;
				Behaviour = behaviour;
			}

			public bool Equals(Category other) => PacketId == other.PacketId && BroadcastKey == other.BroadcastKey && Behaviour == other.Behaviour;
			public override bool Equals(object obj) => obj is Category other && Equals(other);
			public override int GetHashCode() => (PacketId * 397) ^ (BroadcastKey * 31) ^ (Behaviour != null ? Behaviour.GetHashCode() : 0);
		}

		private sealed class Tally
		{
			public long Bytes;
			public long Messages;
			public long ReliableBytes;
		}

		private readonly NetworkManager networkManager;
		private readonly IPeriodicUpdateSystem periodic;
		private readonly float intervalSeconds;
		private readonly Dictionary<NetworkConnection, Dictionary<Category, Tally>> byConnection = new Dictionary<NetworkConnection, Dictionary<Category, Tally>>();
		private readonly Dictionary<ushort, string> broadcastNames = new Dictionary<ushort, string>();
		/// <summary>RPC link index to the RPC type and behaviour it was given to (rebuilt when an unknown link is seen).</summary>
		private readonly Dictionary<ushort, Category> rpcLinks = new Dictionary<ushort, Category>();
		private double rpcLinksBuiltAt = double.NegativeInfinity;
		private static readonly System.Reflection.FieldInfo RpcLinksField =
			typeof(NetworkBehaviour).GetField("_rpcLinks", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		/// <summary>FishNet allocates RPC link indices above its highest PacketId (NetworkManager.StartingRpcLinkIndex).</summary>
		private static readonly ushort HighestPacketId = HighestPacketIdValue();

		private static ushort HighestPacketIdValue()
		{
			ushort highest = 0;
			foreach (PacketId id in (PacketId[])Enum.GetValues(typeof(PacketId)))
			{
				highest = Math.Max(highest, (ushort)id);
			}
			return highest;
		}

		/// <summary>Marks traffic through an RPC link whose owner could not be found (it despawned, or the table was unreadable).</summary>
		private sealed class UnresolvedRpcLink { }
		private readonly StringBuilder text = new StringBuilder(2048);
		private double windowStart;
		private bool stopped;

		private ServerTrafficBreakdown(NetworkManager networkManager, IPeriodicUpdateSystem periodic, float intervalSeconds)
		{
			this.networkManager = networkManager;
			this.periodic = periodic;
			this.intervalSeconds = intervalSeconds;
			IndexBroadcasts();
		}

		/// <summary>
		/// Starts the breakdown when the configuration asks for it (<c>TrafficBreakdownSeconds</c> above 0), else returns null.
		/// </summary>
		public static ServerTrafficBreakdown TryStart(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server)
		{
			if (server?.Configuration == null || !server.Configuration.TryGetInt(ConfigKey, out int seconds) || seconds <= 0)
			{
				return null;
			}
			NetworkManager nm = server.NetworkWrapper?.NetworkManager;
			if (nm == null || !(server is IPeriodicUpdateSystem periodic))
			{
				_ = Log.Warning(LogCategory, "Traffic breakdown asked for, but the server has no network manager or periodic system.");
				return null;
			}
			var breakdown = new ServerTrafficBreakdown(nm, periodic, Math.Max(10, seconds));
			breakdown.windowStart = MonotonicClock.NowSeconds;
			TransportManager.OnServerMessageQueued += breakdown.OnMessageQueued;
			periodic.RegisterPeriodicCallback(breakdown.intervalSeconds, breakdown.OnInterval);
			_ = Log.Info(LogCategory, $"Logging each connection's outbound traffic by message type every {breakdown.intervalSeconds:0} s ({ConfigKey}).");
			return breakdown;
		}

		/// <summary>
		/// Starts a one-off measurement window, whatever the configuration says (the timed <c>/admin simulate</c> uses it).
		/// Counts until <see cref="FinishWindow"/>.
		/// </summary>
		public static ServerTrafficBreakdown BeginWindow(NetworkManager networkManager)
		{
			if (networkManager == null)
			{
				return null;
			}
			var breakdown = new ServerTrafficBreakdown(networkManager, null, 0f);
			breakdown.windowStart = MonotonicClock.NowSeconds;
			TransportManager.OnServerMessageQueued += breakdown.OnMessageQueued;
			return breakdown;
		}

		/// <summary>
		/// Ends a window from <see cref="BeginWindow"/>: the full report (every connection's top message types) and a short
		/// summary, one line per real player plus the top kinds across all of them, each line short enough for chat.
		/// </summary>
		public string FinishWindow(int topKinds, List<string> summary)
		{
			double seconds = Math.Max(1.0, MonotonicClock.NowSeconds - windowStart);
			string full = BuildReport(seconds);
			if (summary != null)
			{
				var all = new Dictionary<Category, Tally>();
				foreach (KeyValuePair<NetworkConnection, Dictionary<Category, Tally>> pair in byConnection)
				{
					long total = 0, messages = 0;
					foreach (KeyValuePair<Category, Tally> t in pair.Value)
					{
						total += t.Value.Bytes;
						messages += t.Value.Messages;
						if (!all.TryGetValue(t.Key, out Tally sum))
						{
							all[t.Key] = sum = new Tally();
						}
						sum.Bytes += t.Value.Bytes;
						sum.Messages += t.Value.Messages;
					}
					summary.Add($"  {Who(pair.Key)}: {total / seconds:0} B/s game data, {messages / seconds:0.0} msg/s");
				}
				var rows = new List<KeyValuePair<Category, Tally>>(all);
				rows.Sort((a, b) => b.Value.Bytes.CompareTo(a.Value.Bytes));
				int connections = Math.Max(1, byConnection.Count);
				for (int i = 0; i < rows.Count && i < topKinds; ++i)
				{
					Tally t = rows[i].Value;
					summary.Add($"  {t.Bytes / seconds / connections,6:0} B/s/player  avg {t.Bytes / Math.Max(1, t.Messages),4} B  {Truncate(Name(rows[i].Key), 70)}");
				}
			}
			Stop();
			return full;
		}

		private static string Truncate(string text, int length) => text.Length <= length ? text : text.Substring(0, length - 1) + "…";

		/// <summary>Stops counting.</summary>
		public void Stop()
		{
			if (stopped)
			{
				return;
			}
			stopped = true;
			TransportManager.OnServerMessageQueued -= OnMessageQueued;
			periodic?.UnregisterPeriodicCallback(OnInterval);
			byConnection.Clear();
		}

		private void OnMessageQueued(byte channel, ArraySegment<byte> segment, NetworkConnection connection)
		{
			if (connection == null || segment.Array == null || segment.Count < 2)
			{
				return;
			}
			Category category = Classify(segment);
			if (!byConnection.TryGetValue(connection, out Dictionary<Category, Tally> tallies))
			{
				byConnection[connection] = tallies = new Dictionary<Category, Tally>();
			}
			if (!tallies.TryGetValue(category, out Tally tally))
			{
				tallies[category] = tally = new Tally();
			}
			tally.Bytes += segment.Count;
			tally.Messages++;
			if (channel == (byte)Channel.Reliable)
			{
				tally.ReliableBytes += segment.Count;
			}
		}

		/// <summary>The message's category from its header: PacketId, then the broadcast type or the behaviour it is about.</summary>
		private Category Classify(ArraySegment<byte> segment)
		{
			byte[] data = segment.Array;
			int at = segment.Offset;
			int end = segment.Offset + segment.Count;
			ushort packetId = (ushort)(data[at] | (data[at + 1] << 8));
			at += 2;
			switch ((PacketId)packetId)
			{
				case PacketId.Broadcast:
					if (at + 2 <= end)
					{
						return new Category(packetId, (ushort)(data[at] | (data[at + 1] << 8)), null);
					}
					break;
				case PacketId.ObserversRpc:
				case PacketId.TargetRpc:
				case PacketId.SyncType:
					if (TryReadBehaviour(data, at, end, out Type behaviour))
					{
						return new Category(packetId, 0, behaviour);
					}
					break;
				default:
					if (packetId > HighestPacketId)
					{
						return ClassifyRpcLink(packetId);
					}
					break;
			}
			return new Category(packetId, 0, null);
		}

		/// <summary>A linked RPC: the RPC type and behaviour its link index was given to.</summary>
		private Category ClassifyRpcLink(ushort linkIndex)
		{
			if (rpcLinks.TryGetValue(linkIndex, out Category known))
			{
				return known;
			}
			double now = MonotonicClock.NowSeconds;
			if (now - rpcLinksBuiltAt >= 1.0)
			{
				rpcLinksBuiltAt = now;
				IndexRpcLinks();
				if (rpcLinks.TryGetValue(linkIndex, out known))
				{
					return known;
				}
			}
			return new Category(0, 0, typeof(UnresolvedRpcLink));
		}

		/// <summary>Longest <see cref="IndexRpcLinks"/> since the last <see cref="ResetLongestReindex"/>, milliseconds.</summary>
		public static double LongestReindexMilliseconds { get; private set; }

		/// <summary>Starts a new measurement of <see cref="LongestReindexMilliseconds"/>.</summary>
		public static void ResetLongestReindex() => LongestReindexMilliseconds = 0d;

		/// <summary>Every spawned behaviour's RPC links (FishNet keeps them internal: read by reflection, a diagnostic only).</summary>
		private void IndexRpcLinks()
		{
			long started = System.Diagnostics.Stopwatch.GetTimestamp();
			try
			{
				IndexRpcLinksCore();
			}
			finally
			{
				double milliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d / System.Diagnostics.Stopwatch.Frequency;
				if (milliseconds > LongestReindexMilliseconds)
				{
					LongestReindexMilliseconds = milliseconds;
				}
			}
		}

		private void IndexRpcLinksCore()
		{
			rpcLinks.Clear();
			if (RpcLinksField == null)
			{
				return;
			}
			try
			{
				foreach (NetworkObject nob in networkManager.ServerManager.Objects.Spawned.Values)
				{
					if (nob == null)
					{
						continue;
					}
					foreach (NetworkBehaviour behaviour in nob.NetworkBehaviours)
					{
						if (behaviour == null || !(RpcLinksField.GetValue(behaviour) is System.Collections.IDictionary links) || links.Count == 0)
						{
							continue;
						}
						foreach (object link in links.Values)
						{
							Type linkType = link.GetType();
							object linkIndex = linkType.GetField("LinkPacketId")?.GetValue(link);
							object rpcPacketId = linkType.GetField("RpcPacketId")?.GetValue(link);
							if (linkIndex is ushort index && rpcPacketId != null)
							{
								rpcLinks[index] = new Category(Convert.ToUInt16(rpcPacketId), 0, behaviour.GetType());
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				_ = Log.Warning(LogCategory, $"Could not read RPC link tables; linked RPCs are counted as unresolved. {ex.GetType().Name}: {ex.Message}");
			}
		}

		/// <summary>The behaviour a message is about: object id (signed packed whole, zigzag), then its component index.</summary>
		private bool TryReadBehaviour(byte[] data, int at, int end, out Type behaviour)
		{
			behaviour = null;
			ulong value = 0;
			int shift = 0;
			while (true)
			{
				if (at >= end || shift > 63)
				{
					return false;
				}
				byte b = data[at++];
				value |= (ulong)(b & 0x7F) << shift;
				if ((b & 0x80) == 0)
				{
					break;
				}
				shift += 7;
			}
			long objectId = (long)(value >> 1) ^ -(long)(value & 1);
			if (at >= end)
			{
				return false;
			}
			byte componentIndex = data[at];
			if (!networkManager.ServerManager.Objects.Spawned.TryGetValue((int)objectId, out NetworkObject nob) || nob == null)
			{
				return false;
			}
			if (componentIndex >= nob.NetworkBehaviours.Count || nob.NetworkBehaviours[componentIndex] == null)
			{
				return false;
			}
			behaviour = nob.NetworkBehaviours[componentIndex].GetType();
			return true;
		}

		private void OnInterval(float elapsed)
		{
			double now = MonotonicClock.NowSeconds;
			double seconds = Math.Max(1.0, now - windowStart);
			windowStart = now;
			if (byConnection.Count == 0)
			{
				return;
			}
			_ = Log.Info(LogCategory, BuildReport(seconds));
			byConnection.Clear();
		}

		/// <summary>The full report for the counts so far: each connection's top message types, then all together.</summary>
		private string BuildReport(double seconds)
		{
			var all = new Dictionary<Category, Tally>();
			text.Clear();
			text.Append($"Outbound game data by message type over {seconds:0} s (before bundling and transport headers):");
			foreach (KeyValuePair<NetworkConnection, Dictionary<Category, Tally>> pair in byConnection)
			{
				AppendConnection(pair.Key, pair.Value, seconds);
				foreach (KeyValuePair<Category, Tally> t in pair.Value)
				{
					if (!all.TryGetValue(t.Key, out Tally sum))
					{
						all[t.Key] = sum = new Tally();
					}
					sum.Bytes += t.Value.Bytes;
					sum.Messages += t.Value.Messages;
					sum.ReliableBytes += t.Value.ReliableBytes;
				}
			}
			if (byConnection.Count > 1)
			{
				text.Append($"\n  ALL {byConnection.Count} connections:");
				AppendRows(all, seconds);
			}
			return text.ToString();
		}

		private static string Who(NetworkConnection connection)
		{
			if (connection.FirstObject != null && connection.FirstObject.TryGetComponent(out IPlayerCharacter character) && !string.IsNullOrEmpty(character.CharacterName))
			{
				return character.CharacterName;
			}
			return $"connection {connection.ClientId}";
		}

		private void AppendConnection(NetworkConnection connection, Dictionary<Category, Tally> tallies, double seconds)
		{
			string who = $"connection {connection.ClientId}";
			if (connection.FirstObject != null && connection.FirstObject.TryGetComponent(out IPlayerCharacter character) && !string.IsNullOrEmpty(character.CharacterName))
			{
				who = $"{character.CharacterName} (connection {connection.ClientId})";
			}
			long total = 0, messages = 0;
			foreach (Tally t in tallies.Values)
			{
				total += t.Bytes;
				messages += t.Messages;
			}
			text.Append($"\n  {who}: {total / seconds:0} B/s in {messages / seconds:0.0} messages/s");
			AppendRows(tallies, seconds);
		}

		private void AppendRows(Dictionary<Category, Tally> tallies, double seconds)
		{
			var rows = new List<KeyValuePair<Category, Tally>>(tallies);
			rows.Sort((a, b) => b.Value.Bytes.CompareTo(a.Value.Bytes));
			long total = 0;
			foreach (KeyValuePair<Category, Tally> r in rows)
			{
				total += r.Value.Bytes;
			}
			int shown = 0;
			long rest = 0;
			foreach (KeyValuePair<Category, Tally> r in rows)
			{
				if (shown >= TopRowsPerConnection)
				{
					rest += r.Value.Bytes;
					continue;
				}
				shown++;
				Tally t = r.Value;
				double share = total > 0 ? 100.0 * t.Bytes / total : 0.0;
				string reliable = t.ReliableBytes > 0 ? $", {100.0 * t.ReliableBytes / Math.Max(1, t.Bytes):0}% reliable" : string.Empty;
				text.Append($"\n    {t.Bytes / seconds,7:0} B/s {share,3:0}%  {t.Messages / seconds,6:0.0}/s  avg {t.Bytes / Math.Max(1, t.Messages),4} B  {Name(r.Key)}{reliable}");
			}
			if (rest > 0)
			{
				text.Append($"\n    {rest / seconds,7:0} B/s       (other kinds)");
			}
		}

		private string Name(Category category)
		{
			string packet = Enum.IsDefined(typeof(PacketId), category.PacketId) ? ((PacketId)category.PacketId).ToString() : $"PacketId {category.PacketId}";
			if ((PacketId)category.PacketId == PacketId.Broadcast)
			{
				return broadcastNames.TryGetValue(category.BroadcastKey, out string name) ? $"Broadcast {name}" : $"Broadcast key {category.BroadcastKey}";
			}
			if ((PacketId)category.PacketId == PacketId.StateUpdate)
			{
				return "StateUpdate (prediction: reconcile)";
			}
			if (category.Behaviour == typeof(UnresolvedRpcLink))
			{
				return "RPC via link (owner not found)";
			}
			return category.Behaviour != null ? $"{packet} {category.Behaviour.Name}" : packet;
		}

		/// <summary>Every broadcast type's key, as FishNet derives it (its full name's stable 16-bit hash).</summary>
		private void IndexBroadcasts()
		{
			foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				Type[] types;
				try
				{
					types = assembly.GetTypes();
				}
				catch (System.Reflection.ReflectionTypeLoadException ex)
				{
					types = ex.Types;
				}
				foreach (Type type in types)
				{
					if (type == null || !type.IsValueType || !typeof(IBroadcast).IsAssignableFrom(type) || type.FullName == null)
					{
						continue;
					}
					ushort key = type.FullName.GetStableHashU16();
					broadcastNames[key] = broadcastNames.TryGetValue(key, out string existing) ? $"{existing}|{type.Name}" : type.Name;
				}
			}
		}
	}
}
