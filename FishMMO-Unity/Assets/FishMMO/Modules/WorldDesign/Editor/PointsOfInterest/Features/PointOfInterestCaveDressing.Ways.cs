#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The layout's built ways drawn out (2026-10-13: "rock bridges leading over crevices and to other deeper areas",
	/// "ladders and ropes"): every climb the planner laid becomes the scene's climbable (what the climbing movement, the
	/// server's NavMesh links and the NPCs read) and its ladder or rope; every crossing its rope walk or trestle. And the
	/// field probes the later passes share.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Ladders and ropes have no colliders</b> (the climbing runtime gives each its own volume): only the spans of a
	/// crossing are walked on, and their colliders are the deck the layout left open for them.
	/// </para>
	/// <para>
	/// <b>A rope hangs plumb</b> from the climb's top, whatever the line leans: a ladder may lean on a lip, a rope cannot.
	/// It is tied off to an iron at the lip's face, a short lead running from that to where it goes over.
	/// </para>
	/// </remarks>
	public sealed partial class PointOfInterestCaveDressing
	{
		private sealed partial class Dresser
		{
			// ── Climbs ────────────────────────────────────────────

			/// <summary>Every climb: its scene climbable (world frame) and its pieces.</summary>
			private void Climbs()
			{
				var random = new DeterministicRNG(unchecked(system.Seed * 0x3A7D + 0x51C3));
				if (context.Points != null)
				{
					context.Points.Climbables ??= new List<SceneClimbable>();
				}
				foreach (CaveClimb climb in system.Climbs)
				{
					context.Points?.Climbables.Add(new SceneClimbable
					{
						Kind = (SceneClimbableKind)(byte)climb.Kind,
						Bottom = system.Origin + climb.Bottom,
						Top = system.Origin + climb.Top,
						Out = climb.Out,
						TopExit = climb.TopExit,
						HalfWidth = climb.Kind == CaveClimbKind.Rope ? 0.12f : 0.3f,
					});
					Climbed++;
					// A mine's ladders are its carpenters' (sawn stiles, pegged rungs); a cave's an explorer's (lashed poles).
					int variant = climb.Kind == CaveClimbKind.Ladder ? (system.Form == CaveForm.Mine ? 0 : 1) : random.Range(0, 2);
					if (climb.Kind == CaveClimbKind.Rope)
					{
						Rope(climb, variant);
					}
					else
					{
						Ladder(climb, variant);
					}
				}
			}

			/// <summary>
			/// A ladder's modules up the climb's line, leant as it leans: up along the line, the back (+z) toward the wall
			/// (−Out, squared to the line), the climber on −z where the climb's Out puts them.
			/// </summary>
			private void Ladder(in CaveClimb climb, int variant)
			{
				Vector3 rise = climb.Top - climb.Bottom;
				float length = rise.magnitude;
				if (length < 0.5f)
				{
					return;
				}
				Vector3 up = rise / length;
				Vector3 wall = climb.Out.sqrMagnitude > 1e-4f ? -climb.Out : climb.TopExit;
				Vector3 back = wall - up * Vector3.Dot(wall, up);
				if (back.sqrMagnitude < 1e-4f)
				{
					back = Vector3.Cross(Vector3.right, up);
				}
				Quaternion turn = Quaternion.LookRotation(back.normalized, up);
				foreach ((string id, float foot) in CaveArt.LadderStack(length, climb.Kind == CaveClimbKind.RopeLadder))
				{
					Lay(id, variant, climb.Bottom + up * foot, turn, Vector3.one);
				}
			}

			/// <summary>
			/// A knotted rope hung plumb from the climb's top to its foot, and its anchor: an iron in the lip's face under the
			/// top (its +z into the rock), a lead from it to where the rope goes over; at the top itself where no face is near.
			/// </summary>
			private void Rope(in CaveClimb climb, int variant)
			{
				float length = climb.Top.y - climb.Bottom.y;
				if (length < 0.5f)
				{
					return;
				}
				Vector3 wall = climb.Out.sqrMagnitude > 1e-4f ? -climb.Out : climb.TopExit;
				wall.y = 0f;
				wall = wall.sqrMagnitude > 1e-4f ? wall.normalized : Vector3.forward;
				Quaternion turn = Quaternion.LookRotation(wall, Vector3.up);
				foreach ((float top, float scaleY) in CaveArt.ClimbRopeStack(length))
				{
					Lay(CaveArt.ClimbRope, variant, climb.Top + Vector3.up * top, turn, new Vector3(1f, scaleY, 1f));
				}
				Vector3 from = climb.Top - Vector3.up * 0.15f;
				if (system.Void(from) < 0f && Surface(from, wall, 1.5f, out Vector3 face))
				{
					Lay(CaveArt.RopeAnchor, variant, face, turn, Vector3.one);
					// The lead from the anchor's ring to the rope's top: a rope module laid along it, hanging −y that way.
					Vector3 ring = face + turn * new Vector3(0f, 0.03f, -0.05f);
					Vector3 lead = climb.Top - ring;
					if (lead.magnitude > 0.1f)
					{
						Vector3 down = lead.normalized;
						Vector3 forward = wall - down * Vector3.Dot(wall, down);
						forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.Cross(Vector3.right, down);
						Lay(CaveArt.ClimbRope, variant, ring, Quaternion.LookRotation(forward, -down), new Vector3(1f, lead.magnitude / CaveArt.ClimbRopeModule, 1f));
					}
				}
				else
				{
					Lay(CaveArt.RopeAnchor, variant, climb.Top, turn, Vector3.one);
				}
			}

			// ── Crossings ─────────────────────────────────────────

			/// <summary>Every crossing: a rope walk between its two ends, or a trestle on bents down to the floor under it.</summary>
			private void Crossings()
			{
				var random = new DeterministicRNG(unchecked(system.Seed * 0x4C1F + 0x2E95));
				foreach (CaveCrossing crossing in system.Crossings)
				{
					Vector3 span = crossing.B - crossing.A;
					var flat = new Vector3(span.x, 0f, span.z);
					if (flat.magnitude < 1f)
					{
						continue;
					}
					flat.Normalize();
					Crossed++;
					int n = Mathf.Max(1, Mathf.CeilToInt(span.magnitude / CaveArt.SpanModule));
					if (crossing.Kind == CaveCrossingKind.Rope)
					{
						RopeWalk(crossing, flat, n, random);
					}
					else
					{
						Trestle(crossing, flat, n);
					}
				}
			}

			/// <summary>
			/// A rope walk: its ends' posts on each ledge, facing each other, and its spans hung between them on a shallow sag
			/// (a few centimetres a metre, as a walk drawn tight sags), each span pitched from joint to joint.
			/// </summary>
			private void RopeWalk(in CaveCrossing crossing, Vector3 flat, int n, DeterministicRNG random)
			{
				int variant = random.Range(0, 2);
				Vector3 a = crossing.A, b = crossing.B;
				float sag = Mathf.Min(0.35f, 0.03f * Vector3.Distance(a, b));
				Vector3 At(int i)
				{
					float f = i / (float)n;
					return Vector3.Lerp(a, b, f) - Vector3.up * (sag * 4f * f * (1f - f));
				}
				Lay(CaveArt.RopeBridgeEnd, variant, a, Quaternion.LookRotation(flat, Vector3.up), Vector3.one);
				Lay(CaveArt.RopeBridgeEnd, variant, b, Quaternion.LookRotation(-flat, Vector3.up), Vector3.one);
				for (int i = 0; i < n; i++)
				{
					Vector3 p0 = At(i), p1 = At(i + 1);
					Vector3 d = p1 - p0;
					// Now and then a worn span, a plank gone.
					int worn = random.NextFloat() < 0.2f ? 1 : 0;
					Lay(CaveArt.RopeBridgeSpan, worn, 0.5f * (p0 + p1), Quaternion.LookRotation(d, Vector3.up), new Vector3(1f, 1f, d.magnitude / CaveArt.SpanModule));
				}
			}

			/// <summary>
			/// A mine's trestle: its spans end to end from one ledge to the other, a bent under each joint scaled down to the
			/// floor the field has under it (none where the deck rests on its ledge).
			/// </summary>
			private void Trestle(in CaveCrossing crossing, Vector3 flat, int n)
			{
				Vector3 a = crossing.A, b = crossing.B, span = b - a;
				float stretch = span.magnitude / (n * CaveArt.SpanModule);
				Quaternion deck = Quaternion.LookRotation(span, Vector3.up);
				for (int i = 0; i < n; i++)
				{
					Lay(CaveArt.TrestleSpan, 0, Vector3.Lerp(a, b, (i + 0.5f) / n), deck, new Vector3(1f, 1f, stretch));
				}
				Quaternion upright = Quaternion.LookRotation(flat, Vector3.up);
				for (int j = 0; j <= n; j++)
				{
					Vector3 joint = Vector3.Lerp(a, b, j / (float)n);
					Vector3 under = joint - Vector3.up * 0.3f;
					if (system.Void(under) >= 0f || !Ground(under, 60f, out float floor))
					{
						continue;
					}
					float depth = joint.y - floor;
					if (depth < 0.6f)
					{
						continue;
					}
					int variant = depth > 4f ? 0 : 1;
					Lay(CaveArt.TrestleBent, variant, joint, upright, new Vector3(1f, CaveArt.TrestleBentScale(depth, variant), 1f));
				}
			}

			// ── Probes ────────────────────────────────────────────

			/// <summary>
			/// The floor under a point in the void (system frame), marched down the field and bisected to a few centimetres;
			/// false where the point is rock or no floor is within <paramref name="reach"/>.
			/// </summary>
			private bool Ground(Vector3 p, float reach, out float floor)
			{
				floor = p.y;
				if (system.Void(p) >= 0f)
				{
					return false;
				}
				const float step = 0.2f;
				float y = p.y;
				while (system.Void(new Vector3(p.x, y - step, p.z)) < 0f)
				{
					y -= step;
					if (y < p.y - reach)
					{
						return false;
					}
				}
				float lo = y - step, hi = y;
				for (int k = 0; k < 4; k++)
				{
					float mid = 0.5f * (lo + hi);
					if (system.Void(new Vector3(p.x, mid, p.z)) < 0f)
					{
						hi = mid;
					}
					else
					{
						lo = mid;
					}
				}
				floor = 0.5f * (lo + hi);
				return true;
			}

			/// <summary>The roof over a point in the void (system frame), as <see cref="Ground"/> marches the floor.</summary>
			private bool Roof(Vector3 p, float reach, out float roof)
			{
				roof = p.y;
				if (system.Void(p) >= 0f)
				{
					return false;
				}
				const float step = 0.2f;
				float y = p.y;
				while (system.Void(new Vector3(p.x, y + step, p.z)) < 0f)
				{
					y += step;
					if (y > p.y + reach)
					{
						return false;
					}
				}
				float lo = y, hi = y + step;
				for (int k = 0; k < 4; k++)
				{
					float mid = 0.5f * (lo + hi);
					if (system.Void(new Vector3(p.x, mid, p.z)) < 0f)
					{
						lo = mid;
					}
					else
					{
						hi = mid;
					}
				}
				roof = 0.5f * (lo + hi);
				return true;
			}

			/// <summary>Where a ray from a point in the void meets the rock (system frame), bisected; false within no reach.</summary>
			private bool Surface(Vector3 from, Vector3 dir, float reach, out Vector3 surface)
			{
				surface = from;
				const float step = 0.2f;
				Vector3 last = from;
				for (float d = step; d <= reach; d += step)
				{
					Vector3 p = from + dir * d;
					if (system.Void(p) >= 0f)
					{
						Vector3 lo = last, hi = p;
						for (int k = 0; k < 5; k++)
						{
							Vector3 mid = 0.5f * (lo + hi);
							if (system.Void(mid) < 0f)
							{
								lo = mid;
							}
							else
							{
								hi = mid;
							}
						}
						surface = 0.5f * (lo + hi);
						return true;
					}
					last = p;
				}
				return false;
			}

			/// <summary>
			/// A wall met by a ray from a point in the void: the surface and the way into the rock there (the field's gradient,
			/// so a piece lies flat on the rock and not square to the ray); false where it meets none, or meets a floor or roof.
			/// </summary>
			private bool Wall(Vector3 from, Vector3 dir, float reach, out Vector3 surface, out Vector3 into)
			{
				into = dir;
				if (system.Void(from) >= 0f || !Surface(from, dir, reach, out surface))
				{
					surface = from;
					return false;
				}
				const float e = 0.15f;
				var g = new Vector3(
					system.Void(surface + Vector3.right * e) - system.Void(surface - Vector3.right * e),
					system.Void(surface + Vector3.up * e) - system.Void(surface - Vector3.up * e),
					system.Void(surface + Vector3.forward * e) - system.Void(surface - Vector3.forward * e));
				if (g.sqrMagnitude < 1e-8f)
				{
					return false;
				}
				into = g.normalized;
				return Mathf.Abs(into.y) < 0.7f;
			}

			/// <summary>Under a grotto's pool or a sea cave's tide.</summary>
			private bool Submerged(Vector3 p) => !float.IsNaN(system.WaterLevel) && system.Origin.y + p.y < system.WaterLevel + 0.05f;

			/// <summary>
			/// Whether a spot on a floor may take a piece: off every lane, feature and way across a hall (by
			/// <paramref name="margin"/>), out of each site's camp ground, away from the mouths' daylight, out of the water.
			/// </summary>
			private bool Clear(Vector3 at, float margin, int exceptSolid = -1)
				=> !InLane(at, margin, exceptSolid) && !OnWay(at, margin) && !NearMouth(at) && !Submerged(at);

			/// <summary>A structure piece laid (system frame); its id noted as missing until the art is generated.</summary>
			private bool Lay(string id, int variant, Vector3 local, Quaternion rotation, Vector3 scale)
			{
				GameObject prefab = StructureKit.Prefab(id, variant);
				if (prefab == null)
				{
					Missing.Add(id);
					return false;
				}
				return Place(prefab, local, rotation, scale);
			}

			private static Vector3 Flat(Vector3 v)
			{
				v.y = 0f;
				return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
			}

			/// <summary>Whether a point lies inside a chamber's footprint (its ellipse in plan), grown by <paramref name="grow"/>.</summary>
			private static bool InFootprint(in CaveChamber c, Vector3 p, float grow)
			{
				float r = c.Yaw * Mathf.Deg2Rad;
				float dx = p.x - c.Centre.x, dz = p.z - c.Centre.z;
				float u = (dx * Mathf.Sin(r) + dz * Mathf.Cos(r)) / Mathf.Max(0.5f, c.Radii.x + grow);
				float v = (dx * Mathf.Cos(r) - dz * Mathf.Sin(r)) / Mathf.Max(0.5f, c.Radii.z + grow);
				return u * u + v * v <= 1f;
			}

			/// <summary>A point in a chamber's plan at a share <paramref name="t"/> of the way out along heading <paramref name="angle"/> (radians), on its floor.</summary>
			private static Vector3 InChamber(in CaveChamber c, float angle, float t)
			{
				float r = c.Yaw * Mathf.Deg2Rad;
				var along = new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
				var across = new Vector3(along.z, 0f, -along.x);
				Vector3 p = c.Centre + along * (Mathf.Cos(angle) * t * c.Radii.x) + across * (Mathf.Sin(angle) * t * c.Radii.z);
				p.y = CaveSystem.FloorOf(c, p);
				return p;
			}

			/// <summary>Whether a chamber is one site's own (its camp's or den's), by its middle.</summary>
			private bool IsOwn(in CaveChamber c)
			{
				foreach (CaveMouth mouth in system.Mouths)
				{
					if (system.TryChamberOf(mouth.SiteId, out CaveChamber own) && (own.Centre - c.Centre).sqrMagnitude < 0.01f)
					{
						return true;
					}
				}
				return false;
			}
		}
	}
}
#endif
