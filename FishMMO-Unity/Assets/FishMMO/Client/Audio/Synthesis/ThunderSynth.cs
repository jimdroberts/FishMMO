using System;
using System.Collections.Generic;
using UnityEngine;
using static FishMMO.Client.SoundSynth;

namespace FishMMO.Client
{
	/// <summary>
	/// The thunder of one lightning strike as heard from one place, synthesised from the strike's own channel.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Thunder is the channel, heard piece by piece.</b> The return stroke heats every metre of its tortuous channel at
	/// once, and each piece bursts outward as a shock that relaxes, within metres, into an N-wave: a sharp rise, a
	/// linear fall through zero, a sharp return (Few 1969). Each piece's wave reaches the listener after its own distance
	/// over the speed of sound, so a channel kilometres long is heard over many seconds: first the nearest piece's crack,
	/// then the roll of the rest, loudest where long straight runs of channel lie broadside to the listener (each piece
	/// radiates most at right angles to itself) and their waves arrive together. That is why thunder rolls, and why each
	/// clap is different.
	/// </para>
	/// <para>
	/// <b>The channel.</b> The visible bolt (<c>SkySchedule.BoltPath</c>: its trunk and branches) made tortuous at a
	/// finer scale, plus the channel hidden inside the cloud — a strike's in-cloud leader runs kilometres sideways at the
	/// cloud's base, which is where the long rumbles come from. All of it is drawn from the strike's seed, so every
	/// client makes the same channel.
	/// </para>
	/// <para>
	/// <b>The air.</b> Each wave falls with distance (spherical spreading, 1/r), stretches a little as it goes, and
	/// loses its highs to absorption — its sharp edges rounded, tens of microseconds near and milliseconds far — so the
	/// dulling is continuous with distance. The ground under the listener returns each wave a moment later (its image),
	/// and the ground, the hills and the cloud scatter it back into a long diffuse tail.
	/// </para>
	/// <para>
	/// <b>Checked against recordings.</b> Six field recordings of thunder: energy peaks in the 63–500 Hz
	/// octaves (centroids 110–520 Hz), within 20 dB of the peak for 3–18 s, rising over 0.3–3 s for a far roll and at
	/// once for a near crack, decaying smoothly. The first version of this put its energy at 16–63 Hz (N-waves several
	/// times too long), buzzed (pieces evenly spaced arrive evenly), jumped where distance bands met, and stopped dead
	/// when the last piece arrived.
	/// </para>
	/// </remarks>
	public static class ThunderSynth
	{
		/// <summary>The thunder's sample rate: its energy is almost all below a few kilohertz.</summary>
		public const int ThunderRate = 22050;


		/// <summary>The channel as polylines, in world metres: the bolt's trunk, its branches, and the in-cloud runs.</summary>
		public sealed class Channel
		{
			public readonly List<Vector3[]> Lines = new List<Vector3[]>();
			public uint Seed;
			public float Intensity = 1f;
			/// <summary>The ground height under the listener, metres, for the ground's image.</summary>
			public float ListenerGround;
		}

		/// <summary>What was made: the samples, and the distance of the first piece heard (sample 0 arrives then).</summary>
		public struct Result
		{
			public float[] Samples;
			public float NearestMetres;
			public float FurthestMetres;
			/// <summary>The piece of channel nearest the listener: where the thunder is first heard from.</summary>
			public Vector3 NearestPoint;
		}

		/// <summary>
		/// Adds the strike's in-cloud channel to <paramref name="channel"/>: two to four runs from the top of the bolt
		/// along the cloud's base, four to twelve kilometres in all (a cloud-to-ground flash's in-cloud leaders commonly
		/// span five to fifteen), wandering as a leader does. Recorded rolls go on pealing ten seconds and more after the
		/// first crack; it is these runs, kilometres further off, that do it.
		/// </summary>
		public static void AddCloudChannel(Channel channel, Vector3 top, float cloudHeight, uint seed)
		{
			var noise = new Noise(Hash(seed, 0xC10Du));
			int runs = 2 + (int)(noise.Next() * 2.99f);
			float total = noise.Range(4000f, 12000f);
			for (int r = 0; r < runs; r++)
			{
				float length = total / runs;
				float heading = noise.Next() * 2f * MathF.PI;
				int steps = Math.Max(4, (int)(length / 60f));
				var line = new Vector3[steps + 1];
				Vector3 at = top;
				line[0] = at;
				for (int s = 1; s <= steps; s++)
				{
					heading += noise.Gauss() * 0.18f;
					float climb = noise.Gauss() * 0.15f * 60f;
					at += new Vector3(MathF.Cos(heading) * 60f, climb, MathF.Sin(heading) * 60f);
					// Held to the cloud's base, give or take a few hundred metres.
					at.y = Math.Clamp(at.y, top.y - 200f, top.y + 400f);
					line[s] = at;
				}
				channel.Lines.Add(line);
			}
		}

