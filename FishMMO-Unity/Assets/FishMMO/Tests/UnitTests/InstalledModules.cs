using System.IO;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Which modules are installed (folders under <see cref="Root"/>), for core tests that also read a module's files: such a
	/// test skips that part, or is ignored, when the module is removed (the module gate).
	/// </summary>
	public static class InstalledModules
	{
		public const string Root = "Assets/FishMMO/Modules/";

		/// <summary>Whether <c>Assets/FishMMO/Modules/&lt;module&gt;</c> is there.</summary>
		public static bool Has(string module) => Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), Root + module));

		/// <summary>True for a path inside a module that is not installed (anything outside the modules is never absent here).</summary>
		public static bool IsAbsent(string path)
		{
			if (path == null || !path.StartsWith(Root, System.StringComparison.Ordinal))
			{
				return false;
			}
			string rest = path.Substring(Root.Length);
			int slash = rest.IndexOf('/');
			return !Has(slash < 0 ? rest : rest.Substring(0, slash));
		}

		/// <summary>Ignores the calling test when the module is not installed.</summary>
		public static void Require(string module)
		{
			if (!Has(module))
			{
				Assert.Ignore($"The {module} module is not installed.");
			}
		}
	}
}
