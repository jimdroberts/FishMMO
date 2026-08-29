using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using FishNet.Object;
using FishNet.Serializing;
using UnityEngine;
using NT = FishNet.Component.Transforming.NetworkTransform;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Covers the configurable position compression scale on <see cref="NT"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A packed position is an Int16 of scaled units, so one multiplier fixes both the reachable
	/// range (32766 / multiplier) and the resolution (1 / multiplier). FishNet's stock 100 gives
	/// centimeter precision within +/-327.66 units, which on an 8-20 km^2 map means nearly every
	/// object falls back to 4 bytes per axis. These tests pin the tradeoff at both ends: that the
	/// project multiplier actually buys the packing back, and that lowering it does not quietly
	/// damage anything else.
	/// </para>
	/// <para>
	/// The scale isolation test is the important one. Position and scale shared a single
	/// <c>multiplier</c> local, and scale's read side divides by its own hardcoded 100. Anyone who
	/// "simplifies" this by pointing scale at the position multiplier breaks scale asymmetrically,
	/// and because Scale defaults to Unpacked the damage stays invisible until a prefab enables
	/// packed scale.
	/// </para>
	/// </remarks>
	public class NetworkTransformPrecisionTests
	{
		private const int POSITION_XYZ = 1 | 2 | 4;
		/// <summary>The value shipped on FishMMO prefabs: decimeter grid within +/-3276.6 units.</summary>
		private const float PROJECT_MULTIPLIER = 10f;
		/// <summary>FishNet's stock value, which every unmodified prefab still gets.</summary>
		private const float STOCK_MULTIPLIER = 100f;

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

		// ── the saving ───────────────────────────────────────────────────────

		/// <summary>
		/// The whole point: at the far corner of a 20 km^2 scene the stock multiplier spends
		/// 4 bytes on each horizontal axis, and the project multiplier packs them into 2.
		/// </summary>
		[Test]
		public void FarFromOrigin_ProjectMultiplierPacksWhatTheStockOneCannot()
		{
			Vector3 farCorner = new Vector3(2200f, 40f, 2200f);

			RoundTrip(farCorner, STOCK_MULTIPLIER, out int stockBytes);
			RoundTrip(farCorner, PROJECT_MULTIPLIER, out int projectBytes);
			RoundTrip(new Vector3(12f, 40f, -8f), STOCK_MULTIPLIER, out int nearOriginBytes);

			// X and Z each drop from a 4-byte float to a 2-byte short. Y packs under both.
			Assert.AreEqual(4, stockBytes - projectBytes,
				"Expected X and Z to each fall from 4 bytes to 2 at the 20 km^2 corner.");
			Assert.AreEqual(nearOriginBytes, projectBytes,
				"A far position should cost exactly what a near one costs once it packs.");
			TestContext.WriteLine($"MEASURE nt.farCorner stock={stockBytes}B project={projectBytes}B");
		}

		// ── the cost ─────────────────────────────────────────────────────────

		/// <summary>
		/// Recovery must land within half the grid the multiplier implies. The writer truncates
		/// toward zero rather than rounding, so the bound is a whole grid step, not half of one.
		/// </summary>
		[Test]
		public void RoundTrip_AcrossTheRange_RecoversWithinOneGridStep()
		{
			float step = 1f / PROJECT_MULTIPLIER; // 10 cm
			foreach (Vector3 p in new[]
			{
				new Vector3(0f, 0f, 0f),
				new Vector3(12.34f, 1.5f, -8.76f),
				new Vector3(327.66f, 40f, -327.66f),
				new Vector3(1414f, 120f, -1414f),
				new Vector3(2236f, 300f, -2236f),
				new Vector3(3200f, 10f, -3200f),
			})
			{
				Vector3 got = RoundTrip(p, PROJECT_MULTIPLIER, out _);
				Assert.LessOrEqual(Mathf.Abs(got.x - p.x), step, $"X drifted too far at {p}.");
				Assert.LessOrEqual(Mathf.Abs(got.y - p.y), step, $"Y drifted too far at {p}.");
				Assert.LessOrEqual(Mathf.Abs(got.z - p.z), step, $"Z drifted too far at {p}.");
			}
		}

		/// <summary>
		/// Past the compressed range the writer falls back to a full float. That is graceful
		/// rather than broken: it costs 2 extra bytes per axis and loses no accuracy.
		/// </summary>
		[Test]
		public void BeyondRange_FallsBackToFloatWithoutLosingAccuracy()
		{
			Vector3 outside = new Vector3(4000f, 10f, -4000f);

			Vector3 got = RoundTrip(outside, PROJECT_MULTIPLIER, out int bytes);
			RoundTrip(new Vector3(2200f, 10f, -2200f), PROJECT_MULTIPLIER, out int inside);

			Assert.AreEqual(outside.x, got.x, 0.0001f, "An unpacked axis should survive exactly.");
			Assert.AreEqual(outside.z, got.z, 0.0001f, "An unpacked axis should survive exactly.");
			Assert.AreEqual(4, bytes - inside, "Falling outside the range should cost 2 bytes per axis.");
		}

		// ── the guards ───────────────────────────────────────────────────────

		/// <summary>
		/// The stock value must behave exactly as it did before the field existed, so that an
		/// unmodified prefab and an upstream project see no change at all.
		/// </summary>
		[Test]
		public void StockMultiplier_KeepsCentimeterPrecisionInsideItsRange()
		{
			Vector3 p = new Vector3(112.61f, 30.9f, -47.21f);

			Vector3 got = RoundTrip(p, STOCK_MULTIPLIER, out int bytes);

			Assert.AreEqual(7, bytes, "Stock packing of three axes is a flags byte plus three shorts.");
			Assert.LessOrEqual(Mathf.Abs(got.x - p.x), 0.01f, "Stock precision is one centimeter.");
			Assert.LessOrEqual(Mathf.Abs(got.z - p.z), 0.01f, "Stock precision is one centimeter.");
		}

		/// <summary>
		/// Position and scale share a <c>multiplier</c> local on the write side while scale's read
		/// side divides by its own hardcoded 100. This asserts they stayed independent.
		/// </summary>
		[Test]
		public void ScaleCompression_IsUnaffectedByThePositionMultiplier()
		{
			GameObject go = new GameObject("NtScale");
			try
			{
				NT nt = Build(go, Vector3.zero, PROJECT_MULTIPLIER);
				go.transform.localScale = new Vector3(2.5f, 1.25f, 0.75f);

				// Ask for packed scale, which is the configuration that would expose a shared multiplier.
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
				Assert.Greater(packet.Length, 0, "Packed scale should have produced bytes.");

				Type tdType = NtType.GetNestedType("TransformData", BindingFlags.Public);
				Type changedFull = NtType.GetNestedType("ChangedFull", BindingFlags.NonPublic);
				MethodInfo deserialize = NtType.GetMethod("DeserializePacket", BindingFlags.Instance | BindingFlags.NonPublic);
				object next = Activator.CreateInstance(tdType);
				object[] args = { new ArraySegment<byte>(packet), Activator.CreateInstance(tdType), next, Enum.ToObject(changedFull, 0) };
				deserialize.Invoke(nt, args);
				Vector3 scale = (Vector3)tdType.GetField("Scale").GetValue(next);

				// Scale is still on the stock 100, so it keeps centimeter precision regardless
				// of what the position multiplier is set to.
				Assert.AreEqual(2.5f, scale.x, 0.01f, "Scale X must not be read through the position multiplier.");
				Assert.AreEqual(1.25f, scale.y, 0.01f, "Scale Y must not be read through the position multiplier.");
				Assert.AreEqual(0.75f, scale.z, 0.01f, "Scale Z must not be read through the position multiplier.");
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

		// ── the wiring ───────────────────────────────────────────────────────

		/// <summary>
		/// Every NetworkTransform in the project must carry the project multiplier. A prefab that
		/// misses it silently reverts to the stock 327 m range and stops packing, costing bandwidth
		/// with no error anywhere.
		/// </summary>
		[Test]
		public void EveryNetworkTransformPrefab_CarriesTheProjectMultiplier()
		{
			const string ntGuid = "a2836e36774ca1c4bbbee976e17b649c";
			string root = Application.dataPath;
			int checkedCount = 0;

			foreach (string path in Directory.GetFiles(root, "*.prefab", SearchOption.AllDirectories))
			{
				string text = File.ReadAllText(path);
				if (!text.Contains(ntGuid))
				{
					continue;
				}

				checkedCount++;
				StringAssert.Contains("_positionMultiplier: 10", text,
					$"{Path.GetFileName(path)} has a NetworkTransform without the project position multiplier.");
			}

			Assert.AreEqual(10, checkedCount, "Expected ten NetworkTransform prefabs; the set changed.");
		}
	}
}