		/// <summary>The thunder of a channel heard at <paramref name="listener"/>. Pure arithmetic: safe off the main thread.</summary>
		public static Result Render(Channel channel, Vector3 listener)
		{
			var renderer = new Renderer(channel, listener);
			renderer.Step(long.MaxValue);
			return renderer.Result;
		}

		/// <summary>
		/// One clap's render, done a piece at a time: <see cref="Step"/> works until a deadline and carries on from there
		/// next time. A web build has no worker thread, and a clap is hundreds of milliseconds of arithmetic: rendered in
		/// one go it froze the game at every strike. Pieces give the same samples as one go, to the bit.
		/// </summary>
		public sealed class Renderer
		{
			/// <summary>Samples, or segments of channel, done between looks at the clock.</summary>
			private const int SamplesPerCheck = 4096;
			private const int SegmentsPerCheck = 16;

			private enum Stage { Place, HighPass, Scatter, Measure, Mix, RemoveDc, Loudest, Scale, Limit, Done }

			private readonly Channel channel;
			private readonly Vector3 listener;
			private readonly Vector3 listenerImage;
			private readonly List<(Vector3 a, Vector3 b)> segments = new List<(Vector3 a, Vector3 b)>();
			private Result result;
			private Noise noise;
			private Stage stage;
			private int next;

			private float typical, decay;
			private int length;
			private float[] dry, wet, mix;
			private readonly float[] kernel = new float[1024];
			private float field;
			private Biquad high, higher;
			private FeedbackDelay scatter;
			private double drySum, wetSum;
			private float gain;
			private float dcIn, dcOut;
			private LoudestStretch loudest;
			private float loudestGain;
			private float envelope;

			/// <summary>Lays out the channel (quick); the samples are made by <see cref="Step"/>.</summary>
			public Renderer(Channel channel, Vector3 listener)
			{
				this.channel = channel;
				this.listener = listener;
				listenerImage = new Vector3(listener.x, 2f * channel.ListenerGround - listener.y, listener.z);
				noise = new Noise(Hash(channel.Seed, 0x7417u));
				foreach (Vector3[] line in channel.Lines)
				{
					for (int i = 0; i + 1 < line.Length; i++)
					{
						Tortuous(line[i], line[i + 1], segments, ref noise);
					}
				}
				result = new Result { NearestMetres = float.MaxValue, FurthestMetres = 0f };
				if (segments.Count == 0)
				{
					result.Samples = Array.Empty<float>();
					stage = Stage.Done;
					return;
				}
				foreach ((Vector3 a, Vector3 b) in segments)
				{
					Reach(a);
					Reach(b);
				}
				typical = 0.5f * (result.NearestMetres + result.FurthestMetres);
				decay = Math.Clamp(ScatterSeconds + 0.25f * typical / 1000f, ScatterSeconds, 7f);
				float spanSeconds = (result.FurthestMetres - result.NearestMetres) / SpeedOfSound;
				length = (int)((spanSeconds + 0.1f + decay * 1.1f) * ThunderRate);
				stage = Stage.Place;
			}

			/// <summary>True once the samples are made.</summary>
			public bool Done => stage == Stage.Done;

			/// <summary>
			/// Metres to the piece of channel nearest the listener, known from the start: the thunder's first sound arrives
			/// that far after the strike, which is how long its render may take.
			/// </summary>
			public float NearestMetres => result.NearestMetres;

			/// <summary>What was made; its samples are null until <see cref="Done"/>.</summary>
			public Result Result => result;

			private void Reach(Vector3 end)
			{
				float r = Vector3.Distance(end, listener);
				if (r < result.NearestMetres)
				{
					result.NearestMetres = r;
					result.NearestPoint = end;
				}
				result.FurthestMetres = Math.Max(result.FurthestMetres, r);
			}

