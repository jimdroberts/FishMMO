namespace FishMMO.Shared
{
	/// <summary>
	/// What a marker represents. Drives the shape and colour a marker is drawn in, the draw order
	/// between overlapping markers, and which filter rows the world map offers.
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

		/// <summary>
		/// A discovered fast-travel point, drawn from the character's own unlock record rather than
		/// from a marker component. Appended last: the enum ordinal is draw order.
		/// </summary>
		Waypoint,

		/// <summary>
		/// A dungeon entrance — the portal in the open world that leads to an instanced scene.
		/// </summary>
		/// <remarks>
		/// Registered by the <c>DungeonEntrance</c> interactable itself rather than authored onto a
		/// prefab, and revealed by the discovery rule: the entrance appears on both maps once the
		/// player has explored the chunk it stands in. It is a fixed, public fixture once found, so
		/// unlike a player character it is drawn exactly and labelled.
		/// <para>
		/// Appended after <see cref="Waypoint"/>, which is not cosmetic — the ordinal is draw order
		/// and inserting anywhere earlier renumbers every type below it.
		/// </para>
		/// </remarks>
		DungeonEntrance,

		/*
		 * Generated points of interest, one type per kind (Jim, 2026-10-10), each named after its
		 * NameGeneration.POIType value so PointOfInterestKinds.MarkerFor maps them by name. Landmark and
		 * DungeonEntrance above serve the kinds of the same name. Appended in POIType's order; the ordinal is
		 * draw order, so new kinds go on the end here too.
		 */
		Camp, Shrine, Tower, Bridge, Clearing, Spring, Cave, Monument, Wreck,
		Waterfall, Rapids, River, Lake, HotSpring, RiverMouth, Delta,
		Peak, Pass, Gorge, Mesa, Butte, Valley, Island, Bay, Headland, NaturalArch, Sinkhole, Crater, DuneSea, SaltFlat, Glacier,
		Volcano, LavaLake, FumaroleField, ObsidianField,
		SunkenTemple, DrownedVillage, WitchHut, StiltVillage, BogShrine, MangroveMaze, WispHollow,
		Grotto, SeaCave, IceCave, LavaTube, Overhang,
		BanditCamp, HuntingLodge, LumberCamp, FishingCamp, MonsterDen, Nest,
		RitualSite, StoneCircle, Temple, Monastery, FeyRing, LeyNexus, FallenStar, CorruptedGrove, Portal,
		Graveyard, Barrow, Crypt, Battlefield, Ossuary,
		Ruins, RuinedTower, Statue, Obelisk, AncientRoad, AbandonedFarm, Hermitage, Oasis,
		Village, Town, City, Capital, Port, Keep, Castle, Fortress, TradingPost, Waystation, Mine, Quarry, Lighthouse,
		SmugglersCove, PirateCove, CoralReef, SunkenShip, SunkenRuins, SunkenCity,
		BossLair, OreVein, CrystalFormation, AncientTree, HerbGrove,
		IceGeyserField, Cryovolcano, MethaneLake, ImpactBasin, TidalRift,
	}
}
