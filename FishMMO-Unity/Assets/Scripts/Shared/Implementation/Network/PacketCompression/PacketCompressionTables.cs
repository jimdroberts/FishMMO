namespace FishMMO.Shared
{
	/// <summary>
	/// The trained code lengths for <see cref="PacketCompression"/>: 256 values (1..15) per direction, built with
	/// <see cref="HuffmanCode.BuildLengths"/> from a byte histogram of real traffic. The scene server's simulate report logs
	/// those histograms ("Packet compression sample") and what the code would have saved on them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Trained 2026-11-14 on the 08:17 UTC simulate run (50 simulated players in a crowd, two real players, 60 s): 1,684
	/// packets to clients (220,410 bytes) and 1,815 to the server (192,547 bytes). On that sample the codes shrink the bytes
	/// to 84.6% and 61.9%, before the per-packet header. Retrain from a later run's histograms when the traffic mix
	/// changes; any complete code decodes correctly, a better-fitted one only compresses more.
	/// </para>
	/// <para>
	/// Null means untrained: compression is not installed anywhere and the wire is unchanged. Both must be set together, and
	/// the client and every server must be deployed from the same build, since each side decodes with the table the other
	/// encoded with.
	/// </para>
	/// </remarks>
	public static class PacketCompressionTables
	{
		/// <summary>Code lengths for packets a server sends to its clients.</summary>
		public static readonly byte[] ToClientsLengths =
		{
			3, 5, 7, 7, 8, 8, 6, 6, 8, 8, 5, 10, 8, 9, 6, 8, 7, 9, 8, 9, 7, 10, 8, 9, 8, 9, 9, 9, 8, 9, 9, 9,
			10, 10, 9, 9, 10, 8, 10, 10, 9, 10, 8, 10, 9, 10, 10, 10, 7, 8, 10, 5, 9, 10, 9, 9, 9, 9, 9, 9, 8, 9, 9, 8,
			8, 9, 8, 6, 9, 10, 10, 9, 10, 9, 9, 9, 10, 9, 9, 8, 10, 10, 9, 10, 9, 8, 10, 9, 10, 10, 10, 10, 9, 9, 9, 10,
			10, 9, 9, 10, 9, 9, 10, 9, 10, 10, 11, 10, 10, 9, 10, 10, 8, 9, 8, 6, 10, 10, 9, 8, 10, 10, 10, 10, 10, 11, 10, 8,
			8, 9, 8, 9, 10, 8, 9, 9, 9, 8, 9, 8, 8, 10, 10, 8, 9, 9, 9, 10, 8, 9, 10, 8, 9, 9, 9, 8, 8, 8, 8, 7,
			7, 7, 7, 7, 7, 8, 9, 8, 9, 8, 10, 10, 10, 8, 9, 8, 10, 8, 8, 8, 9, 8, 8, 7, 9, 9, 9, 9, 8, 7, 7, 7,
			7, 7, 7, 7, 8, 8, 8, 9, 6, 9, 10, 9, 10, 10, 9, 10, 9, 9, 10, 10, 10, 10, 10, 9, 9, 10, 9, 10, 9, 8, 9, 10,
			9, 9, 10, 9, 9, 9, 8, 10, 10, 8, 9, 9, 10, 10, 9, 9, 9, 8, 9, 10, 9, 10, 8, 9, 10, 9, 8, 8, 9, 9, 5, 5,
		};

		/// <summary>Code lengths for packets a client sends to a server.</summary>
		public static readonly byte[] ToServerLengths =
		{
			2, 3, 4, 6, 5, 10, 10, 7, 10, 10, 10, 10, 10, 11, 10, 6, 7, 10, 7, 10, 9, 10, 5, 11, 11, 11, 11, 11, 10, 11, 11, 10,
			10, 11, 11, 11, 11, 11, 11, 11, 11, 12, 12, 5, 10, 11, 10, 11, 11, 12, 9, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11,
			11, 11, 11, 10, 12, 12, 11, 12, 11, 12, 12, 11, 11, 11, 11, 11, 7, 11, 9, 9, 9, 9, 9, 9, 9, 10, 8, 11, 9, 10, 11, 5,
			10, 7, 10, 10, 11, 11, 7, 12, 12, 11, 11, 11, 11, 11, 11, 11, 11, 12, 12, 11, 10, 11, 11, 11, 11, 10, 10, 10, 11, 11, 12, 7,
			4, 9, 10, 10, 10, 9, 9, 8, 8, 9, 9, 9, 7, 11, 9, 9, 6, 11, 11, 10, 11, 11, 11, 11, 10, 7, 11, 11, 12, 11, 12, 10,
			10, 11, 11, 11, 11, 10, 10, 12, 11, 12, 12, 12, 7, 11, 11, 10, 10, 11, 11, 11, 12, 11, 11, 10, 12, 11, 11, 12, 12, 11, 11, 10,
			10, 11, 11, 11, 11, 11, 12, 11, 10, 11, 10, 7, 9, 8, 8, 6, 6, 7, 9, 11, 10, 10, 11, 11, 10, 10, 11, 11, 11, 10, 10, 10,
			10, 11, 11, 11, 9, 11, 10, 11, 11, 11, 11, 7, 11, 10, 11, 9, 9, 11, 10, 11, 9, 11, 10, 11, 10, 11, 11, 11, 10, 10, 9, 7,
		};
	}
}
