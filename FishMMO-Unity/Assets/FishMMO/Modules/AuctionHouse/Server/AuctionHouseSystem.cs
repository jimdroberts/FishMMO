using System;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The auction house on a scene server (issue #299): browsing, posting, buying and cancelling at an Auctioneer, and
	/// the sweep that hands back listings past their time.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One market, kept in the database.</b> Nothing about the listings is held here: every scene server reads and
	/// writes the same table (<c>IAuctionHouseService</c>), so a listing posted in one town is for sale in all of them,
	/// and a server restarting loses nothing.
	/// </para>
	/// <para>
	/// <b>Everything is delivered by mail.</b> A purchase, its proceeds, a cancelled or an expired listing each become a
	/// mail from the "Auction House", written in the same database transaction that ends or changes the listing. Sellers
	/// may be offline and buyers' bags full; neither matters.
	/// </para>
	/// <para>
	/// <b>What is taken from a player is taken first.</b> A post takes the stack and the deposit out of the seller before
	/// the listing exists; a purchase takes the price out of the buyer before the listing is touched. A request that then
	/// fails gives them back — into the bags and purse when the player is still here and has room, by mail when not — so
	/// the failure direction is always "nothing happened", never "it happened twice". The same ordering as the merchant,
	/// mail and trade paths.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "AuctionHouseSystem", menuName = "FishMMO/Server/SceneServer/Auction House System", order = 1)]
	[RequiresDataContainer(typeof(AuctionHouseSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(AuctionHouseSystemRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public partial class AuctionHouseSystem : ServerBehaviour
	{
		/// <summary>Request-guard operation codes, one debounce window each.</summary>
		private enum Operation : byte
		{
			Search = 1,
			Post = 2,
			Buy = 3,
			Cancel = 4,
			MyListings = 5,
			PriceHint = 6,
		}

		/// <summary>The log category.</summary>
		private const string LOG = "AuctionHouseSystem";

		[Header("Economy")]
		[Tooltip("The currency listings are priced, bought and paid in: the same attribute the merchant and mailbox use.")]
		[SerializeField] private CharacterAttributeTemplate currencyTemplate;

		[Tooltip("Listings one character may have at once.")]
		[SerializeField] [Min(1)] private int maxActiveListings = AuctionRules.DefaultMaxActiveListings;

		[Tooltip("The deposit: this percent of the item's merchant price, times how many, per 12 hours listed. Never returned.")]
		[SerializeField] [Min(0f)] private float depositPercent = AuctionRules.DefaultDepositPercent;

		[Tooltip("The smallest deposit any listing pays.")]
		[SerializeField] [Min(0)] private long minimumDeposit = 1;

		[Tooltip("The percent of a sale the house keeps.")]
		[SerializeField] [Range(0f, 100f)] private float cutPercent = AuctionRules.DefaultCutPercent;

		[Header("Expiry")]
		[Tooltip("Seconds between sweeps that hand expired listings back. Every scene server sweeps; they never take the same row.")]
		[SerializeField] [Min(5f)] private float expirySweepIntervalSeconds = 60f;

		[Tooltip("Most listings one sweep ends.")]
		[SerializeField] [Min(1)] private int expiryBatchSize = 200;

		[Header("Request Protection")]
		[Tooltip("Minimum milliseconds between two browse requests from one connection.")]
		[SerializeField] private int searchDebounceMilliseconds = 250;
		[Tooltip("Minimum milliseconds between two posts, purchases or cancellations from one connection.")]
		[SerializeField] private int tradeDebounceMilliseconds = 500;
		[Tooltip("Most replies drained from the main-thread queue per frame.")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;
		[SerializeField] private float debounceSweepIntervalSeconds = 5.0f;
		[SerializeField] private float debounceEntryTtlSeconds = 30.0f;
		[SerializeField] private int debounceSweepMaxRemovals = 128;

		/// <summary>Words the mail.</summary>
		private readonly AuctionMailComposer mailComposer = new AuctionMailComposer();

		/// <summary>Seconds until the next expiry sweep.</summary>
		private float expirySweepIn;

		/// <summary>1 while a sweep is running (the async worker may outlast a frame).</summary>
		private int expirySweepRunning;

		/// <summary>The settings as the window sees them.</summary>
		internal int MaxActiveListings => maxActiveListings;

		/// <inheritdoc />
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error(LOG, "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}
			if (Server.Database?.ServiceRegistry == null)
			{
				Log.Error(LOG, "InitializeOnce: Database ServiceRegistry is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}
			if (!Server.DataContainerRegistry.TryGet<IAuctionHouseSystemMainThreadQueueData>(out _) ||
				!Server.DataContainerRegistry.TryGet<IAuctionHouseSystemRuntimeData>(out _))
			{
				Log.Error(LOG, "InitializeOnce: its data containers are missing");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}
			if (currencyTemplate == null)
			{
				// Not fatal: browsing and cancelling still work, and posting and buying answer Disabled.
				Log.Warning(LOG, "InitializeOnce: no currency template; nothing can be posted or bought.");
			}

			maxActiveListings = Mathf.Max(1, maxActiveListings);
			depositPercent = Mathf.Max(0f, depositPercent);
			minimumDeposit = Math.Max(0, minimumDeposit);
			cutPercent = Mathf.Clamp(cutPercent, 0f, 100f);
			expirySweepIntervalSeconds = Mathf.Max(5f, expirySweepIntervalSeconds);
			expiryBatchSize = Mathf.Clamp(expiryBatchSize, 1, 500);
			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			searchDebounceMilliseconds = Mathf.Max(0, searchDebounceMilliseconds);
			tradeDebounceMilliseconds = Mathf.Max(0, tradeDebounceMilliseconds);
			debounceSweepIntervalSeconds = Mathf.Max(0.25f, debounceSweepIntervalSeconds);
			debounceEntryTtlSeconds = Mathf.Max(1.0f, debounceEntryTtlSeconds);
			debounceSweepMaxRemovals = Mathf.Max(1, debounceSweepMaxRemovals);

			// The first sweep a little after start, not at it: a server coming up has enough to do.
			expirySweepIn = Mathf.Min(10f, expirySweepIntervalSeconds);

			Server.NetworkWrapper.RegisterBroadcast<AuctionSearchBroadcast>(OnSearch, true);
			Server.NetworkWrapper.RegisterBroadcast<AuctionMyListingsRequestBroadcast>(OnMyListings, true);
			Server.NetworkWrapper.RegisterBroadcast<AuctionPriceHintRequestBroadcast>(OnPriceHint, true);
			Server.NetworkWrapper.RegisterBroadcast<AuctionPostBroadcast>(OnPost, true);
			Server.NetworkWrapper.RegisterBroadcast<AuctionBuyBroadcast>(OnBuy, true);
			Server.NetworkWrapper.RegisterBroadcast<AuctionCancelBroadcast>(OnCancel, true);

			Log.Debug(LOG, "Initialized");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc />
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				return;
			}
			DrainMainThreadQueue<IAuctionHouseSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, true);
			if (Server.DataContainerRegistry.TryGet<IAuctionHouseSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard?.Clear();
			}

			Server.NetworkWrapper.UnregisterBroadcast<AuctionSearchBroadcast>(OnSearch);
			Server.NetworkWrapper.UnregisterBroadcast<AuctionMyListingsRequestBroadcast>(OnMyListings);
			Server.NetworkWrapper.UnregisterBroadcast<AuctionPriceHintRequestBroadcast>(OnPriceHint);
			Server.NetworkWrapper.UnregisterBroadcast<AuctionPostBroadcast>(OnPost);
			Server.NetworkWrapper.UnregisterBroadcast<AuctionBuyBroadcast>(OnBuy);
			Server.NetworkWrapper.UnregisterBroadcast<AuctionCancelBroadcast>(OnCancel);
		}

		/// <inheritdoc />
		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue<IAuctionHouseSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, false);
			if (Server.DataContainerRegistry.TryGet<IAuctionHouseSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.Sweep(debounceSweepIntervalSeconds, debounceEntryTtlSeconds, debounceSweepMaxRemovals);
			}

			expirySweepIn -= deltaTime;
			if (expirySweepIn <= 0f)
			{
				expirySweepIn = expirySweepIntervalSeconds;
				StartExpirySweep();
			}
		}

		// ── Shared helpers ────────────────────────────────────────────────────────────

		/// <summary>Runs <paramref name="action"/> on the main thread; false when the queue refused it.</summary>
		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<IAuctionHouseSystemMainThreadQueueData>(action);
		}

		/// <summary>Acquires the request guard for one operation.</summary>
		private bool TryBeginGuard(NetworkConnection conn, Operation operation, out long guardKey)
		{
			guardKey = 0;
			if (!Server.DataContainerRegistry.TryGet<IAuctionHouseSystemRuntimeData>(out var runtimeData))
			{
				return false;
			}
			int debounce = operation == Operation.Search || operation == Operation.MyListings || operation == Operation.PriceHint
				? searchDebounceMilliseconds
				: tradeDebounceMilliseconds;
			return runtimeData.IngressGuard.TryBegin(conn.ClientId, (byte)operation, debounce, out guardKey);
		}

		/// <summary>Releases the request guard. Safe from a worker thread.</summary>
		private void EndGuard(long guardKey)
		{
			if (Server != null && Server.DataContainerRegistry.TryGet<IAuctionHouseSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.End(guardKey);
			}
		}

		/// <summary>
		/// True when the character stands at the Auctioneer its request names: a live scene object in the character's
		/// scene, resolving to an <see cref="IAuctioneer"/> that accepts the interaction (range and the corpse gate).
		/// </summary>
		private static bool AtAuctioneer(IPlayerCharacter character, long interactableID)
		{
			if (character == null || !SceneObject.Objects.TryGetValue(interactableID, out ISceneObject sceneObject))
			{
				return false;
			}
			GameObject gameObject = sceneObject.GameObject;
			if (gameObject == null || !gameObject.activeInHierarchy || gameObject.scene.handle != character.GameObject.scene.handle)
			{
				return false;
			}
			IInteractable interactable = InteractableResolver.Resolve(sceneObject);
			return interactable is IAuctioneer && interactable.CanInteract(character);
		}

		/// <summary>
		/// The character, if it is still on this scene server and still on <paramref name="conn"/>. Main thread.
		/// </summary>
		/// <remarks>
		/// Not a request preamble: it asks whether a refund can be handed over in person. Read from the character mapping,
		/// ungated, because a refund is owed to a player who died or was stunned while the request was answered just the
		/// same.
		/// </remarks>
		private IPlayerCharacter StillHere(NetworkConnection conn, long characterID)
		{
			if (conn == null || !conn.IsActive ||
				!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mapping) ||
				!mapping.CharactersByID.TryGetValue(characterID, out IPlayerCharacter character) ||
				character == null || character.Owner != conn)
			{
				return null;
			}
			return character;
		}

		/// <summary>Sends a result. Main thread.</summary>
		private void SendResult(NetworkConnection conn, AuctionOperation operation, AuctionResult result, long listingID = 0, int quantity = 0, long amount = 0)
		{
			if (conn == null || !conn.IsActive)
			{
				return;
			}
			Server.NetworkWrapper.Broadcast(conn, new AuctionResultBroadcast()
			{
				Operation = operation,
				Result = result,
				ListingID = listingID,
				Quantity = quantity,
				Amount = amount,
			}, true, Channel.Reliable);
		}

		/// <summary>Sends a result from a worker thread.</summary>
		private void SendResultLater(NetworkConnection conn, AuctionOperation operation, AuctionResult result, long listingID = 0, int quantity = 0, long amount = 0)
		{
			if (!TryEnqueueMainThread(() => SendResult(conn, operation, result, listingID, quantity, amount)))
			{
				Log.Warning(LOG, $"The main-thread queue refused a {operation} reply ({result}).");
			}
		}

		/// <summary>The current UTC time to whole microseconds: what the database stores, so a retry's probe matches it exactly.</summary>
		internal static DateTime UtcNowMicroseconds()
		{
			long ticks = DateTime.UtcNow.Ticks;
			return new DateTime(ticks - ticks % 10, DateTimeKind.Utc);
		}
	}
}
