#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Reads a generated scene's points of interest for probes and renders: the records, and a table of them.
	/// </summary>
	public static class PointOfInterestProbe
	{
		/// <summary>The scene's POI asset, from its "Points of Interest" root; null when it has none.</summary>
		public static ScenePointsOfInterest Asset(Scene scene)
		{
			if (!scene.IsValid())
			{
				return null;
			}
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (root.name == PointOfInterestGenerator.RootName && root.TryGetComponent(out ScenePointOfInterestSettings settings))
				{
					return settings.Points;
				}
			}
			return null;
		}

		/// <summary>The scene's records (empty when it has none).</summary>
		public static List<PointOfInterestRecord> Records(Scene scene)
		{
			ScenePointsOfInterest asset = Asset(scene);
			return asset != null ? new List<PointOfInterestRecord>(asset.Points) : new List<PointOfInterestRecord>();
		}

		/// <summary>One line per record: kind, name, position, radius, template, race.</summary>
		public static string Table(IEnumerable<PointOfInterestRecord> records)
		{
			var sb = new StringBuilder();
			sb.AppendLine("kind\tname\tx\ty\tz\tyaw\tradius\ttemplate\trace\tid\tunlock");
			int count = 0;
			foreach (PointOfInterestRecord r in records)
			{
				sb.Append(r.Kind).Append('\t')
					.Append(r.Name).Append('\t')
					.Append(r.Position.x.ToString("F1", CultureInfo.InvariantCulture)).Append('\t')
					.Append(r.Position.y.ToString("F1", CultureInfo.InvariantCulture)).Append('\t')
					.Append(r.Position.z.ToString("F1", CultureInfo.InvariantCulture)).Append('\t')
					.Append(r.Yaw.ToString("F0", CultureInfo.InvariantCulture)).Append('\t')
					.Append(r.Radius.ToString("F1", CultureInfo.InvariantCulture)).Append('\t')
					.Append(string.IsNullOrEmpty(r.Template) ? "-" : r.Template).Append('\t')
					.Append(string.IsNullOrEmpty(r.Race) ? "-" : r.Race).Append('\t')
					.Append(r.Id).Append('\t')
					.Append(r.UnlockIndex).AppendLine();
				count++;
			}
			sb.Append(count).Append(" site(s)");
			return sb.ToString();
		}

		/// <summary>Logs the open scene's points of interest as a table.</summary>
		public static void LogOpenScene()
		{
			Scene scene = EditorSceneManager.GetActiveScene();
			ScenePointsOfInterest asset = Asset(scene);
			if (asset == null)
			{
				Debug.LogWarning($"[POI probe] '{scene.name}' has no points of interest.");
				return;
			}
			Debug.Log($"[POI probe] '{scene.name}': {PointOfInterestStage.Summary(PointOfInterestPlan.FromAsset(asset))}\n{Table(asset.Points)}");
		}
	}
}
#endif
