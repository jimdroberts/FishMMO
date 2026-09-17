using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using FishNet.Serializing;
using UnityEditor;
using UnityEngine.Rendering;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The weather and clock broadcasts through the serializer FishNet actually uses, and the web
	/// player's graphics API order.
	/// </summary>
	/// <remarks>
	/// Every field the server fills must arrive: a storm cell missing its death tick lives forever
	/// on the client, and a clock anchor that loses precision moves the sun.
	/// </remarks>
	[TestFixture]
	public class WeatherWireTests
	{
		private static bool serializersReady;
		private static string serializerFailure;

		/// <summary>Runs FishNet's generated serializer registration, which the test runner never does.</summary>
		private static void EnsureSerializers()
		{
			if (serializersReady || serializerFailure != null)
			{
				return;
			}
			int found = 0;
			try
			{
				foreach (Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
				{
					System.Type[] types;
					try
					{
						types = assembly.GetTypes();
					}
					catch (ReflectionTypeLoadException ex)
					{
						types = System.Array.FindAll(ex.Types, t => t != null);
					}
					foreach (System.Type type in types)
					{
						if (type.Name != "GeneratedWriters___Internal" && type.Name != "GeneratedReaders___Internal")
						{
							continue;
						}
						MethodInfo initialize = type.GetMethod("InitializeOnce", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
						if (initialize == null)
						{
							continue;
						}
						initialize.Invoke(null, null);
						++found;
					}
				}
			}
			catch (System.Exception ex)
			{
				serializerFailure = ex.ToString();
				return;
			}
			if (found == 0)
			{
				serializerFailure = "no generated serializer class was found; FishNet's weaver did not run";
				return;
			}
			serializersReady = true;
		}

		private static T RoundTrip<T>(T value)
		{
			EnsureSerializers();
			LogAssert.IsNull(serializerFailure, serializerFailure);
			var writer = new Writer();
			writer.Write(value);
			var reader = new Reader(writer.GetArraySegment(), null);
			return reader.Read<T>();
		}

		private static StormCell Cell(ushort id)
		{
			return new StormCell
			{
				ID = id,
				PresetID = -123456789,
				Seed = 4000000000u,
				OriginX = 1234.5f,
				OriginZ = -678.25f,
				VelocityX = 3.5f,
				VelocityZ = -1.25f,
				RadiusMeters = 450f,
				PeakIntensity = 0.85f,
				MeanderMeters = 120f,
				MotionTick = 1000,
				BirthTick = 1000,
				MatureTick = 4000,
				DecayTick = 40000,
				DeathTick = 50000,
			};
		}

		private static WeatherLayerEntry Layer(ushort handle)
		{
			return new WeatherLayerEntry { Handle = handle, TemplateID = 987654321, From = 0.1f, To = 0.75f, StartTick = 100, EndTick = 1450, RemoveWhenDone = true };
		}

		[Test]
		public void AFullTimelineArrivesIntact()
		{
			var timeline = new WeatherTimeline
			{
				SceneName = "Tutorial Island",
				Revision = 77,
				Seed = 31337,
				SceneMode = WeatherSceneMode.Fixed,
				FixedPresetID = 5555,
				FixedIntensity = 0.6f,
				Climate = new WeatherClimateEntry { FromTemperature = -0.2f, ToTemperature = 0.3f, FromHumidity = 0.1f, ToHumidity = -0.4f, StartTick = 10, EndTick = 910 },
				Cover = new WeatherCover { Snow = 0.1f, Wet = 0.2f, Ash = 0.3f, Sand = 0.4f },
				CoverTick = 123456,
			};
			timeline.Layers.Add(Layer(1));
			timeline.Layers.Add(Layer(2));
			timeline.Cells.Add(Cell(9));

			var copy = new WeatherTimeline();
			copy.Apply(RoundTrip(timeline.ToBroadcast()));

			LogAssert.AreEqual(timeline.SceneName, copy.SceneName);
			LogAssert.AreEqual(timeline.Revision, copy.Revision);
			LogAssert.AreEqual(timeline.Seed, copy.Seed);
			LogAssert.AreEqual(timeline.SceneMode, copy.SceneMode);
			LogAssert.AreEqual(timeline.FixedPresetID, copy.FixedPresetID);
			LogAssert.AreEqual(timeline.FixedIntensity, copy.FixedIntensity);
			LogAssert.AreEqual(timeline.Climate, copy.Climate);
			LogAssert.AreEqual(timeline.Cover, copy.Cover);
			LogAssert.AreEqual(timeline.CoverTick, copy.CoverTick);
			LogAssert.AreEqual(2, copy.Layers.Count);
			LogAssert.AreEqual(Layer(2), copy.Layers[1]);
			LogAssert.AreEqual(1, copy.Cells.Count);
			LogAssert.AreEqual(Cell(9), copy.Cells[0]);
		}

		[Test]
		public void AnEmptyTimelineArrives()
		{
			WeatherTimelineBroadcast back = RoundTrip(new WeatherTimeline { SceneName = "Empty" }.ToBroadcast());
			var copy = new WeatherTimeline();
			copy.Apply(back);
			LogAssert.AreEqual("Empty", copy.SceneName);
			LogAssert.AreEqual(0, copy.Layers.Count);
			LogAssert.AreEqual(0, copy.Cells.Count);
		}

		[Test]
		public void ADeltaArrivesIntact()
		{
			var delta = new WeatherDeltaBroadcast
			{
				SceneName = "Dungeon",
				Revision = 12,
				Layers = new List<WeatherLayerEntry> { Layer(3) },
				RemovedLayers = new List<ushort> { 4, 65535 },
				Cells = new List<StormCell> { Cell(5) },
				RemovedCells = new List<ushort> { 6 },
				HasClimate = true,
				Climate = new WeatherClimateEntry { ToTemperature = 0.5f, EndTick = 99 },
				HasCover = true,
				Cover = new WeatherCover { Wet = 1f },
				CoverTick = 4321,
			};
			WeatherDeltaBroadcast back = RoundTrip(delta);
			LogAssert.AreEqual(delta.SceneName, back.SceneName);
			LogAssert.AreEqual(delta.Revision, back.Revision);
			CollectionAssert.AreEqual(delta.Layers, back.Layers);
			CollectionAssert.AreEqual(delta.RemovedLayers, back.RemovedLayers);
			CollectionAssert.AreEqual(delta.Cells, back.Cells);
			CollectionAssert.AreEqual(delta.RemovedCells, back.RemovedCells);
			LogAssert.IsTrue(back.HasClimate);
			LogAssert.AreEqual(delta.Climate, back.Climate);
			LogAssert.IsTrue(back.HasCover);
			LogAssert.AreEqual(delta.Cover, back.Cover);
			LogAssert.AreEqual(delta.CoverTick, back.CoverTick);
		}

		[Test]
		public void AResyncRequestArrives()
		{
			LogAssert.AreEqual(41u, RoundTrip(new WeatherResyncRequestBroadcast { HaveRevision = 41 }).HaveRevision);
		}

		[Test]
		public void TheClockAnchorKeepsFullPrecision()
		{
			var message = new WorldClockBroadcast
			{
				Anchor = new WorldClockAnchor { Tick = 4000000000u, WorldSeconds = 1234567890.123456789, Verified = true },
				Previous = new WorldClockAnchor { Tick = 3999999999u, WorldSeconds = 1234567889.987654321 },
				HasPrevious = true,
				SlewTicks = 300,
			};
			WorldClockBroadcast back = RoundTrip(message);
			LogAssert.AreEqual(message.Anchor, back.Anchor, "WorldSeconds must travel as a double, bit for bit");
			LogAssert.AreEqual(message.Previous, back.Previous);
			LogAssert.IsTrue(back.HasPrevious);
			LogAssert.AreEqual(300u, back.SlewTicks);
		}

		[Test]
		public void TheWebPlayerPrefersWebGPU()
		{
			GraphicsDeviceType[] apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.WebGL);
			LogAssert.IsFalse(PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.WebGL), "the list is explicit, not automatic");
			LogAssert.IsTrue(apis.Length >= 2, "WebGL 2 stays as the fallback for browsers without WebGPU");
			LogAssert.AreEqual(GraphicsDeviceType.WebGPU, apis[0]);
			LogAssert.IsTrue(System.Array.IndexOf(apis, GraphicsDeviceType.OpenGLES3) > 0, "WebGL 2 is the fallback");
		}
	}
}
