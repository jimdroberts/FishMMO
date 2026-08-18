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
		public CharacterDetails[] Characters;
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

	/// <summary>
	/// Why a character selection could not be honoured.
	/// </summary>
	public enum CharacterSelectResult : byte
	{
		/// <summary>Selection was accepted; the world server list follows.</summary>
		Success = 0,
		/// <summary>
		/// A different character on this account is still in the world — either playing or
		/// running out a combat-logout timer — so the account cannot switch to another one yet.
		/// </summary>
		OtherCharacterInWorld = 1,
	}

	/// <summary>
	/// Broadcast sent when a character selection is refused, so the client can explain why
	/// instead of appearing to hang on an unanswered request.
	/// </summary>
	public struct CharacterSelectResultBroadcast : IBroadcast
	{
		/// <summary>Reason the selection was refused.</summary>
		public CharacterSelectResult Result;
		/// <summary>Name of the character responsible for the refusal, when applicable.</summary>
		public string CharacterName;
	}
}