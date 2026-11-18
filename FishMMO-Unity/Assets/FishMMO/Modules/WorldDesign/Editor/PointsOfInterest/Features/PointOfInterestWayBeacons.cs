#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A site's beacons (2026-10-26, the biome suite's way lights): where the site biome's Way Lights name a beacon for the site's
	/// kind (<see cref="BiomeWayLightClass.Beacon"/>: a fire basket, a signal pyre, a wart fire, a blubber pyre, a five-pit
	/// beacon), it is laid on the lookout of the site's towers and keeps (their "lookout" mark), up to <see cref="MaxBeacons"/>
	/// a site; a site with no lookout stands one of the ground pyres (not the iron basket, which wants a tower) on open ground
	/// near its middle. Its fire is dark by day and kindled at dusk on the shared clock (ScenePyre).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Defaults unchanged.</b> No default and no authored template names a beacon (every list is the old one by kind, or the
	/// drafts' lights for the town kinds), so this builder lays nothing anywhere until a biome's Way Lights name one; it reads the
	/// authored list itself (no fallback warning) and draws on no shared stream (its spots are hashes of the site's id).
	/// </para>
	/// <para>A street light named for a town's kind is the props' (PropsFeature); a beacon named for one is laid here too.</para>
	/// </remarks>
	[InitializeOnLoad]
	public sealed class PointOfInterestWayBeacons : IPointOfInterestSiteBuilder
	{
		static PointOfInterestWayBeacons() => PointOfInterestSiteBuilders.Register(new PointOfInterestWayBeacons());

		/// <summary>The most beacons one site lays on its lookouts.</summary>
		public const int MaxBeacons = 2;

		/// <summary>The mark a tower's or a keep's top carries where its watch stands (StructurePieces.Towers/Keep).</summary>
		public const string LookoutMark = "lookout";

		/// <summary>The clear metres a ground pyre keeps from the pieces laid round it, beyond their own footprint.</summary>
		public const float GroundClear = 2f;

		public bool Handles(PointOfInterestRecord record) => record != null;

		public void Build(PointOfInterestSiteContext context)
		{
			PointOfInterestRecord record = context.Record;
			if (record == null || context.BiomeAt == null)
			{
				return;
			}
			BiomeTemplate biome = context.BiomeAt(record.Position.x, record.Position.z);
			if (!TryBeacon(biome, record.Kind, out BiomeWayLight light))
			{
				return;
			}
			GameObject prefab = PointOfInterestPieceResolver.Resolve(new PointOfInterestPieceRequest(light.PieceQuery, "timber", record.Id));
			if (prefab == null)
			{
				PointOfInterestFeatureKit.Note(context, $"beacons: no art for {light.Light} yet (Generate Biome Art)");
				return;
			}
			var spots = Lookouts(context.Laid);
			int laid = 0;
			for (int i = 0; i < spots.Count && laid < MaxBeacons; i++, laid++)
			{
				context.AddProp(prefab, spots[i].at, spots[i].rotation, spots[i].scale);
			}
			if (laid == 0 && StandsOnGround(light.Light) && TryGroundSpot(context, out Vector3 ground, out float yaw))
			{
				context.AddProp(prefab, ground, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
				laid = 1;
			}
			context.Notes?.Add($"{record.Name}: {laid} {light.Light} beacon(s), kindled at dusk.");
		}

		/// <summary>
		/// The beacon a biome's authored Way Lights name for a site kind, or false (none named, or the kind's light is no beacon).
		/// Reads only an authored list: the old rule by kind names no beacon.
		/// </summary>
		public static bool TryBeacon(BiomeTemplate biome, POIType kind, out BiomeWayLight light)
		{
			light = default;
			if (biome == null || biome.WayLights == null || biome.WayLights.Count == 0)
			{
				return false;
			}
			return BiomeWayLights.TryFor(biome.WayLights, kind, out light) && BiomeWayLights.ClassOf(light.Light) == BiomeWayLightClass.Beacon;
		}

		/// <summary>True for the beacons built to stand on a hill as well as a tower (every pyre but the iron basket on its stand).</summary>
		public static bool StandsOnGround(BiomeWayLightKind light) => BiomeWayLights.ClassOf(light) == BiomeWayLightClass.Beacon && light != BiomeWayLightKind.Beacon;

		/// <summary>Every lookout of the laid pieces, in the order they were laid: where a beacon stands, turned and scaled as its tower.</summary>
		public static List<(Vector3 at, Quaternion rotation, Vector3 scale)> Lookouts(IReadOnlyList<PointOfInterestSiteContext.PlacedProp> laid)
		{
			var spots = new List<(Vector3, Quaternion, Vector3)>();
			if (laid == null)
			{
				return spots;
			}
			for (int i = 0; i < laid.Count; i++)
			{
				PointOfInterestSiteContext.PlacedProp p = laid[i];
				if (p.Prefab == null || !StructurePieces.TryParsePrefab(p.Prefab.name, out StructurePiece piece, out int variant))
				{
					continue;
				}
				foreach (StructureMark mark in StructurePieces.MarksOf(piece, variant))
				{
					if (mark.Name == LookoutMark)
					{
						spots.Add((p.Position + p.Rotation * Vector3.Scale(mark.At, p.Scale), p.Rotation * Quaternion.Euler(0f, mark.Yaw, 0f), p.Scale));
						break;
					}
				}
			}
			return spots;
		}

		/// <summary>
		/// Open ground near the site's middle for a ground pyre: the first of two rings of twelve spots (from a hashed bearing) that is
		/// dry and keeps <see cref="GroundClear"/> beyond every laid piece's footprint.
		/// </summary>
		private static bool TryGroundSpot(PointOfInterestSiteContext context, out Vector3 at, out float yaw)
		{
			PointOfInterestRecord record = context.Record;
			at = default;
			yaw = 0f;
			if (context.GroundAt == null)
			{
				return false;
			}
			float start = (PointOfInterestPlanner.Hash(record.Id, 0xBEAC, 1, 2) & 0xFFFF) / 65536f * Mathf.PI * 2f;
			float[] radii = { Mathf.Clamp(record.Radius * 0.45f, 6f, 30f), Mathf.Clamp(record.Radius * 0.7f, 9f, 45f) };
			foreach (float r in radii)
			{
				for (int k = 0; k < 12; k++)
				{
					float a = start + k * Mathf.PI / 6f;
					float x = record.Position.x + Mathf.Sin(a) * r, z = record.Position.z + Mathf.Cos(a) * r;
					if (context.IsWet(x, z, 0.5f) || Crowded(context.Laid, x, z))
					{
						continue;
					}
					at = new Vector3(x, context.GroundAt(x, z), z);
					yaw = a * Mathf.Rad2Deg + 180f;
					return true;
				}
			}
			return false;
		}

		private static bool Crowded(IReadOnlyList<PointOfInterestSiteContext.PlacedProp> laid, float x, float z)
		{
			for (int i = 0; i < laid.Count; i++)
			{
				PointOfInterestSiteContext.PlacedProp p = laid[i];
				float reach = 1.5f;
				if (p.Prefab != null && StructurePieces.TryParsePrefab(p.Prefab.name, out StructurePiece piece, out _))
				{
					reach = 0.5f * piece.Footprint.magnitude * Mathf.Max(Mathf.Abs(p.Scale.x), Mathf.Abs(p.Scale.z));
				}
				reach += GroundClear;
				float dx = p.Position.x - x, dz = p.Position.z - z;
				if (dx * dx + dz * dz < reach * reach)
				{
					return true;
				}
			}
			return false;
		}
	}
}
#endif