			/// <summary>
			/// Works until <paramref name="deadline"/> (a <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> reading;
			/// <c>long.MaxValue</c> finishes it). Returns <see cref="Done"/>.
			/// </summary>
			public bool Step(long deadline)
			{
				// At least one piece a call, whatever the deadline: a call that did nothing could be made forever.
				while (stage != Stage.Done)
				{
					switch (stage)
					{
						case Stage.Place:
							if (dry == null)
							{
								dry = new float[length];
							}
							int lastSegment = Math.Min(segments.Count, next + SegmentsPerCheck);
							for (; next < lastSegment; next++)
							{
								PlaceSegment(segments[next]);
							}
							if (next == segments.Count)
							{
								// Below 40 Hz, out of earshot on most speakers and only wasted headroom: a fourth-order cut.
								high = Biquad.HighPass(40f, 0.7f, ThunderRate);
								higher = Biquad.HighPass(40f, 0.7f, ThunderRate);
								Advance(Stage.HighPass);
							}
							break;

						case Stage.HighPass:
							for (int end = Chunk(); next < end; next++)
							{
								dry[next] = higher.Process(high.Process(dry[next]));
							}
							if (next == length)
							{
								// The ground, the hills and the cloud scatter every wave back: a long diffuse tail, more of it the further off.
								wet = new float[length];
								scatter = new FeedbackDelay(decay, ThunderRate);
								Advance(Stage.Scatter);
							}
							break;

						case Stage.Scatter:
							int scatterEnd = Chunk();
							scatter.Run(dry, wet, next, scatterEnd);
							next = scatterEnd;
							if (next == length)
							{
								Advance(Stage.Measure);
							}
							break;

						case Stage.Measure:
							for (int end = Chunk(); next < end; next++)
							{
								drySum += dry[next] * dry[next];
								wetSum += wet[next] * wet[next];
							}
							if (next == length)
							{
								float wetShare = Math.Clamp(0.6f + 0.05f * typical / 1000f, 0.6f, 1f);
								gain = wetShare * Rms(drySum, length) / Math.Max(1e-12f, Rms(wetSum, length));
								mix = new float[length];
								Advance(Stage.Mix);
							}
							break;

						case Stage.Mix:
							for (int end = Chunk(); next < end; next++)
							{
								mix[next] = dry[next] + gain * wet[next];
							}
							if (next == length)
							{
								dry = null;
								wet = null;
								Advance(Stage.RemoveDc);
							}
							break;

						case Stage.RemoveDc:
							int dcEnd = Chunk();
							SoundSynth.RemoveDc(mix, next, dcEnd, ref dcIn, ref dcOut);
							next = dcEnd;
							if (next == length)
							{
								FadeEnds(mix, 8, (int)(0.5f * ThunderRate));
								// By the roll, not the crack: the loudest quarter second at a strong level, the crack then held to full scale.
								loudest = new LoudestStretch(ThunderRate, 0.25f);
								Advance(Stage.Loudest);
							}
							break;

						case Stage.Loudest:
							int loudestEnd = Chunk();
							loudest.Measure(mix, next, loudestEnd);
							next = loudestEnd;
							if (next == length)
							{
								loudestGain = loudest.Gain(length, 0.5f);
								Advance(Stage.Scale);
							}
							break;

						case Stage.Scale:
							int scaleEnd = Chunk();
							SoundSynth.Scale(mix, next, scaleEnd, loudestGain);
							next = scaleEnd;
							if (next == length)
							{
								envelope = 0.95f;
								Advance(Stage.Limit);
							}
							break;

						case Stage.Limit:
							int limitEnd = Chunk();
							SoundSynth.Limit(mix, next, limitEnd, ThunderRate, 0.95f, 0.08f, ref envelope);
							next = limitEnd;
							if (next == length)
							{
								result.Samples = mix;
								mix = null;
								Advance(Stage.Done);
							}
							break;
					}
					if (stage != Stage.Done && System.Diagnostics.Stopwatch.GetTimestamp() >= deadline)
					{
						return false;
					}
				}
				return true;
			}

			private int Chunk() => Math.Min(length, next + SamplesPerCheck);

			private void Advance(Stage to)
			{
				stage = to;
				next = 0;
			}

