using System;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using FishMMO.Shared;
using FishNet.Object;
using FishNet.Serializing;
using UnityEngine;
using NT = FishNet.Component.Transforming.NetworkTransform;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Covers position packing on <see cref="NT"/>: 24-bit integers of scaled units (FISHMMO EDIT)
	/// where FishNet ships 16-bit ones.
	/// </summary>
	/// <remarks>
	/// <para>
	/// One multiplier fixes both the reachable range (8388606 / multiplier) and the resolution
	/// (1 / multiplier). At the stock 100 that is centimetre precision out to +/-83,886 units, so
	/// every position on an 8-20 km^2 map packs, for 3 bytes per axis. The 16-bit form could only
	/// reach the map by dropping the multiplier to 10, and a 10 cm wire grid against a walking
	/// character's 5 cm per tick rendered as alternating stalls and double-speed hops. These tests
	/// pin the resolution against the slowest gait as well as the range against the map.
	/// </para>
	/// <para>
	/// The scale isolation test is the important guard. Position and scale shared a single
	/// <c>multiplier</c> local, and scale's read side divides by its own hardcoded 100 and still
	/// reads 16 bits. Anyone who "simplifies" this by pointing scale at the position path breaks
	/// scale asymmetrically, and because Scale defaults to Unpacked the damage stays invisible
	/// until a prefab enables packed scale.
	/// </para>
	/// </remarks>
	public class NetworkTransformPrecisionTests
	{
		private const int POSITION_XYZ = 1 | 2 | 4;
		/// <summary>The value on every FishMMO prefab, and FishNet's stock: centimetre grid.</summary>
		private const float PROJECT_MULTIPLIER = 100f;
		/// <summary>Half a step is 5 mm; float error at a few km adds a couple of millimetres.</summary>
		private const float HALF_STEP_TOLERANCE = 0.0075f;
		/// <summary>One wire grid step: a send is scheduled once an axis has moved a whole cell.</summary>
		private const float PROJECT_SENSITIVITY = 0.01f;
		/// <summary>A flags byte plus three 24-bit axes.</summary>
		private const int PACKED_XYZ_BYTES = 1 + 3 * 3;

		private static readonly Type NtType = typeof(NT);

		/// <summary>Builds an unspawned NetworkTransform at <paramref name="position"/>.</summary>
		private static NT Build(GameObject go, Vector3 position, float multiplier)
		{
			go.transform.localPosition = position;
			NT nt = go.AddComponent<NT>();
			NtType.GetField("_cachedTransform", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(nt, go.transform);
			NtType.GetField("_positionMultiplier", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(nt, multiplier);
			/* DeserializePacket reads NetworkManager, which dereferences the NetworkObject cache.
			 * That cache is only populated during spawn, so an unspawned component throws before
			 * reaching any of the logic under test. Adding the component auto-adds a NetworkObject;
			 * pointing the cache at it leaves NetworkManager null, which the reader accepts. */
			typeof(NetworkBehaviour)
				.GetField("_networkObjectCache", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(nt, go.GetComponent<NetworkObject>());
			return nt;
		}

		/// <summary>Serializes the position axes and returns the raw bytes.</summary>
		private static byte[] Serialize(NT nt, int changedMask)
		{
			Type changedDelta = NtType.GetNestedType("ChangedDelta", BindingFlags.NonPublic);
			MethodInfo serialize = NtType.GetMethod("SerializeChanged", BindingFlags.Instance | BindingFlags.NonPublic);
			PooledWriter writer = WriterPool.Retrieve();
			try
			{
				serialize.Invoke(nt, new[] { Enum.ToObject(changedDelta, changedMask), writer });
				ArraySegment<byte> seg = writer.GetArraySegment();
				byte[] copy = new byte[seg.Count];
				Array.Copy(seg.Array, seg.Offset, copy, 0, seg.Count);
				return copy;
			}
			finally { writer.Store(); }
		}

		/// <summary>Runs FishNet's own reader over <paramref name="packet"/> and returns the position.</summary>
		private static Vector3 Deserialize(NT nt, byte[] packet)
		{
			Type tdType = NtType.GetNestedType("TransformData", BindingFlags.Public);
			Type changedFull = NtType.GetNestedType("ChangedFull", BindingFlags.NonPublic);
			MethodInfo deserialize = NtType.GetMethod("DeserializePacket", BindingFlags.Instance | BindingFlags.NonPublic);
			object prev = Activator.CreateInstance(tdType);
			object next = Activator.CreateInstance(tdType);
			object[] args = { new ArraySegment<byte>(packet), prev, next, Enum.ToObject(changedFull, 0) };
			deserialize.Invoke(nt, args);
			return (Vector3)tdType.GetField("Position").GetValue(next);
		}

		/// <summary>Serialize then deserialize through the production paths.</summary>
		private static Vector3 RoundTrip(Vector3 position, float multiplier, out int bytes)
		{
			GameObject go = new GameObject("NtPrecision");
			try
			{
				NT nt = Build(go, position, multiplier);
				byte[] packet = Serialize(nt, POSITION_XYZ);
				bytes = packet.Length;
				return Deserialize(nt, packet);
			}
			finally { UnityEngine.Object.DestroyImmediate(go); }
		}

		/// <summary>Every prefab carrying a NetworkTransform, by the script's GUID.</summary>
		private static string[] NetworkTransformPrefabs()
		{
			const string ntGuid = "a2836e36774ca1c4bbbee976e17b649c";
			return Array.FindAll(
				Directory.GetFiles(Application.dataPath, "*.prefab", SearchOption.AllDirectories),
				path => File.ReadAllText(path).Contains(ntGuid));
		}

		// ── range ────────────────────────────────────────────────────────────

		/// <summary>
		/// The far corner of a 20 km^2 scene packs at the same cost and precision as a position
		/// next to the origin. Under the 16-bit form it cost 4 bytes per horizontal axis instead.
		/// </summary>
		[Test]
		public void FarFromOrigin_PacksAtCentimetrePrecision()
		{
			Vector3 farCorner = new Vector3(2200f, 40f, 2200f);

			Vector3 got = RoundTrip(farCorner, PROJECT_MULTIPLIER, out int farBytes);
			RoundTrip(new Vector3(12f, 40f, -8f), PROJECT_MULTIPLIER, out int nearOriginBytes);

			Assert.AreEqual(PACKED_XYZ_BYTES, farBytes, "Three packed axes are a flags byte plus 3 bytes each.");
			Assert.AreEqual(nearOriginBytes, farBytes, "A far position must cost exactly what a near one costs.");
			Assert.LessOrEqual(Mathf.Abs(got.x - farCorner.x), HALF_STEP_TOLERANCE, "X lost centimetre precision far from the origin.");
			Assert.LessOrEqual(Mathf.Abs(got.z - farCorner.z), HALF_STEP_TOLERANCE, "Z lost centimetre precision far from the origin.");
			TestContext.WriteLine($"MEASURE nt.farCorner packed={farBytes}B");
		}

		/// <summary>
		/// Recovery lands within half a grid step everywhere on any map this project will ship:
		/// the writer rounds rather than truncates, so the bound is half a step, not a whole one.
		/// </summary>
		[Test]
		public void RoundTrip_AcrossTheRange_RecoversWithinHalfAGridStep()
		{
			foreach (Vector3 p in new[]
			{
				new Vector3(0f, 0f, 0f),
				new Vector3(12.34f, 1.5f, -8.76f),
				new Vector3(327.66f, 40f, -327.66f),
				new Vector3(1414f, 120f, -1414f),
				new Vector3(2236f, 300f, -2236f),
				new Vector3(3200f, 10f, -3200f),
				new Vector3(20000.12f, 500.5f, -20000.12f),
			})
			{
				Vector3 got = RoundTrip(p, PROJECT_MULTIPLIER, out int bytes);
				Assert.AreEqual(PACKED_XYZ_BYTES, bytes, $"{p} should pack on every axis.");
				Assert.LessOrEqual(Mathf.Abs(got.x - p.x), HALF_STEP_TOLERANCE, $"X drifted too far at {p}.");
				Assert.LessOrEqual(Mathf.Abs(got.y - p.y), HALF_STEP_TOLERANCE, $"Y drifted too far at {p}.");
				Assert.LessOrEqual(Mathf.Abs(got.z - p.z), HALF_STEP_TOLERANCE, $"Z drifted too far at {p}.");
			}
		}

		/// <summary>
		/// Truncation toward zero put a two-cell dead band around every axis origin and biased
		/// every sample toward it. Rounding has neither property.
		/// </summary>
		[Test]
		public void Packing_RoundsToTheNearestCell_InBothDirections()
		{
			Vector3 got = RoundTrip(new Vector3(0.006f, -0.006f, 0.004f), PROJECT_MULTIPLIER, out _);
			Assert.AreEqual(0.01f, got.x, 0.0001f, "+6 mm rounds up to +1 cm; truncation would have read 0.");
			Assert.AreEqual(-0.01f, got.y, 0.0001f, "-6 mm rounds down to -1 cm; truncation would have read 0.");
			Assert.AreEqual(0f, got.z, 0.0001f, "+4 mm rounds to 0.");
		}

		/// <summary>
		/// Past the compressed range the writer falls back to a full float. That is graceful
		/// rather than broken: it costs 1 extra byte per axis and loses no accuracy.
		/// </summary>
		[Test]
		public void BeyondRange_FallsBackToFloatWithoutLosingAccuracy()
		{
			Vector3 outside = new Vector3(90000f, 10f, -90000f);

			Vector3 got = RoundTrip(outside, PROJECT_MULTIPLIER, out int bytes);
			RoundTrip(new Vector3(2200f, 10f, -2200f), PROJECT_MULTIPLIER, out int inside);

			Assert.AreEqual(outside.x, got.x, 0.0001f, "An unpacked axis should survive exactly.");
			Assert.AreEqual(outside.z, got.z, 0.0001f, "An unpacked axis should survive exactly.");
			Assert.AreEqual(2, bytes - inside, "Falling outside the range should cost 1 byte per axis.");
		}

		// ── resolution ───────────────────────────────────────────────────────

		/// <summary>
		/// The reason the grid is 1 cm and not 10: the interpolator plays each received goal at
		/// (quantised distance / tick difference), so a grid coarser than a tick's displacement
		/// turns a steady walk into stalls and hops. The slowest gait sets the bound.
		/// </summary>
		[Test]
		public void WireGrid_IsWellUnderAWalkingTicksDisplacement()
		{
			string scene = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scenes/Server/SceneServer.unity"));
			Match tick = Regex.Match(scene, @"^\s+_tickRate:\s*(\d+)\s*$", RegexOptions.Multiline);
			Assert.IsTrue(tick.Success, "SceneServer.unity no longer serializes a _tickRate; update this test.");
			float tickRate = float.Parse(tick.Groups[1].Value, CultureInfo.InvariantCulture);
			float walkingTick = Constants.Character.WalkSpeed / tickRate;

			int checkedCount = 0;
			foreach (string path in NetworkTransformPrefabs())
			{
				checkedCount++;
				Match multiplier = Regex.Match(File.ReadAllText(path), @"_positionMultiplier:\s*([0-9.]+)");
				Assert.IsTrue(multiplier.Success, $"{Path.GetFileName(path)} has no _positionMultiplier.");
				float gridStep = 1f / float.Parse(multiplier.Groups[1].Value, CultureInfo.InvariantCulture);

				Assert.LessOrEqual(gridStep, walkingTick / 4f,
					$"{Path.GetFileName(path)}: a {gridStep} m wire grid against {walkingTick} m per walking tick " +
					"(WalkSpeed / tick rate) quantises the per-tick delta to a handful of values; the interpolator " +
					"renders that as stalls and hops. Keep the grid at or under a quarter of a walking tick.");
			}
			Assert.AreEqual(10, checkedCount, "Expected ten NetworkTransform prefabs; the set changed.");
		}

		// ── guards ───────────────────────────────────────────────────────────

		/// <summary>Three packed axes: a flags byte plus 3 bytes each, at centimetre precision.</summary>
		[Test]
		public void PackedAxes_CostThreeBytesEach_AtCentimetrePrecision()
		{
			Vector3 p = new Vector3(112.61f, 30.9f, -47.21f);

			Vector3 got = RoundTrip(p, PROJECT_MULTIPLIER, out int bytes);

			Assert.AreEqual(PACKED_XYZ_BYTES, bytes, "Packing three axes is a flags byte plus three 24-bit integers.");
			Assert.LessOrEqual(Mathf.Abs(got.x - p.x), HALF_STEP_TOLERANCE, "Precision is one centimetre.");
			Assert.LessOrEqual(Mathf.Abs(got.z - p.z), HALF_STEP_TOLERANCE, "Precision is one centimetre.");
		}

		/// <summary>
		/// Position and scale share a <c>multiplier</c> local on the write side while scale's read
		/// side divides by its own hardcoded 100 and reads 16 bits. This asserts they stayed independent.
		/// </summary>
		[Test]
		public void ScaleCompression_IsUnaffectedByThePositionPacking()
		{
			GameObject go = new GameObject("NtScale");
			try
			{
				// A deliberately odd position multiplier, so any leak into the scale path shows.
				NT nt = Build(go, Vector3.zero, 10f);
				go.transform.localScale = new Vector3(2.5f, 1.25f, 0.75f);

				// Ask for packed scale, which is the configuration that would expose a shared path.
				Type packingType = NtType.Assembly.GetType("FishNet.Serializing.TransformPackingData");
				Assert.IsNotNull(packingType, "TransformPackingData moved; this guard needs updating.");
				FieldInfo packingField = NtType.GetField("_packing", BindingFlags.Instance | BindingFlags.NonPublic);
				object packing = packingField.GetValue(nt);
				FieldInfo scaleField = packingType.GetField("Scale");
				// Reference type, so mutating in place is enough.
				scaleField.SetValue(packing, Enum.Parse(scaleField.FieldType, "Packed"));

				// ChangedDelta scale bits sit above position and rotation.
				Type changedDelta = NtType.GetNestedType("ChangedDelta", BindingFlags.NonPublic);
				int scaleMask = 0;
				foreach (string name in new[] { "ScaleX", "ScaleY", "ScaleZ", "Extended" })
				{
					object v = Enum.Parse(changedDelta, name);
					scaleMask |= Convert.ToInt32(v);
				}

				byte[] packet = Serialize(nt, scaleMask);
				// Flags A, flags B, three Int16 scale axes: the stock 16-bit scale form.
				Assert.AreEqual(2 + 3 * 2, packet.Length, "Packed scale must keep FishNet's 16-bit form.");

				Type tdType = NtType.GetNestedType("TransformData", BindingFlags.Public);
				Type changedFull = NtType.GetNestedType("ChangedFull", BindingFlags.NonPublic);
				MethodInfo deserialize = NtType.GetMethod("DeserializePacket", BindingFlags.Instance | BindingFlags.NonPublic);
				object next = Activator.CreateInstance(tdType);
				object[] args = { new ArraySegment<byte>(packet), Activator.CreateInstance(tdType), next, Enum.ToObject(changedFull, 0) };
				deserialize.Invoke(nt, args);
				Vector3 scale = (Vector3)tdType.GetField("Scale").GetValue(next);

				// Scale is still on the stock 100, so it keeps centimeter precision regardless
				// of what the position multiplier is set to.
				Assert.AreEqual(2.5f, scale.x, 0.01f, "Scale X must not be read through the position path.");
				Assert.AreEqual(1.25f, scale.y, 0.01f, "Scale Y must not be read through the position path.");
				Assert.AreEqual(0.75f, scale.z, 0.01f, "Scale Z must not be read through the position path.");
			}
			finally { UnityEngine.Object.DestroyImmediate(go); }
		}

		/// <summary>
		/// A zero or negative value in an asset must not reach the divide on the read side, which
		/// would resolve every position to infinity or flip its sign.
		/// </summary>
		[Test]
		public void NonPositiveMultiplier_FallsBackToTheStockScale()
		{
			foreach (float bad in new[] { 0f, -10f })
			{
				Vector3 got = RoundTrip(new Vector3(112.61f, 30.9f, -47.21f), bad, out _);
				Assert.IsFalse(float.IsNaN(got.x) || float.IsInfinity(got.x), $"Multiplier {bad} produced {got.x}.");
				Assert.AreEqual(112.61f, got.x, 0.01f, $"Multiplier {bad} should fall back to the stock scale.");
			}
		}

		// ── wiring ───────────────────────────────────────────────────────────

		/// <summary>
		/// Sends are gated on <c>_positionSensitivity</c>, measured against the last <em>sent</em>
		/// position, so movement below it accumulates rather than being lost. A sensitivity far
		/// finer than the wire grid just schedules packets that decode to the value already sent.
		/// </summary>
		[Test]
		public void MovementBelowTheSensitivity_DoesNotMarkThePositionChanged()
		{
			GameObject go = new GameObject("NtSensitivity");
			try
			{
				NT nt = Build(go, Vector3.zero, PROJECT_MULTIPLIER);
				NtType.GetField("_positionSensitivity", BindingFlags.Instance | BindingFlags.NonPublic)
					.SetValue(nt, PROJECT_SENSITIVITY);
				MethodInfo getChanged = NtType.GetMethod("GetChanged",
					BindingFlags.Instance | BindingFlags.NonPublic,
					null,
					new[] { typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(NetworkBehaviour) },
					null);
				Assert.IsNotNull(getChanged, "GetChanged signature changed; this guard needs updating.");

				Vector3 lastSent = Vector3.zero;
				Quaternion rotation = go.transform.localRotation;
				Vector3 scale = go.transform.localScale;

				int PositionBits(Vector3 to)
				{
					go.transform.localPosition = to;
					object result = getChanged.Invoke(nt, new object[] { lastSent, rotation, scale, null });
					return Convert.ToInt32(result) & POSITION_XYZ;
				}

				Assert.AreEqual(0, PositionBits(new Vector3(0.004f, 0f, 0f)),
					"A 4 mm move is inside the sensitivity and should not schedule a send.");
				Assert.AreEqual(0, PositionBits(new Vector3(0.0099f, 0f, 0f)),
					"A move just under the sensitivity should not schedule a send.");
				Assert.AreEqual(1, PositionBits(new Vector3(0.012f, 0f, 0f)),
					"A 1.2 cm move exceeds the sensitivity and should mark X changed.");
			}
			finally { UnityEngine.Object.DestroyImmediate(go); }
		}

		/// <summary>
		/// The sensitivity and the multiplier have to be chosen together: the first decides how far
		/// something moves before a packet is scheduled, the second how finely that packet can
		/// describe it.
		/// </summary>
		/// <remarks>
		/// Far below the grid step, sends carry no new information. Above it, positions go stale by
		/// more than the quantisation already costs. This pins the pair inside that band so neither
		/// can be retuned alone.
		/// </remarks>
		[Test]
		public void SensitivityAndMultiplier_StayCoherentOnEveryPrefab()
		{
			int checkedCount = 0;

			foreach (string path in NetworkTransformPrefabs())
			{
				string text = File.ReadAllText(path);
				checkedCount++;
				string name = Path.GetFileName(path);
				Match multiplier = Regex.Match(text, @"_positionMultiplier:\s*([0-9.]+)");
				Match sensitivity = Regex.Match(text, @"_positionSensitivity:\s*([0-9.]+)");
				Assert.IsTrue(multiplier.Success, $"{name} has no _positionMultiplier.");
				Assert.IsTrue(sensitivity.Success, $"{name} has no _positionSensitivity.");

				float gridStep = 1f / float.Parse(multiplier.Groups[1].Value, CultureInfo.InvariantCulture);
				float sensitivityValue = float.Parse(sensitivity.Groups[1].Value, CultureInfo.InvariantCulture);

				Assert.GreaterOrEqual(sensitivityValue, gridStep * 0.25f,
					$"{name}: sensitivity {sensitivityValue} is far finer than the {gridStep} wire grid, " +
					"so it schedules sends that cannot carry new information.");
				Assert.LessOrEqual(sensitivityValue, gridStep * 1.0001f,
					$"{name}: sensitivity {sensitivityValue} exceeds the {gridStep} wire grid, " +
					"so positions go stale by more than quantisation already costs.");
			}

			Assert.AreEqual(10, checkedCount, "Expected ten NetworkTransform prefabs; the set changed.");
		}

		/// <summary>
		/// Every NetworkTransform in the project must carry the project multiplier. The 24-bit
		/// form reaches the whole map at 100, so there is no longer any reason for a prefab to
		/// deviate — and a coarser value silently brings the walking stutter back.
		/// </summary>
		[Test]
		public void EveryNetworkTransformPrefab_CarriesTheProjectMultiplier()
		{
			int checkedCount = 0;

			foreach (string path in NetworkTransformPrefabs())
			{
				checkedCount++;
				StringAssert.Contains("_positionMultiplier: 100\n", File.ReadAllText(path).Replace("\r\n", "\n"),
					$"{Path.GetFileName(path)} has a NetworkTransform without the project position multiplier (100).");
			}

			Assert.AreEqual(10, checkedCount, "Expected ten NetworkTransform prefabs; the set changed.");
		}
	}
}
