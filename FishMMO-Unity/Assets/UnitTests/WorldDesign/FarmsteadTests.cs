using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Worked farmland (2026-10-19, PointOfInterestFarmsteads): a farmstead's layout keeps its parts inside its fence, apart, and
	/// clear of the way in and the barn's doors; a reaped field's stooks stand in rows inside its headland; the draws are the
	/// site's and the field's own; the farm kit's roofs that follow a palette stay sound at every pitch; and the fields lay it
	/// all after every field, fence and old terrace.
	/// </summary>
	[TestFixture]
	public class FarmsteadTests
	{
		private const string Features = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/PointsOfInterest/Features";

		/// <summary>Footprints as the kit measures them (unturned x, z), with a large stone house and a tithe barn among them.</summary>
		private static IEnumerable<PointOfInterestFarmsteads.Sizes> Sizes()
		{
			var cottage = new PointOfInterestFarmsteads.Sizes
			{
				House = new Vector2(7.3f, 9.6f), Barn = new Vector2(21.4f, 12.4f), Rick = new Vector2(7.9f, 5.5f), Sty = new Vector2(3.3f, 5.6f),
				Hen = new Vector2(2.4f, 2.5f), Well = new Vector2(2f, 2f), Cart = new Vector2(1.8f, 4.6f), Woodpile = new Vector2(1.7f, 1f),
			};
			yield return cottage;
			PointOfInterestFarmsteads.Sizes large = cottage;
			large.House = new Vector2(11f, 12.6f);
			large.Barn = new Vector2(23.1f, 15.4f);
			yield return large;
			yield return cottage.Scaled(0.6f);
			yield return large.Scaled(1.6f);
		}

		[Test]
		public void Layout_KeepsEveryPartInsideItsFence_ApartAndClearOfTheWayInAndTheBarnDoors()
		{
			foreach (PointOfInterestFarmsteads.Sizes sizes in Sizes())
			{
				for (int seed = 0; seed < 300; seed++)
				{
					PointOfInterestFarmsteads.Mix mix = PointOfInterestFarmsteads.MixFor(seed);
					PointOfInterestFarmsteads.Plan plan = PointOfInterestFarmsteads.Layout(sizes, mix, seed);
					List<PointOfInterestFarmsteads.Placed> parts = plan.Parts;
					string name = $"seed {seed} house {sizes.House}";
					Assert.That(parts.FindAll(p => p.Part == PointOfInterestFarmsteads.Part.Rick).Count, Is.EqualTo(mix.Ricks), name);
					Assert.That(parts.Exists(p => p.Part == PointOfInterestFarmsteads.Part.Barn) && parts.Exists(p => p.Part == PointOfInterestFarmsteads.Part.House), Is.True, name);
					Vector2 half = plan.Size * 0.5f - Vector2.one * (PointOfInterestFarmsteads.Margin - 0.01f);
					for (int i = 0; i < parts.Count; i++)
					{
						PointOfInterestFarmsteads.Placed a = parts[i];
						Assert.That(Mathf.Abs(a.At.x) + a.Extent.x * 0.5f, Is.LessThanOrEqualTo(half.x), $"{name}: {a.Part} out of the yard in x");
						Assert.That(Mathf.Abs(a.At.y) + a.Extent.y * 0.5f, Is.LessThanOrEqualTo(half.y), $"{name}: {a.Part} out of the yard in z");
						for (int j = i + 1; j < parts.Count; j++)
						{
							Assert.That(PointOfInterestFarmsteads.Overlap(a, parts[j], 0.2f), Is.False, $"{name}: {a.Part} and {parts[j].Part} overlap");
						}
					}
					// The way in from the gate (the middle of the −z side) up to the house's door is open.
					PointOfInterestFarmsteads.Placed house = parts.Find(p => p.Part == PointOfInterestFarmsteads.Part.House);
					var lane = new PointOfInterestFarmsteads.Placed
					{
						At = new Vector2(0f, (-plan.Size.y * 0.5f + house.At.y - house.Extent.y * 0.5f) * 0.5f),
						Extent = new Vector2(2.4f, house.At.y - house.Extent.y * 0.5f + plan.Size.y * 0.5f - 0.1f),
					};
					// The barn's doors (its long side facing +x) have a cart's room before them.
					PointOfInterestFarmsteads.Placed barn = parts.Find(p => p.Part == PointOfInterestFarmsteads.Part.Barn);
					Assert.That(barn.Yaw, Is.EqualTo(-90f), "its front (−z) turned to +x, the yard");
					var apron = new PointOfInterestFarmsteads.Placed { At = new Vector2(barn.At.x + barn.Extent.x * 0.5f + 1.5f, barn.At.y), Extent = new Vector2(2.9f, 4.4f) };
					foreach (PointOfInterestFarmsteads.Placed p in parts)
					{
						if (p.Part != PointOfInterestFarmsteads.Part.House)
						{
							Assert.That(PointOfInterestFarmsteads.Overlap(p, lane, 0f), Is.False, $"{name}: {p.Part} stands in the way in");
						}
						if (p.Part != PointOfInterestFarmsteads.Part.Barn)
						{
							Assert.That(PointOfInterestFarmsteads.Overlap(p, apron, 0f), Is.False, $"{name}: {p.Part} before the barn's doors");
						}
					}
					Assert.That(house.Yaw, Is.EqualTo(0f), "the house looks out to the gate");
				}
			}
		}

		[Test]
		public void Layout_AndMix_AreTheSameEveryTime()
		{
			PointOfInterestFarmsteads.Sizes sizes = default;
			foreach (PointOfInterestFarmsteads.Sizes s in Sizes())
			{
				sizes = s;
				break;
			}
			for (int seed = 0; seed < 50; seed++)
			{
				PointOfInterestFarmsteads.Mix mix = PointOfInterestFarmsteads.MixFor(seed);
				Assert.That(PointOfInterestFarmsteads.MixFor(seed), Is.EqualTo(mix));
				Assert.That(mix.Sty || mix.Hen, Is.True, "a sty or a hen house, at least");
				Assert.That(mix.Ricks, Is.InRange(1, 2));
				PointOfInterestFarmsteads.Plan a = PointOfInterestFarmsteads.Layout(sizes, mix, seed), b = PointOfInterestFarmsteads.Layout(sizes, mix, seed);
				Assert.That(b.Size, Is.EqualTo(a.Size));
				Assert.That(b.Parts, Is.EqualTo(a.Parts));
			}
		}

		[Test]
		public void StookRows_StandInsideTheHeadland_InRowsDownTheField()
		{
			foreach (Vector2 size in new[] { new Vector2(60f, 30f), new Vector2(22f, 44f), new Vector2(16f, 12f), new Vector2(72f, 44f) })
			{
				for (int k = 0; k < 20; k++)
				{
					float shift = k / 19f, stagger = 1f - k / 19f;
					List<(Vector2 At, float Yaw)> rows = PointOfInterestFarmsteads.StookRows(size, shift, stagger);
					Assert.That(rows, Is.Not.Empty, $"{size}");
					Assert.That(PointOfInterestFarmsteads.StookRows(size, shift, stagger), Is.EqualTo(rows));
					bool alongX = size.x >= size.y;
					foreach ((Vector2 at, float yaw) in rows)
					{
						Assert.That(yaw, Is.EqualTo(alongX ? 0f : 90f), "each group along its row, down the field's length");
						// A group (about 5.5 m along its row, 1.3 across) inside the field less its headland.
						Vector2 extent = alongX ? new Vector2(5.5f, 1.3f) : new Vector2(1.3f, 5.5f);
						Assert.That(Mathf.Abs(at.x) + extent.x * 0.5f, Is.LessThanOrEqualTo(size.x * 0.5f - PointOfInterestFarmsteads.StookHeadland + 0.6f), $"{size} {at}");
						Assert.That(Mathf.Abs(at.y) + extent.y * 0.5f, Is.LessThanOrEqualTo(size.y * 0.5f - PointOfInterestFarmsteads.StookHeadland + 0.6f), $"{size} {at}");
					}
				}
			}
			Assert.That(PointOfInterestFarmsteads.StookRows(new Vector2(9f, 6f), 0.5f, 0.5f), Is.Empty, "no room inside the headland");
		}

		[Test]
		public void Reaped_IsTheSitesAndTheFieldsOwnDraw_AtItsShare()
		{
			int reaped = 0;
			for (int f = 0; f < 4000; f++)
			{
				bool r = PointOfInterestFarmsteads.Reaped(12345 + f / 8, f % 8);
				Assert.That(PointOfInterestFarmsteads.Reaped(12345 + f / 8, f % 8), Is.EqualTo(r));
				reaped += r ? 1 : 0;
			}
			Assert.That(reaped / 4000f, Is.EqualTo(PointOfInterestFarmsteads.ReapedShare).Within(0.04f));
			Assert.That(PointOfInterestFarmsteads.StubbleCover, Is.InRange(0.05f, 0.5f));
			Assert.That(PointOfInterestFarmsteads.ClaimSite(7), Is.Not.EqualTo(7), "a farmstead's claim is never one of its site's fields");
		}

		/// <summary>The fields lay the farmland after every field, fence and old terrace; the farmsteads draw nothing from the site's sequence.</summary>
		[Test]
		public void Wiring_AfterEveryFieldFenceAndTerrace_OnDrawsOfItsOwn()
		{
			string fields = File.ReadAllText(Features + "/PointOfInterestFields.cs");
			int flights = fields.IndexOf("PointOfInterestFieldKinds.LayAbandonedTerraces(context, ways, ref carved, ref pieces);", StringComparison.Ordinal);
			int dress = fields.IndexOf("PointOfInterestFarmsteads.Dress(context, laidFields, abandoned);", StringComparison.Ordinal);
			int lay = fields.IndexOf("PointOfInterestFarmsteads.Lay(context, laidFields);", StringComparison.Ordinal);
			Assert.That(flights, Is.GreaterThan(0));
			Assert.That(dress, Is.GreaterThan(flights));
			Assert.That(lay, Is.GreaterThan(dress));
			string farm = File.ReadAllText(Features + "/PointOfInterestFarmsteads.cs");
			Assert.That(farm.Contains("context.Random") || farm.Contains("DeterministicRNG"), Is.False, "never the site's sequence");
			Assert.That(farm.Contains("[InitializeOnLoad]"), Is.False, "not a site builder of its own: a new one would move every later builder's draws");
		}

		/// <summary>
		/// The farm kit's roofs that follow their palette (the timber barn's thatch, the coop's boards, the wattle hen house's thatch)
		/// are sound at every pitch: valid, closed enough, within budget, no wider than the kit's piece, the same doors and rooms.
		/// </summary>
		[Test]
		public void FarmRoofs_AtEveryPitch_AreSoundAndOnlyTheirRoofDiffers()
		{
			const int seed = ProceduralArtCatalogue.DefaultSeed;
			foreach ((string id, int v) in new[] { ("Barn", 0), ("HenHouse", 0), ("HenHouse", 1) })
			{
				StructurePiece piece = StructurePieces.Find(id);
				StructureDraft kit = StructurePieces.Draft(piece, v, 0, seed);
				Assert.That(kit.FollowingRoofs.Count, Is.EqualTo(1), $"{id} v{v}: one roof that follows");
				Bounds kitBounds = StructurePieces.Build(piece, v, 0, seed).Mesh.Bounds;
				foreach (StructureRoofPitch pitch in StructureRoofs.Pitches)
				{
					string name = $"{id} v{v} {pitch}";
					StructureDraft draft = StructurePieces.Draft(piece, v, 0, seed, pitch);
					Assert.That(draft.Doors.Count, Is.EqualTo(kit.Doors.Count), name);
					Assert.That(draft.Rooms.Count, Is.EqualTo(kit.Rooms.Count), name);
					int previous = int.MaxValue;
					for (int lod = 0; lod < StructurePieces.LevelCount; lod++)
					{
						StructureMesh m = StructurePieces.Build(piece, v, lod, seed, pitch);
						Assert.That(m.Mesh.Validate(true), Is.Empty, $"{name} LOD{lod}");
						Assert.That(m.Mesh.TriangleCount, Is.LessThanOrEqualTo(lod == 0 ? piece.TriangleBudget : previous), $"{name} LOD{lod}");
						previous = m.Mesh.TriangleCount;
						Bounds b = m.Mesh.Bounds;
						Assert.That(b.size.x, Is.LessThanOrEqualTo(kitBounds.size.x + 0.02f), $"{name} wider");
						Assert.That(b.size.z, Is.LessThanOrEqualTo(kitBounds.size.z + 0.02f), $"{name} deeper");
						Assert.That(b.max.y, Is.LessThanOrEqualTo(25f), name);
					}
				}
			}
		}
	}
}
