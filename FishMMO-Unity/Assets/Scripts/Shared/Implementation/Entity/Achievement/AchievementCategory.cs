namespace FishMMO.Shared
{
	/// <summary>
	/// Categories for achievements, used to group and organize different types of achievements in the game.
	/// </summary>
	public enum AchievementCategory : byte
	{
		/// <summary>
		/// Achievements related to learning or using abilities (skills, spells, etc).
		/// </summary>
		Ability = 0,

		/// <summary>
		/// Achievements for character progression, such as leveling up or stat increases.
		/// </summary>
		Character,

		/// <summary>
		/// Achievements for combat actions, such as defeating enemies or bosses.
		/// </summary>
		Combat,

		/// <summary>
		/// Achievements for crafting items, equipment, or consumables.
		/// </summary>
		Crafting,

		/// <summary>
		/// Achievements for completing dungeons or dungeon-related objectives.
		/// </summary>
		Dungeon,

		/// <summary>
		/// Achievements related to the environment, such as weather, biomes, or world events.
		/// </summary>
		Environment,

		/// <summary>
		/// Achievements for participating in special or limited-time events.
		/// </summary>
		Events,

		/// <summary>
		/// Achievements for exploring the world, discovering locations, or uncovering secrets.
		/// </summary>
		Exploration,

		/// <summary>
		/// Achievements for gathering resources, such as mining, fishing, or harvesting.
		/// </summary>
		Gathering,

		/// <summary>
		/// Achievements for joining, creating, or contributing to a guild.
		/// </summary>
		Guild,

		/// <summary>
		/// Achievements for building, decorating, or owning housing.
		/// </summary>
		Housing,

		/// <summary>
		/// Achievements for discovering or interacting with game lore.
		/// </summary>
		Lore,

		/// <summary>
		/// Achievements for mastering skills, professions, or other game systems.
		/// </summary>
		Mastery,

		/// <summary>
		/// Miscellaneous achievements that do not fit other categories.
		/// </summary>
		Miscellaneous,

		/// <summary>
		/// Achievements related to pets, such as collecting, training, or battling with pets.
		/// </summary>
		Pets,

		/// <summary>
		/// Achievements for player-vs-player (PvP) activities.
		/// </summary>
		PvP,

		/// <summary>
		/// Achievements for seasonal or holiday events.
		/// </summary>
		Seasonal,

		/// <summary>
		/// Achievements for social interactions, such as making friends or joining parties.
		/// </summary>
		Social,

		/// <summary>
		/// Achievements for survival-related activities, such as staying alive or overcoming hazards.
		/// </summary>
		Survival,

		/// <summary>
		/// Achievements for trading, bartering, or using the marketplace.
		/// </summary>
		Trading,

		/// <summary>
		/// Achievements for world-related milestones, such as server-wide goals or global events.
		/// </summary>
		World,
	}
}