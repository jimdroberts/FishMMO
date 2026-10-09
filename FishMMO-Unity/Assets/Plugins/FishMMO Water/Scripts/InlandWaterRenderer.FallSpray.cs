using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared.Biomes;

namespace FishMMO.Water
{
	/// <summary>A fall's spray and mist: stateless particles the shader moves (FishMMO/Water/Waterfall Spray).</summary>
	public sealed partial class InlandWaterRenderer
	{
		/// <summary>Droplets and puffs in each burst where a stream strikes rock on the way down, and how far they are thrown, metres.</summary>
		private const int StrikeSpray = 14, StrikeMist = 2;
		private const float StrikeRadius = 1.2f;
		/// <summary>The most strikes a fall throws spray from.</summary>
		private const int MaxStrikes = 64;

		/// <summary>The particle kinds FishWaterfallSpray.shader draws (uv2.x).</summary>
		private const float KindSpray = 0f, KindMist = 1f, KindStrand = 2f, KindVeil = 3f, KindDrop = 4f, KindStrike = 5f;

		/// <summary>
		/// A fall's spray and mist: quads, each a particle whose life the shader works out from its seed. Vertex data: position
		/// the particle's home, uv0 its corner (−1…1), uv1 two seeds, uv2 (kind, a size: the impact zone's radius, the drop, or a
		/// strike's speed), uv3 where it starts in its impact zone (a share of the radius, xz), normal the river's way downstream
		/// (out of the rock for a strike), colour r the drop over 30 m and g the run from lip to landing over 20 m.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Where each part lands.</b> Spray and mist crowd each part of the curtain's landing (a split fall boils in two
		/// places), dense at the impact and thinning out by R·u², thrown downstream more than back against the rock.
		/// </para>
		/// <para>
		/// <b>Down the fall.</b> Strands and the drops the ropes shed read the traced paths (_FallPathTex), so they fall where
		/// the water falls, round every rock it meets; the strands used to slant straight from the lip to the landing,
		/// through any rock the curtain parted round. Where a stream strikes rock it throws spray off the face.
		/// </para>
		/// </remarks>
		private Mesh SprayMesh(SceneHydrology.River river, Fall fall, SheetBuild sheet)
		{
			Vector3 downstream = fall.FootPoint - fall.LipPoint;
			downstream.y = 0f;
			downstream = downstream.sqrMagnitude > 1e-6f ? downstream.normalized : Vector3.forward;
			var left = new Vector3(-downstream.z, 0f, downstream.x);
			var positions = new List<Vector3>();
			var normals = new List<Vector3>();
			var uv0 = new List<Vector2>();
			var uv1 = new List<Vector2>();
			var uv2 = new List<Vector2>();
			var colours = new List<Color32>();
			var impacts = new List<Vector2>();
			var indices = new List<int>();
			var rng = new System.Random(fall.River * 7919 + fall.Lip * 104729);
			byte drop = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(fall.Drop / 120f));
			Vector3 toLanding = fall.Landing - fall.LipPoint;
			byte runByte = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(new Vector2(toLanding.x, toLanding.z).magnitude / 20f));

