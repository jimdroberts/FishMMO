using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared.Celestial;

namespace FishMMO.Client
{
	/// <summary>
	/// Builds the quads the sky-body shader draws: moons, planets (discs or points), rings, comet
	/// tails, asteroids and meteors, all in one dynamic mesh. Textured bodies are listed apart,
	/// since each needs its own texture.
	/// </summary>
	public sealed class SkyBodyMesh
	{
		public enum Kind
		{
			Disc = 0,
			Ring = 1,
			Tail = 2,
			Meteor = 3,
			Point = 4,
		}

		private readonly List<Vector3> directions = new List<Vector3>();
		private readonly List<Vector2> corners = new List<Vector2>();
		private readonly List<Vector4> shapes = new List<Vector4>();
		private readonly List<Vector4> lighting = new List<Vector4>();
		private readonly List<Vector4> axes = new List<Vector4>();
		private readonly List<Color> colors = new List<Color>();
		private readonly List<Vector4> extras = new List<Vector4>();

		/// <summary>Behind every body: a belt's specks, which anything in the sky covers.</summary>
		public const float BehindAll = -1f;
		/// <summary>In front of every body: a meteor, which burns in this world's own air.</summary>
		public const float InFrontOfAll = 1e6f;
		/// <summary>As many nearer bodies as a pixel is tested against. The nearest are kept.</summary>
		public const int MaxOccluders = 32;

		/// <summary>
		/// The discs that hide what is behind them: direction and drawn angular radius, with the rank
		/// each holds in the far-to-near order beside it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Drawing nearest last settles which of two LIT discs is on top. It does not settle this: by
		/// day a body's unlit side is drawn clear, because the air in front of it is brighter than it
		/// is and what you see there is sky. So a far planet, drawn first, showed straight through the
		/// dark limb of a nearer crescent — the near body is in front of it all the same, lit or not.
		/// </para>
		/// <para>
		/// The sky cannot be read back to paint over the planet, and does not need to be. A body
		/// simply does not draw where a nearer body's disc covers it, and what is left there is the
		/// sky that was always behind both. Each quad carries its rank; the shader hides any pixel
		/// inside the disc of a body ranked nearer.
		/// </para>
		/// </remarks>
		public readonly List<Vector4> Occluders = new List<Vector4>();
		public readonly List<Vector4> OccluderRanks = new List<Vector4>();
		/// <summary>Beside each occluder: its rings' pole in scene space and outer rim (rad), or zero.</summary>
		public readonly List<Vector4> OccluderRings = new List<Vector4>();
		/// <summary>Beside each occluder: inner rim over outer, bands, opacity, 1 when there are rings.</summary>
		public readonly List<Vector4> OccluderRingShapes = new List<Vector4>();
		private readonly List<int> indices = new List<int>();

		public Mesh Mesh { get; private set; }

		/// <summary>Textured bodies: drawn one by one with their own texture.</summary>
		public readonly List<SkyBodyState> Textured = new List<SkyBodyState>();

		/// <summary>
		/// Ringed bodies: their rings are drawn one by one, after every disc. After, because a ring
		/// passes in front of its own planet, and a textured planet is drawn later than this mesh —
		/// in it, the ring's near half went under the disc it should cross.
		/// </summary>
		public readonly List<SkyBodyState> Ringed = new List<SkyBodyState>();

		/// <summary>What one step of drawing the sky's bodies is.</summary>
		public enum StepKind
		{
			/// <summary>A run of plain quads: one sub-mesh of <see cref="Mesh"/>.</summary>
			Quads,
			/// <summary>One body with its own surface texture.</summary>
			Textured,
			/// <summary>One body's rings.</summary>
			Ring,
		}

		/// <summary>One step of the sequence: what to draw, and for a run of quads, which sub-mesh.</summary>
		public struct Step
		{
			public StepKind Kind;
			public SkyBodyState Body;
			public int SubMesh;
			/// <summary>Where the body stands in the far-to-near order: 0 the furthest.</summary>
			public float Rank;
		}

