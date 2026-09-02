using System;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Plays an <see cref="AudioSource"/> through one of the player's volume channels.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The channel model already existed and only Master was ever applied, through
	/// <see cref="AudioListener.volume"/>. That covers every sound at once, which is why it worked
	/// without anything like this component, and also why nothing finer than "all of it" could be
	/// offered. This is the missing piece: it scales one source by its own channel so a player can
	/// turn music down without turning combat down with it.
	/// </para>
	/// <para>
	/// Deliberately not an <c>AudioMixer</c>. A mixer is the usual answer and is a better one once
	/// there is an audio team, but its groups and exposed parameters live in a binary asset that
	/// can only be authored by hand in the editor, and every source has to be pointed at a group
	/// there as well. This keeps the routing in code, where it can be tested and where adding a
	/// sound means adding a component rather than editing a shared asset.
	/// </para>
	/// </remarks>
	[AddComponentMenu("FishMMO/Audio/Channel Audio Source")]
	[RequireComponent(typeof(AudioSource))]
	public sealed class ChannelAudioSource : MonoBehaviour
	{
		/// <summary>The channel this source's volume follows.</summary>
		[SerializeField]
		[Tooltip("Which of the player's volume sliders controls this sound.")]
		private AudioChannel channel = AudioChannel.Effects;

		/// <summary>The source being scaled.</summary>
		private AudioSource source;

		/// <summary>
		/// The volume the sound was authored at, before any channel scaling.
		/// </summary>
		/// <remarks>
		/// Captured once, because the scaling is applied by assigning to the same field it is read
		/// from. Re-reading it later would fold the channel level in again on every change, so a
		/// player nudging a slider would ratchet the sound quieter and never get it back.
		/// </remarks>
		private float authoredVolume = 1.0f;

		/// <summary>Whether <see cref="authoredVolume"/> has been taken yet.</summary>
		private bool captured;

		/// <summary>The channel this source plays on.</summary>
		public AudioChannel Channel => channel;

		/// <summary>
		/// The volume this sound was authored at, ignoring the player's channel level.
		/// </summary>
		/// <remarks>
		/// Assign through here rather than to <c>AudioSource.volume</c> when code wants to change
		/// how loud a sound is in the mix -- fading a track in, quietening a source with distance.
		/// Writing the source directly works until the player next moves a slider, at which point
		/// the value is overwritten by this component and the change is silently lost.
		/// </remarks>
		public float AuthoredVolume
		{
			get => authoredVolume;
			set
			{
				authoredVolume = Mathf.Clamp01(value);
				captured = true;
				Apply();
			}
		}

		private void Awake()
		{
			EnsureResolved();
		}

		private void OnEnable()
		{
			ClientAudioSettings.OnVolumeChanged += OnVolumeChanged;
			Apply();
		}

		private void OnDisable()
		{
			ClientAudioSettings.OnVolumeChanged -= OnVolumeChanged;
		}

		/// <summary>
		/// Re-applies this source's level when its own channel moves.
		/// </summary>
		/// <remarks>
		/// Master is ignored on purpose: it is applied to the listener and reaches every sound
		/// already. Reacting to it here as well would scale by it a second time, so a master at
		/// half would play this source at a quarter while an untagged source next to it played at
		/// half.
		/// </remarks>
		private void OnVolumeChanged(AudioChannel changed)
		{
			if (changed == channel)
			{
				Apply();
			}
		}

		/// <summary>
		/// Writes the authored volume, scaled by the player's level for this channel.
		/// </summary>
		public void Apply()
		{
			/* Resolved here rather than only in Awake. Apply is reachable before Awake has run --
			 * from a caller that sets the channel on a freshly added component, and from the editor
			 * -- and a null source there would silently skip the scaling rather than fail. */
			EnsureResolved();

			if (source == null)
			{
				return;
			}

			source.volume = authoredVolume * ChannelScale(channel);
		}

		/// <summary>
		/// Finds the source and takes its authored volume, if that has not happened yet.
		/// </summary>
		private void EnsureResolved()
		{
			if (source == null)
			{
				source = GetComponent<AudioSource>();
			}

			if (!captured && source != null)
			{
				authoredVolume = source.volume;
				captured = true;
			}
		}

		/// <summary>
		/// The multiplier a channel contributes to a source.
		/// </summary>
		/// <remarks>
		/// One for Master, which is not a per-source channel: it is already on the listener, and a
		/// source tagged Master would otherwise be scaled by it twice. Returning one rather than
		/// refusing the assignment keeps a mis-tagged source merely un-scaled instead of silent,
		/// which is the better way to be wrong.
		/// </remarks>
		/// <param name="channel">The channel to scale by.</param>
		public static float ChannelScale(AudioChannel channel)
		{
			return channel == AudioChannel.Master
				? 1.0f
				: ClientAudioSettings.EffectiveVolume(channel);
		}

		/// <summary>
		/// Points this source at a different channel.
		/// </summary>
		/// <param name="value">The channel to follow.</param>
		public void SetChannel(AudioChannel value)
		{
			if (!Enum.IsDefined(typeof(AudioChannel), value))
			{
				Log.Warning("ChannelAudioSource",
					$"{gameObject.name} was pointed at an unknown audio channel; leaving it on {channel}.");
				return;
			}

			channel = value;
			Apply();
		}
	}
}
