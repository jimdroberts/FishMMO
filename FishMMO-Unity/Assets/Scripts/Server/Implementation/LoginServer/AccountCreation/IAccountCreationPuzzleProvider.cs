using System.Threading;
using System.Threading.Tasks;

namespace FishMMO.Server
{
	/// <summary>
	/// Scaffold interface for client-side proof-of-work
	/// (PoW) puzzles issued during account creation. The global hourly cap
	/// (<c>maxGlobalAccountCreationsPerHour</c>) absorbs the impact of
	/// registration floods, but spending the budget still costs the server
	/// real database I/O. A small PoW puzzle (e.g. a SHA-256 difficulty
	/// challenge) shifts the cost asymmetry: an attacker pays CPU per attempt
	/// while a legitimate user pays only milliseconds.
	///
	/// The intended production implementation:
	///   * Server issues a (challenge, difficulty) tuple bound to the
	///     handshake nonce and connection,
	///   * Client solves and returns a nonce such that
	///     H(challenge || nonce) has the required leading zero bits,
	///   * Server verifies in &lt;1ms and proceeds with account creation.
	///
	/// The puzzle MUST be bound to the connection / handshake to prevent
	/// pre-mining a stockpile of valid solutions. Difficulty is increased
	/// dynamically when the global cap is approaching saturation.
	/// </summary>
	public interface IAccountCreationPuzzleProvider
	{
		/// <summary>Issues a fresh puzzle. Implementations bind it to <paramref name="connectionContext"/>.</summary>
		Task<AccountCreationPuzzle> IssueAsync(string connectionContext, CancellationToken cancellationToken);

		/// <summary>Verifies a client solution. Returns false for stale, reused, or invalid solutions.</summary>
		bool VerifySolution(AccountCreationPuzzle puzzle, byte[] solutionNonce);
	}

	/// <summary>Opaque puzzle payload exchanged with the client.</summary>
	public sealed class AccountCreationPuzzle
	{
		public byte[] Challenge { get; }
		public int Difficulty { get; }

		public AccountCreationPuzzle(byte[] challenge, int difficulty)
		{
			Challenge = challenge;
			Difficulty = difficulty;
		}
	}

	/// <summary>
	/// No-op default that always issues a zero-difficulty puzzle and accepts any
	/// solution. Kept so the production pipeline can be wired in without
	/// touching call sites.
	/// </summary>
	public sealed class NullAccountCreationPuzzleProvider : IAccountCreationPuzzleProvider
	{
		public Task<AccountCreationPuzzle> IssueAsync(string connectionContext, CancellationToken cancellationToken)
		{
			return Task.FromResult(new AccountCreationPuzzle(System.Array.Empty<byte>(), 0));
		}

		public bool VerifySolution(AccountCreationPuzzle puzzle, byte[] solutionNonce) => true;
	}
}
