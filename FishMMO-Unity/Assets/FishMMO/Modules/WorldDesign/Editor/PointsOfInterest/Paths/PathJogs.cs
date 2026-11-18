#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Ways jogging where they cross a strike-slip trace (2026-10-26, phase I2l-b; the I2g-f follow-up of decision 90): the ground on the
	/// trace's left side has slipped along it (SceneLandforms' ShiftLines moves every landform line carved before it), so a way across
	/// the fault meets it, runs along it for the slip and leaves it again where its far side now lies. Pure.
	/// </summary>
	/// <remarks>
	/// The ways are planned after the carve, over the slipped ground, so nothing put them out of line: the jog is laid here, as the
	/// slip would have left a way older than it. A trace's slip fades to nothing a quarter of its length past each end, as the lines'
	/// does; a jog shorter than <see cref="MinJogMetres"/>, one that would cut through a site's own stretch of the way, or one whose new
	/// legs the ground will not carry (<paramref name="clear"/> in <see cref="Apply"/>) is not laid.
	/// </remarks>
	public static class PathJogs
	{
		/// <summary>The shortest slip a way jogs for, metres.</summary>
		public const float MinJogMetres = 3f;

		/// <summary>How far off the trace the jog's far leg steps before it rejoins the way, metres (so the way does not run in the scarp).</summary>
		public const float StepOffMetres = 2f;

		/// <summary>One strike-slip trace: its start, its unit direction, its length, and how far its left side slipped along it (metres, signed).</summary>
		public readonly struct Trace
		{
			public readonly Vector2 Start, Direction;
			public readonly float Length, Offset;

			public Trace(Vector2 start, Vector2 direction, float length, float offset)
			{
				Start = start;
				Direction = direction;
				Length = length;
				Offset = offset;
			}

			/// <summary>The slip at a distance along the trace: all of it between the ends, fading to nothing a quarter of its length past each (as the lines are moved).</summary>
			public float SlipAt(float along)
			{
				float fade = 0.25f * Length;
				if (fade <= 0f)
				{
					return 0f;
				}
				return Offset * Mathf.SmoothStep(0f, 1f, (along + fade) / fade) * (1f - Mathf.SmoothStep(0f, 1f, (along - Length) / fade));
			}

			/// <summary>How far a point lies into the side that slipped (the trace's left), metres; negative on the other side.</summary>
			public float SideOf(Vector2 p)
			{
				Vector2 rel = p - Start;
				return Direction.x * rel.y - Direction.y * rel.x;
			}

			/// <summary>True for a point on the side that slipped (the trace's left).</summary>
			public bool OnSlippedSide(Vector2 p) => SideOf(p) > 0f;
		}

		/// <summary>
		/// Lays the jogs of every trace a way's line crosses (in place): at each crossing whose slip is <see cref="MinJogMetres"/> or more,
		/// the way runs along the trace from the crossing for the slip, steps off it onto the slipped side and rejoins its own line where
		/// that lies as far again from the crossing; the points between are dropped. <paramref name="pinned"/> (as long as the line) marks
		/// points no jog may drop (a site's own stretch, a gorge's rims); it is kept in step. Returns how many jogs were laid.
		/// </summary>
		/// <param name="clear">True when the ground carries a way straight from one point to the other; null takes every leg as clear.</param>
		public static int Apply(List<Vector2> line, List<bool> pinned, IReadOnlyList<Trace> traces, Func<Vector2, Vector2, bool> clear)
		{
			if (line == null || line.Count < 2 || traces == null || traces.Count == 0)
			{
				return 0;
			}
			int laid = 0;
			foreach (Trace trace in traces)
			{
				if (trace.Length <= 0f)
				{
					continue;
				}
				// One jog a trace a way: a way that wanders back over the same fault keeps its later crossings as they are.
				for (int i = 0; i + 1 < line.Count; i++)
				{
					if (!Crosses(trace, line[i], line[i + 1], out Vector2 x, out float along))
					{
						continue;
					}
					float slip = trace.SlipAt(along);
					if (Mathf.Abs(slip) < MinJogMetres)
					{
						continue;
					}
					if (TryJog(line, pinned, trace, i, x, slip, clear))
					{
						laid++;
					}
					break;
				}
			}
			return laid;
		}

		/// <summary>True when segment a→b crosses the trace's line within the reach its slip fades over; the crossing and its distance along the trace.</summary>
		public static bool Crosses(in Trace trace, Vector2 a, Vector2 b, out Vector2 at, out float along)
		{
			at = default;
			along = 0f;
			Vector2 n = new Vector2(-trace.Direction.y, trace.Direction.x);
			float da = Vector2.Dot(a - trace.Start, n), db = Vector2.Dot(b - trace.Start, n);
			if (da * db > 0f || da == db)
			{
				return false;
			}
			float t = da / (da - db);
			at = Vector2.Lerp(a, b, t);
			along = Vector2.Dot(at - trace.Start, trace.Direction);
			float fade = 0.25f * trace.Length;
			return along > -fade && along < trace.Length + fade;
		}

		private static bool TryJog(List<Vector2> line, List<bool> pinned, in Trace trace, int segment, Vector2 x, float slip, Func<Vector2, Vector2, bool> clear)
		{
			Vector2 along = trace.Direction * slip;
			Vector2 left = new Vector2(-trace.Direction.y, trace.Direction.x);
			Vector2 corner = x + along;
			Vector2 stepped = corner + left * StepOffMetres;
			float reach = Mathf.Max(2f * Mathf.Abs(slip), 12f);
			// Whether the way goes on into the slipped side (else it comes from it): by which end of the crossed segment lies further into it,
			// as one end may lie on the trace itself.
			bool forward = trace.SideOf(line[segment + 1]) > trace.SideOf(line[segment]);
			// The way's own point on the slipped side as far from the crossing as the jog reaches; every point between is dropped.
			int keep = -1;
			float run = Vector2.Distance(x, line[forward ? segment + 1 : segment]);
			if (forward)
			{
				for (int k = segment + 1; k < line.Count; k++)
				{
					if (k > segment + 1)
					{
						run += Vector2.Distance(line[k - 1], line[k]);
					}
					if (pinned != null && k < pinned.Count && pinned[k] && run < reach)
					{
						return false;
					}
					if (run >= reach)
					{
						keep = k;
						break;
					}
				}
			}
			else
			{
				for (int k = segment; k >= 0; k--)
				{
					if (k < segment)
					{
						run += Vector2.Distance(line[k + 1], line[k]);
					}
					if (pinned != null && k < pinned.Count && pinned[k] && run < reach)
					{
						return false;
					}
					if (run >= reach)
					{
						keep = k;
						break;
					}
				}
			}
			if (keep < 0)
			{
				return false;
			}
			Vector2 rejoin = line[keep];
			if (clear != null && !(clear(x, corner) && clear(corner, stepped) && clear(stepped, rejoin)))
			{
				return false;
			}
			if (forward)
			{
				// … p[segment], the crossing, along the trace, off it, p[keep] …
				int from = segment + 1, count = keep - from;
				line.RemoveRange(from, count);
				line.InsertRange(from, new[] { x, corner, stepped });
				if (pinned != null)
				{
					pinned.RemoveRange(from, count);
					pinned.InsertRange(from, new[] { false, false, false });
				}
			}
			else
			{
				// … p[keep], off the trace, along it, the crossing, p[segment + 1] …
				int from = keep + 1, count = segment + 1 - from;
				line.RemoveRange(from, count);
				line.InsertRange(from, new[] { stepped, corner, x });
				if (pinned != null)
				{
					pinned.RemoveRange(from, count);
					pinned.InsertRange(from, new[] { false, false, false });
				}
			}
			return true;
		}
	}
}
#endif
