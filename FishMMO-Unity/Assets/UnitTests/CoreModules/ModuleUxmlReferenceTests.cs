using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Every stylesheet a module's window names must exist. A UXML moved into <c>Assets/Modules</c> keeps its
	/// <c>&lt;Style src="../../FishMMO-Theme.uss"/&gt;</c>, which then points at nothing: the window loads, unthemed, and
	/// nothing reports it (the friend list, party, guild, housing and staff windows did exactly this on 2026-11-17).
	/// Module windows name the theme by its absolute path, <c>/Assets/Scripts/Client/GUI/FishMMO-Theme.uss</c>.
	/// </summary>
	[TestFixture]
	public class ModuleUxmlReferenceTests
	{
		[Test]
		public void EveryStylesheetAModuleWindowNames_Exists()
		{
			string root = Directory.GetCurrentDirectory();
			string modules = Path.Combine(root, "Assets/Modules");
			if (!Directory.Exists(modules))
			{
				Assert.Ignore("No modules are installed.");
			}

			var missing = new List<string>();
			int checkedCount = 0;
			foreach (string uxml in Directory.GetFiles(modules, "*.uxml", SearchOption.AllDirectories))
			{
				string text = File.ReadAllText(uxml);
				foreach (Match m in Regex.Matches(text, "<Style\\s+src=\"([^\"]+)\""))
				{
					string src = m.Groups[1].Value;
					string resolved = src.StartsWith("/")
						? Path.Combine(root, src.TrimStart('/'))
						: Path.GetFullPath(Path.Combine(Path.GetDirectoryName(uxml), src));
					++checkedCount;
					if (!File.Exists(resolved))
					{
						missing.Add($"{uxml.Substring(root.Length + 1)} → {src}");
					}
				}
			}
			if (checkedCount == 0)
			{
				Assert.Ignore("No module with a window is installed (the core alone).");
			}
			LogAssert.IsTrue(missing.Count == 0, "module windows name stylesheets that do not exist: " + string.Join("; ", missing));
		}
	}
}
