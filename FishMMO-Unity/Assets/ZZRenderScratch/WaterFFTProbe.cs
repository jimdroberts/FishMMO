using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using FishMMO.Water;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Checks the ocean FFT arithmetically, before anything is rendered.
	/// </summary>
	/// <remarks>
	/// A wrong sign, a missing 1/N² or a mis-indexed butterfly all produce a texture that renders
	/// as "some sort of water" and is quietly wrong — the wrong height, the wrong scale, or offset
	/// from its own normals. Statistics catch every one of those in seconds, where a picture
	/// catches none of them.
	/// </remarks>
	public static class WaterFFTProbe
	{
		private const string ComputePath = "Assets/Plugins/FishMMO Water/Shaders/FishWaterFFT.compute";

		public static void Run()
		{
			var log = new StringBuilder();
			int code = 0;
			try
			{
				log.AppendLine($"compute supported: {SystemInfo.supportsComputeShaders}, FFT supported: {WaterFFT.Supported}");
				var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath);
				if (shader == null)
				{
					log.AppendLine("FAIL: the compute shader did not import.");
					Finish(log, 2);
					return;
				}

				using (var fft = new WaterFFT(shader))
				{
					// Theory: a fully developed sea has a significant wave height of about
					// 0.21 U²/g, and Hs is four times the RMS surface elevation.
					// Bisect the pipeline: where does the signal stop?
					fft.SetSeaState(10f, new Vector2(0.7071f, 0.7071f), 9.81f, fft.Amplitude, 1.2f, 120f, 1u);
					for (int stages = 1; stages <= 4; stages++)
					{
						fft.Evaluate(11.0f, stages);
						Color[] working = Read(fft.Working[2]);
						Color[] output = Read(fft.Displacement[2]);
						double workEnergy = 0.0, outEnergy = 0.0;
						double workPeak = 0.0, outPeak = 0.0;
						for (int i = 0; i < working.Length; i++)
						{
							workEnergy += Math.Abs(working[i].r);
							workPeak = Math.Max(workPeak, Math.Abs(working[i].r));
							outEnergy += Math.Abs(output[i].g);
							outPeak = Math.Max(outPeak, Math.Abs(output[i].g));
						}
						log.AppendLine($"  stages {stages}: working sum {workEnergy,12:0.000} peak {workPeak,10:0.0000} | " +
							$"displacement sum {outEnergy,12:0.000} peak {outPeak,10:0.0000}");
					}

					foreach (float wind in new[] { 5f, 10f, 20f })
					{
						fft.SetSeaState(wind, new Vector2(0.7071f, 0.7071f), 9.81f,
							fft.Amplitude, 1.2f, 120f, 1u);
						fft.Evaluate(11.0f);

						double sum = 0.0, sumSquares = 0.0;
						float lowest = float.MaxValue, highest = float.MinValue;
						int samples = 0;
						double folded = 0.0;

						for (int cascade = 0; cascade < WaterFFT.Cascades; cascade++)
						{
							Color[] pixels = Read(fft.Displacement[cascade]);
							Color[] derivatives = Read(fft.Derivatives[cascade]);
							for (int i = 0; i < pixels.Length; i++)
							{
								float height = pixels[i].g;
								sum += height;
								sumSquares += height * height;
								lowest = Mathf.Min(lowest, height);
								highest = Mathf.Max(highest, height);
								samples++;
								if (derivatives[i].b < 0f)
								{
									folded++;
								}
							}
						}

						// Where does it go to zero? The spectrum, or the transform?
						double h0Energy = 0.0;
						double h0Peak = 0.0;
						for (int cascade = 0; cascade < WaterFFT.Cascades; cascade++)
						{
							Color[] spectrum = Read(fft.Spectrum[cascade]);
							for (int i = 0; i < spectrum.Length; i++)
							{
								double magnitude = Math.Sqrt(spectrum[i].r * spectrum[i].r + spectrum[i].g * spectrum[i].g);
								h0Energy += magnitude;
								h0Peak = Math.Max(h0Peak, magnitude);
							}
						}

						double mean = sum / samples;
						double rms = Math.Sqrt(sumSquares / samples);
						double significant = rms * 4.0;
						double expected = 0.21 * wind * wind / 9.81;
						log.AppendLine($"wind {wind,4:0} m/s | mean {mean,9:0.0000} (want ~0) | rms {rms,7:0.000} m | " +
							$"Hs {significant,6:0.00} m vs theory {expected,6:0.00} m | ratio {significant / Math.Max(1e-6, expected),5:0.00} | " +
							$"range {lowest,7:0.00} .. {highest,6:0.00} | folded {100.0 * folded / samples,5:0.0}% | " +
							$"h0 sum {h0Energy,10:0.0000} peak {h0Peak,10:0.000000}");
					}
				}
			}
			catch (Exception ex)
			{
				log.AppendLine("EXCEPTION: " + ex);
				code = 3;
			}
			Finish(log, code);
		}

		private static Color[] Read(RenderTexture source)
		{
			var readable = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true);
			RenderTexture active = RenderTexture.active;
			RenderTexture.active = source;
			readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
			readable.Apply();
			RenderTexture.active = active;
			Color[] pixels = readable.GetPixels();
			UnityEngine.Object.DestroyImmediate(readable);
			return pixels;
		}

		private static void Finish(StringBuilder log, int code)
		{
			Debug.Log("[WaterFFTProbe]\n" + log);
			System.IO.File.WriteAllText("/tmp/claude-1000/fftprobe.txt", log.ToString());
			EditorApplication.Exit(code);
		}
	}
}