		/// <summary>
		/// Everything to draw, furthest first. This is the only thing that decides what is in front.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The bodies are all pinned to the far plane with no depth written, so which of two covers
		/// the other is purely which was drawn later. That used to be: the shared mesh in whatever
		/// order the bodies were listed in the solar system asset, then every textured body whatever
		/// its distance, then every ring. A far planet listed after a near moon was drawn over it, and
		/// any textured body over any plain one.
		/// </para>
		/// <para>
		/// One sequence instead, by distance. Plain quads still share one mesh, but it is cut into a
		/// sub-mesh at every point where a textured body or a ring has to come between, so the three
		/// kinds interleave and nothing is drawn out of turn.
		/// </para>
		/// </remarks>
		public readonly List<Step> Steps = new List<Step>();

		private struct Run
		{
			public int First, Count;
		}

		private readonly List<Run> runs = new List<Run>();
		private int runStart;
		private readonly List<SkyBodyState> ordered = new List<SkyBodyState>();

		/// <summary>Ends the run of plain quads in progress, if it has any, as a step of its own.</summary>
		private void CloseRun()
		{
			int count = indices.Count - runStart;
			if (count > 0)
			{
				Steps.Add(new Step { Kind = StepKind.Quads, SubMesh = runs.Count });
				runs.Add(new Run { First = runStart, Count = count });
			}
			runStart = indices.Count;
		}

		private void AddStep(StepKind kind, in SkyBodyState body, float rank)
		{
			CloseRun();
			Steps.Add(new Step { Kind = kind, Body = body, Rank = rank });
		}

		public int QuadCount => directions.Count / 4;

		public void Clear()
		{
			directions.Clear();
			corners.Clear();
			shapes.Clear();
			lighting.Clear();
			axes.Clear();
			colors.Clear();
			indices.Clear();
			Textured.Clear();
			Ringed.Clear();
			Steps.Clear();
			Occluders.Clear();
			OccluderRanks.Clear();
			OccluderRings.Clear();
			OccluderRingShapes.Clear();
			extras.Clear();
			runs.Clear();
			runStart = 0;
		}

		public void AddQuad(Vector3 direction, Kind kind, float angularRadius, float illumination, float shadowed, Vector3 light, float brightness, Vector3 axis, float axisLength, Color color, float rank = BehindAll, float distanceKm = 0f)
		{
			// y: how far off the body is, in thousands of km, for the ring of the world underfoot to
			// know whether this body is beyond it or inside it.
			var extra = new Vector4(rank, distanceKm * 0.001f, 0f, 0f);
			int start = directions.Count;
			var shape = new Vector4(angularRadius, (float)kind, illumination, shadowed);
			var lit = new Vector4(light.x, light.y, light.z, brightness);
			var ax = new Vector4(axis.x, axis.y, axis.z, axisLength);
			for (int i = 0; i < 4; i++)
			{
				directions.Add(direction);
				shapes.Add(shape);
				lighting.Add(lit);
				axes.Add(ax);
				colors.Add(color);
				extras.Add(extra);
			}
			corners.Add(new Vector2(-1f, -1f));
			corners.Add(new Vector2(1f, -1f));
			corners.Add(new Vector2(1f, 1f));
			corners.Add(new Vector2(-1f, 1f));
			indices.Add(start);
			indices.Add(start + 2);
			indices.Add(start + 1);
			indices.Add(start);
			indices.Add(start + 3);
			indices.Add(start + 2);
		}

		/// <summary>
		/// A body's rings: one quad as wide as the outer rim, turned so its up is the body's pole as it
		/// appears on the sky. The shader draws the ellipse the ring plane makes and hides the far half
		/// where it passes behind the disc.
		/// </summary>
		/// <param name="pole">The body's north pole in scene space: the ring plane's normal.</param>
		public void AddRing(Vector3 direction, float bodyRadius, Vector3 pole, RingSettings rings, Vector3 light, float rank = BehindAll, float distanceKm = 0f)
		{
			// Packed into the quad's spare fields: shape.z the band count, shape.w the inner rim,
			// axis the pole with the outer rim beside it, lighting.w how solid.
			AddQuad(direction, Kind.Ring, Mathf.Max(bodyRadius, 0.002f), rings.Bands, rings.Inner, light, rings.Opacity, pole, rings.Outer, rings.Tint, rank, distanceKm);
		}

