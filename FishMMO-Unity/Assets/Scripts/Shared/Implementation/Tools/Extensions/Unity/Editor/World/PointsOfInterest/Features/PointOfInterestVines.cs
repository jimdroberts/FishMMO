#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Ivy on the walls a site laid (2026-10-12: "Ruins and old walls"): after the template's features, every upright
	/// face of masonry (and, less, of plaster and timber) on the site's structure pieces that reaches the ground may carry a
	/// mat of the biome's vine climbing it from the ground, cut to the face's clear stretch (below its windows, never across
	/// a doorway), and a little spill at its foot.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Weighted to age, damp and shade.</b> The chance a face is grown is the biome's (<see cref="VineBiomes.Habitat.Ruins"/>:
	/// the damp, shaded woods densest), times how old and neglected the piece is (a ruin, by how far gone; a graveyard's stones
	/// and a crypt; a keep's or a church's old stone a little; a lived-in town's walls seldom, its people keep them clear),
	/// times how readily ivy takes to the material, a little more on a north face (in the shade, where it thrives). Burnt
	/// pieces (a raid is recent) and sunken ones carry none; nor do tents, furniture, clutter or anything under a roof.
	/// </para>
	/// <para>
	/// <b>Visual only.</b> The mats are props without colliders: nothing to walk round or stand on, no NavMesh change.
	/// </para>
	/// </remarks>
	[InitializeOnLoad]
	public sealed class PointOfInterestVines : IPointOfInterestSiteBuilder
	{
		static PointOfInterestVines() => PointOfInterestSiteBuilders.Register(new PointOfInterestVines());

		/// <summary>The most mats one site carries: a ruined town is ivied, not wallpapered.</summary>
		private const int MaxMats = 28;

		/// <summary>
		/// Tags of pieces ivy never grows on: what is carried, furnished or pitched. ("underwater" is not one: it marks the stone
		/// ruins a sunken site may also use; a sunken site itself is left out by its trait, and its algae finish.)
		/// </summary>
		private static readonly string[] Never =
		{
			"tent", "camp", "bedding", "fire", "storage", "banner", "furniture", "bed", "table", "chair", "chest", "shelf", "hearth",
			"bar", "pew", "lectern", "clutter", "door", "step", "light", "torch", "boat", "ship", "wreck", "portal", "market", "stall",
		};

		private static readonly Dictionary<(string, int), List<VineRules.WallFace>> faces = new Dictionary<(string, int), List<VineRules.WallFace>>();

		public bool Handles(PointOfInterestRecord record) => !PointOfInterestKinds.Info(record.Kind).Has(PointOfInterestTraits.Underwater);

		public void Build(PointOfInterestSiteContext context)
		{
			if (context.Laid == null || context.Laid.Count == 0 || context.GroundAt == null)
			{
				return;
			}
			BiomeTemplate biome = context.BiomeAt != null ? context.BiomeAt(context.Record.Position.x, context.Record.Position.z) : null;
			VineBiomes.Habitat habitat = BiomeTemplateArt.Vines(biome);
			if (habitat == null || habitat.Ruins <= 0f)
			{
				return;
			}
			float site = SiteWeight(PointOfInterestKinds.Info(context.Record.Kind));
			if (site <= 0f)
			{
				return;
			}
			DeterministicRNG rng = context.Random;
			string species = habitat.Pick(rng.NextFloat());
			var ways = context.Points != null && context.Points.Paths != null ? new PathExclusion(context.Points.Paths) : null;
			int mats = 0, spills = 0, missing = 0;
			// What the features laid: the vines added below are not walls to grow on.
			int laid = context.Laid.Count;
			for (int i = 0; i < laid && mats < MaxMats; i++)
			{
				PointOfInterestSiteContext.PlacedProp prop = context.Laid[i];
				if (!StructurePieces.TryParsePrefab(prop.Prefab.name, out StructurePiece piece, out int variant))
				{
					continue;
				}
				float age = PieceWeight(piece, prop.Prefab.name);
				if (age <= 0f)
				{
					continue;
				}
				foreach (VineRules.WallFace face in FacesOf(piece, variant))
				{
					Vector3 normal = prop.Rotation * face.Normal;
					// North faces (+z) are in the shade, where ivy thrives.
					float shade = 0.75f + 0.25f * Mathf.Clamp(normal.z, -1f, 1f);
					// The inside of a roofless ruin's walls is grown too, less: it is seen only from within.
					float chance = habitat.Ruins * site * age * VineRules.Takes(face.Material) * shade * (face.Inside ? 0.4f : 1f);
					if (rng.NextFloat() >= chance || !VineRules.FitMat(in face, species, rng, out VineSpot spot))
					{
						continue;
					}
					float stature = Mathf.Abs(prop.Scale.y);
					Vector3 foot = prop.Position + prop.Rotation * Vector3.Scale(spot.Position, prop.Scale);
					// From the ground under its foot, wherever the site's pad left it.
					foot.y = Mathf.Min(foot.y, context.GroundAt(foot.x, foot.z)) - 0.05f;
					spot.Position = foot;
					spot.Yaw += prop.Rotation.eulerAngles.y;
					spot.Scale *= stature;
					GameObject prefab = VinePlacement.PrefabFor(in spot);
					if (prefab == null)
					{
						missing++;
						continue;
					}
					context.AddProp(prefab, spot.Position, VinePlacement.RotationOf(in spot), spot.Scale);
					mats++;
					// A little spilling out over the ground at its foot, off the trodden ways and the water.
					Vector3 at = foot + normal * rng.Range(0.1f, 0.3f);
					if (rng.NextFloat() < 0.45f && (ways == null || !ways.Details(at.x, at.z))
						&& (context.Water == null || context.Water.KindAt(at.x, at.z) == WaterKind.None))
					{
						var spill = new VineSpot
						{
							Species = species, Form = VineForm.Spill, Size = "Small", Variant = rng.Range(0, 2),
							Position = new Vector3(at.x, context.GroundAt(at.x, at.z) - 0.02f, at.z), Yaw = spot.Yaw + rng.Range(-30f, 30f),
							Scale = Vector3.one * (stature * rng.Range(0.7f, 1.05f)),
						};
						GameObject spillPrefab = VinePlacement.PrefabFor(in spill);
						if (spillPrefab != null)
						{
							context.AddProp(spillPrefab, spill.Position, VinePlacement.RotationOf(in spill), spill.Scale);
							spills++;
						}
					}
				}
			}
			if (mats > 0 || missing > 0)
			{
				PointOfInterestFeatureKit.Note(context, $"{mats} wall face(s) grown over with {species}, {spills} spill(s) at their feet"
					+ (missing > 0 ? $"; {missing} left bare (no vine art: Generate Biome Art)" : string.Empty) + ".");
			}
		}

		/// <summary>How neglected a kind of site is: ruins and the dead fully, sacred places and the wilds less, a lived-in settlement little.</summary>
		private static float SiteWeight(PointOfInterestKindInfo info)
		{
			switch (info.Group)
			{
				case PointOfInterestGroup.Ruins:
				case PointOfInterestGroup.Dead:
					return 1f;
				case PointOfInterestGroup.Wetland:
				case PointOfInterestGroup.Sacred:
					return 0.65f;
				case PointOfInterestGroup.Wild:
				case PointOfInterestGroup.Encounter:
				case PointOfInterestGroup.Coast:
					return 0.5f;
				case PointOfInterestGroup.Settlement:
					return 0.22f;
				case PointOfInterestGroup.Alien:
					return 0f;
				default:
					return 0.4f;
			}
		}

		/// <summary>How old and neglected a piece is: a ruin by how far gone; old stone well; a burnt or sunken one, or a carried thing, not at all.</summary>
		private static float PieceWeight(StructurePiece piece, string prefabName)
		{
			if (prefabName.EndsWith("_" + StructureFinish.Charred, StringComparison.Ordinal) || prefabName.EndsWith("_" + StructureFinish.Algae, StringComparison.Ordinal))
			{
				return 0f;
			}
			foreach (string tag in Never)
			{
				if (piece.HasTag(tag))
				{
					return 0f;
				}
			}
			float weight;
			if (piece.RuinOf != null || piece.HasTag("ruin"))
			{
				weight = 0.55f + piece.Decay;
			}
			else if (piece.HasTag("grave") || piece.HasTag("crypt") || piece.HasTag("standing-stone") || piece.HasTag("monument") || piece.HasTag("statue") || piece.HasTag("column"))
			{
				weight = 0.8f;
			}
			else if (piece.HasTag("wall") || piece.HasTag("tower") || piece.HasTag("keep") || piece.HasTag("church") || piece.HasTag("temple") || piece.HasTag("well") || piece.HasTag("bridge"))
			{
				weight = 0.6f;
			}
			else
			{
				weight = 0.35f;
			}
			// A mossy finish is the old, damp version of a piece.
			if (prefabName.EndsWith("_" + StructureFinish.Mossy, StringComparison.Ordinal))
			{
				weight *= 1.3f;
			}
			return piece.Style == StructureStyle.Timber || piece.Style == StructureStyle.Hide ? weight * 0.5f : weight;
		}

		/// <summary>The faces a piece's variant offers (its first reduced level's solids: the walls as built, fewer small parts), cached.</summary>
		private static List<VineRules.WallFace> FacesOf(StructurePiece piece, int variant)
		{
			if (!faces.TryGetValue((piece.Id, variant), out List<VineRules.WallFace> list))
			{
				List<StructureSolid> solids = StructurePieces.BuildSolids(piece, variant, Mathf.Min(1, StructurePieces.LevelCount - 1), StructurePieces.DefaultSeed);
				list = VineRules.WallFaces(solids, StructurePieces.DoorsOf(piece, variant), StructurePieces.RoomsOf(piece, variant));
				faces[(piece.Id, variant)] = list;
			}
			return list;
		}
	}
}
#endif
