using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// The registration form's anti-bot token and its randomised honeypot fields.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why a signed token rather than a fixed hidden field.</b> A honeypot with a fixed name is
	/// defeated once, by whoever writes the bot, and then forever. Here every form gets two or three
	/// decoy names drawn at random from names a form-filling bot or an autofill heuristic fills in
	/// (website, company, fax...), and the token binds exactly which names this form carried, so a
	/// bot cannot learn a list to leave empty from one page load and replay it.
	/// </para>
	/// <para>
	/// <b>Why it is signed.</b> The server keeps no per-form state. The token is
	/// <c>base64url(json).base64url(HMAC-SHA256)</c> under a random per-process key: a forged or
	/// edited token fails the MAC, and a panel restart simply invalidates open forms (the browser
	/// fetches a fresh one if the submit is refused and the user tries again after reloading).
	/// </para>
	/// <para>
	/// <b>Why an age window.</b> A form submitted less than <see cref="MinimumAge"/> after it was
	/// issued was not filled in by a person: a human cannot type an account name, an address, two
	/// matching passwords and pick an age in three seconds, and a script does it in milliseconds. A
	/// token older than <see cref="MaximumAge"/> is refused so a harvested token is not good forever.
	/// </para>
	/// <para>
	/// None of this tells the caller why it refused: <see cref="Check"/> returns a reason for the
	/// log, and the controller answers with the same message a genuine failure gets.
	/// </para>
	/// </remarks>
	public sealed class RegistrationFormTokenService
	{
		/// <summary>Faster than this after issue is not a person.</summary>
		public static readonly TimeSpan MinimumAge = TimeSpan.FromSeconds(3);

		/// <summary>Older than this is refused.</summary>
		public static readonly TimeSpan MaximumAge = TimeSpan.FromHours(1);

		/// <summary>Longest token accepted, so an arbitrary body is never parsed.</summary>
		private const int MaxTokenLength = 1024;

		/// <summary>
		/// Names that autofill and form-filling bots put a value into. Each carries the label shown
		/// beside it, so the decoy reads as a real field to anything that looks.
		/// </summary>
		private static readonly (string Name, string Label)[] DecoyPool =
		{
			("website", "Website"),
			("company", "Company"),
			("fax", "Fax number"),
			("middle_name", "Middle name"),
			("url", "Homepage URL"),
			("homepage", "Homepage"),
			("nickname", "Nickname"),
			("job_title", "Job title"),
			("organization", "Organization"),
			("address2", "Address line 2"),
			("zip_code", "Postcode"),
			("birth_place", "Place of birth"),
		};

		private readonly byte[] key = RandomNumberGenerator.GetBytes(32);

		/// <summary>One decoy field as the browser renders it.</summary>
		public sealed record Decoy(string Name, string Label);

		/// <summary>A freshly issued form.</summary>
		public sealed record IssuedForm(string Token, IReadOnlyList<Decoy> Decoys);

		private sealed class Payload
		{
			public long Iat { get; set; }
			public string[] F { get; set; } = Array.Empty<string>();
			public string N { get; set; } = "";
		}

		/// <summary>Issues a token binding the issue time and two or three random decoy names.</summary>
		public IssuedForm Issue()
		{
			int count = RandomNumberGenerator.GetInt32(2, 4);
			var indices = Enumerable.Range(0, DecoyPool.Length).ToArray();
			// Fisher-Yates over a CSPRNG, then take the first few.
			for (int i = indices.Length - 1; i > 0; i--)
			{
				int j = RandomNumberGenerator.GetInt32(i + 1);
				(indices[i], indices[j]) = (indices[j], indices[i]);
			}
			var decoys = indices.Take(count).Select(i => new Decoy(DecoyPool[i].Name, DecoyPool[i].Label)).ToList();

			var payload = new Payload
			{
				Iat = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
				F = decoys.Select(d => d.Name).ToArray(),
				N = Base64Url(RandomNumberGenerator.GetBytes(12)),
			};
			byte[] body = JsonSerializer.SerializeToUtf8Bytes(payload);
			string token = Base64Url(body) + "." + Base64Url(HMACSHA256.HashData(key, body));
			return new IssuedForm(token, decoys);
		}

		/// <summary>
		/// Checks a submitted form. Returns null when it may proceed, or a reason FOR THE LOG ONLY.
		/// </summary>
		public string Check(string token, IReadOnlyDictionary<string, string> fields)
		{
			if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
			{
				return "missing form token";
			}

			int dot = token.IndexOf('.');
			if (dot <= 0 || dot == token.Length - 1)
			{
				return "malformed form token";
			}

			byte[] body;
			byte[] mac;
			try
			{
				body = FromBase64Url(token.Substring(0, dot));
				mac = FromBase64Url(token.Substring(dot + 1));
			}
			catch (FormatException)
			{
				return "malformed form token";
			}

			if (!CryptographicOperations.FixedTimeEquals(mac, HMACSHA256.HashData(key, body)))
			{
				return "forged form token (or issued before a restart)";
			}

			Payload payload;
			try
			{
				payload = JsonSerializer.Deserialize<Payload>(body);
			}
			catch (JsonException)
			{
				return "unreadable form token";
			}
			if (payload == null)
			{
				return "unreadable form token";
			}

			TimeSpan age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(payload.Iat);
			if (age < MinimumAge)
			{
				return $"submitted {age.TotalMilliseconds:0} ms after the form was issued";
			}
			if (age > MaximumAge)
			{
				return "form token expired";
			}

			foreach (string name in payload.F ?? Array.Empty<string>())
			{
				if (fields != null && fields.TryGetValue(name, out string value) && !string.IsNullOrEmpty(value))
				{
					return $"decoy field '{name}' was filled in";
				}
			}
			return null;
		}

		private static string Base64Url(byte[] bytes) =>
			Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

		private static byte[] FromBase64Url(string value)
		{
			string s = value.Replace('-', '+').Replace('_', '/');
			switch (s.Length % 4)
			{
				case 2: s += "=="; break;
				case 3: s += "="; break;
				case 1: throw new FormatException();
			}
			return Convert.FromBase64String(s);
		}
	}
}