			/// <summary>One segment of channel's waves, direct and off the ground, placed where they arrive.</summary>
			private void PlaceSegment((Vector3 a, Vector3 b) segment)
			{
				(Vector3 a, Vector3 b) = segment;
				// How strongly each stretch of channel was heated: the current, the channel's thickness, its branching vary
				// along it, so some runs are far louder than others. A correlated random field along the channel (an AR(1)
				// over the segments, about ten of them — 80 m — to a correlation), log-normal, mean one.
				field = 0.9f * field + 0.436f * noise.Gauss();
				float heated = MathF.Exp(StrengthSpread * field - 0.5f * StrengthSpread * StrengthSpread);
				Vector3 run = b - a;
				float span = run.magnitude;
				if (span < 1e-3f)
				{
					return;
				}
				Vector3 along = run / span;
				Vector3 middle = (a + b) * 0.5f;
				Vector3 toEar = listener - middle;
				float r = Math.Max(1f, toEar.magnitude);
				// Broadside loudest: |sin θ| between the segment and the line to the ear, with a floor for the rest of its lobe.
				float sin = Vector3.Cross(along, toEar / r).magnitude;
				// One wave shape for the whole segment: its thickness is one, and its path to the ear nearly so.
				// Each segment its own rise too: one rise for every wave at a distance puts the nulls of its edge's spectrum
				// at the same frequencies in all of them, and a far clap is combed by them.
				int width = Kernel(kernel, Period(r) * (0.7f + 0.7f * noise.Next()), RiseSeconds(r) * (0.5f + noise.Next()));
				int sources = Math.Max(1, (int)MathF.Round(span / SourceMetres));
				// Broadside loudest, sharply: the lobe is sin², so only runs nearly square to the ear peal.
				float each = channel.Intensity * heated * (span / sources) * (0.15f + 0.85f * sin * sin);
				for (int k = 0; k < sources; k++)
				{
					// Scattered at random along the segment, not evenly: even spacing arrives evenly, and buzzes.
					Vector3 at = a + run * noise.Next();
					float direct = Vector3.Distance(at, listener);
					Place(dry, kernel, width, (direct - result.NearestMetres) / SpeedOfSound, each / Math.Max(direct, 30f));
					// The ground's return: the wave off the ground under the listener, from the listener's image below it.
					// Rough ground: each return a fraction of a millisecond early or late, so it reinforces the lows and is
					// incoherent above a few hundred hertz (a smooth mirror combs every far clap with the same notches).
					// Spread by a bell curve, not evenly: an even spread of delays averages to a sinc, whose ripple is a comb too.
					float image = Vector3.Distance(at, listenerImage) + noise.Gauss() * GroundRoughnessMetres;
					Place(dry, kernel, width, (image - result.NearestMetres) / SpeedOfSound, GroundReturn * each / Math.Max(image, 30f));
				}
			}
		}

		/// <summary>Metres between the kinks of the channel at its finest: the length of its straight runs.</summary>
		public const float KinkMetres = 8f;

		/// <summary>Metres of channel each point source stands for, scattered at random along its segment.</summary>
		public const float SourceMetres = 1f;

		/// <summary>
		/// The spread of the channel's strength along it (the log-normal's sigma): what makes thunder come in peals
		/// rather than a steady rumble. Fitted so the envelope's flutter matches the recordings' (about 0.4).
		/// </summary>
		public const float StrengthSpread = 0.9f;

		/// <summary>The share of each wave the ground under the listener returns.</summary>
		public const float GroundReturn = 0.7f;

		/// <summary>The decay time, seconds, of the scattered tail from a near strike; further strikes ring longer.</summary>
		public const float ScatterSeconds = 4f;

		/// <summary>Metres the ground's return wanders per source (one standard deviation): rough ground, coherent only at low frequencies.</summary>
		public const float GroundRoughnessMetres = 0.4f;

