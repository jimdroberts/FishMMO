using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using FishMMO.Shared;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.NameGeneration.Editor;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The title engine against the shipped grammar and race data: honorifics
	/// follow gender, places and ordinals attach only where the grammar allows,
	/// registers and professions steer the category, the length budget holds,
	/// composed legends read like authored ones, and batches do not repeat.
	/// </summary>
	[TestFixture]
	public class TitleGenerationTests
	{
		private static readonly Regex Masculine = new Regex(@"\b(Sir|Lord|Master|King|Prince|Duke|Clanfather)\b");
		private static readonly Regex Feminine = new Regex(@"\b(Dame|Lady|Mistress|Queen|Princess|Duchess|Clanmother)\b");

		[OneTimeSetUp]
		public void LoadAssets()
		{
			NamingTemplateEditorLoader.Reload();
		}

		private static List<string> Titles(int count, NameRequest prototype, int firstSeed = 1)
		{
			var titles = new List<string>(count);
			for (int seed = firstSeed; seed < firstSeed + count; seed++)
			{
				titles.Add(new NameGenerator(seed).Generate(new NameRequest
				{
					Race = prototype.Race,
					Gender = prototype.Gender,
					TitleType = prototype.TitleType,
					Register = prototype.Register,
					Profession = prototype.Profession,
					MaxTitleLength = prototype.MaxTitleLength,
					AllowCompoundTitle = prototype.AllowCompoundTitle,
					TitleOnly = true,
				}).Title);
			}
			return titles;
		}

		// ── Grammar asset ─────────────────────────────────────────────

		[Test]
		public void Grammar_CarriesTheTitleTemplatesAndRules()
		{
			Assert.AreEqual(TitleBuilder.DefaultTemplates.Length, NameGrammar.TitleTemplates.Count,
				"The grammar asset should carry the same composition set the builder falls back to.");
			Assert.IsTrue(NameGrammar.PlaceTakingHonorifics.Contains("Lord"));
			Assert.IsFalse(NameGrammar.PlaceTakingHonorifics.Contains("Sir"));
			Assert.IsTrue(NameGrammar.OrdinalTakingHonorifics.Contains("King"));
			Assert.IsFalse(NameGrammar.OrdinalTakingHonorifics.Contains("Sir"));
			Assert.Greater(NameGrammar.GenericOccupations.Length, 20);
		}

		[Test]
		public void Human_HasGenderedHonorificsAndOccupations()
		{
			Assert.IsTrue(RaceRegistry.TryGetTitles("human", out RaceTitles titles));
			CollectionAssert.Contains(titles.HonorificMasculine, "Sir");
			CollectionAssert.Contains(titles.HonorificFeminine, "Dame");
			CollectionAssert.DoesNotContain(titles.Honorific, "Sir");
			CollectionAssert.DoesNotContain(titles.Honorific, "Lady");
			CollectionAssert.Contains(titles.Occupational, "Banker");
			Assert.AreEqual(titles.HonorificFeminine.Length, titles.HonorificFeminine.Distinct().Count(), "No duplicate honorifics.");
		}

		// ── Gender ────────────────────────────────────────────────────

		[Test]
		public void Honorifics_FollowGender()
		{
			var female = Titles(150, new NameRequest { Race = "human", Gender = CharacterGender.Female, TitleType = TitleType.Honorific });
			var male = Titles(150, new NameRequest { Race = "human", Gender = CharacterGender.Male, TitleType = TitleType.Honorific });
			var neutral = Titles(150, new NameRequest { Race = "human", Gender = CharacterGender.Unspecified, TitleType = TitleType.Honorific });

			Assert.AreEqual(0, female.Count(t => Masculine.IsMatch(t)), "A feminine name got a masculine honorific.");
			Assert.AreEqual(0, male.Count(t => Feminine.IsMatch(t)), "A masculine name got a feminine honorific.");
			Assert.AreEqual(0, neutral.Count(t => Masculine.IsMatch(t) || Feminine.IsMatch(t)), "An ungendered name got a gendered honorific.");
			Assert.Greater(female.Count(t => Feminine.IsMatch(t)), 0, "Feminine honorifics never appeared.");
			Assert.Greater(male.Count(t => Masculine.IsMatch(t)), 0, "Masculine honorifics never appeared.");
		}

		[Test]
		public void Pronouns_FollowGender()
		{
			var female = Titles(300, new NameRequest { Race = "human", Gender = CharacterGender.Female, TitleType = TitleType.Honorific });
			var male = Titles(300, new NameRequest { Race = "human", Gender = CharacterGender.Male, TitleType = TitleType.Honorific });
			Assert.AreEqual(0, female.Count(t => t.Contains("of his Name")));
			Assert.AreEqual(0, male.Count(t => t.Contains("of her Name")));
		}

		// ── Grammar rules ─────────────────────────────────────────────

		[Test]
		public void OnlyPlaceTakingHonorifics_TakeAPlace()
		{
			var titles = Titles(400, new NameRequest { Race = "human", Gender = CharacterGender.Male, TitleType = TitleType.Honorific, AllowCompoundTitle = false });
			foreach (string title in titles)
			{
				Match m = Regex.Match(title, @"^(.+?) of (.+)$");
				if (!m.Success)
				{
					continue;
				}
				Assert.IsTrue(NameGrammar.PlaceTakingHonorifics.Contains(m.Groups[1].Value),
					$"'{title}': '{m.Groups[1].Value}' may not take a place.");
			}
			Assert.AreEqual(0, titles.Count(t => t.StartsWith("Sir of") || t.StartsWith("Dame of")));
		}

		[Test]
		public void OnlyOrdinalTakingHonorifics_TakeAnOrdinal()
		{
			var titles = Titles(400, new NameRequest { Race = "human", Gender = CharacterGender.Male, TitleType = TitleType.Honorific, AllowCompoundTitle = false });
			foreach (string title in titles)
			{
				Match m = Regex.Match(title, @"^(.+?), \w[\w-]* of (his|her|their) Name$");
				if (m.Success)
				{
					Assert.IsTrue(NameGrammar.OrdinalTakingHonorifics.Contains(m.Groups[1].Value),
						$"'{title}': '{m.Groups[1].Value}' may not take an ordinal.");
				}
			}
		}

		[Test]
		public void ComposedLegends_CapitaliseTheirObjects()
		{
			var titles = Titles(300, new NameRequest { Race = "human", TitleType = TitleType.Legend, AllowCompoundTitle = false });
			foreach (string title in titles.Where(t => t.StartsWith("Who ")))
			{
				Assert.IsFalse(Regex.IsMatch(title, @"\b(a|an|the) [a-z]"), $"Lower-case object in '{title}'.");
			}
		}

		// ── Register and profession ───────────────────────────────────

		[Test]
		public void Civil_WithProfession_UsesIt()
		{
			var titles = Titles(200, new NameRequest { Race = "human", Gender = CharacterGender.Female, Register = TitleRegister.Civil, Profession = "Banker" });
			Assert.Greater(titles.Count(t => t.Contains("Banker")), 40, "The profession should carry a good share of Civil titles.");
			Assert.AreEqual(0, titles.Count(t => t.StartsWith("Who ")), "Civil titles must not be legends.");
		}

		[Test]
		public void Martial_NeverYieldsTrades()
		{
			var trades = new HashSet<string>(NameGrammar.GenericOccupations);
			var titles = Titles(200, new NameRequest { Race = "human", Gender = CharacterGender.Male, Register = TitleRegister.Martial, AllowCompoundTitle = false });
			Assert.AreEqual(0, titles.Count(t => trades.Contains(t) || t.StartsWith("Master ") && trades.Contains(t.Substring(7))),
				"A Martial title came out as a trade.");
			Assert.Greater(titles.Count(t => RaceRegistry.TryGetTitles("human", out RaceTitles rt) && rt.Rank.Any(r => t.StartsWith(r))), 60,
				"Martial titles should mostly be ranks.");
		}

		[Test]
		public void Mythic_IsLegendsAndEpithets()
		{
			var titles = Titles(200, new NameRequest { Race = "human", Register = TitleRegister.Mythic, AllowCompoundTitle = false });
			Assert.Greater(titles.Count(t => t.StartsWith("Who ") || t.StartsWith("Last of") || t.StartsWith("Slayer") || t.StartsWith("Breaker")), 60);
			Assert.AreEqual(0, titles.Count(t => t == "Sir" || t == "Dame" || t == "Captain"));
		}

		[Test]
		public void Monsters_NeverTakeGenericTrades()
		{
			var trades = new HashSet<string>(NameGrammar.GenericOccupations);
			foreach (string race in new[] { "Slime", "Dire Wolf", "Kraken" })
			{
				var titles = Titles(120, new NameRequest { Race = race, Register = TitleRegister.Civil, AllowCompoundTitle = false });
				Assert.AreEqual(0, titles.Count(t => trades.Any(tr => t == tr || t == "Master " + tr || t == "Guild " + tr || t.StartsWith(tr + " of "))),
					$"{race} was titled with a trade.");
				Assert.Greater(titles.Count(t => t.Length > 0), 100, $"{race} should still get Civil titles from its own honorifics.");
			}
		}

		[Test]
		public void DwarfOccupations_ComeFromTheRace()
		{
			var titles = Titles(200, new NameRequest { Race = "dwarf", TitleType = TitleType.Occupation, AllowCompoundTitle = false });
			Assert.IsTrue(RaceRegistry.TryGetTitles("dwarf", out RaceTitles dwarf));
			Assert.Greater(titles.Count(t => dwarf.Occupational.Any(o => t.Contains(o))), 150);
		}

		// ── Budget and compounding ────────────────────────────────────

		[Test]
		public void LengthBudget_IsNeverExceeded_AndRarelyEmpty()
		{
			var titles = Titles(400, new NameRequest { Race = "human", Gender = CharacterGender.Male, MaxTitleLength = 32 });
			Assert.AreEqual(0, titles.Count(t => t.Length > 32));
			Assert.Less(titles.Count(t => t.Length == 0), 8, "Almost every request should find a title within 32 characters.");
		}

		[Test]
		public void CompoundOff_MeansNoSecondClause()
		{
			var titles = Titles(300, new NameRequest { Race = "orc", Gender = CharacterGender.Male, TitleType = TitleType.Rank, AllowCompoundTitle = false });
			Assert.AreEqual(0, titles.Count(t => Regex.IsMatch(t, @", (the |who |Who )")), "A second clause was appended with compounding off.");
		}

		[Test]
		public void CompoundOn_SometimesAppends_ButWithinBudget()
		{
			var titles = Titles(400, new NameRequest { Race = "human", Gender = CharacterGender.Male, MaxTitleLength = 40, AllowCompoundTitle = true });
			Assert.Greater(titles.Count(t => Regex.IsMatch(t, @", (the |who )")), 5, "Compounding never happened.");
			Assert.AreEqual(0, titles.Count(t => t.Length > 40));
		}

		// ── Repetition and determinism ────────────────────────────────

		[Test]
		public void UnseededBatch_DoesNotRepeatLegends()
		{
			var generator = new NameGenerator(9);
			var titles = Enumerable.Range(0, 30)
				.Select(_ => generator.Generate(new NameRequest { Race = "human", TitleType = TitleType.Legend, TitleOnly = true }).Title)
				.ToList();
			Assert.GreaterOrEqual(titles.Distinct().Count(), 27, "The generator should remember what it just handed out.");
		}

		[Test]
		public void SeededTitles_ReplayExactly_IgnoringMemory()
		{
			var a = new NameGenerator(1);
			var b = new NameGenerator(2);
			for (int i = 0; i < 10; i++)
			{
				var req = new NameRequest { Race = "dwarf", Gender = CharacterGender.Female, RegionSeed = "hall", Index = i, TitleOnly = true };
				Assert.AreEqual(a.Generate(req).Title, b.Generate(req).Title);
			}
		}

		[Test]
		public void EveryRace_GetsATitleInEveryRegister()
		{
			var generator = new NameGenerator(3);
			foreach (string race in RaceRegistry.SupportedRaces)
			{
				foreach (TitleRegister register in new[] { TitleRegister.Civil, TitleRegister.Martial, TitleRegister.Mythic })
				{
					string title = generator.Generate(new NameRequest { Race = race, Register = register, RegionSeed = "suite", TitleOnly = true }).Title;
					Assert.IsNotEmpty(title, $"{race} produced no {register} title.");
				}
			}
		}
	}
}
