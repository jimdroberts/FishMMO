namespace FishMMO.Shared
{
	/// <summary>
	/// What a marker represents. Drives the default icon tier, the draw order between overlapping
	/// markers, and which filter rows the world map offers.
	/// </summary>
	/// <remarks>
	/// Deliberately not a flags enum. A marker is exactly one thing, and the filter UI wants a
	/// stable ordinal per row rather than a bit the authoring can accidentally combine.
	/// </remarks>
	public enum MapMarkerType : byte
	{
		/// <summary>The local player. Always drawn, always last, never clamped to the edge.</summary>
		Self = 0,
		/// <summary>A member of the local player's party.</summary>
		PartyMember,
		/// <summary>A member of the local player's guild who is not in the party.</summary>
		GuildMember,
		/// <summary>Another player character the faction matrix rates as an ally.</summary>
		FriendlyPlayer,
		/// <summary>Another player character with no faction standing either way.</summary>
		NeutralPlayer,
		/// <summary>Another player character the faction matrix rates as an enemy.</summary>
		HostilePlayer,
		/// <summary>A non-hostile NPC with no more specific role.</summary>
		NPC,
		/// <summary>An NPC that sells goods.</summary>
		Vendor,
		/// <summary>An NPC that offers or completes quests.</summary>
		QuestGiver,
		/// <summary>An NPC that teaches abilities.</summary>
		Trainer,
		/// <summary>A banker, mailbox, or other service fixture.</summary>
		Service,
		/// <summary>A gatherable node.</summary>
		Resource,
		/// <summary>A hostile NPC.</summary>
		Enemy,
		/// <summary>A door, chest, lever, or other world interactable.</summary>
		Interactable,
		/// <summary>A teleporter or zone exit.</summary>
		Teleporter,
		/// <summary>An authored point of interest baked into the scene's map definition.</summary>
		Landmark,
		/// <summary>A note the player placed on the world map themselves.</summary>
		Note,
	}
}