		/// <summary>
		/// A straight run made tortuous, down to <see cref="KinkMetres"/>: each half's midpoint pushed aside by about an
		/// eighth of its length, again and again (midpoint displacement), so the channel is crooked at every scale with
		/// straight runs of a few metres between kinks, as photographs of channels show.
		/// </summary>
		private static void Tortuous(Vector3 a, Vector3 b, List<(Vector3, Vector3)> into, ref Noise noise)
		{
			Vector3 run = b - a;
			float span = run.magnitude;
			if (span <= KinkMetres)
			{
				into.Add((a, b));
				return;
			}
			Vector3 dir = run / span;
			Vector3 side = Vector3.Cross(dir, Math.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
			Vector3 other = Vector3.Cross(dir, side);
			Vector3 mid = (a + b) * 0.5f + (side * noise.Gauss() + other * noise.Gauss()) * (0.13f * span);
			Tortuous(a, mid, into, ref noise);
			Tortuous(mid, b, into, ref noise);
		}

		/// <summary>
		/// An N-wave's period at a distance: about 2 ms close by, lengthening slowly with the log of the way it has come as
		/// the wave stretches. Summed along the channel (the coherent runs add their lows) that puts the thunder's energy
		/// in the 63–250 Hz octaves, where the recordings have it.
		/// </summary>
		public static float Period(float metres)
		{
			return 0.002f * (1f + 0.4f * MathF.Log(1f + Math.Max(0f, metres) / 100f));
		}

		/// <summary>
		/// How long an N-wave's sharp edges take to rise once the air has dulled them, seconds: tens of microseconds close
		/// by (a crack bright to 8 kHz), milliseconds kilometres off (the air's absorption takes the highs first).
		/// </summary>
		public static float RiseSeconds(float metres)
		{
			return 20e-6f + 0.15e-6f * Math.Max(0f, metres);
		}

		/// <summary>
		/// One N-wave sampled into <paramref name="kernel"/>: up to +1 over the rise, down in a straight line to −1 over
		/// the period, back to nothing over the rise. The edges are raised-cosine, not straight: a straight ramp's spectrum
		/// has deep, evenly spaced nulls. Returns its length in samples.
		/// </summary>
		private static int Kernel(float[] kernel, float period, float rise)
		{
			float dt = 1f / ThunderRate;
			float total = period + 2f * rise;
			int count = Math.Min(kernel.Length, Math.Max(2, (int)MathF.Ceiling(total / dt) + 1));
			for (int i = 0; i < count; i++)
			{
				float t = i * dt;
				float v;
				if (t < rise)
				{
					v = rise > 0f ? 0.5f - 0.5f * MathF.Cos(MathF.PI * t / rise) : 1f;
				}
				else if (t < rise + period)
				{
					v = 1f - 2f * (t - rise) / period;
				}
				else if (t < total)
				{
					v = -0.5f - 0.5f * MathF.Cos(MathF.PI * (t - rise - period) / Math.Max(rise, 1e-6f));
				}
				else
				{
					v = 0f;
				}
				kernel[i] = v;
			}
			return count;
		}

		/// <summary>Adds the kernel at a delay to the fraction of a sample (split linearly between the two samples it falls between).</summary>
		private static void Place(float[] into, float[] kernel, int width, float delay, float amplitude)
		{
			float start = delay * ThunderRate;
			int first = (int)MathF.Floor(start);
			float frac = start - first;
			float a0 = amplitude * (1f - frac), a1 = amplitude * frac;
			for (int i = 0; i < width; i++)
			{
				int at = first + i;
				if (at < 0 || at + 1 >= into.Length)
				{
					continue;
				}
				into[at] += kernel[i] * a0;
				into[at + 1] += kernel[i] * a1;
			}
		}

		/// <summary>
		/// The scattered field: an eight-line feedback delay network (Jot), each line 50–170 ms, mixed by a Householder
		/// matrix so the echoes multiply into a smooth diffuse tail, decaying by 60 dB over the decay time, each line
		/// low-passed so the highs die first as they do off grass, trees and cloud. Run over a sound in order, a piece at a time.
		/// </summary>
		private sealed class FeedbackDelay
		{
			private const int Lines = 8;
			private static readonly int[] Delays = { 1103, 1427, 1789, 2153, 2539, 2903, 3331, 3779 };
			private readonly float[][] buffers = new float[Lines][];
			private readonly int[] heads = new int[Lines];
			private readonly float[] gains = new float[Lines];
			private readonly OnePole[] damp = new OnePole[Lines];
			private readonly float[] outs = new float[Lines];

			public FeedbackDelay(float decay, int rate)
			{
				for (int l = 0; l < Lines; l++)
				{
					buffers[l] = new float[Delays[l]];
					// 60 dB over the decay time, whatever the line's own length.
					gains[l] = MathF.Pow(10f, -3f * Delays[l] / (decay * rate));
					damp[l] = OnePole.LowPass(4000f, rate);
				}
			}

			/// <summary>The tail of <paramref name="dry"/>'s samples [<paramref name="from"/>, <paramref name="to"/>) into <paramref name="wet"/>.</summary>
			public void Run(float[] dry, float[] wet, int from, int to)
			{
				for (int i = from; i < to; i++)
				{
					float sum = 0f;
					for (int l = 0; l < Lines; l++)
					{
						outs[l] = buffers[l][heads[l]];
						sum += outs[l];
					}
					wet[i] = sum;
					float house = 2f / Lines * sum;
					for (int l = 0; l < Lines; l++)
					{
						float back = damp[l].Process(outs[l] - house) * gains[l];
						// The dry sound fed in with alternating signs, so the lines start decorrelated.
						buffers[l][heads[l]] = back + ((l & 1) == 0 ? dry[i] : -dry[i]);
						// A compare, not a modulo: an integer division per line per sample was a large share of the cost.
						int head = heads[l] + 1;
						heads[l] = head == Delays[l] ? 0 : head;
					}
				}
			}
		}

		private static float Rms(double sumOfSquares, int count)
		{
			return (float)Math.Sqrt(sumOfSquares / Math.Max(1, count));
		}
	}
}
