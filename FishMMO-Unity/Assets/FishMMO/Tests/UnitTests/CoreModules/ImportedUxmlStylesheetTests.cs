using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Every UXML's IMPORTED asset carries every stylesheet its source declares.
	/// </summary>
	/// <remarks>
	/// A UXML imported while a stylesheet it names is somewhere else (moved into a module and back) is cached without that
	/// sheet, and nothing re-imports it when the sheet returns: the source is unchanged. UITarget.uxml and
	/// UIParty.uxml once shipped without UIBuffContainer.uss, so the target and party frames drew no buff icons and each icon's
	/// hover area stretched across the row. The source paths were all valid (ModuleUxmlReferenceTests passed); only the
	/// import was wrong. The fix is a reimport of the named file (Assets → Reimport).
	/// </remarks>
	[TestFixture]
	public class ImportedUxmlStylesheetTests
	{
		private static readonly Regex StyleSrc = new Regex("<(?:ui:)?Style\\s+src=\"([^\"]+)\"", RegexOptions.Compiled);

		[Test]
		public void EveryImportedUxml_CarriesEveryStylesheetItDeclares()
		{
			string[] paths = AssetDatabase.FindAssets("t:VisualTreeAsset", new[] { "Assets/FishMMO" })
				.Select(AssetDatabase.GUIDToAssetPath)
				.Where(p => p.EndsWith(".uxml"))
				.ToArray();

			var stale = new List<string>();
			foreach (string path in paths)
			{
				int declared = StyleSrc.Matches(File.ReadAllText(path)).Count;
				VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
				int imported = asset == null ? 0 : asset.stylesheets.Count(s => s != null);
				if (imported < declared)
				{
					stale.Add($"{path}: declares {declared} stylesheet(s), its import carries {imported} — reimport it");
				}
			}

			LogAssert.IsTrue(paths.Length > 0, "no UXML found under Assets/FishMMO");
			LogAssert.AreEqual(0, stale.Count, string.Join("\n", stale));
		}
	}
}
