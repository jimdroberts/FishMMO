using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that an ability craft happens, and that a craft which does not happen says why.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Reported as "cant craft abilities at the ability crafter". The panel opened, the ability and
	/// its events could be chosen, and the Craft button did nothing at all: <c>UITKAbilityCraft</c>
	/// refused to send anything while its <c>CurrencyTemplateID</c> was zero, and that inspector
	/// field was never wired in the client GUI scene. Crafting had been dead since the UI Toolkit
	/// conversion and nothing in the game said so.
	/// </para>
	/// <para>
	/// Two defects, pinned separately. The wiring is the reason nobody could craft; the silence is
	/// the reason nobody could tell why. The server answered every refusal with a bare
	/// <c>return</c>, so even a request that reached it produced nothing a player could read —
	/// the same defect the merchant buy path had before <see cref="MerchantPurchaseResultTests"/>.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AbilityCraftResultTests
	{
		private const string ServerPath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/Interactable/InteractableSystem.AbilityCraft.cs";

		private const string ClientPath =
			"Assets/Scripts/Client/GUI/World/Ability/Crafting/UITKAbilityCraft.cs";

		private const string BroadcastPath =
			"Assets/Scripts/Shared/Implementation/Network/Interactable/InteractableBroadcasts.cs";

		private const string GuiScenePath = "Assets/Scenes/Client/ClientWorldGUI.unity";

		/// <summary>Source text with line endings normalised, so bounds do not depend on checkout.</summary>
		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>The body of a named method, bounded by the next member's signature.</summary>
		private static string MethodBody(string source, string signature, string nextSymbol)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");

			int end = source.IndexOf(nextSymbol, start, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"the end of {signature} must be locatable");

			return source.Substring(start, end - start);
		}

		[Test]
		public void ACraftResultBroadcastExists()
		{
			string source = ReadSource(BroadcastPath);

			LogAssert.IsTrue(source.Contains("struct AbilityCraftResultBroadcast"),
				"a craft must be answered, as a merchant purchase is");
			LogAssert.IsTrue(source.Contains("enum AbilityCraftFailure"),
				"a refusal must carry a reason, or the panel can only say something went wrong");
		}

		[Test]
		public void NoCraftExitLeavesTheClientUnanswered()
		{
			/* The whole silence defect in one assertion. A bare return inside the handler is a
			 * request the server understood, refused, and dropped — and the panel then waits out
			 * its own watchdog and blames the network. */
			string body = MethodBody(ReadSource(ServerPath),
				"public void OnServerAbilityCraftBroadcastReceived", "public Ability LearnAbility(");

			int searchFrom = 0;
			int refusals = 0;
			bool first = true;
			while (true)
			{
				int at = body.IndexOf("return;", searchFrom, StringComparison.Ordinal);
				if (at < 0)
				{
					break;
				}

				string since = body.Substring(searchFrom, at - searchFrom);

				/* The first exit is the one exception, and it is not reachable as a refusal: with
				 * no connection there is nobody to answer. Every other exit answers, and answers
				 * for itself — a send further up the handler belongs to a different branch. */
				if (first)
				{
					LogAssert.IsTrue(since.Contains("if (conn == null)"),
						"the only unanswered exit may be the one with no connection to answer");
					first = false;
				}
				else
				{
					LogAssert.IsTrue(since.Contains("SendCraftResult("),
						"every exit from the craft handler must answer the client before returning");
					++refusals;
				}

				searchFrom = at + 1;
			}

			LogAssert.IsTrue(refusals >= 10,
				"the craft handler must still refuse for its documented reasons, each with an answer");
		}

		[Test]
		public void EveryRefusalReasonIsDistinguishedOnTheServer()
		{
			/* One reason for three situations tells the player nothing: buying the template,
			 * forgetting an ability, and making room are three different things to go and do. */
			string body = MethodBody(ReadSource(ServerPath),
				"public void OnServerAbilityCraftBroadcastReceived", "public Ability LearnAbility(");

			LogAssert.IsTrue(body.Contains("AbilityCraftFailure.NotKnown"),
				"a template the character never learned must say so");
			LogAssert.IsTrue(body.Contains("AbilityCraftFailure.AlreadyCrafted"),
				"an ability already crafted from that template must say so");
			LogAssert.IsTrue(body.Contains("AbilityCraftFailure.AbilityLimit"),
				"a full ability list must say so");
			LogAssert.IsTrue(body.Contains("AbilityCraftFailure.InsufficientFunds"),
				"a craft the character cannot afford must say so");
			LogAssert.IsTrue(body.Contains("AbilityCraftFailure.InvalidEvents"),
				"events that cannot be combined must say so");
			LogAssert.IsTrue(body.Contains("SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.None"),
				"a successful craft must be answered too, or the submit lock is released by timeout");
		}

		[Test]
		public void TheClientNamesEveryRefusal()
		{
			string body = MethodBody(ReadSource(ClientPath),
				"private static string DescribeCraftFailure", "private void OnMainEntryPointerDown");

			foreach (string name in Enum.GetNames(typeof(AbilityCraftFailure)))
			{
				if (name == nameof(AbilityCraftFailure.None))
				{
					// Not a refusal; the success path writes its own line.
					continue;
				}

				LogAssert.IsTrue(body.Contains($"AbilityCraftFailure.{name}"),
					$"the panel must have wording for {name}, or a refusal reads as a generic shrug");
			}
		}

		[Test]
		public void AnUnsetCurrencyTemplateDoesNotRefuseTheCraft()
		{
			/* The reported symptom, and the shape of the fix. The panel's affordability check is a
			 * courtesy; the server holds the authoritative balance and now answers a craft it
			 * cannot afford with a reason. A client that cannot resolve the currency template must
			 * therefore send the request rather than refusing on the player's behalf — refusing was
			 * what made the Craft button do nothing at all. */
			string body = MethodBody(ReadSource(ClientPath),
				"public void OnCraft()", "private bool TryGetCurrencyBalance");

			int broadcast = body.IndexOf("Client.Broadcast(", StringComparison.Ordinal);
			LogAssert.IsTrue(broadcast >= 0, "the craft request must still be sent");

			int unsetGuard = body.IndexOf("CurrencyTemplateID == 0", StringComparison.Ordinal);
			LogAssert.IsTrue(unsetGuard < 0 || unsetGuard > broadcast,
				"an unset currency template must not stop the craft request being sent");
		}

		[Test]
		public void TheCraftPanelIsWiredToACurrencyAttribute()
		{
			/* The wiring itself. UITKAbilityCraft shipped with CurrencyTemplateID at 0 in the
			 * client GUI scene, which is why nobody could craft. The value is a deterministic hash
			 * of the template's type and asset name, so it can be checked against the actual
			 * attribute assets rather than hard-coded here. */
			string scene = ReadSource(GuiScenePath);

			int component = scene.IndexOf("FishMMO.Client::FishMMO.Client.UITKAbilityCraft", StringComparison.Ordinal);
			LogAssert.IsTrue(component >= 0, "the client GUI scene must still contain the ability craft panel");

			int field = scene.IndexOf("CurrencyTemplateID:", component, StringComparison.Ordinal);
			LogAssert.IsTrue(field > component, "the ability craft panel must still declare a currency template");

			int lineEnd = scene.IndexOf('\n', field);
			string value = scene.Substring(field + "CurrencyTemplateID:".Length,
				lineEnd - field - "CurrencyTemplateID:".Length).Trim();

			LogAssert.IsTrue(int.TryParse(value, out int templateID),
				$"the currency template reference must be a template ID, not '{value}'");
			LogAssert.IsTrue(templateID != 0,
				"an unset currency template is what broke crafting; the panel must name one");

			LogAssert.IsTrue(CharacterAttributeIDs().Contains(templateID),
				$"CurrencyTemplateID {templateID} must resolve to a CharacterAttributeTemplate asset");
		}

		/// <summary>
		/// The deterministic IDs of every character attribute template in the project.
		/// </summary>
		/// <remarks>
		/// <c>CachedScriptableObject.AddToCache</c> computes an asset's ID from its concrete type
		/// name and its asset name, and it runs when the addressables load rather than when the
		/// editor loads an asset — so an asset read here has an ID of zero and the hash has to be
		/// recomputed the same way it is at runtime.
		/// </remarks>
		private static HashSet<int> CharacterAttributeIDs()
		{
			HashSet<int> ids = new HashSet<int>();

			foreach (string guid in AssetDatabase.FindAssets("t:CharacterAttributeTemplate"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				CharacterAttributeTemplate template = AssetDatabase.LoadAssetAtPath<CharacterAttributeTemplate>(path);
				if (template == null)
				{
					continue;
				}

				ids.Add((template.GetType().Name + template.name).GetDeterministicHashCode());
			}

			LogAssert.IsTrue(ids.Count > 0, "the project must still ship character attribute templates");
			return ids;
		}
	}
}
