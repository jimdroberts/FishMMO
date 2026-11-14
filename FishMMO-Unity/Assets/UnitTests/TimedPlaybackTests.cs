using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Timed playback of observed transforms (FISHMMO EDIT in FishNet's NetworkTransform): every observed character is drawn
	/// at one shared point in the past, the smoothed newest received server tick minus the transform's Interpolation, which is
	/// exactly the delay lag compensation rewinds by. Before it, each object's delay was whatever it happened to be when its
	/// playback last restarted (3 or 4 ticks after a stop-and-go), so predicted hits on a character that had just started
	/// moving missed on the server.
	/// </summary>
	public class TimedPlaybackTests
	{
		/// <summary>FishNet's serializer registration does not run under the EditMode runner; the wire test needs it.</summary>
		[OneTimeSetUp]
		public void RegisterProductionSerializers()
		{
			System.Reflection.MethodInfo register = typeof(CharacterReplicateDataDeltaSerializer).GetMethod("RegisterSerializers",
				System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
			LogAssert.IsNotNull(register, "CharacterReplicateDataDeltaSerializer exposes its RegisterSerializers hook.");
			register.Invoke(null, null);
		}

		private static string Read(string relative) =>
			ProjectFiles.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), relative));

		private const string NetworkTransformPath = "Assets/Plugins/FishNet/Runtime/Generated/Component/NetworkTransform/NetworkTransform.cs";

		/// <summary>The delay the client draws at and the delay the server rewinds by are one number.</summary>
		[Test]
		public void EveryNetworkTransformInterpolationIsTheRewindDelay()
		{
			var regex = new Regex(@"^  _interpolation: (\d+)", RegexOptions.Multiline);
			int checkedPrefabs = 0;
			foreach (string path in Directory.GetFiles(Path.Combine(Directory.GetCurrentDirectory(), "Assets"), "*.prefab", SearchOption.AllDirectories))
			{
				string text = ProjectFiles.ReadAllText(path);
				if (!text.Contains("_interpolation:"))
				{
					continue;
				}
				foreach (Match match in regex.Matches(text))
				{
					checkedPrefabs++;
					LogAssert.AreEqual(LagCompensationTick.SpectatorInterpolationTicks.ToString(), match.Groups[1].Value,
						$"{path}: a NetworkTransform drawn at another delay than lag compensation rewinds by turns hits on it into misses");
				}
			}
			LogAssert.IsTrue(checkedPrefabs > 0, "the scan found the character prefabs' NetworkTransforms");
		}

		/// <summary>The pieces that make the delay hold: the clock drives playback, goals wait for their tick, and data is queued.</summary>
		[Test]
		public void ObservedTransformsAreDrawnOnThePlaybackClock()
		{
			string nt = Read(NetworkTransformPath);
			LogAssert.IsTrue(nt.Contains("public static bool TimedPlayback = true;"), "timed playback is on");
			LogAssert.IsTrue(nt.Contains("if (IsTimedPlayback)\n                    MoveToTargetTimed(deltaTime);") ||
				nt.Contains("if (IsTimedPlayback)\r\n                    MoveToTargetTimed(deltaTime);"),
				"a client's per-frame playback runs on the clock");
			LogAssert.IsTrue(nt.Contains("double target = PlaybackClock.DrawTick(_timeManager, _interpolation);"),
				"the draw tick is the clock less the delay (Interpolation at least, more after late data)");
			LogAssert.IsTrue(nt.Contains("double staleness = _reference - remote;") && nt.Contains("if (needed > _extraDelay)"),
				"the delay grows at once with stale data and recovers slowly, so late data slows playback instead of stopping it");
			LogAssert.IsTrue(nt.Contains("TimedStartTick(_goalDataQueue.Peek()) <= _timedTarget"), "a queued goal waits for its tick");
			LogAssert.IsTrue(nt.Contains("nextGd.PlaybackTick = nextTd.Tick;"), "each goal keeps the tick it describes, even a reliable settle");
			LogAssert.IsTrue(nt.Contains("if (queueCount > _interpolation + 1 && !_timedStepActive)"),
				"FishNet's queue-count speed-up does not fight the clock");
		}

		// ── The exact view tick: the client sends the tick it draws at, the server rewinds there ──

		private static double Resolve(uint anchor, uint inputTick, double offset, uint queue = 3)
		{
			LagCompensationTick.EncodeViewTick(offset, out ushort whole, out byte fraction);
			LogAssert.IsTrue(LagCompensationTick.ResolveExactViewTick(anchor, inputTick, whole, fraction, queue, out RewindTarget target),
				$"resolves (anchor {anchor}, input {inputTick}, offset {offset})");
			return target.AsFractionalTick;
		}

		[Test]
		public void TheServerRewindsToExactlyTheTickTheClientDrew()
		{
			// The client's tick counter restarts at connect, so it sits far from the server's: the offset is large and
			// negative, and only its low 16 bits travel.
			uint serverNow = 5_000_000u;
			uint clientInputTick = 1_234u;
			double drawn = serverNow - 9.375;
			double rewound = Resolve(serverNow, clientInputTick, drawn - clientInputTick);
			LogAssert.IsTrue(System.Math.Abs(rewound - drawn) < 1d / 254d, $"rewound to {rewound}, drawn {drawn}");

			// A whole-tick claim and one just short of a whole tick both land exactly.
			LogAssert.IsTrue(System.Math.Abs(Resolve(serverNow, 70_000u, (serverNow - 6d) - 70_000u) - (serverNow - 6d)) < 1e-6, "whole tick");
			double nearlyWhole = serverNow - 4.999;
			LogAssert.IsTrue(System.Math.Abs(Resolve(serverNow, 10u, nearlyWhole - 10u) - nearlyWhole) < 1d / 254d, "fraction near 1");
		}

		[Test]
		public void AClaimFromTheFutureRewindsNothing_AndAnAncientOneIsCapped()
		{
			uint serverNow = 900_000u;
			LagCompensationTick.EncodeViewTick((serverNow + 2.5) - 50u, out ushort whole, out byte fraction);
			LogAssert.IsFalse(LagCompensationTick.ResolveExactViewTick(serverNow, 50u, whole, fraction, 3, out _),
				"a tick ahead of the server is not a view of the past");

			double rewound = Resolve(serverNow, 50u, (serverNow - 400d) - 50u, queue: 3);
			LogAssert.AreEqual((double)(serverNow - (LagCompensationTick.MaximumCompensationTicks + 3 + 1)), rewound,
				"a claim further back than the cap reaches only as far as the cap");

			LogAssert.IsFalse(LagCompensationTick.ResolveExactViewTick(serverNow, 50u, 0, 0, 3, out _),
				"fraction code 0 means no exact claim: the round-trip estimate is used instead");
		}

		[Test]
		public void TheExactViewTickSurvivesTheReplicateWire()
		{
			CharacterReplicateData prev = default;
			prev.AimDirection = AimDirectionCompression.QuantizedFallbackDirection;
			CharacterReplicateData next = prev;
			next.ViewTickOffset = 54321;
			next.ViewTickFraction = 200;

			var full = new FishNet.Serializing.Writer();
			full.WriteCharacterReplicateData(next);
			var fullReader = new FishNet.Serializing.Reader(full.GetArraySegment(), null);
			CharacterReplicateData decoded = fullReader.ReadCharacterReplicateData();
			LogAssert.AreEqual((ushort)54321, decoded.ViewTickOffset, "full form: offset");
			LogAssert.AreEqual((byte)200, decoded.ViewTickFraction, "full form: fraction");
			LogAssert.AreEqual(0, fullReader.Remaining, "full form consumed exactly");

			var delta = new FishNet.Serializing.Writer();
			LogAssert.IsTrue(delta.WriteDelta(prev, next, FishNet.Serializing.DeltaSerializerOption.RootSerialize),
				"a changed exact view tick produces a delta");
			var deltaReader = new FishNet.Serializing.Reader(delta.GetArraySegment(), null);
			CharacterReplicateData decodedDelta = deltaReader.ReadDelta(prev);
			LogAssert.AreEqual((ushort)54321, decodedDelta.ViewTickOffset, "delta form: offset");
			LogAssert.AreEqual((byte)200, decodedDelta.ViewTickFraction, "delta form: fraction");
			LogAssert.AreEqual(0, deltaReader.Remaining, "delta form consumed exactly");
		}
	}
}
