using System;
using UnityEngine;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// The wind heard through what grows round the listener: a hiss in waves over standing grass, the rustle of
	/// broadleaved crowns, the sigh of pines. Each is a loop (<see cref="AmbientSounds"/>) on the Ambient channel, at the
	/// clock's place in it, as loud as the wind is strong and the growth is near and thick.
	/// </summary>
	/// <remarks>
	/// The grass is read off the procedural grass round the listener (<see cref="GrassBladeSystem.CoverAt"/>), the trees
	/// off the crowns (<see cref="LeafCanopyMap.Woodland"/>); both every quarter second, which is as fast as anyone walks
	/// out of a wood. Under a roof or in a cave the beds fall away with the shelter.
	/// </remarks>
	public sealed class WindBedsPresenter : IDisposable
	{
		/// <summary>The normalised wind (1 = 30 m/s) at which the growth is at its loudest: 12 m/s, a strong breeze.</summary>
		public const float FullWind = 0.4f;

		/// <summary>Metres round the listener the trees are counted over.</summary>
		public const float WoodReachMetres = 40f;

		/// <summary>Grass this tall, metres, and thick, is at its loudest.</summary>
		public const float FullGrassMetres = 0.5f;

		private const float FadeSeconds = 1.5f;
		private const float SampleSeconds = 0.25f;

		private static readonly AmbientBed[] beds = (AmbientBed[])Enum.GetValues(typeof(AmbientBed));
		private readonly Transform parent;
		private readonly AudioSource[] sources = new AudioSource[beds.Length];
		private readonly ChannelAudioSource[] channels = new ChannelAudioSource[beds.Length];
		private readonly float[] levels = new float[beds.Length];
		private float grass, wood, deciduous;
		private float sinceSample = float.MaxValue;

		public WindBedsPresenter(Transform parent)
		{
			this.parent = parent;
		}

		/// <summary>How much a wind drives the growth's sound, 0..1: from a breath of air to a strong breeze, a little more in a gust.</summary>
		public static float Drive(float windNormalised, float gust)
		{
			return Mathf.Clamp01(Mathf.Pow(Mathf.Clamp01(windNormalised / FullWind), 1.1f) * (0.8f + 0.4f * Mathf.Clamp01(gust)));
		}

		/// <summary>How loud each bed should be, 0..1.</summary>
		public static float Target(AmbientBed bed, float drive, float grassMetres, float woodland, float deciduousShare, float outdoors)
		{
			switch (bed)
			{
				case AmbientBed.GrassWind: return drive * Mathf.Clamp01(grassMetres / FullGrassMetres) * outdoors;
				case AmbientBed.LeafWind: return drive * woodland * deciduousShare * outdoors;
				case AmbientBed.NeedleWind: return drive * woodland * (1f - deciduousShare) * outdoors;
				default: return 0f;
			}
		}

		public void Update(Vector3 listener, float outdoors, float deltaTime)
		{
			WeatherPresentation presentation = WeatherPresentation.Instance;
			WeatherFrame frame = presentation != null ? presentation.Shown : WeatherFrame.Clear;
			float drive = Drive(frame[WeatherChannel.WindSpeed], frame[WeatherChannel.WindGust]);

			sinceSample += deltaTime;
			if (sinceSample >= SampleSeconds)
			{
				sinceSample = 0f;
				grass = GrassAround(listener);
				wood = LeafCanopyMap.Woodland(listener, WoodReachMetres, out deciduous);
			}

			float step = deltaTime / FadeSeconds;
			for (int i = 0; i < beds.Length; i++)
			{
				float target = Target(beds[i], drive, grass, wood, deciduous, outdoors);
				if (sources[i] == null)
				{
					if (target <= 0.001f)
					{
						continue;
					}
					Make(i);
				}
				AudioSource source = sources[i];
				if (source.clip == null)
				{
					AmbientBed bed = beds[i];
					source.clip = SoundLibrary.Get(AmbientSounds.Key(bed), () => AmbientSounds.Make(bed));
					if (source.clip == null)
					{
						continue;
					}
				}
				levels[i] = Mathf.MoveTowards(levels[i], target, step);
				channels[i].AuthoredVolume = levels[i];
				if (levels[i] > 0.001f && !source.isPlaying)
				{
					ClockedLoop.Start(source);
				}
				else if (levels[i] <= 0.001f && source.isPlaying)
				{
					source.Stop();
				}
				else
				{
					ClockedLoop.Keep(source);
				}
			}
		}

		/// <summary>The standing grass round the listener, metres: the mean of five points over about ten metres.</summary>
		private static float GrassAround(Vector3 at)
		{
			float sum = GrassBladeSystem.CoverAt(at.x, at.z);
			sum += GrassBladeSystem.CoverAt(at.x + 6f, at.z);
			sum += GrassBladeSystem.CoverAt(at.x - 6f, at.z);
			sum += GrassBladeSystem.CoverAt(at.x, at.z + 6f);
			sum += GrassBladeSystem.CoverAt(at.x, at.z - 6f);
			return sum / 5f;
		}

		private void Make(int i)
		{
			var go = new GameObject("Wind " + beds[i]);
			go.transform.SetParent(parent, false);
			AudioSource source = go.AddComponent<AudioSource>();
			source.loop = true;
			source.playOnAwake = false;
			source.spatialBlend = 0f;
			source.volume = 0f;
			ChannelAudioSource channel = go.AddComponent<ChannelAudioSource>();
			channel.SetChannel(AudioChannel.Ambient);
			channel.AuthoredVolume = 0f;
			sources[i] = source;
			channels[i] = channel;
		}

		public void Dispose()
		{
			for (int i = 0; i < sources.Length; i++)
			{
				if (sources[i] != null)
				{
					UnityEngine.Object.Destroy(sources[i].gameObject);
				}
				sources[i] = null;
				channels[i] = null;
				levels[i] = 0f;
			}
		}
	}
}
