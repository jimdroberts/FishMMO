using System;
using System.Collections.Generic;
using System.Text;
using FishNet.Managing.Transporting;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Counts every byte of every packet the server sends and receives over a window, to measure what
	/// <see cref="PacketCompression"/> would save and to train its tables (<see cref="PacketCompressionTables"/>).
	/// </summary>
	/// <remarks>
	/// Observes FishNet's own packets (<see cref="TransportManager.OnServerPacketObserved"/>), before any compression, so
	/// the histogram is the true distribution a table must code. The estimate is for a code trained on this same sample,
	/// so it is the best a static table could do on it: a table trained on one run and used on another does a little worse.
	/// </remarks>
	public sealed class PacketCompressionSampler
	{
		private readonly long[] toClients = new long[256];
		private readonly long[] toServer = new long[256];
		private long toClientsPackets, toServerPackets;
		private long toClientsBytes, toServerBytes;
		private bool running;

		/// <summary>Starts counting. Only one sampler can observe at a time; a second start replaces the first.</summary>
		public void Start()
		{
			Array.Clear(toClients, 0, toClients.Length);
			Array.Clear(toServer, 0, toServer.Length);
			toClientsPackets = toServerPackets = toClientsBytes = toServerBytes = 0;
			TransportManager.OnServerPacketObserved = Observe;
			running = true;
		}

		/// <summary>Stops counting.</summary>
		public void Stop()
		{
			if (running && TransportManager.OnServerPacketObserved == (Action<ArraySegment<byte>, bool>)Observe)
			{
				TransportManager.OnServerPacketObserved = null;
			}
			running = false;
		}

		private void Observe(ArraySegment<byte> packet, bool outbound)
		{
			long[] histogram = outbound ? toClients : toServer;
			byte[] data = packet.Array;
			int end = packet.Offset + packet.Count;
			for (int i = packet.Offset; i < end; ++i)
			{
				histogram[data[i]]++;
			}
			if (outbound)
			{
				toClientsPackets++;
				toClientsBytes += packet.Count;
			}
			else
			{
				toServerPackets++;
				toServerBytes += packet.Count;
			}
		}

		/// <summary>
		/// Stops and reports: a summary line per direction for <paramref name="summary"/>, and the full histograms (the
		/// training data) as the returned text for the log.
		/// </summary>
		public string Finish(List<string> summary)
		{
			Stop();
			summary.Add(Estimate("to clients", toClients, toClientsPackets, toClientsBytes, PacketCompression.ToClients));
			summary.Add(Estimate("to server", toServer, toServerPackets, toServerBytes, PacketCompression.ToServer));
			var log = new StringBuilder();
			log.Append("Packet compression sample (byte histograms; train PacketCompressionTables with HuffmanCode.BuildLengths):\n");
			AppendHistogram(log, "ToClients", toClients);
			AppendHistogram(log, "ToServer", toServer);
			return log.ToString();
		}

		/// <summary>
		/// One direction's saving: the packets as FishNet wrote them against the same packets with a header and length per
		/// packet and a Huffman code trained on this sample (and against the build's own table, when it has one).
		/// </summary>
		internal static string Estimate(string direction, long[] histogram, long packets, long bytes, HuffmanCode current)
		{
			if (packets == 0 || bytes == 0)
			{
				return $"Packet compression {direction}: no packets sampled.";
			}
			// Header byte plus a length varint (1 byte under 128, else 2) per packet; the average size decides which.
			double perPacketOverhead = 1 + PacketCompression.VarintLength((int)Math.Min(int.MaxValue, bytes / packets));
			HuffmanCode trained = new HuffmanCode(HuffmanCode.BuildLengths(histogram));
			double trainedBytes = trained.EncodedBits(histogram) / 8.0 + perPacketOverhead * packets;
			string text = $"Packet compression {direction}: {packets} packets avg {bytes / (double)packets:0} B; a code trained on them saves " +
				$"{100.0 * (1.0 - trainedBytes / bytes):0.0}%";
			if (current != null)
			{
				double currentBytes = current.EncodedBits(histogram) / 8.0 + perPacketOverhead * packets;
				text += $", this build's table {100.0 * (1.0 - currentBytes / bytes):0.0}%";
			}
			return text + " (before transport headers).";
		}

		private static void AppendHistogram(StringBuilder log, string name, long[] histogram)
		{
			log.Append("  ").Append(name).Append(':');
			for (int i = 0; i < histogram.Length; ++i)
			{
				log.Append(i == 0 ? ' ' : ',').Append(histogram[i]);
			}
			log.Append('\n');
		}
	}
}
