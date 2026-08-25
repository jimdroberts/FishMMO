namespace FishMMO.Client
{
	/// <summary>
	/// The volume groups a player can set independently.
	/// </summary>
	/// <remarks>
	/// Deliberately an enum rather than a set of free-standing floats: the channel is the key into
	/// configuration (<c>Audio.Volume.&lt;name&gt;</c>), the index of the row in the options panel,
	/// and the argument every playback call passes. Adding a channel should mean adding one enum
	/// member and one label, not touching three parallel lists.
	/// <para>
	/// <see cref="Master"/> is not a group of its own — it scales all the others. Nothing should
	/// play <em>on</em> Master; <see cref="ClientAudioSettings.EffectiveVolume"/> folds it into
	/// whichever channel is asked for.
	/// </para>
	/// </remarks>
	public enum AudioChannel
	{
		/// <summary>Scales every other channel. Applied to the scene's <c>AudioListener</c>.</summary>
		Master = 0,
		/// <summary>Background score.</summary>
		Music = 1,
		/// <summary>One-shot gameplay sounds: abilities, impacts, footsteps.</summary>
		Effects = 2,
		/// <summary>Looping environmental beds tied to a region or biome.</summary>
		Ambient = 3,
		/// <summary>Interface feedback: clicks, notifications, alerts.</summary>
		Interface = 4,
		/// <summary>Spoken dialogue and emotes.</summary>
		Voice = 5,
	}
}
