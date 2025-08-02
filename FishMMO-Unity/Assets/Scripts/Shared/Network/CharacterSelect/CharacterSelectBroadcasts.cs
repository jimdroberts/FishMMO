using System.Collections.Generic;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for requesting the list of available characters for the account.
	/// No additional data required.
	/// </summary>
	public struct CharacterRequestListBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Broadcast for sending the list of available characters to the client.
	/// Contains a list of character details.
	/// </summary>
	public struct CharacterListBroadcast : IBroadcast
	{
		/// <summary>List of character details for selection.</summary>
		public List<CharacterDetails> Characters;
	}

	/// <summary>
	/// Broadcast for deleting a character from the account.
	/// Contains the name of the character to delete.
	/// </summary>
	public struct CharacterDeleteBroadcast : IBroadcast
	{
		/// <summary>Name of the character to delete.</summary>
		public string CharacterName;
	}

	/// <summary>
	/// Broadcast for selecting a character to play.
	/// Contains the name of the character to select.
	/// </summary>
	public struct CharacterSelectBroadcast : IBroadcast
	{
		/// <summary>Name of the character to select.</summary>
		public string CharacterName;
	}
}