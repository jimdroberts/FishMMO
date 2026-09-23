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
		/// <summary>The observer's longitude, in degrees: its facing arrow shows where that place faces.</summary>
		public float ObserverLongitude { get; set; }
		public float Zoom { get; set; } = 1f;

		/// <summary>
		/// Draw the climate bands: a ring for every temperature a world could have at that distance,
		/// with the liquid-water band picked out. Off by default — it is an analysis overlay, not
		/// part of reading the orrery.
		/// </summary>
		public bool ShowHeatMap { get; set; }

		/// <summary>Draw the goldilocks annulus on its own, without the full heat map.</summary>
		public bool ShowGoldilocks { get; set; }

		/// <summary>Raised when a body is clicked.</summary>
		public event Action<CelestialBody> BodyClicked;

		public OrreryView()
		{
			style.flexGrow = 1f;
			style.minHeight = 240f;
			style.backgroundColor = new Color(0.06f, 0.07f, 0.1f, 1f);
			style.overflow = Overflow.Hidden;
			generateVisualContent += Draw;
			var key = new Label("➤ which way longitude 0 faces (the observer: its own longitude; toward the sun is noon)   ↺ spin")
			{
				pickingMode = PickingMode.Ignore,
			};
			key.style.position = Position.Absolute;
			key.style.left = 6f;
			key.style.bottom = 4f;
			key.style.fontSize = 10f;
			key.style.color = new Color(0.75f, 0.8f, 0.9f, 0.8f);
			Add(key);
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

			/* Under everything else, so the bodies and their orbits read on top of it — the bands are
			 * background, and a planet drawn beneath its own climate ring would be unreadable. */
			if (ShowHeatMap || ShowGoldilocks)
			{
				DrawClimateBands(painter, centre, scale);
			}

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
				// Anything that goes round something: a root star's orbit is its swing about the
				// centre of mass it shares with its companions, and a lone star has none.
				if (!double.IsInfinity(CelestialMath.OrbitHours(System, body)))
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
				if (body is WorldBody world && (!IsMoon(body) || body == Selected || body == Observer))
				{
					DrawFacing(painter, world, p, radius);
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

		/// <summary>
		/// Which way a body faces right now, seen from above: an arrow from its centre toward the
		/// direction longitude 0 faces (the observer's own longitude on the observer), and a short
		/// curved arrow for the way it spins. When the arrow points at the sun it is noon there.
		/// </summary>
		private void DrawFacing(Painter2D painter, WorldBody body, Vector2 p, float radius)
		{
			double tilt = body.AxialTiltDegrees * CelestialMath.Deg2Rad;
			bool observer = body == Observer;
			double theta = CelestialMath.RotationAngle(System, body, Hours) + (observer ? ObserverLongitude * CelestialMath.Deg2Rad : 0.0);
			// Where the meridian's plane (right ascension theta) cuts the orbital plane: the sun sits
			// on that line at local noon whatever the tilt. Then onto the screen (y down).
			// In the body's own frame that line is (cos θ · cos t, sin θ); the frame itself is turned
			// about the ecliptic's pole by how far the axis's lean is from +Y.
			double turn = (body.PoleLongitudeDegrees - WorldBody.DefaultPoleLongitudeDegrees) * CelestialMath.Deg2Rad;
			double flatX = Math.Cos(theta) * Math.Cos(tilt), flatY = Math.Sin(theta);
			var direction = new Vector2(
				(float)(flatX * Math.Cos(turn) - flatY * Math.Sin(turn)),
				-(float)(flatX * Math.Sin(turn) + flatY * Math.Cos(turn)));
			if (direction.sqrMagnitude < 1e-6f)
			{
				return;
			}
			direction.Normalize();
			Color colour = observer ? new Color(0.5f, 0.9f, 1f, 1f) : new Color(1f, 1f, 1f, 0.75f);
			float length = radius + (observer ? 16f : 10f);
			Vector2 start = p + direction * radius;
			Vector2 tip = p + direction * length;
			painter.strokeColor = colour;
			painter.fillColor = colour;
			painter.lineWidth = observer ? 1.8f : 1.2f;
			painter.BeginPath();
			painter.MoveTo(start);
			painter.LineTo(tip);
			painter.Stroke();
			var side = new Vector2(-direction.y, direction.x);
			painter.BeginPath();
			painter.MoveTo(tip + direction * 3f);
			painter.LineTo(tip - direction * 2f + side * 2.5f);
			painter.LineTo(tip - direction * 2f - side * 2.5f);
			painter.ClosePath();
			painter.Fill();

			// Spin: counter-clockwise from above for a prograde (or locked) body, clockwise for a retrograde one.
			float sense = body.Retrograde && !body.TidallyLocked ? -1f : 1f;
			float ring = radius + 3.5f;
			float startAngle = Mathf.Atan2(-direction.y, direction.x) + sense * 0.5f;
			const int steps = 10;
			float sweep = sense * 1.6f;
			Vector2 At(float a) => p + new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * ring;
			painter.lineWidth = 1f;
			painter.strokeColor = new Color(colour.r, colour.g, colour.b, colour.a * 0.7f);
			painter.BeginPath();
			painter.MoveTo(At(startAngle));
			for (int i = 1; i <= steps; i++)
			{
				painter.LineTo(At(startAngle + sweep * i / steps));
			}
			painter.Stroke();
			float endAngle = startAngle + sweep;
			Vector2 end = At(endAngle);
			Vector2 along = (end - At(endAngle - sense * 0.15f)).normalized;
			var across = new Vector2(-along.y, along.x);
			painter.fillColor = painter.strokeColor;
			painter.BeginPath();
			painter.MoveTo(end + along * 2.5f);
			painter.LineTo(end - along * 1.5f + across * 2f);
			painter.LineTo(end - along * 1.5f - across * 2f);
			painter.ClosePath();
			painter.Fill();
		}

		/// <summary>
		/// Rings of colour for the temperature a world would have at each distance, and the band
		/// where water is liquid.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>It asks the same question the biome resolver does.</b> The temperature at a radius is
		/// <see cref="CelestialMath.TemperatureAtDistance"/>, the same fourth-root-of-insolation the
		/// climate offsets use, so a ring's colour and a world's actual biomes cannot disagree. The
		/// goldilocks band is not a separate calculation either: it is exactly where that
		/// temperature falls inside the range <c>BiomeWorldConditions.HasLiquidWater</c> accepts.
		/// </para>
		/// <para>
		/// Drawn as filled arcs from the outside in, so each ring covers the one beyond it and the
		/// whole disc is painted with two hundred strokes rather than a texture. At √AU scale the
		/// inner rings are naturally wider on screen, which is where the temperature changes fastest.
		/// </para>
		/// </remarks>
		private void DrawClimateBands(Painter2D painter, Vector2 centre, float scale)
		{
			double outer = OutermostAu();
			if (outer <= 0.0)
			{
				return;
			}

			const int Rings = 200;
			for (int i = Rings; i >= 1; i--)
			{
				double au = outer * i / Rings;
				float radius = (float)(Math.Sqrt(au) * scale);
				if (radius < 1f)
				{
					continue;
				}
				float t = CelestialMath.TemperatureAtDistance(System, au);
				bool liquid = t > LiquidWaterMin && t < LiquidWaterMax;

				if (ShowHeatMap)
				{
					painter.fillColor = HeatColor(t, liquid);
				}
				else
				{
					// Goldilocks only: the band, and nothing else painted at all.
					if (!liquid)
					{
						continue;
					}
					painter.fillColor = new Color(0.30f, 0.75f, 0.45f, 0.20f);
				}
				painter.BeginPath();
				painter.Arc(centre, radius, 0f, 360f);
				painter.Fill();
			}

			// The band's edges, so where it starts and stops is readable rather than inferred.
			if (ShowGoldilocks || ShowHeatMap)
			{
				painter.strokeColor = new Color(0.45f, 0.95f, 0.60f, 0.85f);
				painter.lineWidth = 1.5f;
				foreach (double edge in new[] { EdgeAu(outer, LiquidWaterMax), EdgeAu(outer, LiquidWaterMin) })
				{
					if (edge <= 0.0)
					{
						continue;
					}
					float r = (float)(Math.Sqrt(edge) * scale);
					painter.BeginPath();
					painter.Arc(centre, r, 0f, 360f);
					painter.Stroke();
				}
			}
		}

		/// <summary>The same thresholds <c>BiomeWorldConditions.HasLiquidWater</c> uses.</summary>
		private const float LiquidWaterMin = -0.75f, LiquidWaterMax = 0.85f;

		/// <summary>Frozen blue through temperate green to scorched red; the liquid band brightened.</summary>
		private static Color HeatColor(float t, bool liquid)
		{
			Color cold = new Color(0.20f, 0.35f, 0.70f);
			Color mild = new Color(0.25f, 0.62f, 0.45f);
			Color hot = new Color(0.78f, 0.28f, 0.18f);
			Color c = t < 0f ? Color.Lerp(cold, mild, Mathf.InverseLerp(-1f, 0f, t))
							 : Color.Lerp(mild, hot, Mathf.InverseLerp(0f, 1f, t));
			// Faint, because bodies and orbits have to stay legible over it.
			c.a = liquid ? 0.34f : 0.20f;
			return c;
		}

		/// <summary>The outermost distance worth painting: the furthest body, with a little margin.</summary>
		private double OutermostAu()
		{
			double outer = 0.0;
			foreach (CelestialBody body in System.Bodies)
			{
				if (body == null || IsMoon(body))
				{
					continue;
				}
				Vector3d at = CelestialMath.Position(System, body, Hours);
				outer = Math.Max(outer, Math.Sqrt(at.X * at.X + at.Y * at.Y));
			}
			return outer * 1.08;
		}

		/// <summary>The distance at which the temperature crosses a value, by bisection.</summary>
		/// <remarks>
		/// Bisected rather than inverted because the temperature at a radius folds in every star in
		/// the system plus the home world's greenhouse reference; there is no closed form to invert,
		/// and forty steps costs nothing once per repaint.
		/// </remarks>
		private double EdgeAu(double outer, float temperature)
		{
			double lo = outer / 5000.0, hi = outer;
			if (CelestialMath.TemperatureAtDistance(System, lo) < temperature ||
				CelestialMath.TemperatureAtDistance(System, hi) > temperature)
			{
				// The whole disc is on one side of it: no edge to draw.
				return 0.0;
			}
			for (int i = 0; i < 40; i++)
			{
				double mid = (lo + hi) * 0.5;
				if (CelestialMath.TemperatureAtDistance(System, mid) > temperature)
				{
					lo = mid;
				}
				else
				{
					hi = mid;
				}
			}
			return (lo + hi) * 0.5;
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
			Color c = body is StarBody orbitingStar ? orbitingStar.StarColor : body.Tint;
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
