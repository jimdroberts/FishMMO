using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using FishMMO.Shared.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A character's behaviours and parts are looked up by INTERFACE: <c>character.TryGet(out IPetController pets)</c>.
	/// </summary>
	/// <remarks>
	/// <c>TryGet&lt;T&gt;</c>/<c>Get&lt;T&gt;</c> throw "must be an interface" for a class, and only at run time: the compiler
	/// accepts any <see cref="ICharacterBehaviour"/>. The travel mark was fetched by its class, so every save of
	/// every player threw, Mark and Recall threw, and a portal's hand-off died half done (the player left under Moron 2's sea
	/// at Moron 1's arrival point). This scan finds such a lookup anywhere in the project's code.
	/// </remarks>
	[TestFixture]
	public class CharacterBehaviourLookupTests
	{
		private static readonly Regex Lookup = new Regex(
			@"\bTryGet\(\s*out\s+(?<t>[A-Za-z_][\w.]*)\s+\w+\s*\)|\b(?:TryGet|Get)<(?<t>[A-Za-z_][\w.]*)>\s*\(",
			RegexOptions.Compiled);

		/// <summary>Simple name → the ICharacterBehaviour CLASSES of that name (an interface of the same name wins).</summary>
		private static Dictionary<string, Type> behaviourClasses;

		private static Dictionary<string, Type> BehaviourClasses()
		{
			if (behaviourClasses != null)
			{
				return behaviourClasses;
			}
			var classes = new Dictionary<string, Type>(StringComparer.Ordinal);
			var interfaces = new HashSet<string>(StringComparer.Ordinal);
			foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				Type[] types;
				try
				{
					types = assembly.GetTypes();
				}
				catch (System.Reflection.ReflectionTypeLoadException e)
				{
					types = e.Types.Where(t => t != null).ToArray();
				}
				foreach (Type type in types)
				{
					if (!typeof(ICharacterBehaviour).IsAssignableFrom(type))
					{
						continue;
					}
					if (type.IsInterface)
					{
						interfaces.Add(type.Name);
					}
					else
					{
						classes[type.Name] = type;
					}
				}
			}
			foreach (string name in interfaces)
			{
				classes.Remove(name);
			}
			return behaviourClasses = classes;
		}

		/// <summary>The offending lookups in <paramref name="code"/>, one "line N: Type" each; null when there are none.</summary>
		private static string Check(string code)
		{
			Dictionary<string, Type> classes = BehaviourClasses();
			var found = new List<string>();
			string[] lines = code.Split('\n');
			for (int i = 0; i < lines.Length; i++)
			{
				string trimmed = lines[i].TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal) ||
					trimmed.StartsWith("/*", StringComparison.Ordinal))
				{
					continue;
				}
				foreach (Match match in Lookup.Matches(lines[i]))
				{
					string name = match.Groups["t"].Value;
					int dot = name.LastIndexOf('.');
					if (dot >= 0)
					{
						name = name.Substring(dot + 1);
					}
					if (classes.ContainsKey(name))
					{
						found.Add($"line {i + 1}: {name} is a class; look it up by its interface");
					}
				}
			}
			return found.Count == 0 ? null : string.Join("; ", found);
		}

		[Test]
		public void TheTravelMark_IsLookedUpByItsInterface_AndTheScanSeesTheOldDefect()
		{
			LogAssert.IsTrue(BehaviourClasses().ContainsKey("TravelMarkController"),
				"the scan must know the mark's class (is the Travel module loaded?)");
			SourceScanPins.HoldsAndFires("RecallAction",
				SourceScanPins.ReadSource("Assets/FishMMO/Modules/Travel/Runtime/TravelSpells/RecallAction.cs"),
				Check,
				SourceScanPins.Replace("TryGet(out ITravelMarkController marks)", "TryGet(out TravelMarkController marks)"),
				"the mark fetched by its class");
		}

		[Test]
		public void NoCharacterBehaviour_IsLookedUpByAClass()
		{
			string root = ProjectFiles.ProjectRoot.Replace('\\', '/') + "/";
			var failures = new List<string>();
			foreach (string file in ProjectFiles.GameSources())
			{
				string reason = Check(File.ReadAllText(file).Replace("\r\n", "\n"));
				if (reason != null)
				{
					string path = file.Replace('\\', '/');
					failures.Add($"{(path.StartsWith(root, StringComparison.Ordinal) ? path.Substring(root.Length) : path)}: {reason}");
				}
			}
			LogAssert.IsTrue(failures.Count == 0, "TryGet/Get throw for a class at run time:\n" + string.Join("\n", failures));
		}
	}
}
