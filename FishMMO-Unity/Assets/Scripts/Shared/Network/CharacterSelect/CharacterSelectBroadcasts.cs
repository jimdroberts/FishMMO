using System.Collections.Generic;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct CharacterRequestListBroadcast : IBroadcast
	{
	}

	public struct CharacterListBroadcast : IBroadcast
	{
		public List<CharacterDetails> Characters;
	}

	public struct CharacterDeleteBroadcast : IBroadcast
	{
		public string CharacterName;
	}

	public struct CharacterSelectBroadcast : IBroadcast
	{
		public string CharacterName;
	}
}