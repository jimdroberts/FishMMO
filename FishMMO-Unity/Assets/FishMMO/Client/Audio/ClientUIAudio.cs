using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Plays one-shot interface sounds — button clicks, confirmations, refusals — on the
	/// <see cref="AudioChannel.Interface"/> channel.
	/// </summary>
	/// <remarks>
	/// <para>The first consumer of the interface channel. Until now nothing in the client owned an
	/// <see cref="AudioSource"/>, so the player's Interface slider adjusted nothing. This owns
	/// one: a single 2D source on a persistent host object, scaled by
	/// <see cref="ChannelAudioSource"/> so the slider and the mute-when-unfocused rule apply to
	/// it like any other channel.</para>
	/// <para>A UI panel that wants a sound assigns a clip to a serialized field and calls
	/// <see cref="Play"/>. A null clip is a no-op rather than an error, because an unassigned
	/// clip is the normal state of a project that has not recorded that sound yet.</para>
	/// </remarks>
	public static class ClientUIAudio
	{
		private static AudioSource source;
		private static ChannelAudioSource channel;

		/// <summary>
		/// Plays a clip once at the interface channel's volume.
		/// </summary>
		/// <param name="clip">The clip. Null plays nothing.</param>
		/// <param name="volume">Authored volume, 0..1, before the channel scale.</param>
		public static void Play(AudioClip clip, float volume = 1.0f)
		{
			if (clip == null)
			{
				return;
			}

			AudioSource audioSource = Resolve();
			if (audioSource == null)
			{
				return;
			}

			/* PlayOneShot scales by the source's volume, which ChannelAudioSource keeps equal to
			 * the interface channel's effective volume. The authored volume rides on top. */
			audioSource.PlayOneShot(clip, Mathf.Clamp01(volume));
		}

		/// <summary>
		/// The host source, created on first use and kept for the life of the process.
		/// </summary>
		private static AudioSource Resolve()
		{
			if (source != null)
			{
				return source;
			}

			GameObject host = new GameObject("UIAudio");
			Object.DontDestroyOnLoad(host);

			source = host.AddComponent<AudioSource>();
			source.playOnAwake = false;
			source.loop = false;
			source.spatialBlend = 0.0f;
			source.ignoreListenerPause = true;
			source.volume = 1.0f;

			channel = host.AddComponent<ChannelAudioSource>();
			channel.SetChannel(AudioChannel.Interface);
			channel.AuthoredVolume = 1.0f;

			return source;
		}
	}
}
