#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A top-down view of the solar system at a moment: orbits, bodies, comet tails and belts.
	/// Distances use a square-root scale so inner and outer planets both fit; moon orbits are
	/// enlarged so they can be seen at all.
	/// </summary>
	public class OrreryView : VisualElement
	{
		private const float MoonRingPixels = 14f;

		private readonly List<(CelestialBody body, Vector2 point)> placed = new List<(CelestialBody, Vector2)>();
		private readonly List<Label> labels = new List<Label>();

		public SolarSystemProfile System { get; set; }
		public double Hours { get; set; }
		public CelestialBody Selected { get; set; }
		public WorldBody Observer { get; set; }
		public float Zoom { get; set; } = 1f;

		/// <summary>Raised when a body is clicked.</summary>
		public event Action<CelestialBody> BodyClicked;

		public OrreryView()
		{
			style.flexGrow = 1f;
			style.minHeight = 240f;
			style.backgroundColor = new Color(0.06f, 0.07f, 0.1f, 1f);
			style.overflow = Overflow.Hidden;
			generateVisualContent += Draw;
			RegisterCallback<PointerDownEvent>(OnPointerDown);
			RegisterCallback<GeometryChangedEvent>(_ => Refresh());
			RegisterCallback<WheelEvent>(evt =>
			{
				Zoom = Mathf.Clamp(Zoom * (evt.delta.y > 0 ? 0.9f : 1.1f), 0.2f, 20f);
				Refresh();
				evt.StopPropagation();
			});
		}

		private Vector2 layoutCentre;
		private float layoutScale;

		/// <summary>Recomputes body positions and labels, then repaints. Call after changing any property.</summary>
		public void Refresh()
		{
			placed.Clear();
			int labelIndex = 0;
			Rect rect = contentRect;
			if (System != null && rect.width >= 10f && rect.height >= 10f)
			{
				layoutCentre = rect.center;
				layoutScale = (Mathf.Min(rect.width, rect.height) * 0.45f) / (float)Math.Sqrt(MaxDistance()) * Zoom;
				foreach (CelestialBody body in System.Bodies)
				{
					if (body == null)
					{
						continue;
					}
					Vector2 p = PointOf(body, Hours, layoutCentre, layoutScale);
					placed.Add((body, p));
					if (!IsMoon(body) || body == Selected)
					{
						Label label = LabelAt(labelIndex++);
						label.text = body.ResolvedName;
						label.style.left = p.x + RadiusOf(body) + 3f;
						label.style.top = p.y - 8f;
						label.style.display = DisplayStyle.Flex;
					}
				}
			}
			for (int i = labelIndex; i < labels.Count; i++)
			{
				labels[i].style.display = DisplayStyle.None;
			}
			MarkDirtyRepaint();
		}

		private static float RadiusOf(CelestialBody body) => body is StarBody ? 7f : IsMoon(body) ? 2.5f : body is CometBody ? 2f : 4.5f;

		private double MaxDistance()
		{
			double max = 0.1;
			if (System == null)
			{
				return 1.0;
			}
			foreach (CelestialBody body in System.Bodies)
			{
				if (body == null || body is StarBody || IsMoon(body))
				{
					continue;
				}
				double apo = body.Orbit.Distance * (1.0 + Mathf.Clamp(body.Orbit.Eccentricity, 0f, 0.99f));
				max = Math.Max(max, body is CometBody ? Math.Min(apo, 40.0) : apo);
			}
			foreach (AsteroidBelt belt in System.AsteroidBelts)
			{
				if (belt != null)
				{
					max = Math.Max(max, Math.Max(belt.InnerAU, belt.OuterAU));
				}
			}
			return max;
		}

		private static bool IsMoon(CelestialBody body) => body.Parent != null && !(body.Parent is StarBody);

		private Vector2 ToScreen(Vector3d au, Vector2 centre, float scale)
		{
			double r = Math.Sqrt(au.X * au.X + au.Y * au.Y);
			if (r < 1e-9)
			{
				return centre;
			}
			double s = Math.Sqrt(r) * scale / r;
			return centre + new Vector2((float)(au.X * s), (float)(-au.Y * s));
		}

		private Vector2 PointOf(CelestialBody body, double hours, Vector2 centre, float scale)
		{
			if (IsMoon(body))
			{
				Vector2 parent = PointOf(body.Parent, hours, centre, scale);
				Vector3d offset = CelestialMath.Position(System, body, hours) - CelestialMath.Position(System, body.Parent, hours);
				double length = Math.Sqrt(offset.X * offset.X + offset.Y * offset.Y);
				float ring = MoonRingPixels + 6f * MoonIndex(body);
				return length < 1e-12 ? parent : parent + new Vector2((float)(offset.X / length) * ring, (float)(-offset.Y / length) * ring);
			}
			return ToScreen(CelestialMath.Position(System, body, hours), centre, scale);
		}

		private int MoonIndex(CelestialBody moon)
		{
			int index = 0;
			foreach (CelestialBody body in System.Bodies)
			{
				if (body == moon)
				{
					return index;
				}
				if (body != null && body.Parent == moon.Parent)
				{
					index++;
				}
			}
			return index;
		}

		private void Draw(MeshGenerationContext context)
		{
			if (System == null || placed.Count == 0)
			{
				return;
			}
			Vector2 centre = layoutCentre;
			float scale = layoutScale;
			Painter2D painter = context.painter2D;

			// Belts: a scatter of their asteroids.
			foreach (AsteroidBelt belt in System.AsteroidBelts)
			{
				if (belt == null)
				{
					continue;
				}
				int shown = Mathf.Min(belt.Count, 600);
				painter.fillColor = new Color(belt.Color.r, belt.Color.g, belt.Color.b, 0.55f);
				for (int i = 0; i < shown; i++)
				{
					Vector2 p = ToScreen(CelestialSky.AsteroidPosition(System, belt, i, Hours), centre, scale);
					painter.BeginPath();
					painter.Arc(p, 0.8f, 0f, 360f);
					painter.Fill();
				}
			}

			foreach ((CelestialBody body, Vector2 _) in placed)
			{
				if (!(body is StarBody) || body.Parent != null)
				{
					DrawOrbit(painter, body, centre, scale);
				}
			}

			foreach ((CelestialBody body, Vector2 p) in placed)
			{
				float radius = RadiusOf(body);
				if (body is CometBody comet)
				{
					Vector2 tip = ToScreen(CelestialSky.CometTailTip(System, comet, Hours), centre, scale);
					painter.strokeColor = new Color(comet.IonTailColor.r, comet.IonTailColor.g, comet.IonTailColor.b, 0.8f);
					painter.lineWidth = 1.5f;
					painter.BeginPath();
					painter.MoveTo(p);
					painter.LineTo(tip);
					painter.Stroke();
				}

				Color tint = body is StarBody star ? star.StarColor : body.Tint;
				tint.a = 1f;
				painter.fillColor = tint;
				painter.BeginPath();
				painter.Arc(p, radius, 0f, 360f);
				painter.Fill();
				if (body.HasRings)
				{
					painter.strokeColor = new Color(tint.r, tint.g, tint.b, 0.7f);
					painter.lineWidth = 1f;
					painter.BeginPath();
					painter.Arc(p, radius + 3f, 0f, 360f);
					painter.Stroke();
				}
				if (body == Selected || body == Observer)
				{
					painter.strokeColor = body == Selected ? new Color(1f, 0.85f, 0.3f, 1f) : new Color(0.5f, 0.9f, 1f, 1f);
					painter.lineWidth = 1.5f;
					painter.BeginPath();
					painter.Arc(p, radius + (body == Selected ? 5f : 8f), 0f, 360f);
					painter.Stroke();
				}
			}
		}

		private void DrawOrbit(Painter2D painter, CelestialBody body, Vector2 centre, float scale)
		{
			double period = CelestialMath.OrbitHours(System, body);
			if (double.IsInfinity(period) || period <= 0.0)
			{
				return;
			}
			bool moon = IsMoon(body);
			int steps = body is CometBody ? 256 : moon ? 48 : 96;
			Color c = body.Tint;
			painter.strokeColor = new Color(c.r, c.g, c.b, moon ? 0.25f : 0.35f);
			painter.lineWidth = 1f;
			painter.BeginPath();
			if (moon)
			{
				Vector2 parent = PointOf(body.Parent, Hours, centre, scale);
				painter.Arc(parent, MoonRingPixels + 6f * MoonIndex(body), 0f, 360f);
				painter.Stroke();
				return;
			}
			// Even steps of eccentric anomaly, not of time: an eccentric body spends almost all of
			// its orbit far out, and even time steps draw its close pass as a few straight chords.
			double e = Mathf.Clamp(body.Orbit.Eccentricity, 0f, 0.99f);
			double offset = body.Orbit.OffsetDegrees * CelestialMath.Deg2Rad;
			for (int i = 0; i <= steps; i++)
			{
				double eccentric = CelestialMath.TwoPi * i / steps;
				double mean = eccentric - e * Math.Sin(eccentric);
				double hours = (mean - offset) / CelestialMath.TwoPi * period;
				Vector2 p = ToScreen(CelestialMath.Position(System, body, hours), centre, scale);
				if (i == 0)
				{
					painter.MoveTo(p);
				}
				else
				{
					painter.LineTo(p);
				}
			}
			painter.Stroke();
		}

		private Label LabelAt(int index)
		{
			while (labels.Count <= index)
			{
				var label = new Label { pickingMode = PickingMode.Ignore };
				label.style.position = Position.Absolute;
				label.style.fontSize = 10;
				label.style.color = new Color(0.85f, 0.88f, 0.95f, 0.9f);
				label.style.paddingLeft = 0;
				label.style.paddingRight = 0;
				label.style.paddingTop = 0;
				label.style.paddingBottom = 0;
				label.style.marginLeft = 0;
				label.style.marginTop = 0;
				labels.Add(label);
				Add(label);
			}
			return labels[index];
		}

		private void OnPointerDown(PointerDownEvent evt)
		{
			Vector2 local = evt.localPosition;
			CelestialBody best = null;
			float bestDistance = 12f;
			foreach ((CelestialBody body, Vector2 point) in placed)
			{
				float d = Vector2.Distance(local, point);
				if (d < bestDistance)
				{
					bestDistance = d;
					best = body;
				}
			}
			if (best != null)
			{
				BodyClicked?.Invoke(best);
				evt.StopPropagation();
			}
		}
	}
}
#endif
