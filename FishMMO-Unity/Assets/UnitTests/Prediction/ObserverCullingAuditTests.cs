using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the outcome of the 2026-09-08 observer condition audit: entities exist out to 100 m and
	/// are shaped by rate, not by despawning.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The stack has three layers — existence (observer conditions), fidelity
	/// (<c>NetworkTransformDistanceLod</c> and the streaming policy), and content (broadcasts, which
	/// follow existence exactly). The audit found existence doing fidelity's job in four separate
	/// places: monsters gone at 50 m, merchants at 30 m, waypoints at 15 m, and a density shrink that
	/// quietly halved every authored range in the one situation ranges are authored for.
	/// </para>
	/// <para>
	/// Every assertion here is one of those four, or one of the two invariants that make the new
	/// numbers real rather than decorative: the grid has to reach past the hysteresis band, and the
	/// LOD table has to have a band covering the far edge.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ObserverCullingAuditTests
	{
		/// <summary>Conditions that gate a CHARACTER, all of which must reach the full range.</summary>
		private static readonly string[] CharacterConditions =
		{
			"PlayerDistanceCondition.asset",
			"MonsterDistanceCondition.asset",
			"InteractableDistanceCondition.asset",
		};

		/// <summary>The range every character-bearing condition must reach, in metres.</summary>
		private const float RequiredCharacterRange = 100f;

		/// <summary>Script guid of <see cref="ClassifiedDistanceCondition"/>.</summary>
		private static string ClassifiedConditionScriptGuid => Regex.Match(
			File.ReadAllText(Path.Combine(Application.dataPath,
				"Scripts/Shared/Implementation/Entity/Prediction/ObserverStreaming/ClassifiedDistanceCondition.cs.meta")),
			@"guid: ([0-9a-f]{32})").Groups[1].Value;

		private static string ConditionPath(string fileName)
			=> Path.Combine(Application.dataPath, "Settings/ObserverConditions", fileName);

		private static float Field(string fileName, string field)
		{
			string path = ConditionPath(fileName);
			LogAssert.IsTrue(File.Exists(path), $"{fileName} not found at {path}.");
			Match m = Regex.Match(File.ReadAllText(path), field + @": ([0-9.]+)");
			LogAssert.IsTrue(m.Success, $"{fileName} must author {field}.");
			return float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
		}

		/// <summary>Furthest an object with this condition is ever visible: base × (1 + hide).</summary>
		private static float HideDistance(string fileName)
			=> Field(fileName, "_maximumDistance") * (1f + Field(fileName, "_hideDistancePercent"));

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
		}

		/// <summary>
		/// No condition that spawns a character may cull inside 100 m.
		/// </summary>
		/// <remarks>
		/// A distance condition is an EXISTENCE gate: below its range the object is despawned on that
		/// client, taking its nameplate, audio source, buffs and every broadcast addressed to its
		/// observers with it. Monsters at 50 m and merchants at 30 m were therefore not a bandwidth
		/// setting but a gameplay one — you could not see a fight across a clearing, and a town's
		/// vendors materialised as you walked into them. Bandwidth beyond the near field belongs to
		/// the LOD intervals and <see cref="ObserverStreamingPolicy.VisibilityBudget"/>.
		/// </remarks>
		[Test]
		public void CharacterConditions_ReachTheFullObserverRange()
		{
			foreach (string fileName in CharacterConditions)
			{
				float distance = Field(fileName, "_maximumDistance");
				LogAssert.IsTrue(distance >= RequiredCharacterRange,
					$"{fileName} culls at {distance} m. A character inside {RequiredCharacterRange} m must " +
					"EXIST on the client and be slowed by the LOD instead — a despawn takes its audio, " +
					"nameplate and combat broadcasts with it.");
			}
		}

		/// <summary>
		/// Waypoints reach as far as characters; dropped items deliberately do not.
		/// </summary>
		/// <remarks>
		/// Both used to share one 15 m condition, which is why a fast-travel node you were walking
		/// towards appeared only once you had arrived. They are split because their costs differ in
		/// kind: waypoints are a fixed handful of landmarks per scene, while dropped loot is
		/// unbounded in count and — unlike characters — is not capped by the visibility budget, so
		/// distance is the only thing bounding it.
		/// </remarks>
		[Test]
		public void Waypoints_ReachFurtherThanDroppedItems()
		{
			float waypoint = Field("WaypointDistanceCondition.asset", "_maximumDistance");
			float worldItem = Field("WorldItemDistanceCondition.asset", "_maximumDistance");

			LogAssert.IsTrue(waypoint >= RequiredCharacterRange,
				$"A waypoint is a landmark you navigate towards; at {waypoint} m it appears after you arrive.");
			LogAssert.IsTrue(worldItem < waypoint,
				$"Dropped items ({worldItem} m) are unbounded in count and outside the visibility budget, " +
				"so their range is the only bound on how many a client spawns.");

			string prefab = ReadSource("Assets/Prefabs/Shared/Entity/Interactables/Waypoints/Waypoint.prefab");
			string guid = Regex.Match(
				ReadSource("Assets/Settings/ObserverConditions/WaypointDistanceCondition.asset.meta"),
				@"guid: ([0-9a-f]{32})").Groups[1].Value;
			LogAssert.IsTrue(prefab.Contains(guid),
				"The Waypoint prefab must reference the waypoint condition, or the split is only on disk.");
		}

		/// <summary>
		/// The density shrink does not reduce an authored range.
		/// </summary>
		/// <remarks>
		/// <para>
		/// It was a third existence cull and the weakest of the three: it hid by DISTANCE and per
		/// OBJECT, shrinking a character's range symmetrically towards everyone including the viewers
		/// who most needed it, while <see cref="ObserverStreamingPolicy.VisibilityBudget"/> bounds the
		/// same quantity — pairs = viewers × budget — by RELEVANCE and per VIEWER, keeping party
		/// members, targets and opponents the shrink was blind to.
		/// </para>
		/// <para>
		/// Its practical effect was to undo the authored range in exactly the place it was authored
		/// for: at the old scale of 0.5 a busy town became a 50 m world. The machinery stays for a
		/// deployment that measures a need for it; the default must not fire.
		/// </para>
		/// </remarks>
		[Test]
		public void DensityShrink_DoesNotReduceAnAuthoredRange()
		{
			LogAssert.AreEqual(RequiredCharacterRange,
				ObserverStreamingPolicy.ScaledRange(RequiredCharacterRange, 10000),
				"Any crowd, at the default scale, must leave the authored range alone — the visibility " +
				"budget is what bounds a client's cost, and it does so by relevance.");
		}

		/// <summary>
		/// If the shrink is re-enabled, its floor still covers the reach of an ability.
		/// </summary>
		/// <remarks>
		/// The old 25 m floor sat below <see cref="ObserverStreamingPolicy.EngagementRange"/>, the
		/// radius inside which lag compensation assumes a tick-exact stream. A shrink reaching its
		/// floor could therefore despawn a character from inside the range it was being shot from,
		/// which no pin protects against: the pins answer the budget condition, not the distance one.
		/// </remarks>
		[Test]
		public void DensityFloor_StaysOutsideTheEngagementRadius()
		{
			LogAssert.IsTrue(
				ObserverStreamingPolicy.MinimumRange >= ObserverStreamingPolicy.EngagementRange,
				$"MinimumRange {ObserverStreamingPolicy.MinimumRange} m is inside the " +
				$"{ObserverStreamingPolicy.EngagementRange} m engagement radius — a re-enabled shrink " +
				"could despawn a character from inside the range it is being attacked from.");
		}

		/// <summary>
		/// Every classification has exactly one condition asset, and no two share a classification.
		/// </summary>
		/// <remarks>
		/// The asset IS the configuration: one file per kind carrying both its range and its budget. Two
		/// assets claiming the same classification would be two budgets for one kind, and whichever
		/// object was ranked first would silently decide which applied.
		/// </remarks>
		[Test]
		public void EveryClassification_HasExactlyOneConditionAsset()
		{
			Dictionary<int, string> byClassification = new Dictionary<int, string>();
			foreach (string path in Directory.GetFiles(
				Path.Combine(Application.dataPath, "Settings/ObserverConditions"), "*DistanceCondition.asset"))
			{
				string name = Path.GetFileName(path);
				string text = File.ReadAllText(path);

				LogAssert.IsTrue(text.Contains(ClassifiedConditionScriptGuid),
					$"{name} is still a plain FishNet DistanceCondition — it carries no classification, so it " +
					"falls back to the shared budget and cannot be tuned separately.");

				int classification = (int)Field(name, "classification");
				LogAssert.IsFalse(byClassification.ContainsKey(classification),
					$"{name} and {(byClassification.TryGetValue(classification, out string other) ? other : "?")} " +
					$"both claim classification {classification}.");
				byClassification[classification] = name;
			}

			foreach (ObserverClassification classification in System.Enum.GetValues(typeof(ObserverClassification)))
			{
				LogAssert.IsTrue(byClassification.ContainsKey((int)classification),
					$"{classification} has no condition asset, so nothing of that kind can ever be authored.");
			}
		}

		/// <summary>
		/// A Titan reaches 1000 m and is never culled by the budget.
		/// </summary>
		/// <remarks>
		/// "Always visible when in the scene" is expressed as a budget of 0 — unlimited — so a Titan's
		/// range is the only thing that ever hides it. A positive budget here would mean the tenth
		/// player to walk into the valley stops seeing the god standing in it.
		/// </remarks>
		[Test]
		public void Titans_ReachTheWholeZoneAndAreNeverBudgetedOut()
		{
			LogAssert.AreEqual(1000f, Field("TitanDistanceCondition.asset", "_maximumDistance"),
				"A Titan is meant to be visible across the zone it walks.");
			LogAssert.AreEqual(0f, Field("TitanDistanceCondition.asset", "visibilityBudget"),
				"0 means unlimited. Any positive budget would cull a Titan from a crowded viewer.");
		}

		/// <summary>
		/// Monsters are budgeted, and their budget is not shared with anything else.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This is the regression for a real defect: the exemption that kept service NPCs out of the
		/// budget was keyed on <c>IInteractable</c>, which <c>NPC</c> implements directly — that is how a
		/// corpse is looted — so every orc in the game satisfied it and the visibility budget stopped
		/// bounding the one population it exists to bound.
		/// </para>
		/// <para>
		/// Per-classification budgets removed the need for any exemption at all, which is why the test
		/// asserts the mechanism is gone rather than that the predicate was corrected.
		/// </para>
		/// </remarks>
		[Test]
		public void Monsters_AreBudgetedAndNothingIsExemptedByInterface()
		{
			LogAssert.IsTrue(Field("MonsterDistanceCondition.asset", "visibilityBudget") > 0f,
				"Monsters must stay capped — they are mobile, predicted and the most numerous thing in a field.");

			string entry = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/ObserverStreaming/ObserverStreamingEntry.cs");
			LogAssert.IsFalse(entry.Contains("GetComponent<IInteractable>"),
				"NPC implements IInteractable, so this predicate exempted every monster from the budget.");
			LogAssert.IsFalse(entry.Contains("IsBudgetExempt"),
				"Exemptions are replaced by per-classification budgets; two mechanisms would drift apart.");
		}

		/// <summary>
		/// The registry ranks within a classification, not across all of them.
		/// </summary>
		/// <remarks>
		/// Separate budgets only mean anything if the ranks they are compared against are separate too.
		/// A single global rank with per-kind budgets would be worse than either: the fortieth player
		/// would exhaust the Interactable budget's rank space and evict a banker that had no competitors.
		/// </remarks>
		[Test]
		public void Ranking_IsPerClassification()
		{
			string registry = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/ObserverStreaming/ObserverStreamingRegistry.cs");

			LogAssert.IsTrue(registry.Contains("classRanks"),
				"Ranks must be counted per classification.");
			LogAssert.IsTrue(Regex.IsMatch(registry, @"ranks\[objectId\]\s*=\s*classRank"),
				"The rank stored for the budget must be the rank within the object's own kind.");
			LogAssert.IsTrue(registry.Contains("budgetsByObjectId[objectId]"),
				"Each ranked object must record the budget it is measured against, or the condition " +
				"cannot tell a Titan's unlimited budget from a player's forty.");
		}

		/// <summary>
		/// A Titan prefab must be wired to skip the grid, and must still be scene-scoped.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Vacuous until the first Titan is authored, which is the point: the wiring is the part that
		/// is easy to get wrong and impossible to see. FishNet's <c>GridCondition</c> is ANDed with
		/// the distance condition and rejects anything past <c>HashGrid._accuracy</c> on an axis, so
		/// a Titan on the manager's defaults would be culled at ~250 m however large its range says
		/// it is — dead configuration of exactly the kind that once made 100 m player visibility
		/// really 35–70 m. Raising the global accuracy instead is not an option: cells are
		/// accuracy/2, and an accuracy wide enough for 1250 m would make the grid's 3×3 neighbourhood
		/// span the whole 8 km² world, turning the broad phase into a no-op for every other object.
		/// </para>
		/// <para>
		/// The <c>SceneCondition</c> half matters just as much and fails silently in the other
		/// direction: <c>IgnoreManager</c> means the prefab gets nothing it does not declare, and a
		/// Titan without it would be observed by players in every unrelated scene instance the
		/// server hosts.
		/// </para>
		/// </remarks>
		[Test]
		public void TitanPrefabs_SkipTheGridAndKeepTheirScene()
		{
			const string gridGuid = "cc503f7541ebd424c94541e6a767efee";
			const string sceneGuid = "2033f54fd2794464bae08fa5a55c8996";
			string titanGuid = Regex.Match(
				ReadSource("Assets/Settings/ObserverConditions/TitanDistanceCondition.asset.meta"),
				@"guid: ([0-9a-f]{32})").Groups[1].Value;

			foreach (string prefab in Directory.GetFiles(
				Path.Combine(Application.dataPath, "Prefabs"), "*.prefab", SearchOption.AllDirectories))
			{
				string text = File.ReadAllText(prefab);
				if (!text.Contains(titanGuid))
				{
					continue;
				}
				string name = Path.GetFileNameWithoutExtension(prefab);

				Match overrideType = Regex.Match(text, @"_overrideType: (\d+)");
				LogAssert.IsTrue(overrideType.Success && overrideType.Groups[1].Value == "3",
					$"{name} must set its NetworkObserver to IgnoreManager (3), or the ObserverManager " +
					"adds the GridCondition and culls the Titan at roughly a quarter of its range.");

				LogAssert.IsFalse(text.Contains(gridGuid),
					$"{name} declares the GridCondition, which clips it to the hash grid's reach.");

				LogAssert.IsTrue(text.Contains(sceneGuid),
					$"{name} must declare the SceneCondition itself — IgnoreManager gives it nothing " +
					"else, and without it the Titan is observed from every scene instance on the server.");
			}
		}

		/// <summary>
		/// The operator override for budgets is all-or-nothing and rejects anything it cannot name.
		/// </summary>
		/// <remarks>
		/// A partially applied override is worse than a rejected one: the server would be running a
		/// budget table nobody wrote and the log line would look like a success. Every malformed
		/// input below must leave the overrides exactly as they were.
		/// </remarks>
		[Test]
		public void VisibilityBudgetOverrides_ApplyAllOrNothing()
		{
			ObserverStreamingPolicy.ClearVisibilityBudgetOverrides();
			try
			{
				LogAssert.IsTrue(ObserverStreamingPolicy.ApplySetting("ObserverVisibilityBudgets", "Player:12, titan:0 ,Monster:7"),
					"A well-formed list, with spaces and any casing, is accepted.");
				LogAssert.AreEqual(12, ObserverStreamingPolicy.ResolveVisibilityBudget(ObserverClassification.Player, 40));
				LogAssert.AreEqual(0, ObserverStreamingPolicy.ResolveVisibilityBudget(ObserverClassification.Titan, 99),
					"0 in the override means unlimited and must win over an authored value.");
				LogAssert.AreEqual(7, ObserverStreamingPolicy.ResolveVisibilityBudget(ObserverClassification.Monster, 30));
				LogAssert.AreEqual(32, ObserverStreamingPolicy.ResolveVisibilityBudget(ObserverClassification.WorldItem, 32),
					"A classification the override does not mention keeps its authored value.");

				foreach (string bad in new[] { "Player:5,Dragon:3", "Player:-1", "Player 5", "Player:5:6", "7:5", "Player:x" })
				{
					LogAssert.IsFalse(ObserverStreamingPolicy.ApplySetting("ObserverVisibilityBudgets", bad),
						$"'{bad}' must be rejected outright.");
					LogAssert.AreEqual(12, ObserverStreamingPolicy.ResolveVisibilityBudget(ObserverClassification.Player, 40),
						$"'{bad}' must not have applied its valid half.");
				}
			}
			finally
			{
				ObserverStreamingPolicy.ClearVisibilityBudgetOverrides();
			}
			LogAssert.AreEqual(40, ObserverStreamingPolicy.ResolveVisibilityBudget(ObserverClassification.Player, 40),
				"Clearing overrides restores the authored value.");
		}

		/// <summary>
		/// Unclassified objects rank in their own bucket, not in the Monster one they default to.
		/// </summary>
		/// <remarks>
		/// <c>Classification</c> falls back to Monster for display, but an unclassified object is
		/// measured against the shared budget. Counting it among Monsters would push every real
		/// monster's rank up against the Monster budget while judging the unclassified object itself
		/// by a different number — two budgets fighting over one counter.
		/// </remarks>
		[Test]
		public void UnclassifiedObjects_RankInTheirOwnBucket()
		{
			LogAssert.IsTrue(ObserverStreamingEntry.UnclassifiedRankBucket < 0,
				"The unclassified bucket must be outside the enum's value range.");
			foreach (ObserverClassification classification in Enum.GetValues(typeof(ObserverClassification)))
			{
				LogAssert.IsTrue((int)classification != ObserverStreamingEntry.UnclassifiedRankBucket,
					$"{classification} collides with the unclassified bucket.");
			}

			string registry = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/ObserverStreaming/ObserverStreamingRegistry.cs");
			LogAssert.IsTrue(registry.Contains("candidate.Entry.RankBucket"),
				"The rank counter must be keyed by RankBucket, which separates unclassified objects.");
			LogAssert.IsFalse(registry.Contains("classRanks.TryGetValue(classification"),
				"Keying the counter by Classification folds unclassified objects into Monster.");
		}

		/// <summary>
		/// Lazy adoption probes for a classification without building an entry, and never demotes a
		/// character to scenery.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The budget condition calls <c>RegisterObject</c> on every timed evaluation of every object
		/// that has no entry — which is every scene prop and ability object, for every connection,
		/// forever. Building an <c>ObserverStreamingEntry</c> to learn that the object is unclassified
		/// would allocate a dictionary and read a transform each time.
		/// </para>
		/// <para>
		/// And when the object turns out to be a character, it must be registered as one: an entry
		/// with no <c>ICharacter</c> is never a viewer and scores nothing, so a player adopted that
		/// way would silently stop seeing the world ranked. Spawn order makes this unlikely today; a
		/// new spawn path or pooling must not be able to make it real.
		/// </para>
		/// </remarks>
		[Test]
		public void LazyAdoption_ProbesWithoutAllocatingAndKeepsCharacters()
		{
			string registry = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/ObserverStreaming/ObserverStreamingRegistry.cs");
			string body = registry.Substring(registry.IndexOf("public static ObserverStreamingEntry RegisterObject", StringComparison.Ordinal));
			body = body.Substring(0, body.IndexOf("\n\t\t}", StringComparison.Ordinal));

			int probe = body.IndexOf("FindClassifiedCondition", StringComparison.Ordinal);
			int build = body.IndexOf("new ObserverStreamingEntry", StringComparison.Ordinal);
			LogAssert.IsTrue(probe >= 0 && build > probe,
				"RegisterObject must probe for a classification before it constructs an entry.");
			LogAssert.IsTrue(body.Contains("TryGetComponent(out ICharacter"),
				"RegisterObject must carry an ICharacter through when the object has one.");

			string register = registry.Substring(registry.IndexOf("public static ObserverStreamingEntry Register(", StringComparison.Ordinal));
			register = register.Substring(0, register.IndexOf("\n\t\t}", StringComparison.Ordinal));
			LogAssert.IsTrue(register.Contains("existing.Character != null || character == null"),
				"Register must replace an entry that was adopted without a character.");
		}

		/// <summary>
		/// Only characters count towards local density.
		/// </summary>
		/// <remarks>
		/// Items and waypoints sit in the same entry list so they can be budgeted. If the density
		/// shrink is ever re-enabled, a field of dropped loot must not read as a crowded town and
		/// shrink the ranges of the players standing in it.
		/// </remarks>
		[Test]
		public void DensityCount_IgnoresBudgetedStatics()
		{
			string registry = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/ObserverStreaming/ObserverStreamingRegistry.cs");
			string body = registry.Substring(registry.IndexOf("private static void ApplyDensityRanges", StringComparison.Ordinal));
			body = body.Substring(0, body.IndexOf("cellCounts[key] = count + 1", StringComparison.Ordinal));
			LogAssert.IsTrue(body.Contains("Character == null"),
				"The neighbour count must skip entries with no character.");
		}

		/// <summary>
		/// The LOD table has a band covering the furthest an object can be observed from.
		/// </summary>
		/// <remarks>
		/// Beyond the last band the last band's interval is reused, so this is not a correctness
		/// cliff — but a table whose furthest edge sits inside the observer range is a table that has
		/// stopped describing the world it shapes, and reading one against the other is how the
		/// 50 m monster cull survived three retunes of the bands beyond it.
		/// </remarks>
		[Test]
		public void LodBands_CoverTheFurthestObservableDistance()
		{
			/* Titans are excluded deliberately. Their range is an order of magnitude past every other
			 * classification, and extending the table to 1000 m would change nothing: every interval
			 * the table can hand out is clamped to ObserverStreamingPolicy.MaxSendInterval (2), so
			 * the far band and a hypothetical 1000 m band are the same band. Demanding coverage would
			 * buy a longer array and no behaviour. */
			float furthest = 0f;
			foreach (string path in Directory.GetFiles(
				Path.Combine(Application.dataPath, "Settings/ObserverConditions"), "*DistanceCondition.asset"))
			{
				string name = Path.GetFileName(path);
				if (name.StartsWith("Titan", StringComparison.Ordinal))
				{
					continue;
				}
				furthest = Mathf.Max(furthest, HideDistance(name));
			}

			string source = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/NetworkTransformDistanceLod.cs");
			float lastBand = 0f;
			foreach (Match m in Regex.Matches(source, @"MaximumDistance = ([0-9.]+)f"))
			{
				lastBand = Mathf.Max(lastBand, float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
			}

			LogAssert.IsTrue(lastBand > 0f, "The LOD must author distance bands.");
			LogAssert.IsTrue(lastBand >= furthest,
				$"The furthest LOD band is {lastBand} m but an object stays observed out to {furthest} m — " +
				"the table no longer covers the range it shapes.");
		}
	}
}