		/// <summary>Adds everything the state holds, within the sky limits. Returns the quads added.</summary>
		public void AddBodies(CelestialState state, SkyProfile profile, SkyLimits limits)
		{
			int moons = 0, planets = 0, comets = 0;
			float scale = profile != null ? profile.BodyScale : 1f;
			// Nearest first to choose, furthest first to draw. The limits used to be counted in the
			// asset's own order, so a sky over its limit of moons kept whichever were listed first;
			// counted from the nearest it keeps the ones that matter, and drops the far specks.
			ordered.Clear();
			foreach (SkyBodyState candidate in state.Bodies)
			{
				if (candidate.Kind != SkyBodyKind.Star && candidate.AboveHorizon && candidate.Body != null)
				{
					ordered.Add(candidate);
				}
			}
			ordered.Sort((a, b) => a.DistanceKm.CompareTo(b.DistanceKm));
			for (int nearest = 0; nearest < ordered.Count; nearest++)
			{
				bool over = false;
				switch (ordered[nearest].Kind)
				{
					case SkyBodyKind.Moon: over = moons++ >= limits.Moons; break;
					case SkyBodyKind.Planet: over = planets++ >= limits.Planets; break;
					case SkyBodyKind.Comet: over = comets++ >= limits.Comets; break;
				}
				if (over)
				{
					// Marked, not removed: a struct in a list, and the draw loop below skips it.
					SkyBodyState dropped = ordered[nearest];
					dropped.Body = null;
					ordered[nearest] = dropped;
				}
			}
			for (int at = ordered.Count - 1; at >= 0; at--)
			{
				SkyBodyState body = ordered[at];
				if (body.Body == null)
				{
					continue;
				}
				// 0 the furthest. A quad is hidden by the disc of anything ranked above it.
				float rank = ordered.Count - 1 - at;
				Color tint = body.Body.Tint;
				// Alpha below 1 asks the shader for an atmosphere rim.
				tint.a = body.Body is WorldBody world && world.HasWeather ? 0.4f : 1f;
				if (body.Kind == SkyBodyKind.Comet)
				{
					float brightness = Mathf.Clamp01(body.Brightness);
					if (brightness < 0.01f)
					{
						continue;
					}
					var comet = (CometBody)body.Body;
					AddQuad(body.Direction, Kind.Tail, 0.002f + 0.004f * brightness, 1f, 0f, body.LightDirection, brightness, body.TailDirection, body.TailLength, comet.IonTailColor, rank, (float)body.DistanceKm);
					AddQuad(body.Direction, Kind.Tail, 0.004f + 0.006f * brightness, 1f, 0f, body.LightDirection, brightness * 0.6f, body.TailDirection, body.TailLength * 0.7f, tint, rank, (float)body.DistanceKm);
					AddQuad(body.Direction, Kind.Point, 0.0015f, 1f, 0f, body.LightDirection, Mathf.Min(1f, brightness * 3f), Vector3.up, 0f, Color.white, rank, (float)body.DistanceKm);
					continue;
				}
				float radius = body.AngularRadius * scale;
				// Anything drawn as a disc hides what is behind it; a point is too small to.
				if (radius >= 0.0012f)
				{
					Occluders.Add(new Vector4(body.Direction.x, body.Direction.y, body.Direction.z, radius));
					OccluderRanks.Add(new Vector4(rank, 0f, 0f, 0f));
					// Its rings hide what is behind them too, by how solid they are: a moon passing
					// behind a ringed planet dims through the rings before it goes behind the disc.
					bool ringed = body.Body.HasRings && body.Body.Rings != null;
					if (ringed)
					{
						Vector3 pole = state.HeliocentricDirection(CelestialMath.PoleOf(body.Body));
						RingSettings rings = body.Body.Rings;
						OccluderRings.Add(new Vector4(pole.x, pole.y, pole.z, radius * rings.Outer));
						OccluderRingShapes.Add(new Vector4(rings.Inner / rings.Outer, rings.Bands, rings.Opacity, 1f));
					}
					else
					{
						OccluderRings.Add(Vector4.zero);
						OccluderRingShapes.Add(Vector4.zero);
					}
				}
				if (body.Textured && body.Body.SurfaceTexture != null)
				{
					Textured.Add(body);
					AddStep(StepKind.Textured, body, rank);
				}
				else if (radius < 0.0012f)
				{
					// Too small for a disc: a point whose brightness follows its phase.
					AddQuad(body.Direction, Kind.Point, 0.0012f, body.Illumination, body.Shadowed, body.LightDirection, Mathf.Lerp(0.25f, 1f, body.Illumination), Vector3.up, 0f, tint, rank, (float)body.DistanceKm);
				}
				else
				{
					AddQuad(body.Direction, Kind.Disc, radius, body.Illumination, body.Shadowed, body.LightDirection, 1.2f, Vector3.up, 0f, tint, rank, (float)body.DistanceKm);
				}
				// Straight after its own body, which it crosses in front of; anything nearer than the
				// pair comes later in the sequence and covers both.
				if (body.Body.HasRings && body.Body.Rings != null && radius >= 0.0012f)
				{
					Ringed.Add(body);
					AddStep(StepKind.Ring, body, rank);
				}
			}
		}

