using System.Collections.Generic;
using System.Reflection;
using FishMMO.Client;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for routing an <see cref="AudioSource"/> through a player volume channel.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Only Master was ever applied, on the listener, so every sound moved together and nothing
	/// finer could be offered. Routing lets a player turn music down without turning combat down
	/// with it, which is the whole point of having channels at all.
	/// </para>
	/// <para>
	/// Two mistakes are easy here and neither is audible as a bug so much as a slow wrongness, so
	/// both have tests: scaling by Master a second time on top of the listener, which makes the
	/// master slider behave quadratically; and re-reading the source's current volume as the base,
	/// which ratchets a sound quieter every time the player touches the slider and never gives it
	/// back.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AudioChannelRoutingTests
	{
		private readonly List<GameObject> created = new List<GameObject>();
		private readonly Dictionary<AudioChannel, float> savedLevels = new Dictionary<AudioChannel, float>();

		[SetUp]
		public void SetUp()
		{
			// Levels are global, so anything moved here has to be put back.
			foreach (AudioChannel channel in ClientAudioSettings.PlayableChannels)
			{
				savedLevels[channel] = ClientAudioSettings.GetVolume(channel);
			}
		}

		[TearDown]
		public void TearDown()
		{
			foreach (KeyValuePair<AudioChannel, float> pair in savedLevels)
			{
				ClientAudioSettings.SetVolume(pair.Key, pair.Value);
			}
			savedLevels.Clear();

			foreach (ChannelAudioSource routed in live)
			{
				if (routed != null)
				{
					Lifecycle(routed, "OnDisable");
				}
			}
			live.Clear();

			foreach (GameObject go in created)
			{
				if (go != null)
				{
					Object.DestroyImmediate(go);
				}
			}
			created.Clear();
		}

		private readonly List<ChannelAudioSource> live = new List<ChannelAudioSource>();

		/// <summary>
		/// Builds a routed source and starts it, since edit mode does not run the Unity callbacks.
		/// </summary>
		/// <remarks>
		/// Awake and OnEnable are invoked by hand because Unity does not call them for a component
		/// added outside play mode. What is under test is the routing -- that a source subscribed
		/// to its channel follows it -- not Unity's dispatch of its own lifecycle, which is not
		/// this project's to verify. TearDown calls OnDisable for the matching reason: without it
		/// the static volume event keeps handlers on destroyed components, and the next test to
		/// move a slider drives a routine on an object that no longer exists.
		/// </remarks>
		private ChannelAudioSource NewSource(AudioChannel channel, float authoredVolume)
		{
			GameObject go = new GameObject($"RoutedSource_{channel}");
			created.Add(go);

			AudioSource audio = go.AddComponent<AudioSource>();
			audio.volume = authoredVolume;

			ChannelAudioSource routed = go.AddComponent<ChannelAudioSource>();
			routed.SetChannel(channel);

			Lifecycle(routed, "Awake");
			Lifecycle(routed, "OnEnable");
			live.Add(routed);

			return routed;
		}

		private static void Lifecycle(ChannelAudioSource routed, string method)
		{
			MethodInfo info = typeof(ChannelAudioSource).GetMethod(
				method, BindingFlags.Instance | BindingFlags.NonPublic);

			LogAssert.IsNotNull(info, $"ChannelAudioSource must still have {method}.");
			info.Invoke(routed, null);
		}

		private static float VolumeOf(ChannelAudioSource routed) =>
			routed.GetComponent<AudioSource>().volume;

		[Test]
		public void ASourceIsScaledByItsOwnChannel()
		{
			ClientAudioSettings.SetVolume(AudioChannel.Music, 0.5f);

			ChannelAudioSource music = NewSource(AudioChannel.Music, 1.0f);

			// The slider curve is squared, so 0.5 on the slider is 0.25 of amplitude.
			LogAssert.AreEqual(ClientAudioSettings.EffectiveVolume(AudioChannel.Music), VolumeOf(music),
				"a routed source must play at its channel's level");
		}

		[Test]
		public void MovingOneChannel_LeavesTheOthersAlone()
		{
			/* The entire reason channels exist. If this fails the player has six sliders that all
			 * do the same thing. */
			ClientAudioSettings.SetVolume(AudioChannel.Music, 1.0f);
			ClientAudioSettings.SetVolume(AudioChannel.Effects, 1.0f);

			ChannelAudioSource music = NewSource(AudioChannel.Music, 1.0f);
			ChannelAudioSource effects = NewSource(AudioChannel.Effects, 1.0f);

			ClientAudioSettings.SetVolume(AudioChannel.Music, 0.0f);

			LogAssert.AreEqual(0.0f, VolumeOf(music), "music was turned off");
			LogAssert.AreEqual(1.0f, VolumeOf(effects), "combat must not follow the music slider");
		}

		[Test]
		public void ASourceFollowsItsChannelWhileTheSliderMoves()
		{
			ClientAudioSettings.SetVolume(AudioChannel.Ambient, 1.0f);
			ChannelAudioSource ambient = NewSource(AudioChannel.Ambient, 1.0f);

			ClientAudioSettings.SetVolume(AudioChannel.Ambient, 0.5f);

			LogAssert.AreEqual(ClientAudioSettings.EffectiveVolume(AudioChannel.Ambient), VolumeOf(ambient),
				"the source must track the slider, not only its value at spawn");
		}

		[Test]
		public void RepeatedChanges_DoNotRatchetTheVolumeDown()
		{
			/* The failure that hides. Scaling is applied by writing the same field it is read from,
			 * so an implementation that re-read the current volume as its base would fold the
			 * channel level in again on every change -- quieter each time, and never recoverable by
			 * putting the slider back. */
			ClientAudioSettings.SetVolume(AudioChannel.Effects, 1.0f);
			ChannelAudioSource effects = NewSource(AudioChannel.Effects, 1.0f);

			for (int i = 0; i < 5; ++i)
			{
				ClientAudioSettings.SetVolume(AudioChannel.Effects, 0.5f);
				ClientAudioSettings.SetVolume(AudioChannel.Effects, 1.0f);
			}

			LogAssert.AreEqual(1.0f, VolumeOf(effects),
				"putting the slider back must restore the original volume exactly");
		}

		[Test]
		public void MasterIsNotAppliedTwice()
		{
			/* Master is on the AudioListener and reaches everything already. A source scaled by it
			 * again would be squared -- a master at half playing this source at a quarter while an
			 * untagged source beside it played at half. */
			ClientAudioSettings.SetVolume(AudioChannel.Master, 0.5f);

			LogAssert.AreEqual(1.0f, ChannelAudioSource.ChannelScale(AudioChannel.Master),
				"Master must contribute nothing per-source; the listener already applies it");
		}

		[Test]
		public void ASourceTaggedMaster_IsLeftUnscaledRatherThanSilenced()
		{
			/* Being wrong quietly is better than being wrong loudly. A mis-tagged source that plays
			 * at full volume is noticed and fixed; one that is silent looks like a missing sound. */
			ClientAudioSettings.SetVolume(AudioChannel.Master, 0.0f);

			ChannelAudioSource tagged = NewSource(AudioChannel.Master, 1.0f);

			LogAssert.AreEqual(1.0f, VolumeOf(tagged),
				"a source tagged Master keeps its authored volume; the listener does the rest");
		}

		[Test]
		public void TheAuthoredVolumeIsWhatGetsScaled()
		{
			/* A quiet sound stays quiet relative to a loud one. Without this the channel level
			 * would flatten every source to the same volume. */
			ClientAudioSettings.SetVolume(AudioChannel.Effects, 1.0f);

			ChannelAudioSource quiet = NewSource(AudioChannel.Effects, 0.25f);

			LogAssert.AreEqual(0.25f, VolumeOf(quiet),
				"the mix balance between sources must survive routing");

			ClientAudioSettings.SetVolume(AudioChannel.Effects, 0.5f);

			LogAssert.AreEqual(0.25f * ClientAudioSettings.EffectiveVolume(AudioChannel.Effects),
				VolumeOf(quiet),
				"and must still hold once the player moves the slider");
		}

		[Test]
		public void EveryChannelIsOfferedToThePlayer()
		{
			/* The sliders are generated from this array, so it is what decides whether a channel a
			 * sound can be routed to is one the player can actually control. */
			foreach (AudioChannel channel in System.Enum.GetValues(typeof(AudioChannel)))
			{
				LogAssert.IsTrue(
					System.Array.IndexOf(ClientAudioSettings.PlayableChannels, channel) >= 0,
					$"{channel} can be routed to but is not offered as a slider");
			}
		}
	}
}
