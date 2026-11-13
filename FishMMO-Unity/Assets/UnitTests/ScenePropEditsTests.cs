using System.Collections.Generic;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A developer's hand edit to a generated prop (ScenePropEdits) names the prop by what the generator gave it — its
	/// source, its prefab and where it stood — and is found again by every bake of the same generation; the Scene view tool
	/// (ScenePropEditTool) picks props by ray.
	/// </summary>
	public class ScenePropEditsTests
	{
		private static ScenePropEdits.Edit Moved(string source, string guid, Vector3 origin, Vector3 to) => new ScenePropEdits.Edit
		{
			Source = source, PrefabGuid = guid, Origin = origin, OriginRotation = Quaternion.identity, OriginScale = Vector3.one,
			Position = to, Rotation = Quaternion.identity, Scale = Vector3.one,
		};

		[Test]
		public void AnEdit_IsFoundByItsSourcePrefabAndOrigin_AndNothingElse()
		{
			var edits = new List<ScenePropEdits.Edit>
			{
				Moved("POI", "house", new Vector3(10f, 2f, 10f), new Vector3(11f, 2f, 10f)),
				Moved("POI", "well", new Vector3(10f, 2f, 10f), new Vector3(9f, 2f, 10f)),
				Moved("Cliffs", "house", new Vector3(10f, 2f, 10f), new Vector3(12f, 2f, 10f)),
			};
			LogAssert.IsTrue(ScenePropEditMath.FindByOrigin(edits, "POI", "house", new Vector3(10f, 2f, 10f)) == 0, "its own edit");
			LogAssert.IsTrue(ScenePropEditMath.FindByOrigin(edits, "POI", "well", new Vector3(10.03f, 2f, 10f)) == 1, "within the match distance");
			LogAssert.IsTrue(ScenePropEditMath.FindByOrigin(edits, "Cliffs", "house", new Vector3(10f, 2f, 10f)) == 2, "by source");
			LogAssert.IsTrue(ScenePropEditMath.FindByOrigin(edits, "POI", "house", new Vector3(10.2f, 2f, 10f)) < 0, "a prop generated elsewhere is not it");
			LogAssert.IsTrue(ScenePropEditMath.FindByOrigin(edits, "POI", "barn", new Vector3(10f, 2f, 10f)) < 0, "another prefab is not it");
			LogAssert.IsTrue(ScenePropEditMath.FindByOrigin(edits, "Boulders", "house", new Vector3(10f, 2f, 10f)) < 0, "another source is not it");
		}

		[Test]
		public void AnEditedProp_IsFoundAgainWhereItWasPut_AndARemovedOneIsNot()
		{
			var edits = new List<ScenePropEdits.Edit> { Moved("POI", "house", new Vector3(0f, 0f, 0f), new Vector3(3f, 0f, 0f)) };
			LogAssert.IsTrue(ScenePropEditMath.FindByPlacement(edits, "POI", "house", new Vector3(3f, 0f, 0f)) == 0, "moved again from where it was put");
			LogAssert.IsTrue(ScenePropEditMath.FindByPlacement(edits, "POI", "house", new Vector3(0f, 0f, 0f)) < 0, "not at its origin any more");
			ScenePropEdits.Edit removed = edits[0];
			removed.Removed = true;
			edits[0] = removed;
			LogAssert.IsTrue(ScenePropEditMath.FindByPlacement(edits, "POI", "house", new Vector3(3f, 0f, 0f)) < 0, "a removed prop stands nowhere");
			LogAssert.IsTrue(ScenePropEditMath.FindByOrigin(edits, "POI", "house", Vector3.zero) == 0, "but its generator's prop is still found, to be left out");
		}

		[Test]
		public void TheNearestOfTwoCloseEdits_IsTheOneFound()
		{
			var edits = new List<ScenePropEdits.Edit>
			{
				Moved("POI", "fence", new Vector3(0.04f, 0f, 0f), Vector3.one),
				Moved("POI", "fence", new Vector3(0.01f, 0f, 0f), Vector3.one * 2f),
			};
			LogAssert.IsTrue(ScenePropEditMath.FindByOrigin(edits, "POI", "fence", Vector3.zero) == 1, "the nearer origin wins");
		}

		[Test]
		public void ARay_EntersAPropsBox_WhereverThePropIsTurnedAndScaled()
		{
			var random = new System.Random(511);
			int hits = 0, misses = 0;
			for (int trial = 0; trial < 3000; trial++)
			{
				var local = new Bounds(new Vector3((float)random.NextDouble(), 1f + (float)random.NextDouble(), 0f), new Vector3(2f, 3f, 1.5f));
				var prop = Matrix4x4.TRS(new Vector3((float)random.NextDouble() * 40f - 20f, 0f, (float)random.NextDouble() * 40f - 20f),
					Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f), Vector3.one * (0.5f + (float)random.NextDouble() * 2f));
				// Aimed at a point inside the box (a hit), or at one well outside it (a miss when the ray stays clear).
				bool aimIn = trial % 2 == 0;
				Vector3 target = prop.MultiplyPoint3x4(local.center + (aimIn ? Vector3.zero : new Vector3(0f, 50f, 0f)));
				Vector3 origin = target + new Vector3((float)random.NextDouble() - 0.5f, 0.3f, (float)random.NextDouble() - 0.5f).normalized * 30f;
				Vector3 direction = (target - origin).normalized;
				float d = ScenePropEditMath.RayBoxDistance(origin, direction, prop.inverse, prop, local);
				if (aimIn)
				{
					hits++;
					LogAssert.IsTrue(!float.IsPositiveInfinity(d) && d <= 30f + 1e-3f, $"trial {trial}: aimed at the box's middle, {d}");
				}
				else if (!float.IsPositiveInfinity(d))
				{
					// A hit aimed away from the box must still land on it.
					Vector3 at = prop.inverse.MultiplyPoint3x4(origin + direction * d);
					Vector3 lo = local.min - Vector3.one * 1e-2f, hi = local.max + Vector3.one * 1e-2f;
					LogAssert.IsTrue(at.x >= lo.x && at.y >= lo.y && at.z >= lo.z && at.x <= hi.x && at.y <= hi.y && at.z <= hi.z, $"trial {trial}: a hit off the box");
				}
				else
				{
					misses++;
				}
			}
			LogAssert.IsTrue(hits > 1000 && misses > 500, $"exercised: {hits} aimed in, {misses} missed");
		}

		[Test]
		public void ARay_HitsATriangle_FromEitherSide_AndMissesBesideIt()
		{
			Vector3 a = new Vector3(0f, 0f, 0f), b = new Vector3(1f, 0f, 0f), c = new Vector3(0f, 1f, 0f);
			float front = ScenePropEditMath.RayTriangle(new Vector3(0.2f, 0.2f, -5f), Vector3.forward, a, b, c);
			float back = ScenePropEditMath.RayTriangle(new Vector3(0.2f, 0.2f, 5f), Vector3.back, a, b, c);
			float beside = ScenePropEditMath.RayTriangle(new Vector3(0.8f, 0.8f, -5f), Vector3.forward, a, b, c);
			float behind = ScenePropEditMath.RayTriangle(new Vector3(0.2f, 0.2f, 5f), Vector3.forward, a, b, c);
			LogAssert.IsTrue(Mathf.Abs(front - 5f) < 1e-4f && Mathf.Abs(back - 5f) < 1e-4f, $"both faces at 5 m: {front}, {back}");
			LogAssert.IsTrue(float.IsPositiveInfinity(beside) && float.IsPositiveInfinity(behind), "beside it and behind the ray: no hit");
		}

		[Test]
		public void EveryBake_LaysTheEdits_ThePoiPiecesAsTheyAreMade()
		{
			string baker = ProjectFiles.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/Terrain/ScenePropBaker.cs");
			LogAssert.IsTrue(baker.Contains("edits.ApplyTo(source, prototypes, props)") && baker.Contains("if (source != PointOfInterestGenerator.PropSource)"),
				"every source but the POI's is edited as it is written");
			string site = ProjectFiles.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/PointsOfInterest/PointOfInterestSiteContext.cs");
			LogAssert.IsTrue(site.Contains("ScenePropEdits.Active.Apply(PointOfInterestGenerator.PropSource, prefab, ref position, ref rotation, ref scale)"),
				"a POI piece is edited before its doors, rooms and emitters are laid from it");
			string stage = ProjectFiles.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/PointsOfInterest/PointOfInterestStage.cs");
			LogAssert.IsTrue(stage.Contains("ScenePropEdits.Active = ScenePropEdits.For(scene, false);") && stage.Contains("ScenePropEdits.Active = null;"),
				"the POI stage holds the scene's edits for its length, and lets go");
		}
	}
}
