using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The colour-vision palettes (<see cref="ColorVision"/>): each stays readable to the eye it is for, and
	/// Normal leaves the game's own colours alone.
	/// </summary>
	/// <remarks>
	/// Distinctness is measured as the eye would see it: the colours are passed through the same dichromacy
	/// simulation the correction filter uses (Machado et al. 2009, severity 1, linear RGB), then compared in
	/// CIELAB (ΔE*ab, where about 2 is just noticeable and 30 is plainly different). The game's own red and
	/// green fail this under deuteranopia (hostile against friendly about 28, better against worse about 14);
	/// that is the case the palettes exist for.
	/// </remarks>
	public class ColorVisionTests
	{
		private static readonly float[][] Protanopia =
		{
			new[] { 0.152286f, 1.052583f, -0.204868f },
			new[] { 0.114503f, 0.786281f, 0.099216f },
			new[] { -0.003882f, -0.048116f, 1.051998f },
		};
		private static readonly float[][] Deuteranopia =
		{
			new[] { 0.367322f, 0.860646f, -0.227968f },
			new[] { 0.280085f, 0.672501f, 0.047413f },
			new[] { -0.011820f, 0.042940f, 0.968881f },
		};
		private static readonly float[][] Tritanopia =
		{
			new[] { 1.255528f, -0.076749f, -0.178779f },
			new[] { -0.078411f, 0.930809f, 0.147602f },
			new[] { 0.004733f, 0.691367f, 0.303900f },
		};

		/// <summary>Hostile, friendly and neutral differ by at least this much, as seen.</summary>
		private const float MinimumAllianceDelta = 30f;
		/// <summary>Better and worse differ by at least this much, as seen.</summary>
		private const float MinimumToneDelta = 60f;

		[TearDown]
		public void Restore()
		{
			ColorVision.Mode = ColorVisionMode.Normal;
		}

		[Test]
		public void NormalKeepsTheGamesOwnColours()
		{
			ColorVision.Mode = ColorVisionMode.Normal;
			Assert.That(ColorVision.Hostile, Is.EqualTo(TinyColor.ToUnityColor(TinyColor.red)));
			Assert.That(ColorVision.Friendly, Is.EqualTo(TinyColor.ToUnityColor(TinyColor.green)));
			Assert.That(ColorVision.Neutral, Is.EqualTo(TinyColor.ToUnityColor(TinyColor.skyBlue)));
			Assert.That((Color32)ColorVision.Good, Is.EqualTo(new Color32(55, 168, 107, 255)));
			Assert.That((Color32)ColorVision.Bad, Is.EqualTo(new Color32(200, 56, 74, 255)));
		}

		[TestCase(ColorVisionMode.Protanopia)]
		[TestCase(ColorVisionMode.Deuteranopia)]
		[TestCase(ColorVisionMode.Tritanopia)]
		public void EachPaletteStaysDistinctToItsEye(ColorVisionMode mode)
		{
			ColorVision.Mode = mode;
			float[][] eye = mode == ColorVisionMode.Protanopia ? Protanopia
				: mode == ColorVisionMode.Deuteranopia ? Deuteranopia
				: Tritanopia;

			Vector3 hostile = Seen(ColorVision.Hostile, eye);
			Vector3 friendly = Seen(ColorVision.Friendly, eye);
			Vector3 neutral = Seen(ColorVision.Neutral, eye);
			Vector3 good = Seen(ColorVision.Good, eye);
			Vector3 bad = Seen(ColorVision.Bad, eye);

			Assert.That(Vector3.Distance(hostile, friendly), Is.GreaterThanOrEqualTo(MinimumAllianceDelta), "hostile against friendly");
			Assert.That(Vector3.Distance(hostile, neutral), Is.GreaterThanOrEqualTo(MinimumAllianceDelta), "hostile against neutral");
			Assert.That(Vector3.Distance(friendly, neutral), Is.GreaterThanOrEqualTo(MinimumAllianceDelta), "friendly against neutral");
			Assert.That(Vector3.Distance(good, bad), Is.GreaterThanOrEqualTo(MinimumToneDelta), "better against worse");
		}

		[Test]
		public void TheGamesOwnRedAndGreenAreWhatDeuteranopiaLoses()
		{
			ColorVision.Mode = ColorVisionMode.Normal;
			Vector3 good = Seen(ColorVision.Good, Deuteranopia);
			Vector3 bad = Seen(ColorVision.Bad, Deuteranopia);
			// The control: if this ever passes the tone threshold, the simulation or the metric has stopped measuring anything.
			Assert.That(Vector3.Distance(good, bad), Is.LessThan(MinimumToneDelta));
		}

		/// <summary>A colour as the given eye sees it, in CIELAB (L*, a*, b*), D65.</summary>
		private static Vector3 Seen(Color color, float[][] eye)
		{
			float r = Linear(color.r), g = Linear(color.g), b = Linear(color.b);
			float sr = Mathf.Clamp01(eye[0][0] * r + eye[0][1] * g + eye[0][2] * b);
			float sg = Mathf.Clamp01(eye[1][0] * r + eye[1][1] * g + eye[1][2] * b);
			float sb = Mathf.Clamp01(eye[2][0] * r + eye[2][1] * g + eye[2][2] * b);
			float x = (0.4124f * sr + 0.3576f * sg + 0.1805f * sb) / 0.95047f;
			float y = 0.2126f * sr + 0.7152f * sg + 0.0722f * sb;
			float z = (0.0193f * sr + 0.1192f * sg + 0.9505f * sb) / 1.08883f;
			float fx = F(x), fy = F(y), fz = F(z);
			return new Vector3(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
		}

		private static float Linear(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);

		private static float F(float t) => t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;
	}
}
