using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Where fences and walls join up (2026-10-19: "fences aren't fully connecting in some cases"; PointOfInterestLinearFeatures.Joins):
	/// over many seeded rectangles, polylines and slopes, consecutive modules' ends meet, every corner of an enclosure has exactly one
	/// post, every gap (a gate's, a break's, a dropped module's, a run's end) is bounded by posts, a gate fills its gap, a solid piece's
	/// corner is closed and its dips sink its neighbours with it. Pure: no scene.
	/// </summary>
	[TestFixture]
	public class FenceJoinTests
	{
		private const float Close = 0.02f;
		private const float Module = 3f;

		/// <summary>Rolling ground with a steady fall, as steep as a module's chord may climb.</summary>
		private static float Ground(float x, float z) => 3f * Mathf.Sin(x * 0.13f) + 2f * Mathf.Cos(z * 0.21f) + 0.05f * x;

		private static Vector3 Joint(Vector2 p) => new Vector3(p.x, Ground(p.x, p.y) - PointOfInterestLinearFeatures.SinkMetres, p.y);

		/// <summary>Where a module's frame puts its end (+1) or its start (−1).</summary>
		private static Vector3 End(Vector3 a, Vector3 b, float module, float sign)
		{
			(Vector3 at, Quaternion rotation, float scale) = PointOfInterestLinearFeatures.ModuleFrame(a, b, module);
			return at + rotation * new Vector3(sign * module * 0.5f * scale, 0f, 0f);
		}

		private static float Plan(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

		/// <summary>The point of the line through a and b over the plan point p (its nearest on the plan).</summary>
		private static Vector3 On(Vector3 a, Vector3 b, Vector2 p)
		{
			var d = new Vector2(b.x - a.x, b.z - a.z);
			float t = Vector2.Dot(p - new Vector2(a.x, a.z), d) / Mathf.Max(1e-8f, d.sqrMagnitude);
			return a + (b - a) * t;
		}

		/// <summary>A run's modules from its stations: a fence's (its post at each start) or a solid piece's (a wall, its dips).</summary>
		private static List<PointOfInterestLinearFeatures.JoinModule> Run(IReadOnlyList<Vector2> line, bool fence, float halfWidth = 0.3f)
		{
			var modules = new List<PointOfInterestLinearFeatures.JoinModule>();
			List<PointOfInterestLinearFeatures.Station> st = PointOfInterestLinearFeatures.Stations(line, Module);
			for (int k = 0; k + 1 < st.Count; k++)
			{
				Vector3 a = Joint(st[k].At), b = Joint(st[k + 1].At);
				float dip = 0.5f * (a.y + b.y) - Joint(0.5f * (st[k].At + st[k + 1].At)).y;
				modules.Add(new PointOfInterestLinearFeatures.JoinModule
				{
					A = a, B = b, Module = Module, Dip = fence ? 0f : dip, HalfWidth = fence ? 0f : halfWidth, PostAtStart = fence, Closes = fence,
				});
			}
			return modules;
		}

		/// <summary>Every post standing once the joints are resolved: the modules' own starts (not squeezed to a sliver), the closers and the given posts.</summary>
		private static List<Vector3> Posts(IReadOnlyList<PointOfInterestLinearFeatures.JoinModule> modules, PointOfInterestLinearFeatures.JoinPlan plan, IEnumerable<Vector3> given = null)
		{
			var posts = new List<Vector3>();
			for (int m = 0; m < modules.Count; m++)
			{
				if (modules[m].PostAtStart && (plan.B[m] - plan.A[m]).magnitude / modules[m].Module >= PointOfInterestLinearFeatures.LeastPostScale)
				{
					posts.Add(plan.A[m]);
				}
			}
			foreach (PointOfInterestLinearFeatures.JoinCloser c in plan.Closers)
			{
				posts.Add(c.At);
			}
			if (given != null)
			{
				posts.AddRange(given);
			}
			return posts;
		}

		private static int PostsAt(List<Vector3> posts, Vector3 at)
		{
			int n = 0;
			foreach (Vector3 p in posts)
			{
				n += Plan(p, at) <= PointOfInterestLinearFeatures.JoinMetres ? 1 : 0;
			}
			return n;
		}

		private static List<Vector2> Polyline(System.Random r)
		{
			var line = new List<Vector2>();
			var p = new Vector2((float)r.NextDouble() * 200f - 100f, (float)r.NextDouble() * 200f - 100f);
			float heading = (float)r.NextDouble() * 360f;
			int points = 2 + r.Next(6);
			for (int i = 0; i < points; i++)
			{
				line.Add(p);
				heading += (float)(r.NextDouble() - 0.5) * 120f;
				p += new Vector2(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad)) * (2f + (float)r.NextDouble() * 25f);
			}
			return line;
		}

		// ── Ends on joints ────────────────────────────────────────

		[Test]
		public void AModulesFrame_PutsItsEndsOnItsJoints_OnAnySlope()
		{
			var r = new System.Random(11);
			for (int i = 0; i < 400; i++)
			{
				var a = new Vector3((float)r.NextDouble() * 50f, (float)r.NextDouble() * 4f, (float)r.NextDouble() * 50f);
				Vector3 b = a + new Vector3((float)(r.NextDouble() - 0.5) * 8f, (float)(r.NextDouble() - 0.5) * 2.5f, (float)(r.NextDouble() - 0.5) * 8f);
				if (Plan(a, b) < 0.2f)
				{
					continue;
				}
				float module = r.NextDouble() < 0.5 ? 3f : 2f;
				Assert.That((End(a, b, module, -1f) - a).magnitude, Is.LessThan(1e-3f), $"start {i}");
				Assert.That((End(a, b, module, 1f) - b).magnitude, Is.LessThan(1e-3f), $"end {i}: scaled to the chord, not the plan");
				// Its +z is the chord's left on the plan: a gate swung to +z swings into an enclosure laid with its inside on the left.
				(_, Quaternion rotation, _) = PointOfInterestLinearFeatures.ModuleFrame(a, b, module);
				Vector3 z = rotation * Vector3.forward;
				Vector2 left = PointOfInterestLinearFeatures.Left(new Vector2(b.x - a.x, b.z - a.z));
				Assert.That(Vector2.Dot(new Vector2(z.x, z.z).normalized, left), Is.GreaterThan(0.99f), $"left {i}");
			}
		}

		[Test]
		public void ARunsModules_MeetEndToEnd_OverRollingGround()
		{
			var r = new System.Random(5);
			for (int i = 0; i < 300; i++)
			{
				List<Vector2> line = Polyline(r);
				foreach (bool fence in new[] { true, false })
				{
					List<PointOfInterestLinearFeatures.JoinModule> modules = Run(line, fence);
					PointOfInterestLinearFeatures.JoinPlan plan = PointOfInterestLinearFeatures.Resolve(modules, null, PointOfInterestLinearFeatures.JoinMetres);
					for (int m = 0; m + 1 < modules.Count; m++)
					{
						Vector3 end = End(plan.A[m], plan.B[m], Module, 1f), start = End(plan.A[m + 1], plan.B[m + 1], Module, -1f);
						if (fence)
						{
							Assert.That((end - start).magnitude, Is.LessThan(Close), $"line {i}, fence, joint {m}");
							continue;
						}
						// A solid piece is carried on past a turn along its chord: both pass through the joint, at the same height.
						var joint = new Vector2(modules[m].B.x, modules[m].B.z);
						Vector3 on0 = On(plan.A[m], plan.B[m], joint), on1 = On(plan.A[m + 1], plan.B[m + 1], joint);
						Assert.That(Plan(on0, new Vector3(joint.x, 0f, joint.y)), Is.LessThan(Close), $"line {i}, wall, joint {m}");
						Assert.That(Plan(on1, new Vector3(joint.x, 0f, joint.y)), Is.LessThan(Close), $"line {i}, wall, joint {m}");
						Assert.That(Mathf.Abs(on0.y - on1.y), Is.LessThan(Close), $"line {i}, wall, joint {m}: no step");
					}
					if (fence)
					{
						// Every joint has a post and the run's far end a closing one; nothing doubled.
						List<Vector3> posts = Posts(modules, plan);
						for (int m = 0; m < modules.Count; m++)
						{
							Assert.That(PostsAt(posts, plan.A[m]), Is.EqualTo(1), $"line {i}, joint {m}");
						}
						Assert.That(PostsAt(posts, plan.B[modules.Count - 1]), Is.EqualTo(1), $"line {i}: the run's end");
					}
				}
			}
		}

		[Test]
		public void ASolidPiecesDip_SinksBothItsJoints_AndItsNeighboursWithIt()
		{
			// A wall across a hollow: its middle module's ground is half a metre under its chord.
			Func<float, float> ground = x => x > 3f && x < 6f ? -0.5f * Mathf.Sin((x - 3f) / 3f * Mathf.PI) : 0f;
			var modules = new List<PointOfInterestLinearFeatures.JoinModule>();
			for (int k = 0; k < 3; k++)
			{
				float x0 = 3f * k, x1 = 3f * (k + 1);
				var a = new Vector3(x0, ground(x0), 0f);
				var b = new Vector3(x1, ground(x1), 0f);
				modules.Add(new PointOfInterestLinearFeatures.JoinModule { A = a, B = b, Module = 3f, Dip = 0.5f * (a.y + b.y) - ground(0.5f * (x0 + x1)), HalfWidth = 0.3f });
			}
			PointOfInterestLinearFeatures.JoinPlan plan = PointOfInterestLinearFeatures.Resolve(modules, null, PointOfInterestLinearFeatures.JoinMetres);
			Assert.That(0.5f * (plan.A[1].y + plan.B[1].y), Is.LessThanOrEqualTo(-0.5f + 1e-4f), "the hollow's module reaches its ground");
			Assert.That(plan.B[0].y, Is.EqualTo(plan.A[1].y).Within(1e-5f), "its neighbour sinks with it");
			Assert.That(plan.A[2].y, Is.EqualTo(plan.B[1].y).Within(1e-5f));
			Assert.That(plan.A[0].y, Is.EqualTo(0f).Within(1e-5f), "the far joints stay");
		}

		// ── Enclosures ────────────────────────────────────────────

		[Test]
		public void ARectanglesSides_RunRoundIt_EachCornerOneEndAndOneStart()
		{
			var r = new System.Random(3);
			for (int i = 0; i < 200; i++)
			{
				var centre = new Vector2((float)r.NextDouble() * 400f, (float)r.NextDouble() * 400f);
				float yaw = (float)r.NextDouble() * 360f;
				var size = new Vector2(4f + (float)r.NextDouble() * 60f, 4f + (float)r.NextDouble() * 60f);
				PointOfInterestLinearFeatures.Side[] sides = PointOfInterestLinearFeatures.RectangleSides(centre, yaw, size, centre + Vector2.up * 100f, (float)r.NextDouble());
				foreach (PointOfInterestLinearFeatures.Side side in sides)
				{
					int starts = 0;
					foreach (PointOfInterestLinearFeatures.Side other in sides)
					{
						starts += (other.From - side.To).magnitude < 1e-3f ? 1 : 0;
					}
					Assert.That(starts, Is.EqualTo(1), $"rect {i}: the corner a side ends at is exactly one side's start");
					Assert.That(Vector2.Dot(PointOfInterestLinearFeatures.Left(side.To - side.From), side.Outward), Is.LessThan(-0.99f), $"rect {i}: its inside on its left");
				}
			}
		}

		[Test]
		public void AFencedRectangle_HasOnePostAtEveryCorner_AndEveryGapEndsOnPosts()
		{
			var r = new System.Random(7);
			for (int i = 0; i < 300; i++)
			{
				var centre = new Vector2((float)r.NextDouble() * 400f - 200f, (float)r.NextDouble() * 400f - 200f);
				float yaw = (float)r.NextDouble() * 360f;
				var size = new Vector2(5f + (float)r.NextDouble() * 50f, 5f + (float)r.NextDouble() * 50f);
				float breaks = i % 3 == 0 ? 0f : (float)r.NextDouble() * 0.5f;
				PointOfInterestLinearFeatures.Side[] sides = PointOfInterestLinearFeatures.RectangleSides(centre, yaw, size, centre + new Vector2(1f, 3f) * 50f);
				var modules = new List<PointOfInterestLinearFeatures.JoinModule>();
				var gatePosts = new List<Vector3>();
				var gaps = new List<(Vector3 A, Vector3 B)>();
				int line = 0;
				foreach (PointOfInterestLinearFeatures.Side side in sides)
				{
					List<PointOfInterestLinearFeatures.Station> st = PointOfInterestLinearFeatures.Stations(new[] { side.From, side.To }, Module);
					int count = st.Count - 1;
					int gate = side.Gate ? PointOfInterestLinearFeatures.GateModule(count) : -1;
					bool[] kept = PointOfInterestLinearFeatures.Kept(count, null, gate, i * 31 + line++, breaks);
					for (int k = 0; k < count; k++)
					{
						Vector3 a = Joint(st[k].At), b = Joint(st[k + 1].At);
						if (k == gate)
						{
							// The gateway: one in two stands (the other could not, and leaves its sides to closing posts).
							if (r.NextDouble() < 0.5)
							{
								modules.Add(new PointOfInterestLinearFeatures.JoinModule { A = a, B = b, Module = 3f });
								gatePosts.Add(a);
								gatePosts.Add(b);
							}
							continue;
						}
						if (kept[k])
						{
							modules.Add(new PointOfInterestLinearFeatures.JoinModule { A = a, B = b, Module = Module, PostAtStart = true, Closes = true });
						}
					}
				}
				PointOfInterestLinearFeatures.JoinPlan plan = PointOfInterestLinearFeatures.Resolve(modules, gatePosts, PointOfInterestLinearFeatures.JoinMetres);
				List<Vector3> posts = Posts(modules, plan, gatePosts);
				for (int m = 0; m < modules.Count; m++)
				{
					if (modules[m].Closes)
					{
						Assert.That(PostsAt(posts, plan.A[m]), Is.GreaterThanOrEqualTo(1), $"rect {i}: module {m}'s start");
						Assert.That(PostsAt(posts, plan.B[m]), Is.GreaterThanOrEqualTo(1), $"rect {i}: module {m}'s end");
					}
				}
				// A post is never laid twice over (a gate's own posts apart, which stand inside its gap).
				var own = Posts(modules, plan);
				for (int p = 0; p < own.Count; p++)
				{
					Assert.That(PostsAt(own, own[p]), Is.EqualTo(1), $"rect {i}: post {p} doubled");
				}
				if (breaks == 0f && gatePosts.Count > 0)
				{
					// Whole, with its gate: every corner one post, and no closing post needed anywhere.
					Assert.That(plan.Closers, Is.Empty, $"rect {i}");
					foreach (PointOfInterestLinearFeatures.Side side in sides)
					{
						Assert.That(PostsAt(own, Joint(side.From)), Is.EqualTo(1), $"rect {i}: corner {side.From}");
					}
				}
			}
		}

		[Test]
		public void AGate_FillsItsGap()
		{
			// The field fence's gap: a side's module at its middle, as wide as the side's step; the gateway scaled to it lands on its sides.
			foreach (float length in new[] { 7f, 11.5f, 24f, 41.2f, 90f })
			{
				int count = Mathf.Max(1, Mathf.RoundToInt(length / 3f));
				float step = length / count;
				var a = new Vector3(step * (count / 2), 0.4f, 0f);
				var b = new Vector3(step * (count / 2 + 1), 1.1f, 0f);
				Assert.That((End(a, b, StructurePieces.GateModule, -1f) - a).magnitude, Is.LessThan(1e-3f));
				Assert.That((End(a, b, StructurePieces.GateModule, 1f) - b).magnitude, Is.LessThan(1e-3f));
			}
			// A yard's: the span left for it is exactly the gateway's width, so it is laid at its own size.
			Assert.That(PointOfInterestYards.GateSpan(Vector2.zero, new Vector2(9f, 0f), new Vector2(4f, 2f), StructurePieces.GateModule, 0.4f, out Vector2 g0, out Vector2 g1), Is.True);
			Assert.That((g1 - g0).magnitude, Is.EqualTo(StructurePieces.GateModule).Within(1e-4f));
		}

		[Test]
		public void AShortStretchesSqueezedPost_TakesAClosingPost()
		{
			var line = new[] { new Vector2(0f, 0f), new Vector2(0.9f, 0f) };
			List<PointOfInterestLinearFeatures.JoinModule> modules = Run(line, true);
			Assert.That(modules.Count, Is.EqualTo(1));
			PointOfInterestLinearFeatures.JoinPlan plan = PointOfInterestLinearFeatures.Resolve(modules, null, PointOfInterestLinearFeatures.JoinMetres);
			Assert.That(plan.Closers.Count, Is.EqualTo(2), "a 0.9 m stretch: its own post a sliver, both its ends closed");
			Assert.That(Vector2.Dot(plan.Closers[0].Out, Vector2.left), Is.GreaterThan(0.99f), "out past the end it closes");
		}

		[Test]
		public void ARunStoppingOnAnothersSide_TakesATeePost()
		{
			// A neighbour's fence along x; a yard's front fence runs up to it from the south and stops on its line, mid-module.
			var modules = new List<PointOfInterestLinearFeatures.JoinModule>();
			modules.AddRange(Run(new[] { new Vector2(0f, 10f), new Vector2(12f, 10f) }, true));
			int side = modules.Count;
			modules.AddRange(Run(new[] { new Vector2(4.4f, 0f), new Vector2(4.4f, 10f) }, true));
			PointOfInterestLinearFeatures.JoinPlan plan = PointOfInterestLinearFeatures.Resolve(modules, null, PointOfInterestLinearFeatures.JoinMetres);
			List<Vector3> posts = Posts(modules, plan);
			Assert.That(PostsAt(posts, plan.B[modules.Count - 1]), Is.EqualTo(1), "a post where it meets the neighbour's line");
			Assert.That(side, Is.GreaterThan(0));
		}

		[Test]
		public void ASolidCorner_IsCarriedOnToCloseItsOutside()
		{
			var r = new System.Random(9);
			for (int i = 0; i < 100; i++)
			{
				var centre = new Vector2((float)r.NextDouble() * 100f, (float)r.NextDouble() * 100f);
				float yaw = (float)r.NextDouble() * 360f;
				var size = new Vector2(6f + (float)r.NextDouble() * 30f, 6f + (float)r.NextDouble() * 30f);
				const float half = 0.3f;
				var modules = new List<PointOfInterestLinearFeatures.JoinModule>();
				PointOfInterestLinearFeatures.Side[] sides = PointOfInterestLinearFeatures.RectangleSides(centre, yaw, size, centre);
				foreach (PointOfInterestLinearFeatures.Side side in sides)
				{
					// On level ground, so the mitre's reach reads straight off the plan.
					foreach (PointOfInterestLinearFeatures.JoinModule m in Run(new[] { side.From, side.To }, false, half))
					{
						PointOfInterestLinearFeatures.JoinModule flat = m;
						flat.A.y = flat.B.y = 0f;
						flat.Dip = 0f;
						modules.Add(flat);
					}
				}
				PointOfInterestLinearFeatures.JoinPlan plan = PointOfInterestLinearFeatures.Resolve(modules, null, PointOfInterestLinearFeatures.JoinMetres);
				foreach (PointOfInterestLinearFeatures.Side side in sides)
				{
					// At a right angle each wall runs on past the corner by its half width: its outside face reaches the other's.
					for (int m = 0; m < modules.Count; m++)
					{
						if (Plan(modules[m].B, Joint(side.To)) < 1e-3f)
						{
							Assert.That(Plan(plan.B[m], modules[m].B), Is.EqualTo(half).Within(Close), $"rect {i}: corner {side.To}");
						}
						if (Plan(modules[m].A, Joint(side.From)) < 1e-3f)
						{
							Assert.That(Plan(plan.A[m], modules[m].A), Is.EqualTo(half).Within(Close), $"rect {i}: corner {side.From}");
						}
					}
				}
			}
			Assert.That(PointOfInterestLinearFeatures.MitreReach(0.3f, Vector2.right, Vector2.left), Is.EqualTo(0f), "straight on: none");
			Assert.That(PointOfInterestLinearFeatures.MitreReach(0.3f, Vector2.right, Vector2.up), Is.EqualTo(0.3f).Within(1e-4f), "a right angle: its half width");
			Assert.That(PointOfInterestLinearFeatures.MitreReach(0.3f, Vector2.right, new Vector2(0.999f, 0.01f)),
				Is.EqualTo(0.3f * Mathf.Tan(0.5f * PointOfInterestLinearFeatures.MaxMitreDegrees * Mathf.Deg2Rad)).Within(1e-3f), "a hairpin: held");
		}

		// ── The pieces ────────────────────────────────────────────

		[Test]
		public void EveryFenceWithAPostAtItsStart_HasItsClosingPost()
		{
			StructurePiece post = StructurePieces.Find(StructurePieces.LinearPost);
			Assert.That(post, Is.Not.Null);
			Assert.That(post.Variants, Is.EqualTo(StructurePieces.PostVariants));
			Assert.That(post.Tags, Is.EqualTo(new[] { "linear" }));
			Assert.That(post.Style, Is.EqualTo(StructureStyle.Timber), "laid in its fence's palette: the same style");
			foreach (string id in new[] { "FieldFence", "LinearPickets", "LinearPoleFence", "LinearSlabFence" })
			{
				foreach (int v in new[] { 0, 1 })
				{
					int closer = StructurePieces.LinearPostFor(id, v);
					Assert.That(closer, Is.InRange(0, post.Variants - 1), $"{id} v{v}");
					Assert.That(StructurePieces.Find(id).Style, Is.EqualTo(post.Style), id);
					Assert.That(StructurePieces.LinearSolid(id), Is.False, $"{id} spans a dip on its posts");
				}
			}
			Assert.That(StructurePieces.LinearPostFor("LinearPickets", 0), Is.Not.EqualTo(StructurePieces.LinearPostFor("LinearPickets", 1)), "the tall garden fence's post is tall");
			foreach (string id in new[] { "LinearHedge", "LinearDrystone", "LinearMortared", "LinearEarthBank", "LinearSlabs", "LinearWattle", "LinearGate", "LinearClearance", "PalisadeSegment" })
			{
				Assert.That(StructurePieces.LinearPostFor(id, 0), Is.EqualTo(-1), $"{id} is whole at its ends");
			}
			foreach (string id in new[] { "LinearDrystone", "LinearMortared", "LinearEarthBank", "LinearClearance" })
			{
				Assert.That(StructurePieces.LinearSolid(id), Is.True, id);
				Assert.That(StructurePieces.LinearHalfWidth(id), Is.InRange(0.1f, 1f), id);
			}
		}

		[Test]
		public void EveryFenceLayer_LaysThroughTheJoins()
		{
			string root = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/PointsOfInterest/Features/";
			string fields = File.ReadAllText(root + "PointOfInterestFields.cs");
			string farms = File.ReadAllText(root + "PointOfInterestFarmsteads.cs");
			string yards = File.ReadAllText(root + "PointOfInterestYards.cs");
			string linear = File.ReadAllText(root + "PointOfInterestLinearFeatures.cs");
			StringAssert.Contains("joins.Module(context, context.Palette, prefab, \"FieldFence\"", fields);
			StringAssert.Contains("joins?.Close();", fields);
			StringAssert.Contains("joins.Module(context, context.Palette, prefab, \"FieldFence\"", farms);
			StringAssert.Contains("joins.Close();", farms);
			StringAssert.Contains("joins: joins, planned: true", yards);
			StringAssert.Contains("laid = joins.Close();", yards);
			StringAssert.Contains("joins.Module(context, context.Palette, prefab, id, variant, pa, pb, module, dip, yScale, tilt);", linear);
			Assert.That(fields.Contains("Quaternion.Euler(0f, yaw, tilt), new Vector3(step / module"), Is.False, "no module laid level at its middle any more");
		}
	}
}
