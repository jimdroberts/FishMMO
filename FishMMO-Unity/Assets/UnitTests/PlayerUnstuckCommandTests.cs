using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using CharacterSystem = FishMMO.Server.Implementation.World.SceneServer.CharacterSystem;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A player's own <c>/unstuck</c> (issue #252).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The gate is <c>CanActOrMove</c>, not <c>CanAct</c>.</b> Only the former refuses a character
	/// in combat; with plain <c>CanAct</c>, <c>/unstuck</c> is a free escape from every fight.
	/// </para>
	/// <para>
	/// <b>Not in an instance</b>, which has <c>/leaveinstance</c> and its own rules. <b>Through the
	/// motor</b>, because prediction reconciles against it and a transform write is corrected
	/// straight back. <b>The cooldown starts only after a move</b>, so a refused attempt can be
	/// retried as soon as its reason clears.
	/// </para>
	/// <para>
	/// <b>Registered and removed together.</b> The chat command registry is static: a word left
	/// behind outlives the <c>CharacterSystem</c> asset and runs against a destroyed instance. The
	/// removal list is read by reflection, so the pin compares what is registered with what is
	/// actually removed.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class PlayerUnstuckCommandTests
	{
		private const string CharacterDirectory = "Assets/Scripts/Server/Implementation/World/SceneServer/Character";
		private const string UnstuckPath = CharacterDirectory + "/CharacterSystem.Unstuck.cs";
		private const string CharacterSystemPath = CharacterDirectory + "/CharacterSystem.cs";

		private const string HandlerSignature = "private bool OnUnstuckCommand(";
		private const string MotorMove = "character.Motor.SetPositionAndRotationAndVelocity(position, rotation, Vector3.zero);";

		private static string Handler(string code) => SourceScanPins.Body(code, HandlerSignature);

		/// <summary>Null when the branch opened by <paramref name="condition"/> exists, returns, and comes before the move.</summary>
		private static string RefusalFailure(string code, string condition, string what)
		{
			string body = Handler(code);
			if (body == null)
			{
				return "OnUnstuckCommand is gone";
			}
			Match open = Regex.Match(body, condition + @"\s*\{");
			if (!open.Success)
			{
				return $"OnUnstuckCommand no longer tests {what}";
			}
			string branch = SourceScanPins.Braced(body, open.Index + open.Length - 1);
			if (branch == null || !Regex.IsMatch(branch, @"return true;\s*\}$"))
			{
				return $"the {what} refusal must return";
			}
			int move = body.IndexOf("Motor.SetPositionAndRotationAndVelocity(", StringComparison.Ordinal);
			return move > open.Index ? null : $"the {what} refusal must come before the move";
		}

		[Test]
		public void TheGateIsCanActOrMoveNotPlainCanAct()
		{
			SourceScanPins.HoldsAndFires("OnUnstuckCommand", SourceScanPins.ReadCode(UnstuckPath),
				c =>
				{
					string body = Handler(c);
					if (body != null && Regex.IsMatch(body, @"CharacterStateValidation\.CanAct\("))
					{
						return "plain CanAct lets a character in combat /unstuck out of the fight";
					}
					return RefusalFailure(c, @"if \(!CharacterStateValidation\.CanActOrMove\(character\)\)", "state gate");
				},
				SourceScanPins.Replace("CharacterStateValidation.CanActOrMove(", "CharacterStateValidation.CanAct("),
				"the gate is weakened to CanAct");
		}

		[Test]
		public void AnInstanceIsRefused()
		{
			SourceScanPins.HoldsAndFires("OnUnstuckCommand", SourceScanPins.ReadCode(UnstuckPath),
				c => RefusalFailure(c, @"if \(character\.IsInInstance\(\)\)", "instance"),
				SourceScanPins.RegexReplaceFirst(@"(if \(character\.IsInInstance\(\)\)\s*\{[^{}]*?)return true;", "$1"),
				"the instance refusal falls through");
		}

		[Test]
		public void TheMoveGoesThroughTheMotorAndNeverTheTransform()
		{
			SourceScanPins.HoldsAndFires("CharacterSystem.Unstuck", SourceScanPins.ReadCode(UnstuckPath),
				c =>
				{
					string body = Handler(c);
					if (body == null || !body.Contains(MotorMove))
					{
						return "OnUnstuckCommand must move through the motor, with the velocity zeroed";
					}
					Match write = Regex.Match(c, @"\.(position|rotation|localPosition|localRotation)\s*=(?!=)|\.SetPositionAndRotation\(");
					return write.Success ? $"'{write.Value}' writes the transform, which prediction corrects straight back" : null;
				},
				SourceScanPins.Replace(MotorMove, "character.Transform.position = position;"),
				"the move writes the transform");
		}

		[Test]
		public void TheCooldownStartsOnlyAfterTheMove()
		{
			SourceScanPins.HoldsAndFires("OnUnstuckCommand", SourceScanPins.ReadCode(UnstuckPath),
				c =>
				{
					int writes = Regex.Matches(c, @"nextUnstuckUtc\[[^\]]+\]\s*=(?!=)|nextUnstuckUtc\.(?:Add|TryAdd)\(").Count;
					if (writes != 1)
					{
						return $"the cooldown is started in {writes} places; it must be exactly one, after the move";
					}
					return SourceScanPins.InOrder(Handler(c),
						"nextUnstuckUtc.TryGetValue(character.ID",
						"Motor.SetPositionAndRotationAndVelocity(",
						"nextUnstuckUtc[character.ID] =");
				},
				SourceScanPins.InsertBefore("if (!TryResolveUnstuckDestination(", "nextUnstuckUtc[character.ID] = now;\n"),
				"the cooldown starts before the move");
		}

		/// <summary>The words registered to <c>OnUnstuckCommand</c> inside an <c>AddCommands</c> table.</summary>
		private static HashSet<string> RegisteredWords(string code)
		{
			var words = new HashSet<string>(StringComparer.Ordinal);
			foreach (Match add in Regex.Matches(code, @"ChatHelper\.AddCommands\("))
			{
				string table = SourceScanPins.Braced(code, code.IndexOf('{', add.Index));
				if (table == null)
				{
					continue;
				}
				foreach (Match entry in Regex.Matches(table, @"\{\s*""(/[a-z]+)""\s*,\s*OnUnstuckCommand\s*\}"))
				{
					words.Add(entry.Groups[1].Value);
				}
			}
			return words;
		}

		private static string RegistrationFailure(string code, string[] removedWords)
		{
			HashSet<string> registered = RegisteredWords(code);
			if (!registered.SetEquals(new[] { "/unstuck", "/stuck" }))
			{
				return $"registered [{string.Join(", ", registered)}]; both /unstuck and /stuck must be";
			}
			if (removedWords == null || !registered.SetEquals(removedWords))
			{
				return $"registered [{string.Join(", ", registered)}] but removes [{string.Join(", ", removedWords ?? Array.Empty<string>())}]";
			}
			string deinitialize = SourceScanPins.Body(code, "public override void OnDeinitialize(");
			return deinitialize != null && deinitialize.Contains("ChatHelper.RemoveCommands(UnstuckCommandWords);")
				? null
				: "OnDeinitialize must remove the unstuck words";
		}

		[Test]
		public void BothWordsAreRegisteredAndRemovedOnUnregister()
		{
			FieldInfo field = typeof(CharacterSystem).GetField("UnstuckCommandWords", BindingFlags.Static | BindingFlags.NonPublic);
			LogAssert.IsNotNull(field, "CharacterSystem.UnstuckCommandWords must exist");
			string[] removed = (string[])field.GetValue(null);

			string code = SourceScanPins.ReadCode(CharacterSystemPath);
			SourceScanPins.HoldsAndFires("CharacterSystem", code,
				c => RegistrationFailure(c, removed),
				SourceScanPins.Replace("{ \"/stuck\", OnUnstuckCommand },", string.Empty),
				"/stuck is not registered");
			SourceScanPins.HoldsAndFires("CharacterSystem", code,
				c => RegistrationFailure(c, removed),
				SourceScanPins.Replace("ChatHelper.RemoveCommands(UnstuckCommandWords);", string.Empty),
				"the words are never removed");

			// Control: the removal list losing a word.
			LogAssert.IsNotNull(RegistrationFailure(code, removed.Where(w => w != "/stuck").ToArray()),
				"the pin must fire when a registered word is missing from the removal list");
		}
	}
}
