#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The sky of a place, looking straight up: zenith in the middle, the horizon at the rim,
	/// north at the top and east on the left, as on a star chart.
	/// </summary>
	/// <remarks>
	/// Computed from the same celestial code the game uses: sun and sky colour from the sun's
	/// altitude (a black sky without air), bodies at their true positions, sizes and phases,
	/// textured once they pass the profile's sky share, comet tails away from the star, active
	/// shower radiants and a seeded star field that turns with the body. It previews positions and
	/// timing; the look of the final sky comes from the sky shader.
	/// </remarks>
	public class SkyPreviewView : VisualElement
	{
		private const int StarCount = 700;
		/// <summary>
		/// The surface to draw a body with: a hand-made one, else the baked one off disk.
		/// </summary>
		/// <remarks>
		/// The bake no longer writes onto <see cref="CelestialBody.SurfaceTexture"/> — it is build
		/// output and a committed asset must not point at it — so a preview that reads only that
		/// field shows every generated world as a blank disc. Read straight off disk here, which
		/// the editor can always do and which is current the instant a bake finishes.
		/// </remarks>
		private static Texture2D SurfaceOf(CelestialBody body)
		{
			if (body == null)
			{
				return null;
			}
			if (body.SurfaceTexture != null)
			{
				return body.SurfaceTexture;
			}
			return body is WorldBody world ? PlanetSurfaceBaker.Baked(world) : null;
		}

		private static readonly Vector3[] stars = BuildStars();

		private readonly List<SkyBodyView> views = new List<SkyBodyView>();
		/// <summary>Draws the bodies above the textured images; this element draws the sky beneath them.</summary>
		private readonly VisualElement foreground = new VisualElement { pickingMode = PickingMode.Ignore };
		private Color lastSky;
		private readonly List<Image> textured = new List<Image>();
		private readonly List<Label> labels = new List<Label>();

		public SolarSystemProfile System { get; set; }
		public WorldBody Observer { get; set; }
		public double Hours { get; set; }
		public double Latitude { get; set; }
		public double Longitude { get; set; }
		public bool ShowLabels { get; set; } = true;

		/// <summary>The bodies computed for the last refresh.</summary>
		public IReadOnlyList<SkyBodyView> Views => views;

		public SkyPreviewView()
		{
			style.flexGrow = 1f;
			style.minHeight = 240f;
			style.backgroundColor = new Color(0.04f, 0.04f, 0.05f, 1f);
			style.overflow = Overflow.Hidden;
			generateVisualContent += Draw;
			foreground.style.position = Position.Absolute;
			foreground.style.left = 0;
			foreground.style.top = 0;
			foreground.style.right = 0;
			foreground.style.bottom = 0;
			foreground.generateVisualContent += DrawForeground;
			Add(foreground);
			RegisterCallback<GeometryChangedEvent>(_ => Refresh());
		}

		private static Vector3[] BuildStars()
		{
			var result = new Vector3[StarCount];
			var random = new global::System.Random(20260917);
			for (int i = 0; i < StarCount; i++)
			{
				double ra = random.NextDouble() * Math.PI * 2.0;
				double dec = Math.Asin(random.NextDouble() * 2.0 - 1.0);
				double magnitude = Math.Pow(random.NextDouble(), 3.0);
				result[i] = new Vector3((float)ra, (float)dec, (float)magnitude);
			}
			return result;
		}

		private float Radius => Mathf.Max(10f, Mathf.Min(contentRect.width, contentRect.height) * 0.47f);

		private Vector2 Centre => contentRect.center;

		/// <summary>Screen point of an altitude/azimuth, in the element's space.</summary>
		public Vector2 ToScreen(double altitude, double azimuth)
		{
			double r = (90.0 - altitude) / 90.0 * Radius;
			double a = azimuth * CelestialMath.Deg2Rad;
			return Centre + new Vector2((float)(-r * Math.Sin(a)), (float)(-r * Math.Cos(a)));
		}

		private float PixelsPerDegree => Radius / 90f;

		/// <summary>Recomputes the sky and repaints.</summary>
		public void Refresh()
		{
			views.Clear();
			if (System != null && Observer != null)
			{
				CelestialSky.Bodies(System, Observer, Hours, Latitude, Longitude, views);
			}

			int imageIndex = 0;
			int labelIndex = 0;
			foreach (SkyBodyView view in views)
			{
				if (!view.AboveHorizon)
				{
					continue;
				}
				Vector2 p = ToScreen(view.Altitude, view.Azimuth);
				float size = BodyPixels(view);
				Texture2D surface = SurfaceOf(view.Body);
				if (view.Textured && surface != null)
				{
					Image image = ImageAt(imageIndex++);
					image.image = surface;
					image.style.left = p.x - size;
					image.style.top = p.y - size;
					image.style.width = size * 2f;
					image.style.height = size * 2f;
					image.style.display = DisplayStyle.Flex;
				}
				if (ShowLabels && (view.Body is StarBody || size >= 2.5f || view.Body is CometBody))
				{
					Label label = LabelAt(labelIndex++);
					label.text = view.Body.ResolvedName;
					label.style.left = p.x + size + 3f;
					label.style.top = p.y - 7f;
					label.style.display = DisplayStyle.Flex;
				}
			}
			for (int i = imageIndex; i < textured.Count; i++)
			{
				textured[i].style.display = DisplayStyle.None;
			}
			for (int i = labelIndex; i < labels.Count; i++)
			{
				labels[i].style.display = DisplayStyle.None;
			}
			MarkDirtyRepaint();
			foreground.MarkDirtyRepaint();
		}

		private float BodyPixels(SkyBodyView view)
		{
			float radius = (float)(view.AngularDiameterDegrees * 0.5) * PixelsPerDegree;
			return Mathf.Max(view.Body is StarBody ? 4f : 1.6f, radius);
		}

		private double SunAltitude()
		{
			double best = -90.0;
			foreach (SkyBodyView view in views)
			{
				if (view.Body is StarBody)
				{
					best = Math.Max(best, view.Altitude);
				}
			}
			return best;
		}

		private Color SkyColour(double sunAltitude)
		{
			if (Observer != null && Observer.Atmosphere == AtmosphereKind.None)
			{
				return new Color(0.01f, 0.01f, 0.02f, 1f);
			}
			float thickness = Observer != null && Observer.Atmosphere == AtmosphereKind.Thin ? 0.55f : Observer != null && Observer.Atmosphere == AtmosphereKind.Thick ? 1.25f : 1f;
			var night = new Color(0.02f, 0.03f, 0.07f, 1f);
			var twilight = new Color(0.32f, 0.26f, 0.38f, 1f);
			var day = new Color(0.36f, 0.58f, 0.9f, 1f);
			Color c;
			if (sunAltitude <= -12.0)
			{
				c = night;
			}
			else if (sunAltitude <= 0.0)
			{
				c = Color.Lerp(night, twilight, (float)((sunAltitude + 12.0) / 12.0));
			}
			else
			{
				c = Color.Lerp(twilight, day, Mathf.Clamp01((float)(sunAltitude / 15.0)));
			}
			c = Color.Lerp(night, c, Mathf.Clamp01(thickness));
			// The sun's colour tints the day sky, as it does in the game (SkySystem.TintBySuns).
			if (System != null && System.PrimaryStar is StarBody star)
			{
				Color tint = Color.Lerp(Color.white, star.SkyTint, Mathf.Clamp01((float)((sunAltitude + 6.0) / 12.0)));
				c = new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, 1f);
			}
			return c;
		}

		private void Draw(MeshGenerationContext context)
		{
			if (System == null || Observer == null)
			{
				return;
			}
			Painter2D painter = context.painter2D;
			Vector2 centre = Centre;
			float radius = Radius;
			double sunAltitude = SunAltitude();
			Color sky = SkyColour(sunAltitude);

			painter.fillColor = sky;
			painter.BeginPath();
			painter.Arc(centre, radius, 0f, 360f);
			painter.Fill();

			// Horizon glow toward the sun.
			if (Observer.Atmosphere != AtmosphereKind.None && sunAltitude > -12.0)
			{
				foreach (SkyBodyView view in views)
				{
					if (!(view.Body is StarBody))
					{
						continue;
					}
					Vector2 edge = ToScreen(Math.Max(0.0, view.Altitude), view.Azimuth);
					float strength = Mathf.Clamp01(1f - Mathf.Abs((float)view.Altitude) / 12f) * 0.5f;
					for (int ring = 6; ring >= 1; ring--)
					{
						painter.fillColor = new Color(1f, 0.55f, 0.25f, strength / 6f);
						painter.BeginPath();
						painter.Arc(edge, radius * 0.08f * ring, 0f, 360f);
						painter.Fill();
					}
				}
			}

			DrawStars(painter, sunAltitude);
			DrawGrid(painter, centre, radius);
			DrawShowers(painter, sunAltitude);
			lastSky = sky;

			// Horizon rim.
			painter.strokeColor = new Color(0.6f, 0.65f, 0.7f, 0.8f);
			painter.lineWidth = 1.5f;
			painter.BeginPath();
			painter.Arc(centre, radius, 0f, 360f);
			painter.Stroke();
		}

		private void DrawForeground(MeshGenerationContext context)
		{
			if (System == null || Observer == null)
			{
				return;
			}
			foreach (SkyBodyView view in views)
			{
				if (view.AboveHorizon)
				{
					DrawBody(context.painter2D, view, lastSky.a > 0f ? lastSky : SkyColour(SunAltitude()));
				}
			}
		}

		private void DrawStars(Painter2D painter, double sunAltitude)
		{
			float visibility = Observer.Atmosphere == AtmosphereKind.None ? 1f : Mathf.Clamp01((float)(-sunAltitude - 3.0) / 9.0f);
			if (visibility <= 0f)
			{
				return;
			}
			double meridian = CelestialSky.MeridianRightAscension(System, Observer, Hours, Longitude);
			for (int i = 0; i < stars.Length; i++)
			{
				Vector3 s = stars[i];
				double hourAngle = CelestialMath.WrapPi(meridian - s.x);
				CelestialSky.Horizontal(Latitude, s.y, hourAngle, out double alt, out double az);
				if (alt <= 0.0)
				{
					continue;
				}
				float brightness = (0.35f + 0.65f * s.z) * visibility;
				painter.fillColor = new Color(0.9f, 0.92f, 1f, brightness);
				painter.BeginPath();
				painter.Arc(ToScreen(alt, az), 0.5f + s.z * 1.1f, 0f, 360f);
				painter.Fill();
			}
		}

		private static void DrawGrid(Painter2D painter, Vector2 centre, float radius)
		{
			painter.strokeColor = new Color(1f, 1f, 1f, 0.07f);
			painter.lineWidth = 1f;
			for (int alt = 30; alt < 90; alt += 30)
			{
				painter.BeginPath();
				painter.Arc(centre, radius * (90f - alt) / 90f, 0f, 360f);
				painter.Stroke();
			}
			painter.BeginPath();
			painter.MoveTo(centre + new Vector2(0f, -radius));
			painter.LineTo(centre + new Vector2(0f, radius));
			painter.MoveTo(centre + new Vector2(-radius, 0f));
			painter.LineTo(centre + new Vector2(radius, 0f));
			painter.Stroke();
		}

		private void DrawShowers(Painter2D painter, double sunAltitude)
		{
			if (Observer.Atmosphere == AtmosphereKind.None || sunAltitude > -6.0 || System.MeteorShowers == null)
			{
				return;
			}
			double day = Hours / CelestialMath.HomeSolarDayHours(System);
			double dayOfYear = day - Math.Floor(day / System.DaysPerYear) * System.DaysPerYear;
			double meridian = CelestialSky.MeridianRightAscension(System, Observer, Hours, Longitude);
			foreach (MeteorShower shower in System.MeteorShowers)
			{
				if (shower == null)
				{
					continue;
				}
				float rate = shower.RateOn(dayOfYear, System.DaysPerYear);
				if (rate <= 0f)
				{
					continue;
				}
				double hourAngle = CelestialMath.WrapPi(meridian - shower.RadiantRightAscension * CelestialMath.Deg2Rad);
				CelestialSky.Horizontal(Latitude, shower.RadiantDeclination * CelestialMath.Deg2Rad, hourAngle, out double alt, out double az);
				if (alt <= 0.0)
				{
					continue;
				}
				Vector2 p = ToScreen(alt, az);
				float alpha = Mathf.Clamp01(rate / Mathf.Max(1f, shower.PeakPerHour));
				painter.strokeColor = new Color(shower.Color.r, shower.Color.g, shower.Color.b, 0.35f + 0.5f * alpha);
				painter.lineWidth = 1f;
				int streaks = Mathf.Clamp(Mathf.RoundToInt(rate / 10f), 2, 12);
				for (int k = 0; k < streaks; k++)
				{
					float angle = k * Mathf.PI * 2f / streaks + 0.3f;
					var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
					painter.BeginPath();
					painter.MoveTo(p + dir * 6f);
					painter.LineTo(p + dir * (14f + 10f * alpha));
					painter.Stroke();
				}
			}
		}

		private void DrawBody(Painter2D painter, SkyBodyView view, Color sky)
		{
			Vector2 p = ToScreen(view.Altitude, view.Azimuth);
			float size = BodyPixels(view);
			CelestialBody body = view.Body;

			if (body is StarBody star)
			{
				Color glow = star.StarColor;
				for (int ring = 4; ring >= 1; ring--)
				{
					painter.fillColor = new Color(glow.r, glow.g, glow.b, 0.12f);
					painter.BeginPath();
					painter.Arc(p, size * (1f + ring * 0.8f), 0f, 360f);
					painter.Fill();
				}
				painter.fillColor = Color.Lerp(glow, Color.white, 0.5f);
				painter.BeginPath();
				painter.Arc(p, size, 0f, 360f);
				painter.Fill();
				return;
			}

			if (body is CometBody comet)
			{
				CelestialSky.HorizontalOf(System, Observer, CelestialSky.CometTailTip(System, comet, Hours), Hours, Latitude, Longitude, out double tipAlt, out double tipAz);
				Vector2 tip = ToScreen(tipAlt, tipAz);
				float brightness = Mathf.Clamp01((float)CelestialSky.CometBrightness(System, comet, Hours));
				painter.strokeColor = new Color(comet.IonTailColor.r, comet.IonTailColor.g, comet.IonTailColor.b, 0.25f + 0.6f * brightness);
				painter.lineWidth = 2f;
				painter.BeginPath();
				painter.MoveTo(p);
				painter.LineTo(tip);
				painter.Stroke();
				painter.fillColor = new Color(1f, 1f, 1f, 0.4f + 0.6f * brightness);
				painter.BeginPath();
				painter.Arc(p, 2f, 0f, 360f);
				painter.Fill();
				return;
			}

			if (view.Textured && SurfaceOf(body) != null)
			{
				// The image child draws the surface; shade the night side over it below.
			}
			else
			{
				Color tint = body.Tint;
				tint.a = 1f;
				painter.fillColor = tint;
				painter.BeginPath();
				painter.Arc(p, size, 0f, 360f);
				painter.Fill();
			}

			// Phase: darken the unlit part. The terminator is drawn as an ellipse whose width
			// follows the illumination; the lit side faces the star.
			double illumination = view.Illumination;
			if (illumination < 0.98 && size >= 2f)
			{
				Vector2 sunward = SunScreenDirection(p);
				var shade = new Color(sky.r * 0.35f, sky.g * 0.35f, sky.b * 0.35f, 0.92f);
				DrawShadow(painter, p, size, sunward, (float)illumination, shade);
			}

			if (body.HasRings)
			{
				painter.strokeColor = new Color(body.Tint.r, body.Tint.g, body.Tint.b, 0.8f);
				painter.lineWidth = Mathf.Max(1f, size * 0.12f);
				painter.BeginPath();
				for (int i = 0; i <= 40; i++)
				{
					float t = Mathf.PI * 2f * i / 40;
					var point = p + new Vector2(Mathf.Cos(t) * size * 2.1f, Mathf.Sin(t) * size * 0.55f);
					if (i == 0)
					{
						painter.MoveTo(point);
					}
					else
					{
						painter.LineTo(point);
					}
				}
				painter.Stroke();
			}
		}

		private Vector2 SunScreenDirection(Vector2 from)
		{
			foreach (SkyBodyView view in views)
			{
				if (view.Body == System.PrimaryStar)
				{
					Vector2 to = ToScreen(Math.Max(-60.0, view.Altitude), view.Azimuth);
					Vector2 d = to - from;
					return d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.up;
				}
			}
			return Vector2.up;
		}

		/// <summary>Covers the dark part of a disc: a half disc away from the star plus a terminator ellipse.</summary>
		private static void DrawShadow(Painter2D painter, Vector2 centre, float radius, Vector2 sunward, float illumination, Color shade)
		{
			// k: +1 new (all dark) … −1 full (all lit). The terminator's half-width is |k| × radius.
			float k = 1f - 2f * illumination;
			var away = -sunward;
			var across = new Vector2(-away.y, away.x);
			const int steps = 32;
			painter.fillColor = shade;
			painter.BeginPath();
			// The dark limb: the semicircle facing away from the star.
			for (int i = 0; i <= steps; i++)
			{
				float t = Mathf.PI * i / steps;
				Vector2 point = centre + (across * Mathf.Cos(t) + away * Mathf.Sin(t)) * radius;
				if (i == 0)
				{
					painter.MoveTo(point);
				}
				else
				{
					painter.LineTo(point);
				}
			}
			// Back along the terminator: bulges toward the star when more than half is dark.
			for (int i = steps; i >= 0; i--)
			{
				float t = Mathf.PI * i / steps;
				Vector2 point = centre + (across * Mathf.Cos(t) - away * Mathf.Sin(t) * k) * radius;
				painter.LineTo(point);
			}
			painter.ClosePath();
			painter.Fill();
		}

		private Image ImageAt(int index)
		{
			while (textured.Count <= index)
			{
				var image = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleToFit };
				image.style.position = Position.Absolute;
				image.style.borderTopLeftRadius = 999;
				image.style.borderTopRightRadius = 999;
				image.style.borderBottomLeftRadius = 999;
				image.style.borderBottomRightRadius = 999;
				image.style.overflow = Overflow.Hidden;
				textured.Add(image);
				Insert(IndexOf(foreground), image);
			}
			return textured[index];
		}

		private Label LabelAt(int index)
		{
			while (labels.Count <= index)
			{
				var label = new Label { pickingMode = PickingMode.Ignore };
				label.style.position = Position.Absolute;
				label.style.fontSize = 10;
				label.style.color = new Color(0.92f, 0.94f, 1f, 0.9f);
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
	}
}
#endif
