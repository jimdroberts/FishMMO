using System;
using System.IO;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Ability knowledge has to survive a logout, and all three kinds of it have to come back.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Three separate defects made knowledge look like it was never saved, and they compounded:
	/// </para>
	/// <list type="number">
	/// <item>The character save covered eight sub-entity tables and not this one. Only the paths
	/// that GRANT knowledge wrote rows, so anything learned another way — a lore object, a
	/// scroll — was never written at all.</item>
	/// <item>The restore was nested inside <c>abilityData.Count &gt; 0</c>, so a character with no
	/// CRAFTED ability loaded no knowledge, however many rows it had. That is every new character
	/// and everyone who had bought templates but not yet crafted.</item>
	/// <item>The restore resolved every row as a <c>BaseAbilityTemplate</c>. An
	/// <c>AbilityEvent</c> is not one — it derives from <c>Trigger</c> and lives in a different
	/// cache — so every effect came back null and was dropped even when the branch above ran.</item>
	/// </list>
	/// </remarks>
	[TestFixture]
	public class KnownAbilityPersistenceTests
	{
		private const string SavingPath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.Saving.cs";

		private const string LoadingPath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.Loading.cs";

		private const string KnowledgePath =
			"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityController.Knowledge.cs";

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		private static int Occurrences(string source, string text) =>
			source.Split(new[] { text }, StringSplitOptions.None).Length - 1;

		[Test]
		public void TheCharacterSaveCoversKnowledge()
		{
			string source = ReadSource(SavingPath);

			LogAssert.IsTrue(source.Contains("List<CharacterKnownAbilityData> KnownAbilities"),
				"the sub-entity snapshot must carry knowledge, like the other eight tables");
			LogAssert.IsTrue(source.Contains("AppendKnownAbilityData(character, snapshot.KnownAbilities, snapshot.KnowledgeVersions)"),
				"and the capture must collect it, with the version it was captured at");
			LogAssert.IsTrue(source.Contains("SaveKnownAbilitiesAsync(s.KnownAbilities, s.KnowledgeVersions)"),
				"and both save paths must write it");
		}

		[Test]
		public void ASaveClearsTheDirtyMarkOnlyForTheKnowledgeItWrote()
		{
			/* The write runs off the main thread, and the dirty mark is cleared when it completes.
			 * Something learned in between is not in the rows just written, so clearing the mark
			 * unconditionally lost it until the next learn (issue #267). A learn bumps the version;
			 * the clear only happens while the version is still the one captured. */
			string knowledge = ReadSource(KnowledgePath);
			int raised = Occurrences(knowledge, "KnowledgeDirty = true;");
			LogAssert.IsTrue(raised > 0, "learning must still raise the mark");
			LogAssert.AreEqual(raised, Occurrences(knowledge, "KnowledgeDirty = true;\n\t\t\t++KnowledgeVersion;"),
				"every learn that raises the mark must also move the version");

			string saving = ReadSource(SavingPath);
			int mark = saving.IndexOf("private void MarkKnowledgePersisted(Dictionary<long, long> knowledgeVersions)", StringComparison.Ordinal);
			LogAssert.IsTrue(mark >= 0, "the completion must still clear the mark per captured version");

			string body = saving.Substring(mark, Math.Min(800, saving.Length - mark));
			int check = body.IndexOf("abilityController.KnowledgeVersion == captured.Value", StringComparison.Ordinal);
			int clear = body.IndexOf("abilityController.KnowledgeDirty = false", StringComparison.Ordinal);
			LogAssert.IsTrue(check >= 0 && clear > check,
				"the mark is cleared only when nothing was learned since the capture");
		}

		[Test]
		public void TheSaveIsGatedOnSomethingActuallyBeingLearned()
		{
			/* Knowledge is written whole, because the controller keeps it as two id sets with
			 * nothing to hang a per-row version on. That is only affordable because a character
			 * that learned nothing writes nothing. */
			string source = ReadSource(SavingPath);
			int append = source.IndexOf("private void AppendKnownAbilityData", StringComparison.Ordinal);
			LogAssert.IsTrue(append >= 0, "the capture must still exist");

			string body = source.Substring(append, Math.Min(1200, source.Length - append));
			LogAssert.IsTrue(body.Contains("KnowledgeDirty"),
				"the capture must be gated on the dirty mark");
			LogAssert.IsTrue(body.Contains("KnownBaseAbilities") && body.Contains("KnownAbilityEvents"),
				"and must write both kinds, which share one table");
		}

		[Test]
		public void KnowledgeIsRestoredWhetherOrNotTheCharacterHasCraftedAnything()
		{
			/* The nesting defect. A character with rows and no crafted ability got an empty
			 * Knowledge tab on every login. */
			string source = ReadSource(LoadingPath);

			int restore = source.IndexOf("RestoreKnownAbilities(character, abilityController", StringComparison.Ordinal);
			LogAssert.IsTrue(restore >= 0, "the restore must still be called");

			int abilityGuard = source.IndexOf("if (abilityData != null)", StringComparison.Ordinal);
			LogAssert.IsTrue(abilityGuard >= 0 && abilityGuard < restore,
				"crafted abilities are restored in their own guarded block");

			/* The call must not sit inside that block: the text between the guard and the call has
			 * to close it. */
			string between = source.Substring(abilityGuard, restore - abilityGuard);
			LogAssert.IsTrue(between.Contains("\n\t\t\t\t}\n"),
				"the knowledge restore must be outside the crafted-ability guard");
		}

		[Test]
		public void BothKindsOfKnowledgeAreResolvedFromTheOneTable()
		{
			/* Base abilities and effects share character_known_ability, keyed by template id, and
			 * they live in different caches. Asking only one of them dropped the other. */
			string source = ReadSource(LoadingPath);
			int restore = source.IndexOf("private static void RestoreKnownAbilities", StringComparison.Ordinal);
			LogAssert.IsTrue(restore >= 0, "the restore helper must exist");

			string body = source.Substring(restore, Math.Min(2600, source.Length - restore));
			LogAssert.IsTrue(body.Contains("BaseAbilityTemplate.Get<BaseAbilityTemplate>"),
				"a row may name a base ability");
			LogAssert.IsTrue(body.Contains("AbilityEvent.Get<AbilityEvent>"),
				"or an effect, which is the half that used to be dropped");
			LogAssert.IsTrue(body.Contains("LearnBaseAbilities") && body.Contains("LearnAbilityEvents"),
				"and each is learned into its own set");
		}

		[Test]
		public void ARestoreDoesNotMarkTheCharacterDirty()
		{
			/* Everything applied by a restore is already in the database. Leaving the mark set
			 * would rewrite the whole knowledge set on the first save after every login. */
			string source = ReadSource(LoadingPath);
			int restore = source.IndexOf("private static void RestoreKnownAbilities", StringComparison.Ordinal);
			string body = source.Substring(restore, Math.Min(2600, source.Length - restore));

			LogAssert.IsTrue(body.Contains("KnowledgeDirty = false"),
				"a restore must clear the dirty mark it raised while learning");
		}

		[Test]
		public void AnUnknownTemplateIsReportedRatherThanSilentlyDropped()
		{
			/* Content that was removed, or an addressable that did not load. The row is left alone
			 * either way — the alternative is a save that quietly deletes knowledge because this
			 * build could not resolve it. */
			string source = ReadSource(LoadingPath);
			int restore = source.IndexOf("private static void RestoreKnownAbilities", StringComparison.Ordinal);
			string body = source.Substring(restore, Math.Min(2600, source.Length - restore));

			LogAssert.IsTrue(body.Contains("Log.Warning"), "an unresolvable row must say so");
		}

		[Test]
		public void LearningRaisesTheDirtyMarkThatTheSaveReads()
		{
			string source = ReadSource(KnowledgePath);

			LogAssert.IsTrue(source.Contains("public bool KnowledgeDirty { get; set; }"),
				"the controller must expose the mark the save reads");

			int learn = source.IndexOf("public bool LearnBaseAbility(", StringComparison.Ordinal);
			LogAssert.IsTrue(learn >= 0, "the learn method must still exist");

			string body = source.Substring(learn, Math.Min(1200, source.Length - learn));
			LogAssert.IsTrue(body.Contains("KnownBaseAbilities.Add(template.ID)") && body.Contains("KnowledgeDirty = true"),
				"learning something new must mark the character for the next save");
		}
	}
}
