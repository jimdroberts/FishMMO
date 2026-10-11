#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Writes the point-of-interest template assets from <see cref="PointOfInterestTemplateSpecs"/>: one or more for every
	/// kind the structure kit builds, each laying its pieces (<see cref="PropsFeature"/>) and carrying the kind's
	/// recommended gameplay features (<see cref="PointOfInterestFeatureDefaults"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Idempotent and gentle.</b> An asset that already exists is left alone, hand edits and all; only missing ones are
	/// written. Forcing rewrites every one in place (its GUID kept, so scenes that name it keep finding it).
	/// </para>
	/// <para>
	/// <b>Split by size where the gameplay differs.</b> The recommended features depend on the size class (an elite
	/// only from medium sites up, a dungeon entrance only in a large cave), and a template's features are fixed, so a
	/// spec whose sizes get different features is written as one asset per run of sizes that share them:
	/// "Camp - Hide Tents (Small)" and "Camp - Hide Tents (Medium-Large)".
	/// </para>
	/// </remarks>
	public static class PointOfInterestTemplateAuthoring
	{
		/// <summary>What one run did.</summary>
		public sealed class Report
		{
			public readonly List<string> Created = new List<string>();
			public readonly List<string> Updated = new List<string>();
			public readonly List<string> Kept = new List<string>();
			/// <summary>Template assets under the folder that no spec writes any more (left in place).</summary>
			public readonly List<string> Stale = new List<string>();
			public readonly List<string> Problems = new List<string>();

			public override string ToString()
			{
				var sb = new StringBuilder();
				sb.Append($"{Created.Count} created, {Updated.Count} rewritten, {Kept.Count} kept as they were");
				if (Stale.Count > 0)
				{
					sb.Append($", {Stale.Count} no longer written by any spec (left in place: {string.Join(", ", Stale)})");
				}
				if (Problems.Count > 0)
				{
					sb.Append($"; problems: {string.Join("; ", Problems)}");
				}
				return sb.ToString();
			}
		}

		/// <summary>One asset to write: a spec at a run of size classes, its name and path.</summary>
		public readonly struct Planned
		{
			public readonly PointOfInterestTemplateSpec Spec;
			public readonly string Name;
			public readonly string Path;
			public readonly bool Small, Medium, Large;
			/// <summary>The size class the features are taken at: the run's first.</summary>
			public readonly int FeatureSize;

			public Planned(PointOfInterestTemplateSpec spec, string name, bool small, bool medium, bool large, int featureSize)
			{
				Spec = spec;
				Name = name;
				Path = $"{spec.GroupFolder}/{name}.asset";
				Small = small;
				Medium = medium;
				Large = large;
				FeatureSize = featureSize;
			}
		}

		[DashboardTool(DashboardToolAttribute.WorldSceneDetails, "Author POI templates", Section = "Generated scenes", Order = 13,
			Tooltip = "Writes any missing point-of-interest template under Assets/Templates/World/PointsOfInterest (one or more per structure kind: layout, structure-kit pieces, gameplay features). Existing templates are left as they are.")]
		public static void AuthorFromDashboard() => Log(AuthorAll(false));

		[DashboardTool(DashboardToolAttribute.WorldSceneDetails, "Re-author POI templates (overwrite)", Section = "Generated scenes", Order = 14,
			Tooltip = "Rewrites EVERY generated point-of-interest template from code, in place (GUIDs kept). Hand edits to them are lost.",
			Confirm = "Rewrite every generated point-of-interest template from code? Hand edits to those assets are lost.")]
		public static void ReauthorFromDashboard() => Log(AuthorAll(true));

		/// <summary>
		/// <c>-executeMethod FishMMO.Shared.WorldDesign.PointOfInterestTemplateAuthoring.AuthorFromCommandLine</c> (add
		/// <c>-force</c> to rewrite existing assets): in batch mode exits 0 when it wrote without problems, 1 otherwise.
		/// </summary>
		public static void AuthorFromCommandLine()
		{
			int code = 1;
			try
			{
				bool force = Array.IndexOf(Environment.GetCommandLineArgs(), "-force") >= 0;
				Report report = AuthorAll(force);
				Log(report);
				code = report.Problems.Count == 0 ? 0 : 1;
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
			if (Application.isBatchMode)
			{
				EditorApplication.Exit(code);
			}
		}

		/// <summary>Writes every template (see the class remarks); <paramref name="force"/> rewrites existing ones.</summary>
		public static Report AuthorAll(bool force)
		{
			var report = new Report();
			StructureKitPieceSource.Register();
			List<Planned> plan = Plan(PointOfInterestTemplateSpecs.All(), FeatureSignature);
			var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (Planned planned in plan)
			{
				written.Add(planned.Path);
				try
				{
					Write(planned, force, report);
				}
				catch (Exception e)
				{
					report.Problems.Add($"{planned.Name}: {e.Message}");
					Debug.LogException(e);
				}
			}
			AssetDatabase.SaveAssets();
			foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(PointOfInterestTemplate)}", new[] { PointOfInterestTemplate.Folder }))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (!written.Contains(path))
				{
					report.Stale.Add(path);
				}
			}
			report.Stale.Sort(StringComparer.Ordinal);
			return report;
		}

		/// <summary>
		/// The assets a spec list makes: each spec once, or once per run of its size classes whose features differ
		/// (<paramref name="signature"/> of the features at a size). Pure given the signature.
		/// </summary>
		public static List<Planned> Plan(List<PointOfInterestTemplateSpec> specs, Func<PointOfInterestTemplateSpec, int, string> signature)
		{
			var plan = new List<Planned>();
			foreach (PointOfInterestTemplateSpec spec in specs)
			{
				var runs = new List<List<int>>();
				string last = null;
				foreach (int size in spec.Sizes())
				{
					string sig = signature != null ? signature(spec, size) : string.Empty;
					if (runs.Count == 0 || sig != last)
					{
						runs.Add(new List<int>());
					}
					runs[runs.Count - 1].Add(size);
					last = sig;
				}
				if (runs.Count <= 1)
				{
					int first = runs.Count == 1 ? runs[0][0] : 1;
					plan.Add(new Planned(spec, spec.Name, spec.Small, spec.Medium, spec.Large, first));
					continue;
				}
				foreach (List<int> run in runs)
				{
					string label = run.Count == 1 ? SizeName(run[0]) : $"{SizeName(run[0])}-{SizeName(run[run.Count - 1])}";
					plan.Add(new Planned(spec, $"{spec.Name} ({label})", run.Contains(0), run.Contains(1), run.Contains(2), run[0]));
				}
			}
			return plan;
		}

		public static string SizeName(int size) => size <= 0 ? "Small" : size == 1 ? "Medium" : "Large";

		/// <summary>
		/// The features a template carries at a size class: its props, then the kind's recommended gameplay. A chamber
		/// template keeps the site-wide ones (the name-toast region, discovery) at the site and builds the rest in the
		/// cave's chamber (<see cref="PointOfInterestChamberFeature"/>).
		/// </summary>
		public static List<PointOfInterestFeature> FeaturesFor(PointOfInterestTemplateSpec spec, int sizeClass)
		{
			var built = new List<PointOfInterestFeature>
			{
				new PropsFeature { Finish = spec.Finish, FinishChance = spec.FinishChance, KeepOutOfWater = !spec.InWater },
			};
			var site = new List<PointOfInterestFeature>();
			foreach (PointOfInterestFeature feature in PointOfInterestFeatureDefaults.For(spec.Kind, sizeClass))
			{
				bool siteWide = feature is RegionFeature || feature is ExplorationFeature;
				if (spec.InChamber && siteWide)
				{
					site.Add(feature);
					continue;
				}
				if (spec.InChamber && feature is DungeonEntranceFeature door)
				{
					// The chamber is where it goes; the tunnel's far end is the chamber's back wall.
					door.AtTunnelEnd = false;
				}
				built.Add(feature);
			}
			ReserveGameplaySpots(spec, built);
			if (!spec.InChamber)
			{
				return built;
			}
			site.Add(new PointOfInterestChamberFeature { Features = built });
			return site;
		}

		/// <summary>What the features at a size are, for telling sizes apart: each one's type and serialized fields.</summary>
		private static string FeatureSignature(PointOfInterestTemplateSpec spec, int sizeClass)
		{
			var sb = new StringBuilder();
			foreach (PointOfInterestFeature feature in PointOfInterestFeatureDefaults.For(spec.Kind, sizeClass))
			{
				if (feature == null)
				{
					continue;
				}
				sb.Append(feature.GetType().FullName).Append(':').Append(EditorJsonUtility.ToJson(feature)).Append('|');
			}
			return sb.ToString();
		}

		private static void Write(Planned planned, bool force, Report report)
		{
			var template = AssetDatabase.LoadAssetAtPath<PointOfInterestTemplate>(planned.Path);
			if (template != null && !force)
			{
				report.Kept.Add(planned.Name);
				return;
			}
			bool created = template == null;
			if (created)
			{
				template = ScriptableObject.CreateInstance<PointOfInterestTemplate>();
			}
			Apply(planned, template);
			if (created)
			{
				WorldEditorAssets.EnsureFolder(planned.Spec.GroupFolder);
				AssetDatabase.CreateAsset(template, planned.Path);
				report.Created.Add(planned.Name);
			}
			else
			{
				EditorUtility.SetDirty(template);
				report.Updated.Add(planned.Name);
			}
		}

		/// <summary>Copies a planned spec into a template object (every field the spec owns).</summary>
		public static void Apply(Planned planned, PointOfInterestTemplate template)
		{
			PointOfInterestTemplateSpec spec = planned.Spec;
			template.name = planned.Name;
			template.Kind = spec.Kind;
			template.Weight = spec.Weight;
			template.DefaultBiomeWeight = spec.DefaultBiomeWeight;
			template.BiomeWeights = new List<PointOfInterestBiomeWeight>(spec.BiomeWeights);
			template.RaceCategories = new List<string>(spec.RaceCategories);
			template.Races = new List<string>();
			template.Small = planned.Small;
			template.Medium = planned.Medium;
			template.Large = planned.Large;
			template.PadFlatness = spec.PadFlatness;
			template.Style = spec.Style;
			template.Layout = spec.Layout;
			template.Pieces = new List<PointOfInterestPieceSlot>();
			foreach (PointOfInterestPieceSlot slot in spec.Pieces)
			{
				template.Pieces.Add(new PointOfInterestPieceSlot
				{
					Tag = slot.Tag,
					MinCount = slot.MinCount,
					MaxCount = slot.MaxCount,
					MinDecay = slot.MinDecay,
					MaxDecay = slot.MaxDecay,
					MinScale = slot.MinScale,
					MaxScale = slot.MaxScale,
					Role = slot.Role,
					Style = slot.Style,
				});
			}
			template.Features = FeaturesFor(spec, planned.FeatureSize);
		}

		/// <summary>
		/// Puts the site's waypoint, respawn point, portal and dungeon door where the layout keeps clear: just ahead of the
		/// centrepiece (on the main street where there is one), and tells the props to leave those spots empty. Without
		/// it a keep at a city's centre would stand on the waypoint the feature's default offset puts 4 m behind the middle.
		/// </summary>
		public static void ReserveGameplaySpots(PointOfInterestTemplateSpec spec, List<PointOfInterestFeature> features)
		{
			PropsFeature props = features.Find(f => f is PropsFeature) as PropsFeature;
			if (props == null)
			{
				return;
			}
			props.Clearings = new List<Vector3>();
			float front = CentreDepth(spec) * 0.5f;
			PointOfInterestKindRule rule = PointOfInterestRules.Default().RuleFor(spec.Kind);
			// A chamber is a few metres across (PointOfInterestChamberFeature.MinRadius at least), whatever the site's footprint.
			float limit = spec.InChamber ? 3.5f : (rule != null ? rule.FootprintMin : 10f) * PointOfInterestLayouts.Inset * 0.85f;
			Vector2 Spot(float x, float z, float radius)
			{
				var at = new Vector2(x, z);
				float reach = Mathf.Max(0f, limit - radius);
				return at.magnitude > reach && at.magnitude > 0f ? at * (reach / at.magnitude) : at;
			}
			void Clear(Vector2 at, float radius) => props.Clearings.Add(new Vector3(at.x, radius, at.y));
			foreach (PointOfInterestFeature feature in features)
			{
				switch (feature)
				{
					case WaypointFeature waypoint:
						waypoint.Offset = Spot(-3f, front + 3f, 2f);
						Clear(waypoint.Offset, 2f);
						break;
					case RespawnFeature respawn:
						respawn.Offset = Spot(3f, front + 3f, 1.5f);
						Clear(respawn.Offset, 1.5f);
						break;
					case DungeonEntranceFeature door:
						// In a chamber "ahead" is back toward the mouth, so the door stands behind the middle, deeper in.
						door.Offset = Spot(0f, spec.InChamber ? -(front + 2f) : front + 2f, 1.5f);
						Clear(door.Offset, 1.5f);
						break;
					case PortalFeature portal:
						if (front <= 0f)
						{
							Clear(portal.Offset, 3f);
						}
						break;
				}
			}
		}

		/// <summary>The depth of the template's centrepiece (its first middle slot, the deepest piece it may be), metres; 0 for none.</summary>
		private static float CentreDepth(PointOfInterestTemplateSpec spec)
		{
			for (int s = 0; s < spec.Pieces.Count; s++)
			{
				PointOfInterestPieceSlot slot = spec.Pieces[s];
				if (PointOfInterestLayouts.RoleFor(spec.Layout, s, slot.Role) != PointOfInterestSlotRole.Centre || slot.MaxCount <= 0)
				{
					continue;
				}
				float depth = 0f;
				string style = string.IsNullOrEmpty(slot.Style) ? spec.Style : slot.Style;
				foreach (StructurePiece piece in StructureKitPieceSource.Candidates(slot.Tag, style))
				{
					depth = Mathf.Max(depth, piece.Footprint.y);
				}
				return depth;
			}
			return 0f;
		}

		private static void Log(Report report)
		{
			if (report.Problems.Count == 0)
			{
				Debug.Log($"[POI templates] {report}");
			}
			else
			{
				Debug.LogWarning($"[POI templates] {report}");
			}
		}
	}
}
#endif
