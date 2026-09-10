using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable class containing details about a character for selection and display.
	/// </summary>
	[Serializable]
	public class CharacterDetails
	{
		/// <summary>Name of the character.</summary>
		public string CharacterName;
		/// <summary>Name of the scene where the character is currently located.</summary>
		public string SceneName;
		/// <summary>Template ID representing the character's race.</summary>
		public int RaceTemplateID;
		/// <summary>
		/// True when this character's body is still in the world because its owner disconnected
		/// during combat. Selecting it resumes that body rather than starting a fresh session.
		/// </summary>
		/// <remarks>
		/// Surfaced so the player is told what happened instead of silently being dropped back
		/// into a character that may have taken damage — or died — while they were gone.
		/// </remarks>
		public bool IsCombatLogged;
		/// <summary>
		/// What the character is wearing, as one template ID per occupied slot, for dressing the
		/// preview model on the character select screen.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Deliberately not the character's equipment state. Selection needs to know which item
		/// template sits in which slot and nothing else — no item ids, no stack sizes, no rolled
		/// attributes, no durability — because the screen draws a model and offers no interaction
		/// with the items themselves. Sending the real equipment data here would put a character's
		/// entire loadout on the wire, for every character on the account, before the player has
		/// even chosen one.
		/// </para>
		/// <para>
		/// Null means the list was not sent, which is not the same as an empty array meaning the
		/// character is wearing nothing. A consumer that cannot tell those apart will strip a
		/// preview model bare on a server that simply did not populate this.
		/// </para>
		/// </remarks>
		public EquippedItemEntry[] EquippedItems;
	}

	/// <summary>
	/// One occupied equipment slot, as presented to the character select screen.
	/// </summary>
	/// <remarks>
	/// An array of these rather than a <c>Dictionary&lt;int, int&gt;</c>, which is what this
	/// replaced. FishNet has no built-in serializer for a dictionary, so that field could never
	/// have reached the client as written — it would have needed one registered through
	/// <c>NetworkManager.Serializer.RegisterSerializerType</c> first. An array of a serializable
	/// struct is handled by codegen with nothing registered, and the slot doubles as the key, so
	/// the dictionary bought nothing the array does not already give.
	/// <para>
	/// Only occupied slots appear. An absent slot is an empty one; there is no entry meaning
	/// "nothing here".
	/// </para>
	/// </remarks>
	[Serializable]
	public struct EquippedItemEntry
	{
		/// <summary>Which slot this describes.</summary>
		public ItemSlot Slot;
		/// <summary>Template ID of the item in that slot. Never 0 for an entry that exists.</summary>
		public int TemplateID;
	}
}
