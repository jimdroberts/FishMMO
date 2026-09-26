using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;
using FishMMO.Shared;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that a character's saved buffs are exactly its current buffs: written as one set with
	/// the character row, ordered by the row's version, and never resurrected at login.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The defect: <c>character_buffs</c> rows were only ever upserted, the save wrote nothing for a
	/// character with no buffs, and the load restores every row. A buff that expired, was dismissed
	/// or was stripped on death after it had first been saved came back at the next login. Per-buff
	/// versions could not make a delete safe: a buff re-applied after it ended restarts its counter,
	/// and an empty set leaves no row to carry a version, so an older save landing late could re-add
	/// what a newer one dropped.
	/// </para>
	/// <para>
	/// The SQL itself — set replacement, the row guard, and saves landing out of order or in parallel
	/// — was validated against a throwaway PostgreSQL through the real services; it cannot run here.
	/// What is pinned here is the pure flattening rule and the shape that keeps the set riding the row.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class BuffSetPersistenceTests
	{
		private const string SavingPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.Saving.cs";

		private static CharacterBuffData Buff(long characterId, int template, long version = 1, double remaining = 30.0)
		{
			return new CharacterBuffData(0, version, characterId, template, remaining, 1.0, 2, 3);
		}

		private static List<(long CharacterID, long Version, IReadOnlyList<CharacterBuffData> Buffs)> Sets(
			params (long CharacterID, long Version, IReadOnlyList<CharacterBuffData> Buffs)[] sets)
		{
			return new List<(long, long, IReadOnlyList<CharacterBuffData>)>(sets);
		}

		#region FlattenSets

		[Test]
		public void AnEmptySet_StillNamesItsCharacter_SoItsRowsAreDeleted()
		{
			CharacterBuffService.BuffSetRows rows = CharacterBuffService.FlattenSets(Sets((18, 5, new List<CharacterBuffData>())));

			LogAssert.IsTrue(rows != null, "an empty set is an instruction, not nothing");
			LogAssert.AreEqual(1, rows.Owners.Length, "its character is named");
			LogAssert.AreEqual(18L, rows.Owners[0], "as the owner whose rows are replaced");
			LogAssert.AreEqual(0, rows.RowCount, "with no rows to put back");
		}

		[Test]
		public void EveryRow_TakesItsSetsCharacterAndVersion_WhateverTheDtoSays()
		{
			/* The set is written only after its character row passed that row's version and ownership
			 * checks, so the row's character and version are the ones proven. A DTO naming anyone else
			 * must not ride on that proof. */
			var set = new List<CharacterBuffData> { Buff(99, 101, version: 3), Buff(18, 102, version: 1) };
			CharacterBuffService.BuffSetRows rows = CharacterBuffService.FlattenSets(Sets((18, 40, set)));

			LogAssert.AreEqual(2, rows.RowCount, "both buffs are written");
			LogAssert.AreEqual(18L, rows.CharacterIds[0], "under the set's character");
			LogAssert.AreEqual(18L, rows.CharacterIds[1], "both of them");
			LogAssert.AreEqual(40L, rows.Versions[0], "at the set's version");
			LogAssert.AreEqual(40L, rows.Versions[1], "both of them");
			LogAssert.AreEqual(101, rows.TemplateIds[0], "the template is the buff's own");
			LogAssert.AreEqual(2, rows.Stacks[0], "and so is its state");
			LogAssert.AreEqual(3, rows.TickCounts[0], "all of it");
		}

		[Test]
		public void ATemplateNamedTwice_IsWrittenOnce()
		{
			/* An INSERT ... ON CONFLICT that meets one key twice fails the whole statement, and that
			 * statement shares the character row's transaction: a duplicate would lose the row too. */
			var set = new List<CharacterBuffData> { Buff(18, 101, remaining: 10), Buff(18, 101, remaining: 99), Buff(18, 102) };
			CharacterBuffService.BuffSetRows rows = CharacterBuffService.FlattenSets(Sets((18, 7, set)));

			LogAssert.AreEqual(2, rows.RowCount, "one row per template");
			LogAssert.AreEqual(10.0, rows.RemainingTimes[0], "the first occurrence is kept");
		}

		[Test]
		public void ACharacterNamedTwice_KeepsTheNewerSet()
		{
			CharacterBuffService.BuffSetRows rows = CharacterBuffService.FlattenSets(Sets(
				(18, 5, new List<CharacterBuffData> { Buff(18, 101) }),
				(18, 6, new List<CharacterBuffData> { Buff(18, 202) }),
				(18, 4, new List<CharacterBuffData> { Buff(18, 303) })));

			LogAssert.AreEqual(1, rows.Owners.Length, "one owner");
			LogAssert.AreEqual(1, rows.RowCount, "one set's rows");
			LogAssert.AreEqual(202, rows.TemplateIds[0], "the newest snapshot's");
			LogAssert.AreEqual(6L, rows.Versions[0], "at its version");
		}

		[Test]
		public void ANullSet_OrNoSets_WriteNothing()
		{
			LogAssert.IsNull(CharacterBuffService.FlattenSets(null), "no sets");
			LogAssert.IsNull(CharacterBuffService.FlattenSets(Sets()), "an empty list of sets");
			LogAssert.IsNull(CharacterBuffService.FlattenSets(Sets((18, 5, null))),
				"a null set says nothing about buffs, so it must not delete anybody's");
		}

		#endregion

		#region The set rides the row

		private static CharacterData Snapshot(IReadOnlyList<CharacterBuffData> buffs)
		{
			return new CharacterData(
				id: 18, name: "n", nameLowercase: "n", account: "a", selected: false, worldServerID: 1,
				sceneName: "s", sceneHandle: 1, bindScene: "b", bindX: 0, bindY: 0, bindZ: 0,
				instanceID: 0, instanceX: 0, instanceY: 0, instanceZ: 0,
				instanceRotX: 0, instanceRotY: 0, instanceRotZ: 0, instanceRotW: 1,
				raceID: 1, modelIndex: 0, x: 0, y: 0, z: 0, rotX: 0, rotY: 0, rotZ: 0, rotW: 1,
				accessLevel: 1, online: true, flags: 0, version: 9,
				timeCreated: DateTime.UtcNow, lastSaved: DateTime.UtcNow, buffs: buffs);
		}

		[Test]
		public void ASnapshotWithoutASet_SaysNothingAboutBuffs()
		{
			CharacterData fetched = Snapshot(null);
			LogAssert.IsNull(fetched.Buffs, "a fetched row, or the world server's flag edit, leaves the stored buffs alone");
			LogAssert.IsNull(default(CharacterData).Buffs, "and that is the default");
		}

		[Test]
		public void TheCopiesOfASnapshot_KeepItsSet()
		{
			var set = new List<CharacterBuffData>();
			CharacterData snapshot = Snapshot(set);

			LogAssert.IsTrue(ReferenceEquals(set, snapshot.WithVersion(10).Buffs), "a re-versioned snapshot is still the same capture");
			LogAssert.IsTrue(ReferenceEquals(set, snapshot.WithFlagsVersionAndTimestamp(0, 10, DateTime.UtcNow).Buffs), "and so is a re-flagged one");
		}

		[Test]
		public void TheBuffService_HasNoWriteOfItsOwn()
		{
			/* A second writer beside the set — the old per-row upsert — could only add and overwrite,
			 * which is the resurrection itself. */
			LogAssert.IsFalse(typeof(IPersistManyAction<CharacterBuffData>).IsAssignableFrom(typeof(ICharacterBuffService)),
				"ICharacterBuffService must not offer a bulk upsert");
			LogAssert.IsNull(typeof(CharacterBuffService).GetMethod("PersistAsync", BindingFlags.Public | BindingFlags.Instance),
				"nor may the service");
		}

		[Test]
		public void ABuffInstance_CarriesNoPersistenceVersion()
		{
			LogAssert.IsNull(typeof(Buff).GetField("Version", BindingFlags.Public | BindingFlags.Instance),
				"a per-instance counter restarts when a buff is re-applied, so it cannot order a set");
		}

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		private static string CodeOnly(string source)
		{
			var kept = new System.Text.StringBuilder(source.Length);
			foreach (string line in source.Split('\n'))
			{
				string t = line.TrimStart();
				if (t.StartsWith("//") || t.StartsWith("/*") || t.StartsWith("*"))
				{
					continue;
				}
				kept.Append(line).Append('\n');
			}
			return kept.ToString();
		}

		private static string MethodBody(string source, string signature, string nextSymbol)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");
			int end = source.IndexOf(nextSymbol, start + signature.Length, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"the end of {signature} must be locatable");
			return source.Substring(start, end - start);
		}

		[Test]
		public void TheSnapshotCapturesTheSet_AndNoSubEntityPathWritesBuffs()
		{
			string source = CodeOnly(ReadSource(SavingPath));

			string build = MethodBody(source, "private CharacterData BuildCharacterData(IPlayerCharacter character)", "private enum CharacterSaveOutcome");
			LogAssert.IsTrue(build.Contains("buffs: CaptureBuffSet(character, character.Version)"),
				"the set is captured with the row, at the row's version, so every save path and the retry queue carry it");

			string snapshot = MethodBody(source, "private sealed class SubEntitySnapshot", "public SubEntitySnapshot(");
			LogAssert.IsFalse(snapshot.Contains("CharacterBuffData"),
				"buffs are not a sub-entity list: written apart from the row, they had no ordering that could delete");
			LogAssert.IsFalse(source.Contains("ICharacterBuffService"), "nothing in the save path writes buffs on its own");
		}

		[Test]
		public void ACharacterWithNoBuffs_CapturesAnEmptySet_NotNothing()
		{
			string capture = CodeOnly(MethodBody(ReadSource(SavingPath),
				"private List<CharacterBuffData> CaptureBuffSet(IPlayerCharacter character, long version)",
				"private void AppendAttributeData("));

			int empty = capture.IndexOf("if (buffController.Buffs.Count == 0)", StringComparison.Ordinal);
			LogAssert.IsTrue(empty >= 0, "the no-buffs case is handled explicitly");
			string branch = capture.Substring(empty, capture.IndexOf('}', empty) - empty);
			LogAssert.IsTrue(branch.Contains("return buffs;") && !branch.Contains("return null;"),
				"no buffs is an empty set — the instruction that deletes the rows of buffs that have ended");
		}

		#endregion
	}
}
