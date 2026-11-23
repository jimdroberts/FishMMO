using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Server → owner: the account's explored chunks for one scene (<see cref="FogCodec"/>), when the character spawns in it
	/// and in answer to a <see cref="FogOfWarRequestBroadcast"/>.
	/// </summary>
	public struct FogOfWarSceneBroadcast : IBroadcast
	{
		/// <summary>The scene the fog belongs to.</summary>
		public string SceneName;

		/// <summary><see cref="FogCodec.Encode"/> of the explored chunks; empty when nothing is explored there.</summary>
		public byte[] Data;
	}

	/// <summary>
	/// Server → owner: every scene the account has entered at least once (it has fog there), with how many chunks are
	/// explored in each. Sent at spawn; the map's scene dropdown lists these.
	/// </summary>
	public struct FogOfWarScenesBroadcast : IBroadcast
	{
		public string[] SceneNames;

		public int[] ExploredCounts;
	}

	/// <summary>Owner → server: send me my fog for this scene (the map is showing a scene the character is not in).</summary>
	public struct FogOfWarRequestBroadcast : IBroadcast
	{
		public string SceneName;
	}
}
