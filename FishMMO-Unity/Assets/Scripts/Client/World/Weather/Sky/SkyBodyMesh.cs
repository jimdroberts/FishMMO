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
		private readonly List<int> indices = new List<int>();

		public Mesh Mesh { get; private set; }

		/// <summary>Textured bodies: drawn one by one with their own texture.</summary>
		public readonly List<SkyBodyState> Textured = new List<SkyBodyState>();

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
		}

		public void AddQuad(Vector3 direction, Kind kind, float angularRadius, float illumination, float shadowed, Vector3 light, float brightness, Vector3 axis, float axisLength, Color color)
		{
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

		/// <summary>Adds everything the state holds, within the sky limits. Returns the quads added.</summary>
		public void AddBodies(CelestialState state, SkyProfile profile, SkyLimits limits)
		{
			int moons = 0, planets = 0, comets = 0;
			float scale = profile != null ? profile.BodyScale : 1f;
			foreach (SkyBodyState body in state.Bodies)
			{
				if (body.Kind == SkyBodyKind.Star || !body.AboveHorizon || body.Body == null)
				{
					continue;
				}
				switch (body.Kind)
				{
					case SkyBodyKind.Moon when moons++ >= limits.Moons:
					case SkyBodyKind.Planet when planets++ >= limits.Planets:
					case SkyBodyKind.Comet when comets++ >= limits.Comets:
						continue;
				}
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
					AddQuad(body.Direction, Kind.Tail, 0.002f + 0.004f * brightness, 1f, 0f, body.LightDirection, brightness, body.TailDirection, body.TailLength, comet.IonTailColor);
					AddQuad(body.Direction, Kind.Tail, 0.004f + 0.006f * brightness, 1f, 0f, body.LightDirection, brightness * 0.6f, body.TailDirection, body.TailLength * 0.7f, tint);
					AddQuad(body.Direction, Kind.Point, 0.0015f, 1f, 0f, body.LightDirection, Mathf.Min(1f, brightness * 3f), Vector3.up, 0f, Color.white);
					continue;
				}
				float radius = body.AngularRadius * scale;
				if (body.Textured && body.Body.SurfaceTexture != null)
				{
					Textured.Add(body);
				}
				else if (radius < 0.0012f)
				{
					// Too small for a disc: a point whose brightness follows its phase.
					AddQuad(body.Direction, Kind.Point, 0.0012f, body.Illumination, body.Shadowed, body.LightDirection, Mathf.Lerp(0.25f, 1f, body.Illumination), Vector3.up, 0f, tint);
				}
				else
				{
					AddQuad(body.Direction, Kind.Disc, radius, body.Illumination, body.Shadowed, body.LightDirection, 1.2f, Vector3.up, 0f, tint);
				}
				if (body.Body.HasRings)
				{
					AddQuad(body.Direction, Kind.Ring, Mathf.Max(radius, 0.002f), body.Illumination, 0f, body.LightDirection, 0.9f, Vector3.up, 0f, tint);
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
				AddQuad(head, Kind.Meteor, 0.0012f, 1f, 0f, Vector3.up, meteor.Brightness * fade, -meteor.Heading, meteor.Length * 0.5f * (0.3f + t), new Color(1f, 0.95f, 0.85f));
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
			Mesh.indexFormat = directions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
			Mesh.SetVertices(directions);
			Mesh.SetUVs(0, corners);
			Mesh.SetUVs(1, shapes);
			Mesh.SetUVs(2, lighting);
			Mesh.SetUVs(3, axes);
			Mesh.SetColors(colors);
			Mesh.SetTriangles(indices, 0, false);
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
