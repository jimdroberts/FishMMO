using System;
using System.Security.Cryptography;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// The shape of a beta code, and of the program name a batch of codes is minted under.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Twelve characters from a 32-symbol alphabet with <c>0</c>/<c>O</c> and <c>1</c>/<c>I</c>
	/// removed, displayed as three groups of four. A code is read off an email or a forum post and
	/// typed by hand, and those are the pairs a player cannot tell apart in most fonts: a code that
	/// is valid but untypeable is a support ticket. 32^12 is 2^60, so guessing one is not a
	/// strategy even before the login server's rate limits.
	/// </para>
	/// <para>
	/// The stored form is the displayed form, hyphens included. Every path that accepts a code from
	/// outside runs it through <see cref="Normalize"/> first, so <c>abcd efgh jkmn</c> and
	/// <c>ABCD-EFGH-JKMN</c> are one code and the unique index on <c>beta_codes.code</c> is over one
	/// spelling of it.
	/// </para>
	/// </remarks>
	public static class BetaCodeFormat
	{
		/// <summary>The symbols a code is drawn from: A–Z and 2–9 without O and I.</summary>
		public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

		/// <summary>Significant characters in a code, hyphens excluded.</summary>
		public const int CodeLength = 12;

		/// <summary>Characters per displayed group.</summary>
		public const int GroupSize = 4;

		/// <summary>Length of the displayed and stored form, <c>XXXX-XXXX-XXXX</c>.</summary>
		public const int DisplayLength = 14;

		/// <summary>Longest program name.</summary>
		public const int ProgramMaxLength = 32;

		/// <summary>The message for a program name that fails <see cref="IsValidProgram"/>.</summary>
		public const string InvalidProgramError =
			"A program name is 1 to 32 characters of lowercase letters, digits, hyphens and underscores.";

		/// <summary>
		/// Generates a new code in display form from a cryptographic random source.
		/// </summary>
		/// <remarks>
		/// Rejection sampling rather than <c>byte % 32</c>: a byte that falls in the incomplete top
		/// range is thrown away, so every symbol is exactly equally likely. With a 32-symbol
		/// alphabet 256 divides evenly and nothing is ever rejected, but the loop stays correct if
		/// the alphabet is ever changed to a size that does not — which is exactly when a modulo
		/// would quietly start favouring the first few letters. <c>RandomNumberGenerator.GetInt32</c>
		/// would do this for us, but it is .NET 6 and this assembly targets netstandard2.1.
		/// </remarks>
		public static string Generate()
		{
			char[] chars = new char[CodeLength];
			byte[] buffer = new byte[CodeLength * 2];
			int limit = 256 - (256 % Alphabet.Length);
			int filled = 0;

			using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
			{
				while (filled < CodeLength)
				{
					rng.GetBytes(buffer);
					for (int i = 0; i < buffer.Length && filled < CodeLength; i++)
					{
						if (buffer[i] >= limit)
						{
							continue;
						}
						chars[filled++] = Alphabet[buffer[i] % Alphabet.Length];
					}
				}
			}

			return Format(chars);
		}

		/// <summary>
		/// Canonicalises a code typed or pasted by a person.
		/// </summary>
		/// <remarks>
		/// Uppercases, drops hyphens and whitespace wherever they appear, and requires exactly
		/// twelve alphabet characters. Anything else — including a <c>0</c> or <c>O</c> the player
		/// believed they saw — is null rather than a best guess: silently mapping a mistyped symbol
		/// onto a valid one would turn a typo into somebody else's code.
		/// </remarks>
		/// <param name="code">The code as entered.</param>
		/// <returns>The code as <c>XXXX-XXXX-XXXX</c>, or null when it is not a well-formed code.</returns>
		public static string? Normalize(string? code)
		{
			if (string.IsNullOrWhiteSpace(code))
			{
				return null;
			}

			char[] raw = new char[CodeLength];
			int count = 0;
			foreach (char ch in code)
			{
				if (ch == '-' || char.IsWhiteSpace(ch))
				{
					continue;
				}
				if (count == CodeLength)
				{
					return null;
				}
				char upper = char.ToUpperInvariant(ch);
				if (Alphabet.IndexOf(upper) < 0)
				{
					return null;
				}
				raw[count++] = upper;
			}

			return count == CodeLength ? Format(raw) : null;
		}

		/// <summary>
		/// Whether a program name is 1–32 characters of <c>[a-z0-9_-]</c>.
		/// </summary>
		/// <remarks>
		/// Tests the name as given; callers normalise with <see cref="NormalizeProgram"/> first. The
		/// name is an identifier the login server's configuration lists, so it is kept to characters
		/// that survive a config file, an environment variable and a URL without quoting.
		/// </remarks>
		public static bool IsValidProgram(string? program)
		{
			if (string.IsNullOrEmpty(program) || program.Length > ProgramMaxLength)
			{
				return false;
			}
			foreach (char ch in program)
			{
				bool ok = (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_' || ch == '-';
				if (!ok)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Trims and lowercases a program name. Null becomes the empty string, which is not valid.
		/// </summary>
		/// <remarks>
		/// Lowercased so "Closed-Beta" in a config file and "closed-beta" typed into the panel are
		/// one program: a mismatch here would lock every tester out while the codes looked fine.
		/// </remarks>
		public static string NormalizeProgram(string? program)
		{
			return program == null ? string.Empty : program.Trim().ToLowerInvariant();
		}

		private static string Format(char[] raw)
		{
			return new string(raw, 0, GroupSize) + "-" +
				new string(raw, GroupSize, GroupSize) + "-" +
				new string(raw, GroupSize * 2, GroupSize);
		}
	}
}
