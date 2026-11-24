using System.Linq;
using FishNet.Object;
using FishNet.Serializing;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// <c>/admin region</c>: the words it reads, the box, the ring <c>all</c> lays out, the settings both peers build the
	/// region from, the pulse that paces stay actions, and the prefab the command spawns.
	/// </summary>
	[TestFixture]
	public class TestRegionCommandTests
	{
		private const string PrefabPath = "Assets/FishMMO/Modules/Staff/Runtime/TestRegions/Staff Test Region.prefab";

		private static StaffSystem.TestRegionRequest Parse(string kind, string rest)
		{
			LogAssert.IsTrue(StaffSystem.TryParseTestRegion(kind, rest, out StaffSystem.TestRegionRequest request, out string problem),
				$"'{kind} {rest}' parses ({problem})");
			return request;
		}

		private static void AssertRefused(string kind, string rest)
		{
			LogAssert.IsFalse(StaffSystem.TryParseTestRegion(kind, rest, out _, out string problem), $"'{kind} {rest}' is refused");
			LogAssert.IsFalse(string.IsNullOrEmpty(problem), $"and '{kind} {rest}' says why");
		}

		[Test]
		public void ABuff_TakesItsWholeName_AndTheBoxFromAnywhere()
		{
			StaffSystem.TestRegionRequest request = Parse("buff", "20x10x30 Minor Increase Move Speed");
			LogAssert.AreEqual(StaffTestRegionKind.Buff, request.Kind);
			LogAssert.AreEqual("Minor Increase Move Speed", request.Subject);
			LogAssert.AreEqual(new Vector3(20f, 10f, 30f), request.Size);
		}

		[Test]
		public void AnAttribute_TakesItsAmountLast_ANameWithSpaces_First()
		{
			StaffSystem.TestRegionRequest request = Parse("attribute", "Attack Speed 25 16x16");
			LogAssert.AreEqual(StaffTestRegionKind.Attribute, request.Kind);
			LogAssert.AreEqual("Attack Speed", request.Subject);
			LogAssert.AreEqual(25, request.Amount);
			LogAssert.AreEqual(new Vector3(16f, 8f, 16f), request.Size, "WxD keeps the default height");
		}

		[Test]
		public void AResource_MayDrain()
		{
			StaffSystem.TestRegionRequest request = Parse("resource", "Mana -5");
			LogAssert.AreEqual(StaffTestRegionKind.Resource, request.Kind);
			LogAssert.AreEqual("Mana", request.Subject);
			LogAssert.AreEqual(-5, request.Amount);
		}

		[Test]
		public void Damage_TakesItsAmountFirst_AndAnOptionalType()
		{
			StaffSystem.TestRegionRequest plain = Parse("damage", "5");
			LogAssert.AreEqual(StaffTestRegionKind.Damage, plain.Kind);
			LogAssert.AreEqual(5, plain.Amount);
			LogAssert.AreEqual(string.Empty, plain.Subject);

			StaffSystem.TestRegionRequest typed = Parse("damage", "8 Fire Damage");
			LogAssert.AreEqual(8, typed.Amount);
			LogAssert.AreEqual("Fire Damage", typed.Subject);

			LogAssert.AreEqual(-3, Parse("damage", "-3").Amount, "negative heals");
		}

		[Test]
		public void NameAndFogAndAll_NeedNothing()
		{
			LogAssert.AreEqual(string.Empty, Parse("name", "").Subject);
			LogAssert.AreEqual("The Old Mill", Parse("name", "The Old Mill").Subject);
			LogAssert.AreEqual(0.2f, Parse("fog", "0.2").FogDensity);
			LogAssert.AreEqual(0.05f, Parse("fog", "").FogDensity);
			StaffSystem.TestRegionRequest all = Parse("all", "30x5x30");
			LogAssert.IsTrue(all.All);
			LogAssert.AreEqual(new Vector3(30f, 5f, 30f), all.Size);
		}

		[Test]
		public void MissingOrMalformedWords_AreRefused()
		{
			AssertRefused("buff", "");
			AssertRefused("buff", "20x10x20");
			AssertRefused("attribute", "Armor");
			AssertRefused("attribute", "Armor 0");
			AssertRefused("attribute", "50");
			AssertRefused("resource", "Mana lots");
			AssertRefused("damage", "");
			AssertRefused("damage", "Fire Damage");
			AssertRefused("fog", "thick");
			AssertRefused("fog", "2");
			AssertRefused("volcano", "");
		}

		[Test]
		public void TheBox_IsBounded()
		{
			LogAssert.IsTrue(StaffSystem.TryParseBox("1x1x1", out _, out _));
			LogAssert.IsTrue(StaffSystem.TryParseBox("200x200x200", out _, out _));
			LogAssert.IsFalse(StaffSystem.TryParseBox("201x10x10", out _, out _));
			LogAssert.IsFalse(StaffSystem.TryParseBox("0.5x10x10", out _, out _));
			LogAssert.IsFalse(StaffSystem.TryParseBox("10x10x10x10", out _, out _));
			AssertRefused("buff", "Root 500x10x10");
		}

		[Test]
		public void TheRing_KeepsNeighboursApart_AndTheCallerOutside()
		{
			Vector3 size = new Vector3(12f, 8f, 12f);
			foreach (int count in new[] { 1, 2, 6 })
			{
				float radius = StaffSystem.TestRegionRingRadius(size, count);
				float diagonal = new Vector2(size.x, size.z).magnitude;
				LogAssert.IsTrue(radius > diagonal * 0.5f, $"{count}: the caller stands outside every box");
				if (count > 1)
				{
					float chord = 2f * radius * Mathf.Sin(Mathf.PI / count);
					LogAssert.IsTrue(chord >= diagonal, $"{count}: neighbouring boxes cannot touch, whatever the facing");
				}
			}
		}

		[Test]
		public void Settings_RoundTripThePayload()
		{
			var settings = new StaffTestRegionSettings
			{
				Kind = StaffTestRegionKind.Damage,
				Size = new Vector3(20f, 10f, 30f),
				Label = "Damage 5/s Fire Damage",
				TemplateID = -123456,
				Amount = 5,
				PulseTicks = 30,
				FogDensity = 0.05f,
			};
			Writer writer = new Writer();
			settings.Write(writer);
			writer.WriteInt32(0x5EED);
			Reader reader = new Reader(writer.GetArraySegment(), null);
			StaffTestRegionSettings back = StaffTestRegionSettings.Read(reader);
			LogAssert.AreEqual(settings.Kind, back.Kind);
			LogAssert.AreEqual(settings.Size, back.Size);
			LogAssert.AreEqual(settings.Label, back.Label);
			LogAssert.AreEqual(settings.TemplateID, back.TemplateID);
			LogAssert.AreEqual(settings.Amount, back.Amount);
			LogAssert.AreEqual(settings.PulseTicks, back.PulseTicks);
			LogAssert.AreEqual(settings.FogDensity, back.FogDensity);
			LogAssert.AreEqual(0x5EED, reader.ReadInt32(), "reads exactly what it wrote, so behaviours after it in the payload are not misread");
		}

		[Test]
		public void Sanitize_ClampsTheBox_AndNamesAnUnnamedRegion()
		{
			StaffTestRegionSettings s = StaffTestRegion.Sanitize(new StaffTestRegionSettings
			{
				Size = new Vector3(0f, 1000f, float.NaN),
				Label = "  ",
				PulseTicks = 0,
			});
			LogAssert.AreEqual(new Vector3(StaffTestRegion.MinimumEdge, StaffTestRegion.MaximumEdge, StaffTestRegion.MinimumEdge), s.Size);
			LogAssert.AreEqual("Test Region", s.Label);
			LogAssert.AreEqual(1, s.PulseTicks);
			LogAssert.AreEqual(StaffTestRegion.MaximumLabelLength,
				StaffTestRegion.Sanitize(new StaffTestRegionSettings { Label = new string('a', 200) }).Label.Length);
		}

		[Test]
		public void TheOutline_WalksAllTwelveEdges()
		{
			Vector3[] path = StaffTestRegion.OutlinePath(new Vector3(2f, 4f, 6f));
			var edges = new System.Collections.Generic.HashSet<string>();
			for (int i = 1; i < path.Length; ++i)
			{
				Vector3 a = path[i - 1], b = path[i];
				int axesChanged = (a.x != b.x ? 1 : 0) + (a.y != b.y ? 1 : 0) + (a.z != b.z ? 1 : 0);
				LogAssert.AreEqual(1, axesChanged, $"step {i} runs along one edge of the box, not across a face");
				string ka = a.ToString("F3"), kb = b.ToString("F3");
				edges.Add(string.CompareOrdinal(ka, kb) < 0 ? ka + kb : kb + ka);
			}
			LogAssert.AreEqual(12, edges.Count);
		}

		[Test]
		public void TheShell_CoversAllSixFaces_WoundTheSameWay()
		{
			Vector3[] corners = StaffTestRegion.BoxCorners(new Vector3(2f, 4f, 6f));
			int[] triangles = StaffTestRegion.BoxTriangles;
			LogAssert.AreEqual(8, corners.Length);
			LogAssert.AreEqual(36, triangles.Length, "twelve triangles");
			var faces = new System.Collections.Generic.Dictionary<Vector3, int>();
			for (int t = 0; t < triangles.Length; t += 3)
			{
				Vector3 a = corners[triangles[t]], b = corners[triangles[t + 1]], c = corners[triangles[t + 2]];
				Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
				Vector3 centroid = (a + b + c) / 3f;
				LogAssert.IsTrue(Vector3.Dot(normal, centroid) > 0f, $"triangle {t / 3} faces outwards like the rest");
				Vector3 face = new Vector3(Mathf.Round(normal.x), Mathf.Round(normal.y), Mathf.Round(normal.z));
				faces[face] = faces.TryGetValue(face, out int n) ? n + 1 : 1;
			}
			LogAssert.AreEqual(6, faces.Count, "one face per side");
			LogAssert.IsTrue(faces.Values.All(n => n == 2), "two triangles to each face");
		}

		[Test]
		public void TheStayPulse_PassesOnceAnInterval()
		{
			LogAssert.AreEqual(1, Enumerable.Range(1, 30).Count(t => TickIntervalCondition.Passes(30, (uint)t)));
			LogAssert.AreEqual(30, Enumerable.Range(1, 30).Count(t => TickIntervalCondition.Passes(1, (uint)t)));
			LogAssert.IsFalse(TickIntervalCondition.Passes(30, null), "no clock, no pulse");
			LogAssert.IsTrue(TickIntervalCondition.Passes(1, null));
		}

#if UNITY_EDITOR
		[Test]
		public void ThePrefab_IsASpawnableRegion_ThatSeesPlayers()
		{
			NetworkObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<NetworkObject>(PrefabPath);
			LogAssert.IsNotNull(prefab, PrefabPath + " exists and carries a NetworkObject");
			LogAssert.IsNotNull(prefab.GetComponent<StaffTestRegion>());
			LogAssert.IsNotNull(prefab.GetComponent<Region>());
			LogAssert.IsNotNull(prefab.GetComponent<BoxCollider>());
			LogAssert.IsFalse(prefab.EnableStateForwarding, "state forwarding is for platforms only");

			var trigger = prefab.GetComponent<FishNet.Component.Prediction.NetworkTrigger>();
			LogAssert.IsNotNull(trigger);
			// Layers 0 detects nothing, silently: the region would never see anyone walk in.
			LogAssert.IsTrue((trigger.GetLayers().value & LayerMask.GetMask("Player")) != 0, "the trigger polls the Player layer");

			string[] collections = UnityEditor.AssetDatabase.FindAssets("t:DefaultPrefabObjects");
			LogAssert.IsTrue(collections.Length > 0);
			var objects = UnityEditor.AssetDatabase.LoadAssetAtPath<FishNet.Managing.Object.DefaultPrefabObjects>(
				UnityEditor.AssetDatabase.GUIDToAssetPath(collections[0]));
			bool listed = false;
			for (int i = 0; i < objects.GetObjectCount() && !listed; ++i)
			{
				listed = objects.GetObject(true, i) == prefab;
			}
			LogAssert.IsTrue(listed, "the prefab is in the spawnable prefabs, or clients cannot spawn it");
		}
#endif
	}
}
