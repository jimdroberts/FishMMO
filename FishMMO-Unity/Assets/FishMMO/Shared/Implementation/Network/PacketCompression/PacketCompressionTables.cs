namespace FishMMO.Shared
{
	/// <summary>
	/// The trained code lengths for <see cref="PacketCompression"/>: 256 values (1..15) per direction, built with
	/// <see cref="HuffmanCode.BuildLengths"/> from a byte histogram of real traffic. The scene server's simulate report logs
	/// those histograms ("Packet compression sample") and what the code would have saved on them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Trained on two simulate runs with acknowledged reconciles and the rotation threshold (99 simulated players fighting in
	/// two teams within 150 m, one real player moving through the fight in one run and standing at its edge in the other,
	/// 120 s each: 1063 KB to clients and 60 KB to the server), two parts to one blended with the previous table's
	/// distribution (itself a frames-era combat run blended with older ones). On those runs the codes shrink the bytes
	/// to/from clients by about 7% and 32-41% (the previous table: 6% and 26-36%), before the per-packet header. Retrain from a later run's histograms when the traffic mix changes; any complete code decodes correctly, a
	/// better-fitted one only compresses more.
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
			3, 5, 7, 6, 7, 8, 6, 7, 7, 8, 7, 9, 6, 9, 8, 7, 8, 8, 8, 8, 8, 9, 7, 7, 8, 8, 8, 8, 8, 9, 8, 8,
			8, 9, 9, 9, 9, 9, 9, 8, 8, 9, 8, 9, 9, 9, 9, 9, 7, 8, 8, 6, 8, 9, 8, 8, 8, 9, 9, 8, 8, 9, 9, 5,
			7, 8, 8, 7, 8, 9, 10, 9, 9, 9, 9, 9, 10, 9, 9, 9, 9, 9, 9, 10, 10, 9, 10, 9, 9, 10, 10, 9, 9, 9, 9, 9,
			10, 9, 9, 10, 9, 9, 9, 10, 10, 9, 10, 10, 10, 9, 8, 10, 9, 9, 9, 8, 9, 9, 9, 9, 9, 9, 10, 10, 9, 9, 9, 7,
			8, 8, 8, 8, 8, 7, 8, 8, 8, 8, 8, 8, 8, 8, 9, 9, 8, 8, 8, 9, 9, 9, 9, 8, 9, 8, 8, 9, 9, 8, 8, 7,
			8, 8, 8, 7, 8, 8, 8, 8, 8, 8, 9, 8, 9, 8, 9, 8, 9, 8, 8, 8, 9, 8, 8, 8, 9, 9, 8, 8, 8, 9, 8, 8,
			8, 8, 8, 8, 8, 9, 9, 9, 7, 8, 9, 8, 9, 8, 9, 9, 9, 9, 9, 9, 9, 9, 9, 8, 9, 8, 9, 8, 8, 8, 8, 8,
			9, 9, 9, 9, 9, 9, 9, 9, 10, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 8, 9, 9, 9, 9, 8, 9, 9, 8, 8, 8,
		};

		/// <summary>Code lengths for packets a client sends to a server.</summary>
		public static readonly byte[] ToServerLengths =
		{
			2, 3, 4, 8, 7, 5, 9, 8, 9, 6, 10, 9, 10, 9, 8, 6, 5, 9, 7, 9, 9, 8, 5, 9, 9, 9, 9, 8, 9, 9, 10, 10,
			9, 11, 10, 10, 11, 10, 11, 11, 10, 11, 11, 8, 11, 11, 10, 10, 11, 12, 11, 12, 9, 11, 11, 10, 11, 8, 6, 11, 11, 11, 11, 11,
			11, 11, 11, 12, 11, 12, 9, 12, 11, 11, 11, 11, 11, 12, 11, 11, 10, 11, 11, 9, 10, 11, 10, 10, 10, 10, 8, 11, 10, 12, 10, 9,
			9, 9, 10, 10, 10, 11, 10, 10, 10, 10, 10, 10, 11, 10, 10, 10, 10, 10, 10, 12, 10, 10, 10, 9, 10, 10, 10, 10, 10, 9, 11, 8,
			5, 8, 9, 9, 10, 9, 9, 8, 9, 10, 10, 10, 10, 10, 10, 9, 6, 7, 7, 10, 10, 10, 10, 9, 9, 10, 10, 11, 11, 11, 11, 9,
			9, 11, 10, 11, 11, 10, 9, 9, 9, 11, 10, 11, 10, 11, 11, 9, 9, 11, 10, 11, 11, 10, 10, 9, 10, 11, 11, 11, 10, 11, 10, 9,
			9, 10, 10, 11, 11, 11, 11, 9, 10, 11, 11, 10, 10, 10, 10, 9, 9, 10, 10, 10, 10, 10, 10, 8, 9, 11, 10, 10, 10, 10, 9, 9,
			9, 11, 9, 11, 10, 10, 10, 9, 10, 11, 10, 10, 10, 10, 6, 9, 9, 10, 9, 10, 7, 10, 9, 9, 6, 10, 9, 9, 9, 9, 8, 6,
		};
	}
}
