using System;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// The client's audio volumes: one level per <see cref="AudioChannel"/>, persisted to
	/// configuration, applied at boot, and readable by anything that plays a sound.
	/// </summary>
	/// <remarks>
	/// <para><b>Only Master is offered today.</b> See <see cref="PlayableChannels"/>: the other
	/// five levels are stored and applied correctly but nothing in the client plays through them
	/// yet, so the options panel does not show sliders that cannot be heard.</para>
	///
	/// <para><b>Why this is not an AudioMixer.</b> A mixer would be the natural home for per-channel
	/// levels, but it has to exist as an asset with a matching exposed parameter per group, and a
	/// mixer parameter set before the mixer has been loaded is silently dropped. This keeps the
	/// levels as plain data that any caller can read synchronously and that survives having no
	/// audio asset loaded at all — which is the state the client boots in. Master is the one level
	/// with somewhere to go on its own: <see cref="AudioListener.volume"/> scales everything the
	/// scene plays, whether or not the caller knew about channels.</para>
	///
	/// <para><b>Levels are stored, and applied, as a perceptual curve.</b> Loudness is not linear
	/// in amplitude: a slider at half travel that halves the amplitude sounds far quieter than
	/// half. <see cref="ToAmplitude"/> squares the slider value, which is the cheap approximation
	/// everything from game options to mixing desks uses, so the middle of the slider lands near
	/// the middle of the perceived range. The stored value is always the slider position, so the
	/// curve can be changed later without invalidating anybody's settings.</para>
	///
	/// <para><b>Muting when unfocused</b> is a volume decision, not a pause: the client keeps
	/// simulating, and a player who alt-tabs to a browser should not have to hear it. It is applied
	/// on top of Master rather than by writing zero into it, so the saved level is not destroyed by
	/// switching windows.</para>
	/// </remarks>
	public static class ClientAudioSettings
	{
		/// <summary>Number of channels, cached so the arrays below cannot drift from the enum.</summary>
		private static readonly int ChannelCount = Enum.GetValues(typeof(AudioChannel)).Length;

		/// <summary>
		/// The channels the client currently routes audio through, and therefore the only ones the
		/// options panel offers.
		/// </summary>
		/// <remarks>
		/// <para><b>Master alone, because Master is the only one that reaches anything.</b> It is
		/// applied to <see cref="AudioListener.volume"/>, which scales every sound the scene plays
		/// whether or not the caller knew about channels. The other five have no consumer: nothing
		/// in the client owns an <c>AudioSource</c> yet, so a Music or Effects slider would save its
		/// value perfectly and change nothing a player could hear. A control that does nothing is
		/// worse than a missing one — it teaches the player that the settings screen lies.</para>
		///
		/// <para>The rest of this class deliberately stays whole. Every channel keeps its key, its
		/// default, its stored level and its change event, so wiring up the audio system later is
		/// adding entries to this one array rather than rebuilding the model — and a level saved by
		/// a build that offered more channels is still read back correctly by one that offers
		/// fewer.</para>
		/// </remarks>
		public static readonly AudioChannel[] PlayableChannels =
		{
			AudioChannel.Master,
			AudioChannel.Music,
			AudioChannel.Effects,
			AudioChannel.Ambient,
			AudioChannel.Interface,
			AudioChannel.Voice,
		};

		/// <summary>Player-facing label for each channel, indexed by <see cref="AudioChannel"/>.</summary>
		public static readonly string[] ChannelLabels =
		{
			"Master Volume",
			"Music",
			"Sound Effects",
			"Ambient",
			"Interface",
			"Voice",
		};

		/// <summary>Default level for each channel, indexed by <see cref="AudioChannel"/>.</summary>
		/// <remarks>
		/// Music sits below the rest on purpose. It is the only channel that plays continuously,
		/// and a score mixed level with combat effects buries the audio cues a player reacts to.
		/// </remarks>
		private static readonly float[] ChannelDefaults =
		{
			1.0f,   // Master
			0.6f,   // Music
			1.0f,   // Effects
			0.8f,   // Ambient
			0.8f,   // Interface
			1.0f,   // Voice
		};

		/// <summary>Current level per channel, as slider positions in the range 0..1.</summary>
		/// <remarks>
		/// Sized from the enum, not from a literal. Static field initialisers run in declaration
		/// order, so <see cref="ChannelCount"/> above is already populated here — and a channel
		/// added to the enum widens this array instead of silently falling off the end of it.
		/// </remarks>
		private static readonly float[] levels = new float[ChannelCount];

		/// <summary>True once <see cref="ApplySaved"/> has populated <see cref="levels"/>.</summary>
		private static bool loaded;

		/// <summary>True while the client's window does not have focus.</summary>
		private static bool unfocused;

		/// <summary>Raised whenever any channel's level changes.</summary>
		/// <remarks>
		/// Playback that has already started cannot be rescaled by reading a number later, so a
		/// looping source — music, an ambient bed — has to be told. One-shots do not need to
		/// subscribe; they read the level as they are fired.
		/// </remarks>
		public static event Action<AudioChannel> OnVolumeChanged;

		/// <summary>Mute the client while its window is not focused.</summary>
		public static bool MuteWhenUnfocused
		{
			get => ClientSettings.GetBool(ClientSettings.AudioMuteUnfocusedKey, false);
			set
			{
				ClientSettings.Set(ClientSettings.AudioMuteUnfocusedKey, value);
				ApplyMasterToListener();
			}
		}

		/// <summary>
		/// The slider position saved for a channel, in the range 0..1.
		/// </summary>
		public static float GetVolume(AudioChannel channel)
		{
			EnsureLoaded();

			int index = (int)channel;
			if (index < 0 || index >= levels.Length)
			{
				return 1.0f;
			}
			return levels[index];
		}

		/// <summary>
		/// Sets a channel's level, persists it, and applies it.
		/// </summary>
		/// <param name="channel">The channel to change.</param>
		/// <param name="value">Slider position in the range 0..1. Clamped.</param>
		public static void SetVolume(AudioChannel channel, float value)
		{
			EnsureLoaded();

			int index = (int)channel;
			if (index < 0 || index >= levels.Length)
			{
				return;
			}

			float clamped = float.IsNaN(value) ? DefaultVolume(channel) : Mathf.Clamp01(value);
			if (Mathf.Approximately(levels[index], clamped))
			{
				return;
			}

			levels[index] = clamped;
			ClientSettings.Set(KeyFor(channel), clamped);

			if (channel == AudioChannel.Master)
			{
				ApplyMasterToListener();
			}

			try
			{
				OnVolumeChanged?.Invoke(channel);
			}
			catch (Exception ex)
			{
				Log.Error("ClientAudioSettings", "A volume-changed subscriber threw.", ex);
			}
		}

		/// <summary>
		/// The amplitude a sound on this channel should actually be played at.
		/// </summary>
		/// <param name="channel">The channel the sound belongs to.</param>
		/// <returns>A multiplier in the range 0..1, ready to assign to <c>AudioSource.volume</c>.</returns>
		/// <remarks>
		/// Master is <b>not</b> folded in here. It is already applied to the
		/// <see cref="AudioListener"/>, and applying it twice would square it — a master at 0.5
		/// would play everything at a quarter. Callers therefore pass their own channel and get
		/// only that channel's contribution.
		/// </remarks>
		public static float EffectiveVolume(AudioChannel channel)
		{
			return ToAmplitude(GetVolume(channel));
		}

		/// <summary>The default level for a channel.</summary>
		public static float DefaultVolume(AudioChannel channel)
		{
			int index = (int)channel;
			return index >= 0 && index < ChannelDefaults.Length ? ChannelDefaults[index] : 1.0f;
		}

		/// <summary>The player-facing label for a channel.</summary>
		public static string LabelFor(AudioChannel channel)
		{
			int index = (int)channel;
			return index >= 0 && index < ChannelLabels.Length ? ChannelLabels[index] : channel.ToString();
		}

		/// <summary>The configuration key a channel's level is stored under.</summary>
		public static string KeyFor(AudioChannel channel)
		{
			return ClientSettings.AudioVolumePrefix + channel.ToString();
		}

		/// <summary>
		/// Converts a slider position into an amplitude multiplier.
		/// </summary>
		/// <remarks>
		/// Squared, so the slider's travel maps roughly onto perceived loudness rather than onto
		/// raw amplitude. Zero stays exactly zero, which matters — a channel dragged to the bottom
		/// must be silent, not merely quiet.
		/// </remarks>
		public static float ToAmplitude(float sliderValue)
		{
			float clamped = Mathf.Clamp01(sliderValue);
			return clamped * clamped;
		}

		/// <summary>
		/// Reads every channel from configuration and applies the result.
		/// </summary>
		public static void ApplySaved()
		{
			loaded = true;

			for (int i = 0; i < levels.Length && i < ChannelCount; ++i)
			{
				AudioChannel channel = (AudioChannel)i;
				levels[i] = ClientSettings.GetFloat(KeyFor(channel), DefaultVolume(channel), 0.0f, 1.0f);
			}

			ApplyMasterToListener();
			ClientAudioFocusWatcher.Install();
		}

		/// <summary>
		/// Restores the offered channels to their defaults and persists the result.
		/// </summary>
		/// <remarks>
		/// Scoped to <see cref="PlayableChannels"/>, which is what the button the player pressed
		/// actually offers. That is now every channel, but the scoping stays: it is the reason a
		/// channel retired from the list stops being written to Configuration.cfg rather than
		/// lingering there as a setting nobody is shown. A channel with no stored key already
		/// resolves to <see cref="DefaultVolume"/>, so there is nothing to put back for one left
		/// out.
		/// </remarks>
		public static void ResetToDefaults()
		{
			EnsureLoaded();

			for (int i = 0; i < PlayableChannels.Length; ++i)
			{
				AudioChannel channel = PlayableChannels[i];

				int index = (int)channel;
				if (index < 0 || index >= levels.Length)
				{
					continue;
				}

				levels[index] = DefaultVolume(channel);
				ClientSettings.Set(KeyFor(channel), levels[index]);
			}

			ClientSettings.Set(ClientSettings.AudioMuteUnfocusedKey, false);
			ApplyMasterToListener();

			for (int i = 0; i < PlayableChannels.Length; ++i)
			{
				try
				{
					OnVolumeChanged?.Invoke(PlayableChannels[i]);
				}
				catch (Exception ex)
				{
					Log.Error("ClientAudioSettings", "A volume-changed subscriber threw.", ex);
				}
			}
		}

		/// <summary>
		/// Records whether the client's window has focus and re-applies the master level.
		/// </summary>
		/// <param name="focused">True when the window has focus.</param>
		internal static void SetWindowFocused(bool focused)
		{
			unfocused = !focused;
			ApplyMasterToListener();
		}

		/// <summary>
		/// Writes the master level onto the listener, honouring the unfocused mute.
		/// </summary>
		private static void ApplyMasterToListener()
		{
			float amplitude = ToAmplitude(GetVolume(AudioChannel.Master));

			if (unfocused && MuteWhenUnfocused)
			{
				amplitude = 0.0f;
			}

			AudioListener.volume = amplitude;
		}

		/// <summary>
		/// Populates the levels on first use, for callers that run before the boot phase.
		/// </summary>
		private static void EnsureLoaded()
		{
			if (loaded)
			{
				return;
			}
			ApplySaved();
		}
	}
}
