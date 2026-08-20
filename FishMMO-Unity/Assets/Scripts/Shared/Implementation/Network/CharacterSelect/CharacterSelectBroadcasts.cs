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
		/// <summary>
		/// The selection could not be completed for a reason the player cannot act on — a
		/// database failure, a saturated worker pool, or a request refused by the server's
		/// per-connection cooldown. Retrying is the only useful advice.
		/// </summary>
		Failed = 2,
	}

	/// <summary>
	/// Broadcast sent for every character selection — accepted or refused — so the client
	/// always has a terminal answer instead of appearing to hang on an unanswered request.
	/// </summary>
	/// <remarks>
	/// The success case matters as much as the refusals. The client disables its connect
	/// button and arms a reply deadline when the request goes out, and only this message ends
	/// that wait: a selection that succeeded but said nothing left the deadline armed, so the
	/// character-select panel reappeared over the server list (or over world entry) half a
	/// minute later claiming the server had not responded.
	/// </remarks>
	public struct CharacterSelectResultBroadcast : IBroadcast
	{
		/// <summary>Outcome of the selection.</summary>
		public CharacterSelectResult Result;
		/// <summary>Name of the character responsible for the refusal, when applicable.</summary>
		public string CharacterName;
	}
}