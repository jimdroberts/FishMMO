using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Server → owner: explore the whole of the named scene's map, as if every part of it had been walked. Sent by
	/// <c>/gm reveal map</c>. Exploration lives in the player's own files, so the server can only ask; the client's map
	/// applies it when it is showing that scene.
	/// </summary>
	public struct MapRevealBroadcast : IBroadcast
	{
		/// <summary>The scene the reveal is for: the character's scene when the command ran.</summary>
		public string SceneName;
	}
}
