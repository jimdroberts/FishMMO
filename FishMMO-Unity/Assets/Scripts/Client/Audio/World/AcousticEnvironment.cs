using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>One echo of a sound: where it seems to come from, how much later, and how much of it comes back.</summary>
	public struct EchoTap
	{
		/// <summary>The image of the source in the wall: where the echo is heard from.</summary>
		public Vector3 Position;
		/// <summary>Seconds after the direct sound.</summary>
		public float Delay;
		/// <summary>The echo's level against the direct sound's.</summary>
		public float Gain;
		/// <summary>Metres the echo travels, source to wall to listener.</summary>
		public float Path;
	}

	/// <summary>
	/// What the space round the listener does to sound: a cave rings, a hall booms, a canyon throws a footstep back from
	/// its far wall. Measured from the listener with rays, and given to the audio two ways: a reverb (the diffuse tail of
	/// every sound in an enclosed space) and echoes (the distinct return of a loud sound off a wall far enough away to
	/// hear it apart).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The reverb, from the room's size and what it is made of.</b> The rays give the mean distance to the surfaces
	/// round the listener and what each one is (rock, boards, earth, snow: <see cref="GroundSurface"/>), so how much
	/// sound each absorbs. A ray that escapes is an opening, and an opening absorbs everything that reaches it. The
	/// decay time is Eyring's: T60 = 0.161 V / (−S ln(1 − ᾱ)), with V/S = mean free path / 4 and the mean free path
	/// 4/3 of the mean ray (a sphere's). A rock cavern with a few openings rings for three or four seconds; a timber
	/// room half a second; a forest, whose trunks are hit but whose sky is open, not at all. The reverb's level follows
	/// how roofed the place is, so only a space with a ceiling has a tail.
	/// </para>
	/// <para>
	/// <b>The echoes, by images.</b> A flat wall returns a sound as if from the source's mirror image behind it: the
	/// echo comes from that image, delayed by the extra path, weakened by spreading over it and by what the wall
	/// absorbs. Walls nearer than <see cref="EchoMinMetres"/> return in under a tenth of a second, which the ear hears as
	/// part of the sound (the reverb has them); further ones are heard apart. Only walls that face back towards the
	/// listener return a sound to it, so the ground seen at a grazing angle throws back nothing.
	/// </para>
	/// <para>
	/// <b>The reverb is Unity's</b> (an <see cref="AudioReverbZone"/> held round the listener), which WebGL does not
	/// have; the echoes are ordinary delayed sources, which it does. On WebGL a cave still echoes, it only does not ring.
	/// </para>
	/// </remarks>
	public sealed class AcousticEnvironment : IDisposable
	{
		/// <summary>Rays round the listener, spread evenly over the sphere.</summary>
		public const int Rays = 48;

		/// <summary>Rays cast each frame: the whole sphere every six frames.</summary>
		public const int RaysPerFrame = 8;

		/// <summary>The furthest a surface is looked for, metres: an echo from further is too faint to matter.</summary>
		public const float ReachMetres = 90f;

		/// <summary>A wall nearer than this, metres, returns its echo inside a tenth of a second: part of the sound, not apart from it.</summary>
		public const float EchoMinMetres = 17f;

		/// <summary>The most echoes a sound is given.</summary>
		public const int MaxEchoes = 3;

		/// <summary>Seconds the reverb takes to settle to a new place (the measurement steps; the ear should not).</summary>
		public const float SettleSeconds = 0.6f;

		private static readonly Vector3[] directions = Fibonacci(Rays);
		private readonly float[] distance = new float[Rays];
		private readonly Vector3[] point = new Vector3[Rays];
		private readonly Vector3[] normal = new Vector3[Rays];
		private readonly float[] absorption = new float[Rays];
		private readonly bool[] hit = new bool[Rays];
		private readonly bool[] measured = new bool[Rays];
		private readonly List<int> echoWalls = new List<int>();
		private readonly List<(float score, int ray)> ranked = new List<(float, int)>();
		private int next;
		private Vector3 listener;
		private int mask;
		private AudioReverbZone zone;
		private readonly Transform parent;

		// What the measurement says now, and what the reverb is easing to it.
		private float wetTarget, decayTarget = 1f, hfTarget = -1000f, reflectionsDelayTarget = 0.02f, reverbDelayTarget = 0.04f;
		private float wet, decay = 1f, hf = -1000f, reflectionsDelay = 0.02f, reverbDelay = 0.04f;

		public AcousticEnvironment(Transform parent)
		{
			this.parent = parent;
			int solid = LayerMask.GetMask("Default", "Ground");
			mask = solid != 0 ? solid : Physics.DefaultRaycastLayers;
			int player = LayerMask.NameToLayer("Player");
			if (player >= 0)
			{
				mask &= ~(1 << player);
			}
		}

		/// <summary>0..1: the share of the sphere round the listener closed within reach.</summary>
		public float Enclosure { get; private set; }

		/// <summary>0..1: the share of the sky above the listener closed: 1 under a roof or in a cave.</summary>
		public float Roofed { get; private set; }

		/// <summary>The decay time of the space, seconds (Eyring), before the reverb eases to it.</summary>
		public float DecaySeconds { get; private set; }

		/// <summary>0..1: how much of the reverb is heard (the reverb's level as eased).</summary>
		public float Wet => wet;

		/// <summary>The mean distance to the surfaces round the listener that rays reached, metres.</summary>
		public float MeanDistance { get; private set; }

		/// <summary>The walls that throw echoes back now, as rays (for the debug view).</summary>
		public int EchoWallCount => echoWalls.Count;

		/// <summary>Casts this frame's share of the rays and moves the reverb on.</summary>
		public void Update(Vector3 at, PhysicsScene physics, float deltaTime)
		{
			// A jump (a teleport, a scene) measures everything again at once rather than over six frames.
			bool jumped = (at - listener).sqrMagnitude > 25f * 25f;
			listener = at;
			int casts = jumped ? Rays : RaysPerFrame;
			for (int c = 0; c < casts; c++)
			{
				Cast(physics, next);
				next = (next + 1) % Rays;
			}
			Measure();
			Apply(jumped ? float.MaxValue : deltaTime);
		}

		private void Cast(PhysicsScene physics, int i)
		{
			measured[i] = true;
			if (physics.Raycast(listener, directions[i], out RaycastHit h, ReachMetres, mask, QueryTriggerInteraction.Ignore))
			{
				hit[i] = true;
				distance[i] = h.distance;
				point[i] = h.point;
				normal[i] = h.normal;
				absorption[i] = Absorption(h);
			}
			else
			{
				hit[i] = false;
				distance[i] = ReachMetres;
				absorption[i] = 1f;
			}
		}

		/// <summary>
		/// The share of sound a surface absorbs, mid frequencies (a sound engineer's tables, rounded): rock and ice
		/// throw almost all of it back, boards some, earth and litter less, snow almost none.
		/// </summary>
		public static float AbsorptionOf(SurfaceKind kind)
		{
			switch (kind)
			{
				case SurfaceKind.Stone: return 0.04f;
				case SurfaceKind.Ice: return 0.03f;
				case SurfaceKind.Metal: return 0.05f;
				case SurfaceKind.Water: return 0.02f;
				case SurfaceKind.Wood: return 0.12f;
				case SurfaceKind.Dirt: return 0.25f;
				case SurfaceKind.Mud: return 0.2f;
				case SurfaceKind.Gravel: return 0.3f;
				case SurfaceKind.Sand: return 0.35f;
				case SurfaceKind.Grass: return 0.45f;
				case SurfaceKind.Leaves: return 0.55f;
				case SurfaceKind.Snow: return 0.7f;
				default: return 0.25f;
			}
		}

		private static float Absorption(in RaycastHit h)
		{
			if (h.collider is TerrainCollider)
			{
				// Terrain is the ground or a cliff of earth and rock; the steep is rock.
				return h.normal.y < 0.6f ? 0.06f : 0.25f;
			}
			return AbsorptionOf(GroundSurface.OfCollider(h.collider, h.point, h.normal));
		}

		private void Measure()
		{
			int count = 0, hits = 0, up = 0, upHits = 0;
			float sumDistance = 0f, sumAbsorption = 0f, nearest = ReachMetres;
			for (int i = 0; i < Rays; i++)
			{
				if (!measured[i])
				{
					continue;
				}
				count++;
				sumAbsorption += absorption[i];
				if (directions[i].y > 0.3f)
				{
					up++;
					if (hit[i])
					{
						upHits++;
					}
				}
				if (hit[i])
				{
					hits++;
					sumDistance += distance[i];
					nearest = Mathf.Min(nearest, distance[i]);
				}
			}
			if (count == 0)
			{
				return;
			}
			Enclosure = (float)hits / count;
			Roofed = up > 0 ? (float)upHits / up : 0f;
			MeanDistance = hits > 0 ? sumDistance / hits : ReachMetres;

			// Eyring, over the whole sphere: openings absorb all that reaches them.
			float alpha = Mathf.Clamp(sumAbsorption / count, 0.01f, 0.99f);
			float freePath = MeanDistance * (4f / 3f);
			DecaySeconds = Mathf.Clamp(0.161f * (freePath / 4f) / -Mathf.Log(1f - alpha), 0.1f, 8f);

			// Heard only where there is a roof to hold the sound in, and fully only where it is mostly closed in.
			wetTarget = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.9f, Roofed)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 0.85f, Enclosure));
			decayTarget = DecaySeconds;
			// The walls' own absorption (without the openings) takes the top off: soft rooms are dull, rock is bright.
			float wallAlpha = hits > 0 ? Mathf.Clamp01((sumAbsorption - (count - hits)) / hits) : 0.5f;
			hfTarget = Mathf.Clamp(-200f - 6000f * wallAlpha, -4000f, 0f);
			reflectionsDelayTarget = Mathf.Clamp(2f * nearest / SoundSynth.SpeedOfSound, 0.002f, 0.3f);
			reverbDelayTarget = Mathf.Clamp(freePath / SoundSynth.SpeedOfSound, 0.005f, 0.1f);

			ChooseEchoWalls();
		}

		/// <summary>Picks the walls that will throw echoes: far enough to be heard apart, facing back, hard, and not all one way.</summary>
		private void ChooseEchoWalls()
		{
			ranked.Clear();
			for (int i = 0; i < Rays; i++)
			{
				if (!hit[i] || distance[i] < EchoMinMetres)
				{
					continue;
				}
				float facing = Vector3.Dot(normal[i], -directions[i]);
				if (facing < 0.5f)
				{
					continue;
				}
				float reflect = 1f - absorption[i];
				ranked.Add((reflect * facing / distance[i], i));
			}
			ranked.Sort((a, b) => b.score.CompareTo(a.score));
			echoWalls.Clear();
			foreach ((float _, int ray) in ranked)
			{
				bool apart = true;
				foreach (int chosen in echoWalls)
				{
					if (Vector3.Dot(directions[chosen], directions[ray]) > 0.8f)
					{
						apart = false;
						break;
					}
				}
				if (apart)
				{
					echoWalls.Add(ray);
					if (echoWalls.Count >= MaxEchoes)
					{
						break;
					}
				}
			}
		}

		/// <summary>
		/// The echoes of a sound made at <paramref name="source"/>, by its images in the walls chosen round the listener.
		/// Each tap's gain is against the direct sound as heard: the ratio of the two paths (spreading) times what the wall
		/// returns.
		/// </summary>
		public void EchoesOf(Vector3 source, List<EchoTap> into)
		{
			into.Clear();
			float direct = Mathf.Max(1f, Vector3.Distance(source, listener));
			foreach (int ray in echoWalls)
			{
				Vector3 n = normal[ray];
				Vector3 image = source - 2f * Vector3.Dot(source - point[ray], n) * n;
				float path = Vector3.Distance(image, listener);
				float delay = (path - direct) / SoundSynth.SpeedOfSound;
				if (delay < 0.08f || path > ReachMetres * 2.5f)
				{
					continue;
				}
				into.Add(new EchoTap
				{
					Position = image,
					Delay = delay,
					Gain = (1f - absorption[ray]) * direct / path,
					Path = path,
				});
			}
		}

		/// <summary>Eases the reverb toward the measurement and writes it to the zone.</summary>
		private void Apply(float deltaTime)
		{
			float k = deltaTime >= float.MaxValue ? 1f : 1f - Mathf.Exp(-deltaTime / SettleSeconds);
			wet = Mathf.Lerp(wet, wetTarget, k);
			decay = Mathf.Lerp(decay, decayTarget, k);
			hf = Mathf.Lerp(hf, hfTarget, k);
			reflectionsDelay = Mathf.Lerp(reflectionsDelay, reflectionsDelayTarget, k);
			reverbDelay = Mathf.Lerp(reverbDelay, reverbDelayTarget, k);
#if !UNITY_WEBGL || UNITY_EDITOR
			if (zone == null)
			{
				var go = new GameObject("Acoustic Reverb");
				go.transform.SetParent(parent, false);
				zone = go.AddComponent<AudioReverbZone>();
				zone.reverbPreset = AudioReverbPreset.User;
				// The listener is always inside: the zone goes where it goes.
				zone.minDistance = 100000f;
				zone.maxDistance = 100001f;
				zone.diffusion = 100f;
				zone.density = 100f;
			}
			zone.transform.position = listener;
			bool on = wet > 0.002f;
			if (zone.enabled != on)
			{
				zone.enabled = on;
			}
			if (!on)
			{
				return;
			}
			// Millibels: the reverb's level from how closed in the place is, -3 dB at its fullest.
			int room = Mathf.RoundToInt(Mathf.Clamp(2000f * Mathf.Log10(wet) - 300f, -10000f, 0f));
			zone.room = room;
			zone.roomHF = Mathf.RoundToInt(hf);
			zone.roomLF = 0;
			zone.decayTime = decay;
			// Long tails lose their top to the air as they go.
			zone.decayHFRatio = Mathf.Clamp(0.9f - decay * 0.06f, 0.3f, 0.9f);
			zone.reflections = Mathf.Clamp(room - 200, -10000, 1000);
			zone.reflectionsDelay = reflectionsDelay;
			zone.reverb = Mathf.Clamp(room + 200, -10000, 2000);
			zone.reverbDelay = reverbDelay;
#endif
		}

		public void Dispose()
		{
			if (zone != null)
			{
				UnityEngine.Object.Destroy(zone.gameObject);
				zone = null;
			}
		}

		/// <summary>Points spread evenly over the sphere: the golden-angle spiral.</summary>
		private static Vector3[] Fibonacci(int count)
		{
			var result = new Vector3[count];
			float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
			for (int i = 0; i < count; i++)
			{
				float y = 1f - 2f * (i + 0.5f) / count;
				float r = Mathf.Sqrt(1f - y * y);
				float a = golden * i;
				result[i] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
			}
			return result;
		}
	}
}
