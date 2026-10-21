#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Where a fence's or a wall's modules meet (2026-10-19: "fences aren't fully connecting in some cases"): every line of modules is
	/// queued and laid only when its joints are resolved, so a run joins up wherever it is laid from.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Ends on joints.</b> A module is laid between two joints on the ground, its frame's ends (±half its built length along x) on them:
	/// pitched to the chord and scaled along x to the chord's own length, not the plan's, so on a slope its ends still reach its
	/// neighbours'. A joint's height is the ground's there, the same for both modules meeting on it; a solid piece (a wall, a bank,
	/// a hedge's base) is sunk where the ground dips under its middle by sinking both its joints, so its neighbours sink with it and
	/// no step opens between them.
	/// </para>
	/// <para>
	/// <b>Posts.</b> A fence's module carries one post, at its start: the next module's start closes it. Where a run ends, a gap opens
	/// (a gate that could not stand, a break, water, a way, a door) or a run stops against another's side, no module's own post
	/// stands at that end, and a closing post (StructurePieces' LinearPost, the fence's own post) is laid there; one whose own start
	/// post is squeezed to a sliver (a short stretch) takes one over it. A gateway stands on its own posts and closes the ends beside it.
	/// </para>
	/// <para>
	/// <b>Corners.</b> Where two modules of a solid piece meet at a turn, each is carried on past the joint by its half width times the
	/// tangent of half the turn, so the corner's outside is closed (the mitre) and its inside overlaps.
	/// </para>
	/// </remarks>
	public static partial class PointOfInterestLinearFeatures
	{
		/// <summary>How near two module ends (or an end and a post) lie on the plan to be one joint, metres.</summary>
		public const float JoinMetres = 0.12f;
		/// <summary>The least a module's own start post may be squeezed along its line (its scale) and still close its joint: a shorter one is a sliver.</summary>
		public const float LeastPostScale = 0.6f;
		/// <summary>How far a module's joints are sunk below the ground they stand on, metres: its footing takes up the rest.</summary>
		public const float SinkMetres = 0.04f;
		/// <summary>The sharpest turn a solid piece's corner is closed for, degrees: past it the mitre would run on further than the piece is thick.</summary>
		public const float MaxMitreDegrees = 120f;

		/// <summary>One module as its joints see it.</summary>
		public struct JoinModule
		{
			/// <summary>Its ends on the ground, <see cref="A"/> its start (its frame's −x end).</summary>
			public Vector3 A, B;
			/// <summary>The length it is built at along x, metres.</summary>
			public float Module;
			/// <summary>How far the ground under its middle lies below its chord, metres (0 for a piece that spans a dip).</summary>
			public float Dip;
			/// <summary>How far either side of its line it stands, for its corners' mitre (0: none).</summary>
			public float HalfWidth;
			/// <summary>Whether its start carries its own post.</summary>
			public bool PostAtStart;
			/// <summary>Whether an end of it no post stands at takes a closing post.</summary>
			public bool Closes;
		}

		/// <summary>A closing post: where it stands (on the ground), the run's direction out past the end it closes (plan x, z), and the module it closes.</summary>
		public struct JoinCloser
		{
			public Vector3 At;
			public Vector2 Out;
			public int Module;
		}

		/// <summary>Modules' joints resolved: each module's ends as laid, and where the closing posts stand.</summary>
		public sealed class JoinPlan
		{
			public Vector3[] A, B;
			public readonly List<JoinCloser> Closers = new List<JoinCloser>();
		}

		/// <summary>
		/// A module's frame between two joints (pure): its middle, the rotation laying its x along the chord (its +z the chord's left on
		/// the plan), and its scale along x so its ends (±<paramref name="module"/>/2) land on the joints.
		/// </summary>
		public static (Vector3 Position, Quaternion Rotation, float ScaleX) ModuleFrame(Vector3 a, Vector3 b, float module)
		{
			return (0.5f * (a + b), AlongX(a, b), module > 0f ? (b - a).magnitude / module : 1f);
		}

		/// <summary>
		/// How far a solid piece is carried on past a joint where it turns (pure): its <paramref name="halfWidth"/> times the tangent of
		/// half the turn between its direction out of the joint <paramref name="outOf"/> (from its own end, outward) and its neighbour's
		/// <paramref name="neighbourOut"/>. 0 on a straight; the turn held to <see cref="MaxMitreDegrees"/>.
		/// </summary>
		public static float MitreReach(float halfWidth, Vector2 outOf, Vector2 neighbourOut)
		{
			if (!(halfWidth > 0f) || outOf.sqrMagnitude < 1e-10f || neighbourOut.sqrMagnitude < 1e-10f)
			{
				return 0f;
			}
			// Straight on, the two point opposite ways: the turn is what is left of half a circle.
			float turn = 180f - Vector2.Angle(outOf, neighbourOut);
			if (turn < 0.5f)
			{
				return 0f;
			}
			return halfWidth * Mathf.Tan(0.5f * Mathf.Min(turn, MaxMitreDegrees) * Mathf.Deg2Rad);
		}

		/// <summary>
		/// Resolves where modules meet (pure): their ends grouped into joints (within <paramref name="tolerance"/> on the plan, in the order
		/// they were queued), each joint sunk by the deepest dip of the solid modules on it, a solid piece's two ends at a turn carried on
		/// past it (<see cref="MitreReach"/>), and a closing post at every end of a fence no post stands at: not a module's own start post
		/// (unless squeezed below <see cref="LeastPostScale"/>), nor one of <paramref name="posts"/> (a gateway's), nor a closing post laid
		/// before it.
		/// </summary>
		public static JoinPlan Resolve(IReadOnlyList<JoinModule> modules, IReadOnlyList<Vector3> posts, float tolerance)
		{
			int n = modules != null ? modules.Count : 0;
			var plan = new JoinPlan { A = new Vector3[n], B = new Vector3[n] };
			if (n == 0)
			{
				return plan;
			}
			float cell = Mathf.Max(1e-3f, tolerance);
			// The joints: end e (2·module + 0 for A, 1 for B) in the first group whose first end lies within the tolerance.
			var group = new int[2 * n];
			var anchors = new List<Vector2>();
			var members = new List<List<int>>();
			var grid = new Dictionary<(int, int), List<int>>();
			(int, int) Cell(Vector2 p) => (Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell));
			for (int e = 0; e < 2 * n; e++)
			{
				Vector3 at3 = (e & 1) == 0 ? modules[e >> 1].A : modules[e >> 1].B;
				var at = new Vector2(at3.x, at3.z);
				(int cx, int cz) = Cell(at);
				int found = -1;
				for (int dz = -1; dz <= 1 && found < 0; dz++)
				{
					for (int dx = -1; dx <= 1 && found < 0; dx++)
					{
						if (!grid.TryGetValue((cx + dx, cz + dz), out List<int> near))
						{
							continue;
						}
						foreach (int g in near)
						{
							if ((anchors[g] - at).magnitude <= tolerance && (found < 0 || g < found))
							{
								found = g;
							}
						}
					}
				}
				if (found < 0)
				{
					found = anchors.Count;
					anchors.Add(at);
					members.Add(new List<int>(2));
					if (!grid.TryGetValue((cx, cz), out List<int> list))
					{
						list = new List<int>(1);
						grid[(cx, cz)] = list;
					}
					list.Add(found);
				}
				group[e] = found;
				members[found].Add(e);
			}
			// Each joint sunk by the deepest dip of the modules on it, so every module on it keeps its end there.
			var sink = new float[anchors.Count];
			for (int e = 0; e < 2 * n; e++)
			{
				sink[group[e]] = Mathf.Max(sink[group[e]], Mathf.Max(0f, modules[e >> 1].Dip));
			}
			for (int m = 0; m < n; m++)
			{
				plan.A[m] = modules[m].A - Vector3.up * sink[group[2 * m]];
				plan.B[m] = modules[m].B - Vector3.up * sink[group[2 * m + 1]];
			}
			// The fences' closing posts, on the joints as sunk (before any mitre, which only a solid piece takes).
			var standing = new List<Vector2>();
			if (posts != null)
			{
				foreach (Vector3 p in posts)
				{
					standing.Add(new Vector2(p.x, p.z));
				}
			}
			var ownPost = new bool[n];
			for (int m = 0; m < n; m++)
			{
				JoinModule j = modules[m];
				float length = (plan.B[m] - plan.A[m]).magnitude;
				ownPost[m] = j.PostAtStart && j.Module > 0f && length / j.Module >= LeastPostScale;
				if (ownPost[m])
				{
					standing.Add(new Vector2(plan.A[m].x, plan.A[m].z));
				}
			}
			bool Stands(Vector3 at)
			{
				var p = new Vector2(at.x, at.z);
				foreach (Vector2 s in standing)
				{
					if ((s - p).magnitude <= tolerance)
					{
						return true;
					}
				}
				return false;
			}
			for (int m = 0; m < n; m++)
			{
				if (!modules[m].Closes)
				{
					continue;
				}
				Vector3 a = plan.A[m], b = plan.B[m];
				var along = new Vector2(b.x - a.x, b.z - a.z);
				if (along.sqrMagnitude < 1e-10f)
				{
					continue;
				}
				along.Normalize();
				if (!ownPost[m] && !Stands(a))
				{
					plan.Closers.Add(new JoinCloser { At = a, Out = -along, Module = m });
					standing.Add(new Vector2(a.x, a.z));
				}
				if (!Stands(b))
				{
					plan.Closers.Add(new JoinCloser { At = b, Out = along, Module = m });
					standing.Add(new Vector2(b.x, b.z));
				}
			}
			// A solid piece's corners: where exactly two ends of two modules meet at a turn, each carried on past it.
			var reachA = new float[n];
			var reachB = new float[n];
			foreach (List<int> joint in members)
			{
				if (joint.Count != 2 || joint[0] >> 1 == joint[1] >> 1)
				{
					continue;
				}
				Vector2 Out(int e)
				{
					int m = e >> 1;
					Vector3 d = (e & 1) == 0 ? plan.A[m] - plan.B[m] : plan.B[m] - plan.A[m];
					return new Vector2(d.x, d.z).normalized;
				}
				for (int k = 0; k < 2; k++)
				{
					int e = joint[k], other = joint[1 - k];
					float reach = MitreReach(modules[e >> 1].HalfWidth, Out(e), Out(other));
					if (modules[other >> 1].HalfWidth > 0f && reach > 0f)
					{
						if ((e & 1) == 0)
						{
							reachA[e >> 1] = reach;
						}
						else
						{
							reachB[e >> 1] = reach;
						}
					}
				}
			}
			for (int m = 0; m < n; m++)
			{
				if (reachA[m] > 0f || reachB[m] > 0f)
				{
					Vector3 d = (plan.B[m] - plan.A[m]).normalized;
					plan.A[m] -= d * reachA[m];
					plan.B[m] += d * reachB[m];
				}
			}
			return plan;
		}

		/// <summary>A point on the plan seated on a site's ground, sunk <see cref="SinkMetres"/>: a joint.</summary>
		public static Vector3 Joint(PointOfInterestSiteContext context, Vector2 p)
		{
			float g = context?.GroundAt != null ? context.GroundAt(p.x, p.y) : 0f;
			return new Vector3(p.x, g - SinkMetres, p.y);
		}

		/// <summary>
		/// The modules of one place's lines queued until their joints are known (<see cref="Resolve"/>), then laid with their closing
		/// posts by <see cref="Close"/>. One per enclosure or line laid together: a field's four sides, a site's yards (their runs meet
		/// at their corners and at their neighbours' sides), a lane's verge.
		/// </summary>
		public sealed class Joins
		{
			private struct Laying
			{
				public PointOfInterestSiteContext Context;
				public BiomeStructurePalette Palette;
				public GameObject Prefab, Closer;
				public float YScale, Tilt;
			}

			private readonly List<JoinModule> modules = new List<JoinModule>();
			private readonly List<Laying> laying = new List<Laying>();
			private readonly List<Vector3> posts = new List<Vector3>();

			/// <summary>The closing posts the last <see cref="Close"/> wanted but had no art for.</summary>
			public int MissingPosts { get; private set; }

			/// <summary>The modules queued and not yet laid.</summary>
			public int Count => modules.Count;

			/// <summary>
			/// Queues the kit piece <paramref name="id"/> (its <paramref name="variant"/>'s <paramref name="prefab"/>) from joint
			/// <paramref name="a"/> to <paramref name="b"/>, built <paramref name="module"/> long, its ground dipping <paramref name="dip"/>
			/// under its middle, scaled <paramref name="yScale"/> in y and leant <paramref name="tilt"/> degrees across its line; laid into
			/// <paramref name="context"/> in <paramref name="palette"/>.
			/// </summary>
			public void Module(PointOfInterestSiteContext context, BiomeStructurePalette palette, GameObject prefab, string id, int variant,
				Vector3 a, Vector3 b, float module, float dip, float yScale, float tilt)
			{
				int post = StructurePieces.LinearPostFor(id, variant);
				modules.Add(new JoinModule
				{
					A = a,
					B = b,
					Module = module,
					Dip = StructurePieces.LinearSolid(id) ? dip : 0f,
					HalfWidth = StructurePieces.LinearHalfWidth(id),
					PostAtStart = post >= 0,
					Closes = post >= 0,
				});
				laying.Add(new Laying
				{
					Context = context,
					Palette = palette,
					Prefab = prefab,
					Closer = post >= 0 ? StructureKit.Prefab(StructurePieces.LinearPost, post) : null,
					YScale = yScale,
					Tilt = tilt,
				});
			}

			/// <summary>Queues a gateway from joint <paramref name="a"/> to <paramref name="b"/> (built <paramref name="module"/> wide): it stands on its own posts, which close the runs either side.</summary>
			public void Gate(PointOfInterestSiteContext context, BiomeStructurePalette palette, GameObject prefab, Vector3 a, Vector3 b, float module)
			{
				Module(context, palette, prefab, "LinearGate", 0, a, b, module, 0f, 1f, 0f);
				posts.Add(a);
				posts.Add(b);
			}

			/// <summary>Lays everything queued with its closing posts, each in its own palette; returns the props laid. Empty after.</summary>
			public int Close()
			{
				JoinPlan plan = Resolve(modules, posts, JoinMetres);
				int laid = 0;
				MissingPosts = 0;
				for (int m = 0; m < modules.Count; m++)
				{
					Laying l = laying[m];
					if (l.Prefab == null || l.Context == null)
					{
						continue;
					}
					(Vector3 at, Quaternion rotation, float scale) = ModuleFrame(plan.A[m], plan.B[m], modules[m].Module);
					laid += Add(l, l.Prefab, at, rotation * Quaternion.Euler(l.Tilt, 0f, 0f), new Vector3(scale, l.YScale, 1f));
				}
				foreach (JoinCloser c in plan.Closers)
				{
					Laying l = laying[c.Module];
					if (l.Context == null)
					{
						continue;
					}
					if (l.Closer == null)
					{
						MissingPosts++;
						continue;
					}
					// Upright, its x out along the run past the end it closes.
					Quaternion upright = Quaternion.LookRotation(new Vector3(c.Out.x, 0f, c.Out.y), Vector3.up) * Quaternion.Euler(0f, -90f, 0f);
					laid += Add(l, l.Closer, c.At, upright, new Vector3(1f, l.YScale, 1f));
				}
				modules.Clear();
				laying.Clear();
				posts.Clear();
				return laid;
			}

			private static int Add(Laying l, GameObject prefab, Vector3 at, Quaternion rotation, Vector3 scale)
			{
				BiomeStructurePalette was = l.Context.Palette;
				l.Context.Palette = l.Palette;
				l.Context.AddProp(prefab, at, rotation, scale);
				l.Context.Palette = was;
				return 1;
			}
		}
	}
}
#endif