		/// <summary>Adds a sample of the belts' asteroids as faint points.</summary>
		public void AddAsteroids(CelestialState state, SkyLimits limits)
		{
			if (state.System == null || state.Observer == null)
			{
				return;
			}
			int budget = limits.VisibleAsteroids;
			Vector3d observer = CelestialMath.Position(state.System, state.Observer, state.Hours);
			foreach (AsteroidBelt belt in state.System.AsteroidBelts)
			{
				if (belt == null)
				{
					continue;
				}
				int count = Mathf.Min(belt.Count, budget);
				budget -= count;
				for (int i = 0; i < count; i++)
				{
					Vector3d p = CelestialSky.AsteroidPosition(state.System, belt, i, state.Hours);
					Vector3 direction = state.HeliocentricDirection(p - observer);
					if (direction.y < -0.02f)
					{
						continue;
					}
					float brightness = 0.12f + 0.2f * SkySchedule.Unit(SkySchedule.Hash((uint)belt.Seed, (uint)i));
					AddQuad(direction, Kind.Point, 0.0008f, 1f, 0f, Vector3.up, brightness, Vector3.up, 0f, belt.Color);
				}
				if (budget <= 0)
				{
					break;
				}
			}
		}

		/// <summary>Adds the meteors alight at a time.</summary>
		public void AddMeteors(List<Meteor> meteors, double now)
		{
			foreach (Meteor meteor in meteors)
			{
				double age = now - meteor.Time;
				if (age < 0.0 || age > meteor.Duration)
				{
					continue;
				}
				float t = (float)(age / meteor.Duration);
				Vector3 head = Vector3.Slerp(meteor.Direction, (meteor.Direction + meteor.Heading * meteor.Length).normalized, t);
				float fade = Mathf.Sin(t * Mathf.PI);
				AddQuad(head, Kind.Meteor, 0.0012f, 1f, 0f, Vector3.up, meteor.Brightness * fade, -meteor.Heading, meteor.Length * 0.5f * (0.3f + t), new Color(1f, 0.95f, 0.85f), InFrontOfAll);
			}
		}

		/// <summary>Copies what was added into the mesh.</summary>
		public Mesh Upload()
		{
			if (Mesh == null)
			{
				Mesh = new Mesh { name = "Sky Bodies", hideFlags = HideFlags.DontSave };
				Mesh.MarkDynamic();
			}
			Mesh.Clear();
			if (directions.Count == 0)
			{
				return Mesh;
			}
			Mesh.SetVertices(directions);
			Mesh.SetUVs(0, corners);
			Mesh.SetUVs(1, shapes);
			Mesh.SetUVs(2, lighting);
			Mesh.SetUVs(3, axes);
			Mesh.SetColors(colors);
			Mesh.SetUVs(4, extras);
			// One index buffer, cut into a sub-mesh for every run of plain quads in the sequence.
			CloseRun();
			Mesh.SetIndexBufferParams(indices.Count, IndexFormat.UInt32);
			Mesh.SetIndexBufferData(indices, 0, 0, indices.Count, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
			Mesh.subMeshCount = Mathf.Max(1, runs.Count);
			for (int i = 0; i < runs.Count; i++)
			{
				Mesh.SetSubMesh(i, new SubMeshDescriptor(runs[i].First, runs[i].Count), MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
			}
			Mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
			return Mesh;
		}

		public void Dispose()
		{
			if (Mesh != null)
			{
				if (Application.isPlaying) Object.Destroy(Mesh); else Object.DestroyImmediate(Mesh);
				Mesh = null;
			}
		}
	}
}
