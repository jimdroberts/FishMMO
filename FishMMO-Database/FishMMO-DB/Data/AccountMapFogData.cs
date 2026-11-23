namespace FishMMO.Database.Data
{
	/// <summary>
	/// One scene an account has explored, as the map's scene list wants it: the scene and how much of it is explored.
	/// </summary>
	/// <remarks>
	/// Fog of war is account-wide (issue #241): every character on an account shares one explored map per
	/// scene, and a row exists for every scene any of them has entered — which is what "scenes entered at least once" is.
	/// </remarks>
	public readonly struct AccountMapFogSummary
	{
		public readonly string SceneName;
		public readonly int ExploredCount;

		public AccountMapFogSummary(string sceneName, int exploredCount)
		{
			SceneName = sceneName;
			ExploredCount = exploredCount;
		}
	}

	/// <summary>What a fog merge writes: the merged encoding and its explored count.</summary>
	public readonly struct AccountMapFogMerge
	{
		public readonly byte[] Data;
		public readonly int ExploredCount;

		public AccountMapFogMerge(byte[] data, int exploredCount)
		{
			Data = data;
			ExploredCount = exploredCount;
		}
	}
}
