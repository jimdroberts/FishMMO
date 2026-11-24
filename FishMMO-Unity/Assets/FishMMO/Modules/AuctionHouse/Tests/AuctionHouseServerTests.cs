using System;
using System.Collections.Generic;
using NUnit.Framework;
using FishMMO.Database.Data;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;

namespace FishMMO.UnitTests
{
	/// <summary>The auction house system's pure pieces: what the window is sent, and the words of the mail.</summary>
	[TestFixture]
	public class AuctionHouseServerTests
	{
		[Test]
		public void EveryPurchaseOutcomeHasItsOwnResult()
		{
			var seen = new HashSet<AuctionResult>();
			foreach (AuctionBuyOutcome outcome in Enum.GetValues(typeof(AuctionBuyOutcome)))
			{
				AuctionResult result = AuctionHouseSystem.ToResult(outcome);
				Assert.AreNotEqual(AuctionResult.ServerError, result, outcome.ToString());
				Assert.IsTrue(seen.Add(result), $"{outcome} shares its result");
			}
			Assert.AreEqual(AuctionResult.Success, AuctionHouseSystem.ToResult(AuctionBuyOutcome.Bought));
		}

		[Test]
		public void ListingsAreSentWithTimeLeftAndWhoseTheyAre()
		{
			DateTime now = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
			var rows = new List<AuctionListingData>
			{
				new AuctionListingData { ID = 7, SellerID = 3, SellerName = "Ann", ItemTemplateID = 11, ItemSeed = 99, Quantity = 4, UnitPrice = 25, ExpiresUtc = now.AddHours(2).AddSeconds(0.5) },
				new AuctionListingData { ID = 8, SellerID = 4, SellerName = null, ItemTemplateID = 12, Quantity = 1, UnitPrice = 1, ExpiresUtc = now.AddMinutes(-5) },
			};
			AuctionListingEntry[] entries = AuctionHouseSystem.ToEntries(rows, 3, now);
			Assert.AreEqual(2, entries.Length);
			Assert.AreEqual(7, entries[0].ListingID);
			Assert.AreEqual(99, entries[0].Seed, "the seed rides along so the window shows the item it really is");
			Assert.AreEqual(7201, entries[0].SecondsLeft, "rounded up");
			Assert.IsTrue(entries[0].Own);
			Assert.IsFalse(entries[1].Own);
			Assert.AreEqual(0, entries[1].SecondsLeft, "expired, not negative");
			Assert.AreEqual(string.Empty, entries[1].SellerName);
			Assert.AreEqual(0, AuctionHouseSystem.ToEntries(null, 1, now).Length);
		}

		[Test]
		public void TheMailSaysWhatHappenedAndComesFromTheAuctionHouse()
		{
			IAuctionMailComposer mail = new AuctionMailComposer();
			Assert.AreEqual(AuctionRules.MailSenderName, mail.SenderName);
			var listing = new AuctionListingData { ItemTemplateID = int.MinValue, ItemName = "iron ore", SellerName = "Ann", Quantity = 15 };

			AuctionMailText sold = mail.Sold(listing, 5, 100, 5, 95, "Bob");
			StringAssert.Contains("iron ore", sold.Subject, "an unknown template falls back to the listing's own name");
			StringAssert.Contains("Bob", sold.Body);
			StringAssert.Contains("95", sold.Body);
			StringAssert.Contains("15 are still for sale", sold.Body);

			listing.Quantity = 0;
			StringAssert.DoesNotContain("still for sale", mail.Sold(listing, 5, 100, 5, 95, "Bob").Body, "nothing left, nothing said");

			foreach (AuctionMailText text in new[] { mail.Bought(listing, 5, 100), mail.Cancelled(listing), mail.Expired(listing) })
			{
				Assert.IsFalse(string.IsNullOrWhiteSpace(text.Subject));
				Assert.IsFalse(string.IsNullOrWhiteSpace(text.Body));
				Assert.LessOrEqual(text.Subject.Length, 200, "the mailbox's subject limit");
			}
		}

		[Test]
		public void TheDatabaseInstantIsWholeMicroseconds()
		{
			// A purchase's retry finds its first attempt's mail by this instant, so it must survive the round trip exactly.
			for (int i = 0; i < 100; i++)
			{
				Assert.AreEqual(0, AuctionHouseSystem.UtcNowMicroseconds().Ticks % 10);
			}
		}
	}
}
