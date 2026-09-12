using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.InputSystem;
using FishMMO.Client;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// What the shipped input asset binds, and what the Options panel prints for it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// These read <c>PlayerControls.inputactions</c> as text. Nothing can load the asset in an
	/// EditMode test — the generated wrapper wants the Input System's runtime — so the file is the
	/// only thing available to assert against, and the facts below are ones a player would notice
	/// immediately if they were wrong: a key that does nothing, two actions fighting over one key,
	/// and a settings row captioned with an identifier instead of a word.
	/// </para>
	/// <para>
	/// Nothing here asserts what an action DOES beyond its binding — that is the input controller's
	/// job, and the pin key's half of it is proven in <see cref="PinnedTargetInteractionTests"/>.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ShippedKeyBindingTests
	{
		private const string InputActionsPath = "Assets/Prefabs/Client/Input/PlayerControls.inputactions";

		/// <summary>The asset's text, with its line endings normalised to \n.</summary>
		private static string InputActions
		{
			get
			{
				string path = Path.Combine(Directory.GetCurrentDirectory(), InputActionsPath);
				LogAssert.IsTrue(File.Exists(path), $"{InputActionsPath} not found at {path}.");
				return File.ReadAllText(path).Replace("\r\n", "\n");
			}
		}

		/// <summary>
		/// One string field of a single JSON object, or null when it is not there.
		/// </summary>
		/// <param name="entry">The text of one object.</param>
		/// <param name="field">The field's name, without quotes.</param>
		/// <remarks>
		/// A hand-rolled read rather than a parser, because the value wanted is always a string and
		/// the objects are flat. The opening quote is part of the key searched for so that a field
		/// whose name merely ends in the one asked for cannot answer for it — <c>isPartOfComposite</c>
		/// must not be read as <c>action</c>.
		/// </remarks>
		private static string Field(string entry, string field)
		{
			string key = $"\"{field}\": \"";
			int start = entry.IndexOf(key, StringComparison.Ordinal);
			if (start < 0)
			{
				return null;
			}
			start += key.Length;
			int end = entry.IndexOf('"', start);
			return end < 0 ? null : entry.Substring(start, end - start);
		}

		/// <summary>The whole object that opens at <paramref name="open"/>, braces included.</summary>
		private static string ObjectAt(string source, int open)
		{
			int depth = 0;
			for (int i = open; i < source.Length; ++i)
			{
				if (source[i] == '{')
				{
					++depth;
				}
				else if (source[i] == '}' && --depth == 0)
				{
					return source.Substring(open, i - open + 1);
				}
			}
			return null;
		}

		/// <summary>
		/// The text of one action map, by name, or null when the asset has no such map.
		/// </summary>
		/// <remarks>
		/// A map is the only object that lists actions, which is what tells the two map objects
		/// apart from the action objects nested inside them. The scan starts after the
		/// <c>"maps"</c> key so the document object itself is never a candidate.
		/// </remarks>
		private static string MapScope(string mapName)
		{
			string source = InputActions;
			int maps = source.IndexOf("\"maps\"", StringComparison.Ordinal);
			if (maps < 0)
			{
				return null;
			}

			for (int open = source.IndexOf('{', maps); open >= 0; open = source.IndexOf('{', open + 1))
			{
				string candidate = ObjectAt(source, open);
				if (candidate == null)
				{
					return null;
				}
				if (candidate.IndexOf("\"actions\"", StringComparison.Ordinal) >= 0 &&
					string.Equals(Field(candidate, "name"), mapName, StringComparison.Ordinal))
				{
					return candidate;
				}
			}
			return null;
		}

		/// <summary>
		/// Visits every innermost object of a scope and hands its text to a callback.
		/// </summary>
		/// <remarks>
		/// A binding is an object with no braces inside it, so an object whose next opening brace
		/// lies past its own closing brace is one of them — the containers around it (the document,
		/// each action map, each action) are skipped because they enclose others. That is a weaker
		/// claim than "this is valid JSON" and a much simpler one, which matters because the value
		/// of the scan is that a reader can see it is right.
		/// </remarks>
		private static void ForEachInnermostObject(string scope, Action<string> visit)
		{
			for (int i = 0; i < scope.Length; ++i)
			{
				if (scope[i] != '{')
				{
					continue;
				}

				int close = scope.IndexOf('}', i);
				if (close < 0)
				{
					return;
				}

				int nextOpen = scope.IndexOf('{', i + 1);
				if (nextOpen >= 0 && nextOpen < close)
				{
					continue;
				}

				visit(scope.Substring(i, close - i + 1));
				i = close;
			}
		}

		/// <summary>Every control path bound to an action, in the order the asset lists them.</summary>
		/// <remarks>
		/// Scanned over the whole file rather than one map: a key owned by an action in ANOTHER map
		/// is still a key the player's presses reach, so a collision across maps matters as much as
		/// one within a map.
		/// </remarks>
		private static List<string> PathsFor(string actionName)
		{
			List<string> paths = new List<string>();
			ForEachInnermostObject(InputActions, entry =>
			{
				if (string.Equals(Field(entry, "action"), actionName, StringComparison.Ordinal))
				{
					string path = Field(entry, "path");
					if (!string.IsNullOrEmpty(path))
					{
						paths.Add(path);
					}
				}
			});
			return paths;
		}

		/// <summary>Every action bound to a control path, across every map.</summary>
		private static List<string> ActionsOn(string path)
		{
			List<string> actions = new List<string>();
			ForEachInnermostObject(InputActions, entry =>
			{
				if (string.Equals(Field(entry, "path"), path, StringComparison.Ordinal))
				{
					actions.Add(Field(entry, "action"));
				}
			});
			return actions;
		}

		/// <summary>Every action of the Player map that has a binding.</summary>
		private static List<string> PlayerActionNames()
		{
			List<string> names = new List<string>();
			string player = MapScope("Player");
			LogAssert.IsNotNull(player, "the asset must still declare a Player action map");

			ForEachInnermostObject(player, entry =>
			{
				string action = Field(entry, "action");
				if (!string.IsNullOrEmpty(action) && !names.Contains(action))
				{
					names.Add(action);
				}
			});
			return names;
		}

		/// <summary>
		/// The pin key is bound to F, and F is bound to nothing else.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Both halves are needed for F to be a key the settings screen can work with. The
		/// key-binding list refuses a rebind that would create a duplicate and marks a duplicate
		/// it finds as a conflict, so a key shared by two actions cannot be cleanly given to either
		/// one from the panel — a shipped collision is a row the player can never rebind.
		/// </para>
		/// <para>
		/// One collision is authored on purpose: Escape drives Cancel, CloseLastUI and Menu as a
		/// chain, exempted by <c>SharedBindingGroups</c>. F is not part of it, so a second binding
		/// on F would be an ordinary mistake rather than a designed one.
		/// </para>
		/// </remarks>
		[Test]
		public void PinKey_IsFAndFAlone()
		{
			List<string> pin = PathsFor("PinTarget");
			LogAssert.AreEqual(1, pin.Count, "PinTarget must have exactly one binding — a second would be a row the player cannot tell apart.");
			LogAssert.AreEqual("<Keyboard>/f", pin[0], "and it must be F.");

			List<string> onF = ActionsOn("<Keyboard>/f");
			LogAssert.AreEqual(1, onF.Count, $"F must belong to the pin key alone; it is shared with {string.Join(", ", onF)}.");
			LogAssert.AreEqual("PinTarget", onF[0], "and the one action on it must be the pin.");
		}

		/// <summary>Whether the Options panel refuses to build a row for a binding.</summary>
		/// <remarks>
		/// The panel's own predicate, called through reflection. It is the gate between a binding
		/// and a row: what it refuses, the player cannot see and cannot rebind.
		/// </remarks>
		private static bool IsNonRebindable(InputBinding binding)
		{
			MethodInfo method = typeof(UITKOptions).GetMethod("IsNonRebindable", BindingFlags.Static | BindingFlags.NonPublic);
			LogAssert.IsNotNull(method, "UITKOptions must still declare IsNonRebindable");
			return (bool)method.Invoke(null, new object[] { binding });
		}

		/// <summary>
		/// The pin key gets a row the player can rebind.
		/// </summary>
		/// <remarks>
		/// The control is built from the path the asset actually binds, so this is a statement
		/// about the shipped F rather than about a string written twice. A pointer control is
		/// checked alongside it because a predicate that returned false for everything would
		/// otherwise satisfy the first assertion while the panel had stopped filtering anything.
		/// </remarks>
		[Test]
		public void PinKey_GetsARowThePlayerCanRebind()
		{
			string path = PathsFor("PinTarget")[0];

			LogAssert.IsFalse(IsNonRebindable(new InputBinding(path)),
				$"{path} must produce a row in the key-binding list, or the pin key cannot be changed from the Options panel.");
			LogAssert.IsTrue(IsNonRebindable(new InputBinding("<Mouse>/position")),
				"and the filter must still refuse the pointer controls, or the check above proves nothing.");
		}

		/// <summary>
		/// The map is on M and the minimap on N.
		/// </summary>
		/// <remarks>
		/// Adjacent one-key toggles that look alike, so the direction is asserted rather than the
		/// pair being merely present: getting them the wrong way round produces a panel that still
		/// works and is still wrong.
		/// </remarks>
		[Test]
		public void MapKeys_AreTheMapOnMAndTheMinimapOnN()
		{
			List<string> map = PathsFor("WorldMap");
			List<string> minimap = PathsFor("Minimap");

			LogAssert.AreEqual(1, map.Count, "the world map must have one keyboard key");
			LogAssert.AreEqual(1, minimap.Count, "as must the minimap");
			LogAssert.AreEqual("<Keyboard>/m", map[0], "the world map holds M");
			LogAssert.AreEqual("<Keyboard>/n", minimap[0], "and the minimap holds N beside it.");
		}

		/// <summary>
		/// Each map key belongs to its own action, so neither row is a conflict.
		/// </summary>
		[Test]
		public void MapKeys_AreNotShared()
		{
			LogAssert.AreEqual("WorldMap", ActionsOn("<Keyboard>/m")[0], "M is the map's");
			LogAssert.AreEqual("Minimap", ActionsOn("<Keyboard>/n")[0], "and N the minimap's");
			LogAssert.AreEqual(1, ActionsOn("<Keyboard>/m").Count, "with nothing else on M");
			LogAssert.AreEqual(1, ActionsOn("<Keyboard>/n").Count, "and nothing else on N.");
		}

		/// <summary>
		/// Every action the Options panel lists has a caption of its own.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The table is read through reflection rather than restated, because what matters is that
		/// an action has an ENTRY and not what the entry says — several are deliberately their own
		/// name, so comparing the caption against the name would flag <c>Jump</c> as missing while
		/// the row it produces is exactly right.
		/// </para>
		/// <para>
		/// The check is completeness. An action added to the asset and not to the table falls back
		/// to its raw name, which is how the pin row came to read <c>PinTarget</c> in the first
		/// place. That fallback is deliberately left in place — it is what keeps a missing entry a
		/// cosmetic fault rather than a blank row — so nothing at runtime would report the
		/// regression, and this is where it is caught instead.
		/// </para>
		/// </remarks>
		[Test]
		public void EveryListedAction_HasACaption()
		{
			FieldInfo field = typeof(UITKOptions).GetField("ActionDisplayNames", BindingFlags.Static | BindingFlags.NonPublic);
			LogAssert.IsNotNull(field, "UITKOptions must still declare ActionDisplayNames");
			Dictionary<string, string> captions = (Dictionary<string, string>)field.GetValue(null);
			LogAssert.IsNotNull(captions, "and it must still be populated");

			List<string> names = PlayerActionNames();
			Assert.Greater(names.Count, 20, "the Player map must still list the actions the panel builds rows from");

			List<string> missing = new List<string>();
			foreach (string name in names)
			{
				if (!captions.ContainsKey(name))
				{
					missing.Add(name);
				}
			}

			LogAssert.AreEqual(0, missing.Count,
				$"these actions would be listed under their identifier: {string.Join(", ", missing)}.");
		}
	}
}
