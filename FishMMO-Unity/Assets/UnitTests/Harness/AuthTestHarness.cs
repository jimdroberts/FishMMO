using System;
using System.Threading;
using FishMMO.Auth.Implementation;

namespace FishMMO.UnitTests.Harness
{
	/// <summary>
	/// Pairs a <see cref="TestClientCore"/> and <see cref="TestServerCore"/> in-process so
	/// tests can drive the full handshake → SRP → success flow without any networking or
	/// FishNet dependency. Owns the worker cancellation token and tears workers down on dispose.
	/// </summary>
	internal sealed class AuthTestHarness : IDisposable
	{
		public TestClientCore Client { get; }
		public TestServerCore Server { get; }
		public InMemoryAccountStore Store { get; }

		private readonly CancellationTokenSource cts = new CancellationTokenSource();
		private readonly SrpAccountManager<int> accountManager;
		private bool disposed;

		public AuthTestHarness(int connectionId = 1, long loginServerId = 42)
		{
			AuthTestTrace.Log("Harness", "ctor.start", $"conn={connectionId} loginServerId={loginServerId}");
			Store = new InMemoryAccountStore();
			accountManager = new SrpAccountManager<int>(nameof(AuthTestHarness));

			Server = new TestServerCore(accountManager, Store)
			{
				TokenSigningKey = CryptoHelper.GenerateKey(CryptoHelper.HmacKeyLength),
				TotpMasterKey = CryptoHelper.GenerateKey(32),
				LoginServerId = loginServerId,
			};
			Client = new TestClientCore();

			Server.Pair(Client);
			Client.Pair(Server, connectionId);
			Client.TokenStore = Store;

			Server.InitializeWorkers(cts.Token);
			AuthTestTrace.Log("Harness", "ctor.done", "workers initialized");
		}

		public void Dispose()
		{
			if (disposed) return;
			disposed = true;
			AuthTestTrace.Log("Harness", "dispose.start");
			try { Server.ShutdownWorkers(); } catch { /* swallow */ }
			cts.Cancel();
			cts.Dispose();
			accountManager.Clear();
			Client.Dispose();
			AuthTestTrace.Log("Harness", "dispose.done");
		}
	}
}