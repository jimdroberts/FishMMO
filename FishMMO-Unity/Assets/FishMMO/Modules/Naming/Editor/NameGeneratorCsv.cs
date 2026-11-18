using System.Collections.Generic;
using System.Text;

namespace FishMMO.Shared.NameGeneration.Editor
{
	/// <summary>
	/// Builds the CSV text the window's "Export CSV" button writes to disk:
	/// one header line naming the columns, then one line per generated result.
	/// Kept apart from the window so the formatting is testable without a UI.
	/// </summary>
	public static class NameGeneratorCsv
	{
		public static string FromCharacters(IReadOnlyList<CharacterEntry> rows)
		{
			var sb = new StringBuilder();
			sb.AppendLine("Name,Title,Race,Meaning,Category");
			foreach (var r in rows)
			{
				Row(sb, r.Name, r.Title, r.Race, r.Meaning, r.TitleCategory);
			}
			return sb.ToString();
		}

		public static string FromCities(IReadOnlyList<CityNameEntry> rows)
		{
			var sb = new StringBuilder();
			sb.AppendLine("Name,CityType,Race,Meaning");
			foreach (var r in rows)
			{
				Row(sb, r.Name, r.CityType, r.Race, r.Meaning);
			}
			return sb.ToString();
		}

		public static string FromDungeons(IReadOnlyList<DungeonNameEntry> rows)
		{
			var sb = new StringBuilder();
			sb.AppendLine("Name,Biome,Meaning");
			foreach (var r in rows)
			{
				Row(sb, r.Name, r.Biome, r.Meaning);
			}
			return sb.ToString();
		}

		public static string FromPOIs(IReadOnlyList<POINameEntry> rows)
		{
			var sb = new StringBuilder();
			sb.AppendLine("Name,POIType,Biome,Meaning");
			foreach (var r in rows)
			{
				Row(sb, r.Name, r.POIType, r.Biome, r.Meaning);
			}
			return sb.ToString();
		}

		public static string FromItems(IReadOnlyList<ItemNameEntry> rows)
		{
			var sb = new StringBuilder();
			sb.AppendLine("Name,ItemType,Race,Meaning");
			foreach (var r in rows)
			{
				Row(sb, r.Name, r.ItemCategory, r.Race, r.Meaning);
			}
			return sb.ToString();
		}

		private static void Row(StringBuilder sb, params string[] fields)
		{
			for (int i = 0; i < fields.Length; i++)
			{
				if (i > 0)
				{
					sb.Append(',');
				}
				sb.Append(Field(fields[i]));
			}
			sb.AppendLine();
		}

		/// <summary>RFC 4180 field quoting: quote only when the value needs it.</summary>
		public static string Field(string s)
		{
			if (string.IsNullOrEmpty(s))
			{
				return "";
			}
			if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
			{
				return s;
			}
			return "\"" + s.Replace("\"", "\"\"") + "\"";
		}
	}
}
