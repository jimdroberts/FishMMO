using System;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// Where the cloud march samples (FishCloudVolume.hlsl, FishCloudMarch), through its C# twin
	/// (<see cref="CloudMarchTwin"/>): every sample of a band is a real one, the steps are laid out from
	/// the band's own edge and from each cloud's own near edge, and none climbs more than a share of the
	/// thinnest cloud that can be at its height.
	/// </summary>
	/// <remarks>
	/// Issue #238, 2026-09-29: in Jim's editor the Ray Jitter diagnostic proved the grain was the rays'
	/// phase, and turning it off left rings across a thin layer and terraces inside cumulus. The march
	/// strode through a band's empty air at up to 0.15 of the band's depth in height per stride and walked
	/// back on the stride's grid when it landed in cloud: a sheet thinner than a stride was all or nothing
	/// per ray (rings with the jitter off, a coin toss per pixel with it on — the grain), and a cloud's
	/// face was sampled at the same distances from the camera by every ray (terraces). Each test here
	/// runs the defect's control through the same twin, so it is shown to fire. Measured on a Python port
	/// first: the thin sheet's rings 0.54 → 0.003 of opacity, its noise 17–33 % → 0.4–2.4 %, a cumulus
	/// face's terraces 1.7 % → 0.3 % rms.
	/// </remarks>
	[TestFixture]
	public class CloudMarchTests
	{
		private static double Smooth(double e0, double e1, double x)
		{
			double t = Math.Min(1.0, Math.Max(0.0, (x - e0) / (e1 - e0)));
			return t * t * (3.0 - 2.0 * t);
		}

		private static double Radians(double degrees) => degrees * Math.PI / 180.0;

		// A sheet of cloud a hundred metres deep, its edges twenty metres soft, in the middle of the
		// 2 km middle band: the kind of layer 8.png drew in rings.
		private static CloudMarchTwin Sheet()
		{
			return new CloudMarchTwin
			{
				Bands = new[] { new CloudMarchTwin.Band { Bottom = 3000.0, Top = 5000.0, Thinnest = 2000.0, Column = false } },
				Density = (t, h) => 0.004 * Smooth(3880.0, 3920.0, h) * (1.0 - Smooth(3980.0, 4020.0, h)),
				Colour = (t, h) => 1.0,
			};
		}

		// What the march would give with an infinitely fine step, remapped at its early exit as it is.
		private static double Exact(CloudMarchTwin twin, double dy)
		{
			double near = 3000.0 / dy, far = 5000.0 / dy;
			int n = 40000;
			double dt = (far - near) / n, tau = 0.0;
			for (int k = 0; k < n; k++)
			{
				double t = near + (k + 0.5) * dt;
				tau += twin.Density(t, t * dy) * dt;
			}
			double transmittance = Math.Exp(-tau);
			double kept = Math.Max(0.0, (transmittance - twin.ExitAt) / (1.0 - twin.ExitAt));
			return 1.0 - kept;
		}

		// How far the answer with the jitter off strays from its own trend over neighbouring rays: the
		// rings or terraces, and nothing of how the answer changes smoothly with the angle.
		private static double Banding(Func<double, double> jitterOff, double from, double step, int count, int reach, out double rms)
		{
			var values = new double[count];
			for (int k = 0; k < count; k++)
			{
				values[k] = jitterOff(from + step * k);
			}
			double worst = 0.0, sum = 0.0;
			int n = 0;
			for (int k = reach; k < count - reach; k++)
			{
				double mean = 0.0;
				for (int j = k - reach; j <= k + reach; j++)
				{
					mean += values[j];
				}
				mean /= 2 * reach + 1;
				double off = values[k] - mean;
				worst = Math.Max(worst, Math.Abs(off));
				sum += off * off;
				n++;
			}
			rms = Math.Sqrt(sum / Math.Max(1, n));
			return worst;
		}

		[Test]
		public void AThinSheetInAWideBandIsFoundByEveryRay()
		{
			CloudMarchTwin twin = Sheet();
			double rings = Banding(a => twin.Opacity(Math.Sin(Radians(a)), 0.5), 15.0, 0.05, 1400, 10, out _);
			LogAssert.IsTrue(rings < 0.01, $"with the jitter off the sheet is smooth across the sky: rings of {rings:0.0000} of opacity");
			foreach (double elevation in new[] { 20.0, 30.0, 45.0, 60.0, 90.0 })
			{
				double dy = Math.Sin(Radians(elevation));
				double exact = Exact(twin, dy);
				double mean = 0.0, square = 0.0;
				for (int j = 0; j < 32; j++)
				{
					double v = twin.Opacity(dy, (j + 0.5) / 32.0);
					mean += v;
					square += v * v;
				}
				mean /= 32.0;
				double spread = Math.Sqrt(Math.Max(0.0, square / 32.0 - mean * mean));
				LogAssert.IsTrue(Math.Abs(mean - exact) < 0.02 * exact, $"{elevation}°: over the jitter the sheet counts {mean:0.0000} of {exact:0.0000}");
				LogAssert.IsTrue(spread < 0.06 * exact, $"{elevation}°: and the jitter moves it by {spread:0.0000}, a little of {exact:0.0000}, not all or nothing");
			}

			// The control: strides held to a share of the band's whole depth, taken at once, as they were.
			CloudMarchTwin strided = Sheet();
			strided.LayerShare = 1.0;
			strided.EmptyRamp = 0.001;
			strided.EmptyGrowth = 100.0;
			double stridedRings = Banding(a => strided.Opacity(Math.Sin(Radians(a)), 0.5), 15.0, 0.05, 1400, 10, out _);
			LogAssert.IsTrue(stridedRings > 0.1, $"the control fires: strides a share of the band deep draw the sheet in rings ({stridedRings:0.000})");
		}

		// A cumulus: a round cloud 1.2 km across, 4 km out and 2 km up, its mixing shell 60 m, lit from
		// the camera's side so its face is bright and darkens with depth — where a face drew terraces.
		private static double Cumulus(CloudMarchTwin twin, double elevation, double jitter)
		{
			const double X = 4000.0, Y = 2000.0, R = 600.0, Beta = 0.05;
			double dy = Math.Sin(Radians(elevation));
			double dx = Math.Sqrt(1.0 - dy * dy);
			double b = dx * X + dy * Y, c = X * X + Y * Y - R * R, disc = b * b - c;
			double entry = disc > 0.0 ? b - Math.Sqrt(disc) : 0.0;
			twin.Bands = new[] { new CloudMarchTwin.Band { Bottom = 1000.0, Top = 3000.0, Thinnest = 2000.0, Column = true } };
			twin.Density = (t, h) =>
			{
				double r = Math.Sqrt((t * dx - X) * (t * dx - X) + (h - Y) * (h - Y));
				return Beta * Smooth(0.0, 60.0, R - r);
			};
			twin.Colour = (t, h) => 0.2 + 0.8 * Math.Exp(-0.5 * Beta * Math.Max(0.0, t - entry));
			return twin.Opacity(dy, jitter);
		}

		[Test]
		public void ACloudsFaceHasNoTerracesWithTheJitterOff()
		{
			var twin = new CloudMarchTwin();
			Banding(a => Cumulus(twin, a, 0.5), 20.5, 0.02, 350, 12, out double rms);
			LogAssert.IsTrue(rms < 0.008, $"the face is smooth from ray to ray: {rms:0.0000} rms off its trend");

			// The control: the steps not re-anchored on the cloud's edge — every ray walks the face on the
			// grid it was already on, at the same distances from the camera as its neighbours.
			var gridded = new CloudMarchTwin { Halvings = 0 };
			Banding(a => Cumulus(gridded, a, 0.5), 20.5, 0.02, 350, 12, out double griddedRms);
			LogAssert.IsTrue(griddedRms > 0.015, $"the control fires: on the grid the face is terraced ({griddedRms:0.0000} rms)");
		}

		[Test]
		public void EveryLayerIsCrossedByAtLeastFourSamplesAtAnyAngle()
		{
			// A thin deck and a thick one in the column, and the middle and high layers at their
			// thinnest and their deepest; the stack's own depth sets the tier's step, so each is tried
			// with the high ice over it as well.
			var decks = new[]
			{
				new CloudMarchTwin.Band { Bottom = 1000.0, Top = 1080.0, Thinnest = 80.0, Column = true },
				new CloudMarchTwin.Band { Bottom = 1000.0, Top = 1480.0, Thinnest = 480.0, Column = true },
				new CloudMarchTwin.Band { Bottom = 3000.0, Top = 3201.0, Thinnest = 201.0, Column = false },
				new CloudMarchTwin.Band { Bottom = 3000.0, Top = 5000.0, Thinnest = 2000.0, Column = false },
				new CloudMarchTwin.Band { Bottom = 8000.0, Top = 8151.0, Thinnest = 151.0, Column = false },
			};
			var ice = new CloudMarchTwin.Band { Bottom = 9000.0, Top = 11000.0, Thinnest = 2000.0, Column = false };
			foreach (CloudMarchTwin.Band deck in decks)
			{
				foreach (bool stacked in new[] { false, true })
				{
					var twin = new CloudMarchTwin
					{
						Bands = stacked && deck.Top < ice.Bottom ? new[] { deck, ice } : new[] { deck },
						Density = (t, h) => 0.0,
					};
					double finest = twin.FinestAt(0.5 * (deck.Bottom + deck.Top));
					double low = 0.5 * (deck.Bottom + deck.Top) - 0.5 * finest, high = low + finest;
					for (double elevation = 1.0; elevation <= 90.0; elevation += 0.5)
					{
						foreach (double jitter in new[] { 0.02, 0.5, 0.98 })
						{
							int inside = 0;
							twin.Sampled = h => { if (h > low && h < high) inside++; };
							twin.Opacity(Math.Sin(Radians(elevation)), jitter);
							LogAssert.IsTrue(inside >= 4, $"{deck.Bottom}–{deck.Top} m{(stacked ? " under the ice" : "")} at {elevation}°, jitter {jitter}: {inside} samples in its thinnest {finest:0} m");
						}
					}
				}
			}
		}

		[Test]
		public void TheRaysPhaseNoLongerMovesTheCloud()
		{
			// 2026-09-29: each sample's extinction was held over its whole step, which is the cloud moved
			// along the ray by (jitter − ½)·step — so where a ray's phase put its samples changed its
			// answer wherever the extinction changes across a step: the grain. Between consecutive
			// samples in straight lines, the phase drops out to first order. The spread of one ray's
			// answer over its phase, a thin sheet and a cumulus's lit face, each against the old way.
			double sheet = 0.0, sheetOld = 0.0, face = 0.0, faceOld = 0.0;
			int angles = 0;
			foreach (double elevation in new[] { 20.0, 30.0, 45.0, 60.0, 90.0 })
			{
				double dy = Math.Sin(Radians(elevation));
				CloudMarchTwin now = Sheet();
				CloudMarchTwin old = Sheet();
				old.Trapezoid = false;
				double exact = Exact(now, dy);
				sheet += Spread(j => now.Opacity(dy, j)) / exact;
				sheetOld += Spread(j => old.Opacity(dy, j)) / exact;
				angles++;
			}
			sheet /= angles;
			sheetOld /= angles;
			int faces = 0;
			for (double elevation = 20.6; elevation <= 27.4; elevation += 0.4)
			{
				var now = new CloudMarchTwin();
				var old = new CloudMarchTwin { Trapezoid = false };
				double e = elevation;
				face += Spread(j => Cumulus(now, e, j));
				faceOld += Spread(j => Cumulus(old, e, j));
				faces++;
			}
			face /= faces;
			faceOld /= faces;
			LogAssert.IsTrue(sheet < 0.006, $"a thin sheet moves {sheet * 100.0:0.00} % of itself with the ray's phase");
			LogAssert.IsTrue(face < 0.7 * faceOld, $"a lit face moves {face:0.0000} with the phase, against {faceOld:0.0000} the old way");
			LogAssert.IsTrue(sheetOld > 0.01, $"the control fires: held over its step, the sheet moved {sheetOld * 100.0:0.00} % with the phase");
		}

		// The spread of what a ray gathers over its phase, 32 phases.
		private static double Spread(Func<double, double> ray)
		{
			double mean = 0.0, square = 0.0;
			for (int j = 0; j < 32; j++)
			{
				double v = ray((j + 0.5) / 32.0);
				mean += v;
				square += v * v;
			}
			mean /= 32.0;
			return Math.Sqrt(Math.Max(0.0, square / 32.0 - mean * mean));
		}

		[Test]
		public void TheShaderIsTheTwinsMarch()
		{
			string volume = SourceScanPins.ReadCode("Assets/Prefabs/Client/Weather/Shaders/FishCloudVolume.hlsl");
			string march = SourceScanPins.Body(volume, "float4 FishCloudMarch(");
			SourceScanPins.HoldsAndFires("one sample in each step, at the ray's phase of it", march,
				code => SourceScanPins.InOrder(code,
					"float at = travelled + jitter * stepHere;",
					"float3 position = origin + direction * at;",
					"travelled += stepHere;"),
				SourceScanPins.Replace("float at = travelled + jitter * stepHere;", "float at = travelled + 0.5 * stepHere;"),
				"every ray sampling its steps at the same place");
			SourceScanPins.HoldsAndFires("no step climbs more than a share of the thinnest cloud", march,
				code => code != null && code.Contains("stepHere = min(stepHere, max(0.25 * fineNear, 0.15 * finest / climb) * deepIn);") ? null : "the climb guard is gone or no longer the twin's",
				SourceScanPins.Replace("stepHere = min(stepHere, max(0.25 * fineNear, 0.15 * finest / climb) * deepIn);", ""),
				"strides that step over a thin sheet");
			SourceScanPins.HoldsAndFires("the steps re-anchor on a cloud's near edge", march,
				code => SourceScanPins.InOrder(code,
					"for (int e = 0; e < 5; e++)",
					"within = middle;",
					"outside = middle;",
					"travelled = 0.5 * (outside + within);"),
				SourceScanPins.Replace("travelled = 0.5 * (outside + within);", "travelled = at;"),
				"the walk through a cloud left on the grid it found the cloud on");
			SourceScanPins.HoldsAndFires("the optical-depth limit loosens as the twin's does", march,
				code => code != null && code.Contains("float tauStep = (_FishCloudStepTau.x > 0.0 ? _FishCloudStepTau.x : 0.35) * (economy ? rsqrt(max(transmittance, 0.02)) : 1.0) * deepIn;")
					&& code.Contains("float deepIn = economy && lastSigma > 0.0 && transmittance < 0.2 ? sqrt(0.2 / max(transmittance, 0.02)) : 1.0;")
					? null : "the march's in-cloud limit is no longer the twin's",
				SourceScanPins.Replace("(economy ? rsqrt(max(transmittance, 0.02)) : 1.0)", "1.0"),
				"a limit that never loosens");
			SourceScanPins.HoldsAndFires("the samples are integrated as the twin's trapezoid", march,
				code => code != null
					&& code.Contains("segmentDepth = 0.5 * (prevTotal + total) * span;")
					&& code.Contains("source = (prevTotal * (2.0 * fromColour + colour) + total * (fromColour + 2.0 * colour)) / (3.0 * (prevTotal + total));")
					&& code.Contains("float closing = exp(-0.5 * prevTotal * max(0.0, at - prevAt));")
					? null : "the march no longer integrates between its samples as the twin does",
				SourceScanPins.Replace("segmentDepth = 0.5 * (prevTotal + total) * span;", "segmentDepth = total * stepHere;"),
				"each sample's extinction held over its whole step");
			SourceScanPins.HoldsAndFires("the empty air of a band is walked, not strided", march,
				code => code != null && code.Contains("min(6.0, 1.0 + emptyRun / (4.0 * stepBase))") && !code.Contains("stride") ? null : "the empty air's steps are no longer the twin's",
				SourceScanPins.Replace("travelled += stepHere;\n            lastEmpty = at;", "travelled += stride;\n            lastEmpty = at;"),
				"a stride through the band's empty air");
			string finest = SourceScanPins.Body(volume, "float FishCloudFinestAt(");
			SourceScanPins.HoldsAndFires("a layer's thinnest cloud is a quarter of its band", finest,
				code => code != null && code.Contains("finest = min(finest, column ? depth : 0.25 * depth);") ? null : "FishCloudFinestAt no longer matches the twin",
				SourceScanPins.Replace("column ? depth : 0.25 * depth", "depth"),
				"a layer's sheet taken to be as deep as its band");
		}
	}

	/// <summary>
	/// A C# twin of FishCloudMarch's sampling (FishCloudVolume.hlsl) on a flat world, for a ray climbing
	/// from the ground: the tier's step with distance, its growth through a band's empty air and inside
	/// cloud, the climb guard, the optical-depth limit, the leap over air no band can be in, and the
	/// near-edge search. No fog, no immersion, no light march: a density and a colour along the ray.
	/// </summary>
	internal sealed class CloudMarchTwin
	{
		public struct Band
		{
			public double Bottom, Top, Thinnest;
			public bool Column;
		}

		public Band[] Bands;
		/// <summary>Extinction (1/m) at a distance along the ray and a height.</summary>
		public Func<double, double, double> Density;
		/// <summary>What a sample scatters toward the camera.</summary>
		public Func<double, double, double> Colour = (t, h) => 1.0;
		/// <summary>Told the height of every sample the march takes.</summary>
		public Action<double> Sampled;
		public int Steps = 48;
		public double ExitAt = 0.05;
		public int Halvings = 5;
		public double LayerShare = 0.25;
		public double ClimbShare = 0.15;
		public double EmptyGrowth = 6.0;
		public double EmptyRamp = 4.0;
		/// <summary>
		/// The trapezoid between consecutive samples, as the shader integrates (<c>_FishCloudFix.x</c> 0);
		/// false holds each sample's extinction over its whole step, as it did until 2026-09-29.
		/// </summary>
		public bool Trapezoid = true;
		/// <summary>
		/// Dense cloud walked as the shader walks it (<c>_FishCloudFixB.y</c> 0): the in-cloud optical-depth
		/// limit loosened as the ray dims, 1/√T, and every limit on a step grown as √(0.2/T) once under a
		/// fifth of the light is left; false keeps them fixed, as they were until 2026-09-29. (The haze under
		/// a base is not in this twin: it has no haze.)
		/// </summary>
		public bool Economy = true;

		/// <summary>FishCloudFinestAt: the thinnest cloud that can be at a height, 0 where none can.</summary>
		public double FinestAt(double h)
		{
			double finest = 1e9;
			foreach (Band band in Bands)
			{
				if (h > band.Bottom && h < band.Top)
				{
					double depth = Math.Max(50.0, band.Thinnest > 0.0 ? band.Thinnest : band.Top - band.Bottom);
					finest = Math.Min(finest, band.Column ? depth : LayerShare * depth);
				}
			}
			return finest < 1e8 ? finest : 0.0;
		}

		private bool Possible(double h)
		{
			foreach (Band band in Bands)
			{
				if (h > band.Bottom && h < band.Top)
				{
					return true;
				}
			}
			return false;
		}

		private static double SmoothStep(double e0, double e1, double x)
		{
			double t = Math.Min(1.0, Math.Max(0.0, (x - e0) / (e1 - e0)));
			return t * t * (3.0 - 2.0 * t);
		}

		/// <summary>What the ray gathers climbing at <paramref name="dy"/> (the sine of its elevation): its opacity when the colour is 1.</summary>
		public double Opacity(double dy, double jitter)
		{
			double lowest = double.MaxValue, highest = double.MinValue, thinnest = 1e6;
			foreach (Band band in Bands)
			{
				lowest = Math.Min(lowest, band.Bottom);
				highest = Math.Max(highest, band.Top);
				thinnest = Math.Min(thinnest, Math.Max(50.0, band.Thinnest > 0.0 ? band.Thinnest : band.Top - band.Bottom));
			}
			double near = lowest / dy, far = highest / dy;
			double fine = Math.Min(90.0, Math.Max(18.0, (far - near) / Steps));
			double fineNear = Math.Max(18.0, fine * 0.5);
			double climb = Math.Max(Math.Abs(dy), 0.05);

			double travelled = near, lastEmpty = near, lastSigma = 0.0, emptyRun = 0.0;
			bool inside = false;
			double emptyDistance = 0.0, insideUntil = 0.0, insideSamples = 0.0;
			double transmittance = 1.0, scattered = 0.0, lastColour = 0.0;
			// The trapezoid's last sample: where, its extinction and its colour. The ray starts below
			// the bands, where there is no cloud.
			double prevAt = near, prevSigma = 0.0, prevColour = 0.0;
			int budget = Steps * 4;
			for (int i = 0; i < budget; i++)
			{
				if (transmittance < ExitAt || travelled >= far)
				{
					break;
				}
				double finest = FinestAt((travelled + 1.0) * dy);
				if (finest <= 0.0)
				{
					double next = 1e9;
					foreach (Band band in Bands)
					{
						double floor = band.Bottom / dy;
						if (floor > travelled + 1.0)
						{
							next = Math.Min(next, floor);
						}
					}
					if (Trapezoid && prevSigma > 0.0)
					{
						// Closed where cloud stops being possible, falling to nothing in a straight line.
						double closing = Math.Exp(-0.5 * prevSigma * Math.Max(0.0, travelled - prevAt));
						scattered += transmittance * prevColour * (1.0 - closing);
						transmittance *= closing;
					}
					prevAt = next;
					prevSigma = 0.0;
					if (next > far)
					{
						break;
					}
					inside = false;
					travelled = next;
					lastEmpty = next;
					lastSigma = 0.0;
					emptyRun = 0.0;
					continue;
				}
				double stepBase = Math.Max(Math.Min(Math.Max(travelled * 0.006, fineNear), fine), travelled * 0.004);
				double dimmed = 1.0 + 0.5 * (1.0 - SmoothStep(0.4, 0.6, transmittance));
				double growth = inside ? Math.Min(2.0, 1.0 + insideSamples / 20.0) : Math.Min(EmptyGrowth, 1.0 + emptyRun / (EmptyRamp * stepBase));
				// Deep in a medium, under a fifth of the light, every limit grows as √(0.2/T) (economy).
				double deepIn = Economy && lastSigma > 0.0 && transmittance < 0.2 ? Math.Sqrt(0.2 / Math.Max(transmittance, 0.02)) : 1.0;
				double step = stepBase * dimmed * growth * deepIn;
				step = Math.Min(step, Math.Max(stepBase, thinnest * 0.5 * deepIn));
				step = Math.Min(step, Math.Max(0.25 * fineNear, ClimbShare * finest / climb) * deepIn);
				if (lastSigma > 0.0)
				{
					double tauStep = 0.35 * (Economy ? 1.0 / Math.Sqrt(Math.Max(transmittance, 0.02)) : 1.0) * deepIn;
					step = Math.Min(step, Math.Max(0.1 * stepBase, tauStep / lastSigma));
				}
				step = Math.Min(step, far - travelled);
				double at = travelled + jitter * step;
				double h = at * dy;
				Sampled?.Invoke(h);
				double density = Possible(h) ? Density(at, h) : 0.0;
				if (density > 0.0 && !inside)
				{
					inside = true;
					emptyDistance = 0.0;
					insideUntil = at;
					double outside = lastEmpty, within = at;
					for (int e = 0; e < Halvings; e++)
					{
						double middle = 0.5 * (outside + within);
						double probe = Possible(middle * dy) ? Density(middle, middle * dy) : 0.0;
						if (probe > 0.0)
						{
							within = middle;
						}
						else
						{
							outside = middle;
						}
					}
					travelled = 0.5 * (outside + within);
					lastSigma = density;
					emptyRun = 0.0;
					prevAt = travelled;
					prevSigma = 0.0;
					continue;
				}
				if (density > 0.0)
				{
					double colour = Colour(at, h);
					double depth, source;
					if (Trapezoid)
					{
						// Extinction and light each in a straight line from the last sample: the light is the
						// exact mean over the segment, weighted by the extinction.
						double from = prevSigma > 0.0 ? prevColour : colour;
						depth = 0.5 * (prevSigma + density) * Math.Max(0.0, at - prevAt);
						source = (prevSigma * (2.0 * from + colour) + density * (from + 2.0 * colour)) / (3.0 * (prevSigma + density));
					}
					else
					{
						depth = density * step;
						source = colour;
					}
					double clarity = Math.Exp(-depth);
					scattered += transmittance * source * (1.0 - clarity);
					transmittance *= clarity;
					lastColour = colour;
					prevAt = at;
					prevSigma = density;
					prevColour = colour;
					travelled += step;
					lastSigma = density;
					emptyDistance = 0.0;
					insideSamples += 1.0;
				}
				else
				{
					if (Trapezoid && prevSigma > 0.0)
					{
						double closing = Math.Exp(-0.5 * prevSigma * Math.Max(0.0, at - prevAt));
						scattered += transmittance * prevColour * (1.0 - closing);
						transmittance *= closing;
					}
					prevAt = at;
					prevSigma = 0.0;
					travelled += step;
					lastEmpty = at;
					lastSigma = 0.0;
					if (inside)
					{
						emptyDistance += step;
						if (at > insideUntil && emptyDistance > fine * 4.0)
						{
							inside = false;
							emptyRun = 0.0;
						}
					}
					else
					{
						emptyRun += step;
					}
				}
			}
			if (Trapezoid && prevSigma > 0.0 && transmittance >= ExitAt)
			{
				// Ended in cloud: the rest of the last step at the last sample's own extinction.
				double closing = Math.Exp(-prevSigma * Math.Max(0.0, Math.Min(travelled, far) - prevAt));
				scattered += transmittance * prevColour * (1.0 - closing);
				transmittance *= closing;
			}
			double kept = Math.Min(1.0, Math.Max(0.0, (transmittance - ExitAt) / (1.0 - ExitAt)));
			return scattered + (transmittance - kept) * lastColour;
		}
	}
}
