using System;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.UnitTests
{
	/// <summary>The auction house's arithmetic and limits (<see cref="AuctionRules"/>), which the server enforces and the window shows.</summary>
	[TestFixture]
	public class AuctionRulesTests
	{
		[Test]
		public void DurationsAre12_24And48Hours()
		{
			Assert.AreEqual(12, AuctionRules.Hours(AuctionDuration.Short));
			Assert.AreEqual(24, AuctionRules.Hours(AuctionDuration.Medium));
			Assert.AreEqual(48, AuctionRules.Hours(AuctionDuration.Long));
			Assert.IsFalse(AuctionRules.IsValid((AuctionDuration)3), "the wire cannot name a fourth");
		}

		[Test]
		public void TheDepositScalesWithPriceQuantityAndDuration()
		{
			// 5% of 100 x 4 = 20 per 12 hours.
			Assert.AreEqual(20, AuctionRules.Deposit(100, 4, AuctionDuration.Short, 5f, 1));
			Assert.AreEqual(40, AuctionRules.Deposit(100, 4, AuctionDuration.Medium, 5f, 1));
			Assert.AreEqual(80, AuctionRules.Deposit(100, 4, AuctionDuration.Long, 5f, 1));
		}

		[Test]
		public void TheDepositRoundsUpAndNeverFallsUnderTheMinimum()
		{
			Assert.AreEqual(1, AuctionRules.Deposit(3, 1, AuctionDuration.Short, 5f, 1), "0.15 rounds up to 1");
			Assert.AreEqual(5, AuctionRules.Deposit(3, 1, AuctionDuration.Short, 5f, 5), "the minimum wins");
			Assert.AreEqual(5, AuctionRules.Deposit(0, 10, AuctionDuration.Long, 5f, 5), "a worthless item pays the minimum");
			Assert.AreEqual(2, AuctionRules.Deposit(30, 1, AuctionDuration.Short, 5f, 0), "1.5 rounds up to 2");
		}

		[Test]
		public void TheDepositIsCappedAtWhatAMailCanCarry()
		{
			Assert.AreEqual(AuctionRules.MaxListingTotal, AuctionRules.Deposit(int.MaxValue, int.MaxValue, AuctionDuration.Long, 100f, 1));
		}

		[Test]
		public void TheCutRoundsDownSoTheSellerKeepsTheRest()
		{
			Assert.AreEqual(5, AuctionRules.Cut(100, 5f));
			Assert.AreEqual(0, AuctionRules.Cut(19, 5f), "0.95 rounds down to nothing");
			Assert.AreEqual(1, AuctionRules.Cut(20, 5f));
			Assert.AreEqual(0, AuctionRules.Cut(100, 0f));
			Assert.AreEqual(100, AuctionRules.Cut(100, 100f));
			Assert.AreEqual(100, AuctionRules.Cut(100, 250f), "never more than the sale");
		}

		[Test]
		public void AListingMustFitInAMail()
		{
			Assert.IsTrue(AuctionRules.TryTotal(5, 20, out long total));
			Assert.AreEqual(100, total);
			Assert.IsTrue(AuctionRules.TryTotal(int.MaxValue, 1, out _));
			Assert.IsFalse(AuctionRules.TryTotal(int.MaxValue, 2, out _), "over the 32-bit mail currency");
			Assert.IsFalse(AuctionRules.TryTotal(0, 1, out _));
			Assert.IsFalse(AuctionRules.TryTotal(1, 0, out _));
			Assert.IsFalse(AuctionRules.TryTotal(-5, 3, out _));
			Assert.IsFalse(AuctionRules.TryTotal(long.MaxValue, long.MaxValue, out _), "no overflow");
		}

		[Test]
		public void ItemsAreShelvedByTheirTemplate()
		{
			var made = new ScriptableObject[]
			{
				ScriptableObject.CreateInstance<WeaponTemplate>(),
				ScriptableObject.CreateInstance<ArmorTemplate>(),
				ScriptableObject.CreateInstance<CraftingMaterialTemplate>(),
				ScriptableObject.CreateInstance<StructureKitTemplate>(),
			};
			try
			{
				Assert.AreEqual(AuctionCategory.Weapons, AuctionRules.Categorize((BaseItemTemplate)made[0]));
				Assert.AreEqual(AuctionCategory.Armor, AuctionRules.Categorize((BaseItemTemplate)made[1]));
				Assert.AreEqual(AuctionCategory.Materials, AuctionRules.Categorize((BaseItemTemplate)made[2]));
				Assert.AreEqual(AuctionCategory.StructureKits, AuctionRules.Categorize((BaseItemTemplate)made[3]));
				Assert.AreEqual(AuctionCategory.Other, AuctionRules.Categorize(null));
			}
			finally
			{
				foreach (ScriptableObject o in made)
				{
					UnityEngine.Object.DestroyImmediate(o);
				}
			}
		}

		[Test]
		public void TheCategoryValuesArePersistedAndMustNotMove()
		{
			// They are stored on the listing rows: renumbering one reshelves every listing already posted.
			Assert.AreEqual(0, (int)AuctionCategory.All);
			Assert.AreEqual(1, (int)AuctionCategory.Weapons);
			Assert.AreEqual(2, (int)AuctionCategory.Armor);
			Assert.AreEqual(3, (int)AuctionCategory.Consumables);
			Assert.AreEqual(4, (int)AuctionCategory.Materials);
			Assert.AreEqual(5, (int)AuctionCategory.StructureKits);
			Assert.AreEqual(6, (int)AuctionCategory.OtherGear);
			Assert.AreEqual(7, (int)AuctionCategory.Other);
		}

		[Test]
		public void TheSortMatchesTheDatabaseOrderOneForOne()
		{
			foreach (AuctionSort sort in Enum.GetValues(typeof(AuctionSort)))
			{
				Assert.AreEqual(sort.ToString(), ((FishMMO.Database.Data.AuctionSearchOrder)(byte)sort).ToString());
			}
		}

		[Test]
		public void EveryResultSaysSomething()
		{
			foreach (AuctionOperation operation in Enum.GetValues(typeof(AuctionOperation)))
			{
				foreach (AuctionResult result in Enum.GetValues(typeof(AuctionResult)))
				{
					string text = AuctionRules.Describe(operation, result);
					Assert.IsFalse(string.IsNullOrWhiteSpace(text), $"{operation}/{result}");
					if (result != AuctionResult.ServerError)
					{
						Assert.AreNotEqual(AuctionRules.Describe(operation, AuctionResult.ServerError), text, $"{operation}/{result} has its own words");
					}
				}
			}
		}

		[Test]
		public void TimeLeftAndSearchText()
		{
			Assert.AreEqual("Expired", AuctionRules.FormatTimeLeft(0));
			Assert.AreEqual("< 1h", AuctionRules.FormatTimeLeft(3599));
			Assert.AreEqual("47h", AuctionRules.FormatTimeLeft(48 * 3600 - 1));
			Assert.AreEqual(string.Empty, AuctionRules.CleanSearch("   "));
			Assert.AreEqual("iron", AuctionRules.CleanSearch("  iron "));
			Assert.AreEqual(AuctionRules.MaxSearchLength, AuctionRules.CleanSearch(new string('a', 500)).Length);
		}

		[Test]
		public void SearchTextWildcardsAreLiteral()
		{
			Assert.AreEqual(@"50\% off\_x\\", FishMMO.Database.Npgsql.Services.AuctionHouseService.EscapeLike(@"50% off_x\"));
		}
	}
}