			void Quad(Vector3 home, Vector3 normal, float kind, float size, Vector2 impact, byte g)
			{
				var seed = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
				int start = positions.Count;
				foreach (Vector2 corner in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) })
				{
					positions.Add(home);
					normals.Add(normal);
					uv0.Add(corner);
					uv1.Add(seed);
					uv2.Add(new Vector2(kind, size));
					colours.Add(new Color32(drop, g, 0, 255));
					impacts.Add(impact);
				}
				indices.Add(start); indices.Add(start + 2); indices.Add(start + 1);
				indices.Add(start); indices.Add(start + 3); indices.Add(start + 2);
			}

			// Home on the landing line, out from it by R·u², mostly downstream: an offset back toward the rock is cut to a third.
			void Impact(Vector3 centre, float lineWidth, float radius, float kind)
			{
				Vector3 onLine = centre + left * (((float)rng.NextDouble() - 0.5f) * lineWidth);
				float u = (float)rng.NextDouble();
				float r = radius * u * u;
				float turn = (float)rng.NextDouble() * Mathf.PI * 2f;
				Vector3 offset = (downstream * Mathf.Cos(turn) + left * Mathf.Sin(turn)) * r;
				float back = Vector3.Dot(offset, downstream);
				if (back < 0f)
				{
					offset -= downstream * (back * (2f / 3f));
				}
				Quad(onLine + offset, downstream, kind, radius, new Vector2(offset.x, offset.z) / radius, runByte);
			}

			// ── Where each part lands ────────────────────────────────────────
			float totalDischarge = 0f;
			foreach (Part part in fall.Parts)
			{
				totalDischarge += part.Discharge;
			}
			/* Dense, not big: many small drops and puffs (Jim, 2026-10-08), twice the drops there were and as many puffs as
			 * drops, each puff a third the size it was, so the cloud is thick and its grain fine. */
			int spray = Mathf.Clamp(Mathf.RoundToInt(6f * SprayDensity * fall.Width * Mathf.Sqrt(fall.Drop)), 128, 8000);
			// Puffs for the cloud, more for a taller fall (it fills more air). Soft and overlapping (the shader's shared mist
			// field joins them), so fewer are needed than drops.
			int mist = Mathf.Clamp(Mathf.RoundToInt(spray * (0.6f + 0.01f * fall.Drop)), 96, 5000);
			foreach (Part part in fall.Parts)
			{
				float share = totalDischarge > 0f ? part.Discharge / totalDischarge : 1f / fall.Parts.Count;
				float radius = Mathf.Clamp(0.5f * part.Width + 0.15f * fall.Drop, 2f, Mathf.Max(2f, fall.PoolRadius));
				int partSpray = Mathf.Max(16, Mathf.RoundToInt(spray * share));
				int partMist = Mathf.Max(4, Mathf.RoundToInt(mist * share));
				for (int k = 0; k < partSpray; k++)
				{
					Impact(part.Landing, part.Width * 0.9f, radius, KindSpray);
				}
				for (int k = 0; k < partMist; k++)
				{
					// The cloud starts on the impact and a little round it, hugging the landing.
					Impact(part.Landing, part.Width * 0.9f, radius + 0.05f * fall.Drop, KindMist);
				}
			}

			// ── Off the rock where the water strikes it ─────────────────────
			if (fall.Strikes != null)
			{
				int every = Mathf.Max(1, Mathf.CeilToInt(fall.Strikes.Count / (float)MaxStrikes));
				for (int s = 0; s < fall.Strikes.Count; s += every)
				{
					Strike strike = fall.Strikes[s];
					for (int k = 0; k < StrikeSpray; k++)
					{
						Vector3 jitter = left * (((float)rng.NextDouble() - 0.5f) * StripMetres);
						Quad(strike.Point + jitter, strike.Normal, KindStrike, strike.Speed, Vector2.zero, runByte);
					}
					for (int k = 0; k < StrikeMist; k++)
					{
						Impact(strike.Point, StripMetres, StrikeRadius, KindMist);
					}
				}
			}

			// ── Down the fall ────────────────────────────────────────────────
			/* Strands (2) peel off the curtain and fall with it, and the ropes shed drops (4), both on the traced paths;
			 * a veil of mist (3) hangs over the fall, thickest low down where it shatters. Homes at the lip: the shader
			 * places them from the paths. More where more of the fall is broken water. */
			float broken = Mathf.Max(0f, fall.Drop - fall.BreakupLength);
			bool paths = sheet != null && sheet.Paths != null;
			int strands = Mathf.Clamp(Mathf.RoundToInt(fall.Width * (broken + 2f) * 1.2f), 16, 800);
			int drops = paths ? Mathf.Clamp(Mathf.RoundToInt(fall.Width * broken * 2f), 0, 1200) : 0;
			int veil = Mathf.Clamp(Mathf.RoundToInt(fall.Width * fall.Drop * 1.2f), 32, 640);
			for (int k = 0; k < strands; k++)
			{
				Quad(fall.LipPoint + left * (((float)rng.NextDouble() - 0.5f) * fall.Width * 0.95f), downstream, KindStrand, fall.Drop, Vector2.zero, runByte);
			}
			for (int k = 0; k < drops; k++)
			{
				Quad(fall.LipPoint, downstream, KindDrop, fall.Drop, Vector2.zero, runByte);
			}
			for (int k = 0; k < veil; k++)
			{
				Quad(fall.LipPoint + left * (((float)rng.NextDouble() - 0.5f) * fall.Width * 0.95f), downstream, KindVeil, fall.Drop, Vector2.zero, runByte);
			}

			var mesh = new Mesh { name = $"Spray {fall.River}.{fall.Lip}", hideFlags = HideFlags.DontSave };
			if (positions.Count > 65000)
			{
				mesh.indexFormat = IndexFormat.UInt32;
			}
			mesh.SetVertices(positions);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uv0);
			mesh.SetUVs(1, uv1);
			mesh.SetUVs(2, uv2);
			mesh.SetUVs(3, impacts);
			mesh.SetColors(colours);
			mesh.SetTriangles(indices, 0);
			// The particles move in the shader: bound where they can reach, the whole fall and round its landing.
			float reach = fall.PoolRadius * 2.5f + fall.Drop;
			var bounds = new Bounds(fall.Landing + Vector3.up * (0.5f * fall.Drop), new Vector3(2f * reach, 2f * fall.Drop + reach, 2f * reach));
			bounds.Encapsulate(new Bounds(fall.LipPoint, Vector3.one * (fall.Width + 8f)));
			mesh.bounds = bounds;
			return mesh;
		}
	}
}
