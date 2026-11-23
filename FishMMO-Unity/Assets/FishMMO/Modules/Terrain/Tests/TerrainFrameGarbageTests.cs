using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using NUnit.Framework;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>Split from ClientFrameGarbageTests: the Terrain module's half.</summary>
	public class TerrainFrameGarbageTests
	{
		[Test]
		public void PerFrameSync_ReusesNoStopwatch()
		{
			foreach (string file in new[] { "Assets/FishMMO/Modules/Terrain/Client/Grass/GrassBladeSystem.cs", "Assets/FishMMO/Modules/Terrain/Client/Scatter/DetailScatterSystem.cs" })
			{
				foreach (string line in CodeLines(Read(file)))
				{
					LogAssert.IsFalse(line.Contains("Stopwatch.StartNew"), $"{file}: Stopwatch.StartNew is an object a frame; read Stopwatch.GetTimestamp: {line.Trim()}");
				}
			}
		}


		private static string Read(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(ProjectFiles.Exists(path), $"{relativePath} not found at {path}");
			return ProjectFiles.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>Lines that are code, not comment.</summary>
		/// <summary>Lines that are code, not comment.</summary>
		private static IEnumerable<string> CodeLines(string text)
		{
			foreach (string line in text.Split('\n'))
			{
				string trimmed = line.TrimStart();
				if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
				{
					continue;
				}
				int comment = trimmed.IndexOf("//", StringComparison.Ordinal);
				yield return comment >= 0 && !trimmed.Contains("\"") ? trimmed.Substring(0, comment) : trimmed;
			}
		}
	}
}
