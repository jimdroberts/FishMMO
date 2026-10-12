#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Builds its own features in a carved cave's chamber instead of at the site's anchor (the cave's mouth): a camp or a
	/// den on the chamber floor, a large cave's dungeon entrance at its back (2026-10-10: a large cave ends in a
	/// dungeon entrance; a small one is a walk-in grotto).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Re-anchored, not rewritten.</b> Every feature places through the site's record and
	/// <see cref="PointOfInterestSiteContext.OnGround"/>, so the inner features are handed a context whose record is a copy
	/// of the site's moved to the chamber (<see cref="CaveShaper.TryChamber"/>): its floor as the anchor, its floor radius
	/// as the footprint, its heading turned back toward the mouth (so "ahead" is the way a player comes in), and the
	/// ground the chamber floor's plane (the terrain above a chamber is the hillside). No feature needs to know it is in a cave.
	/// </para>
	/// <para>
	/// The copy keeps the site's id, seed, race and name, so names, scene ids and the shape lookups (a dungeon entrance's
	/// tunnel) are the site's. A site that is not a carved cave (its shaper rejected it, or none ran) builds the features
	/// at its anchor as usual.
	/// </para>
	/// </remarks>
	[Serializable]
	public sealed class PointOfInterestChamberFeature : PointOfInterestFeature
	{
		/// <summary>The smallest footprint the chamber is laid out in, metres.</summary>
		[Min(1f)] public float MinRadius = 3f;

		[SerializeReference] public List<PointOfInterestFeature> Features = new List<PointOfInterestFeature>();

		public override void Build(PointOfInterestSiteContext context)
		{
			PointOfInterestSiteContext inner = context;
			if (CaveShaper.TryChamber(context.Points, context.Record.Id, out Vector3 floor, out float radius, out float yaw, out float slope))
			{
				inner = InChamber(context, floor, Mathf.Max(MinRadius, radius), Mathf.Repeat(yaw + 180f, 360f), slope, yaw);
			}
			else
			{
				context.Notes?.Add($"{context.Record.Name}: no carved chamber, so its chamber features stand at the site.");
			}
			if (Features == null)
			{
				return;
			}
			foreach (PointOfInterestFeature feature in Features)
			{
				if (feature == null)
				{
					continue;
				}
				try
				{
					feature.Build(inner);
				}
				catch (Exception ex)
				{
					Debug.LogException(ex);
					context.Notes?.Add($"{context.Record.Name}: chamber feature {feature.GetType().Name} threw: {ex.Message}");
				}
			}
		}

		/// <summary>
		/// A context like the site's, anchored at a chamber floor, its radius and heading; the ground is the floor's plane,
		/// rising <paramref name="slope"/> a metre along <paramref name="slopeYaw"/> (flat by default).
		/// </summary>
		public static PointOfInterestSiteContext InChamber(PointOfInterestSiteContext context, Vector3 floor, float radius, float yaw, float slope = 0f, float slopeYaw = 0f)
		{
			PointOfInterestRecord site = context.Record;
			var record = new PointOfInterestRecord
			{
				Id = site.Id,
				Kind = site.Kind,
				Position = floor,
				Yaw = yaw,
				Radius = radius,
				SizeClass = site.SizeClass,
				BiomeID = site.BiomeID,
				VariantIndex = site.VariantIndex,
				Race = site.Race,
				Name = site.Name,
				Description = site.Description,
				NameSeed = site.NameSeed,
				DetailTier = site.DetailTier,
				RequiresDiscovery = site.RequiresDiscovery,
				ParentId = site.ParentId,
				RiverId = site.RiverId,
				PlanetRiver = site.PlanetRiver,
				LakeId = site.LakeId,
				Template = site.Template,
				SiteSeed = site.SiteSeed,
				UnlockIndex = site.UnlockIndex,
			};
			float level = floor.y;
			float sr = slopeYaw * Mathf.Deg2Rad, sx = Mathf.Sin(sr) * slope, sz = Mathf.Cos(sr) * slope;
			float slopeDegrees = Mathf.Atan(Mathf.Abs(slope)) * Mathf.Rad2Deg;
			return new PointOfInterestSiteContext
			{
				Scene = context.Scene,
				Root = context.Root,
				Record = record,
				Template = context.Template,
				Random = context.Random,
				Points = context.Points,
				Request = context.Request,
				TerrainFolder = context.TerrainFolder,
				// A river on the hillside above a chamber is not water in it.
				Water = null,
				GroundAt = (east, north) => level + (east - floor.x) * sx + (north - floor.z) * sz,
				SlopeAt = (east, north) => slopeDegrees,
				BiomeAt = context.BiomeAt,
				Notes = context.Notes,
				Sink = context.Sink,
				Doors = context.Doors,
				Rooms = context.Rooms,
			};
		}
	}
}
#endif
