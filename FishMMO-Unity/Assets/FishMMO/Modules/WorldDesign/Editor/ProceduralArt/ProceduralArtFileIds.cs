#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Gives every object inside a generated prefab a file ID that is a function of where it sits,
	/// so the same prefab built on any machine names its objects identically.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> A GUID names a file; a file ID names an object inside it. Unity numbers a prefab's
	/// objects at random when it is saved, and a reference INTO a prefab — a terrain's tree or detail
	/// prototype, a GameObject field — is stored as both: <c>{fileID: &lt;root GameObject's ID&gt;,
	/// guid: &lt;prefab's GUID&gt;, type: 3}</c>. With nothing generated committed, every clone builds its
	/// own prefabs, so the GUID (<see cref="ProceduralArtPayload.GuidFor"/>) and the root's file ID
	/// must both come out the same everywhere, or committed terrain data would point at nothing.
	/// </para>
	/// <para>
	/// <b>The key.</b> Each object's ID is a hash of the prefab's project path and of
	/// <see cref="KeyOf">its place</see>: a GameObject by its hierarchy path from the root (each
	/// segment its name and its index among same-named siblings, the root itself <c>/</c>, so renaming
	/// the root changes nothing); a component by its GameObject's path, its type (class ID, and for a
	/// script its script reference) and its index among components of that type on the object. That is
	/// exactly what a build controls, and nothing it does not — the order Unity happened to write the
	/// objects in, or the random IDs it chose — enters the result.
	/// </para>
	/// <para>
	/// <b>The value.</b> The first 64 bits of an MD5, with the top bit cleared and the next one set:
	/// always positive (Unity writes negative IDs only for objects it derived itself) and at least
	/// 2^62, far above every ID Unity reserves — <c>class ID × 100000</c> main objects, 100100000 for
	/// a prefab asset itself, and 0 for "none". A collision inside one file (odds about 2^-60) is
	/// resolved by re-hashing with a counter, in key order, so even that is deterministic.
	/// </para>
	/// <para>
	/// <b>Byte-identical output.</b> After the IDs are replaced the documents are sorted by ID, so the
	/// file no longer reflects the order in which Unity wrote objects with random IDs. The same build
	/// therefore yields the same bytes, which lets the generator skip unchanged prefabs and makes any
	/// difference between two machines a real one.
	/// </para>
	/// <para>
	/// Only references that stay inside the file — <c>{fileID: N}</c> with no GUID — are rewritten;
	/// references to meshes and materials carry their own GUID and fixed main file ID and are left
	/// alone. A prefab with nested prefab instances or stripped objects is refused rather than
	/// guessed at: the generator never makes one.
	/// </para>
	/// </remarks>
	public static class ProceduralArtFileIds
	{
		private const string Salt = "FishMMO.ProceduralArtFileId:";

		private static readonly Regex Header = new Regex(@"^--- !u!(\d+) &(-?\d+)( stripped)?[ \t]*$", RegexOptions.CultureInvariant);
		private static readonly Regex LocalReference = new Regex(@"\{fileID: (-?\d+)\}", RegexOptions.CultureInvariant);
		private static readonly Regex Field = new Regex(@"^  (m_Name|m_GameObject|m_Father|m_Script): (.*)$", RegexOptions.CultureInvariant);
		private static readonly Regex ListReference = new Regex(@"^  - (?:component: )?\{fileID: (-?\d+)\}\s*$", RegexOptions.CultureInvariant);

		private const int GameObjectClass = 1;
		private const int TransformClass = 4;
		private const int RectTransformClass = 224;
		private const int MonoBehaviourClass = 114;

		/// <summary>One YAML document: its header, its object's class and file ID, and its lines.</summary>
		private sealed class Document
		{
			public int ClassId;
			public long FileId;
			public bool Stripped;
			public readonly List<string> Lines = new List<string>();
			public string Name;
			public long GameObject;
			public long Father;
			public string Script;
			/// <summary>A GameObject's components, or a Transform's children, in file order.</summary>
			public readonly List<long> List = new List<long>();
		}

		/// <summary>
		/// The deterministic file ID for an object with this key in the prefab at this path, before
		/// any collision counter. Exposed for tests.
		/// </summary>
		public static long IdFor(string assetPath, string key, int counter = 0)
		{
			string text = Salt + Normalise(assetPath) + "\n" + key + (counter > 0 ? "#" + counter.ToString(CultureInfo.InvariantCulture) : string.Empty);
			using (MD5 md5 = MD5.Create())
			{
				byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(text));
				ulong value = BitConverter.ToUInt64(hash, 0);
				if (!BitConverter.IsLittleEndian)
				{
					value = ReverseBytes(value);
				}
				return (long)((value & 0x3FFFFFFFFFFFFFFFUL) | 0x4000000000000000UL);
			}
		}

		private static ulong ReverseBytes(ulong v)
		{
			ulong r = 0;
			for (int i = 0; i < 8; i++)
			{
				r = (r << 8) | (v & 0xFF);
				v >>= 8;
			}
			return r;
		}

		/// <summary>
		/// Rewrites a prefab's YAML with deterministic file IDs and canonical document order.
		/// </summary>
		/// <param name="assetPath">The prefab's project path, part of every ID.</param>
		/// <param name="yaml">The prefab as Unity saved it.</param>
		/// <param name="rewritten">The rewritten text (LF line endings), or null on failure.</param>
		/// <param name="remap">Every object's old file ID → new one, for rewriting references from other files.</param>
		/// <param name="error">Why it could not be rewritten, or null.</param>
		public static bool TryRewrite(string assetPath, string yaml, out string rewritten, out Dictionary<long, long> remap, out string error)
		{
			rewritten = null;
			remap = null;
			if (!TryParse(yaml, out List<string> preamble, out List<Document> documents, out error))
			{
				return false;
			}
			if (!TryAssignKeys(documents, out Dictionary<long, string> keys, out error))
			{
				return false;
			}

			// Keys in ordinal order, so a collision counter is spent the same way on every machine.
			var ordered = new List<KeyValuePair<long, string>>(keys);
			ordered.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
			var used = new HashSet<long>();
			remap = new Dictionary<long, long>();
			foreach (KeyValuePair<long, string> pair in ordered)
			{
				int counter = 0;
				long id = IdFor(assetPath, pair.Value);
				while (!used.Add(id))
				{
					id = IdFor(assetPath, pair.Value, ++counter);
				}
				remap[pair.Key] = id;
			}

			Dictionary<long, long> map = remap;
			var sb = new StringBuilder(yaml.Length + 64);
			foreach (string line in preamble)
			{
				sb.Append(line).Append('\n');
			}
			documents.Sort((a, b) => map[a.FileId].CompareTo(map[b.FileId]));
			foreach (Document doc in documents)
			{
				sb.Append("--- !u!").Append(doc.ClassId.ToString(CultureInfo.InvariantCulture))
					.Append(" &").Append(map[doc.FileId].ToString(CultureInfo.InvariantCulture)).Append('\n');
				foreach (string line in doc.Lines)
				{
					sb.Append(LocalReference.Replace(line, m =>
					{
						long old = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
						return old != 0 && map.TryGetValue(old, out long now) ? "{fileID: " + now.ToString(CultureInfo.InvariantCulture) + "}" : m.Value;
					})).Append('\n');
				}
			}
			rewritten = sb.ToString();
			return true;
		}

		/// <summary>
		/// The key an object is hashed by (see the class remarks), by its current file ID. Exposed so a
		/// test can check that what TerrainData references — the root GameObject — has the key "GO:/".
		/// </summary>
		public static bool TryKeys(string yaml, out Dictionary<long, string> keys, out string error)
		{
			keys = null;
			return TryParse(yaml, out _, out List<Document> documents, out error) && TryAssignKeys(documents, out keys, out error);
		}

		/// <summary>The current file ID of the prefab's root GameObject, or 0.</summary>
		public static long RootGameObjectId(string yaml)
		{
			if (!TryKeys(yaml, out Dictionary<long, string> keys, out _))
			{
				return 0;
			}
			foreach (KeyValuePair<long, string> pair in keys)
			{
				if (pair.Value == RootKey)
				{
					return pair.Key;
				}
			}
			return 0;
		}

		/// <summary>The key of the root GameObject: what a reference to the prefab points at.</summary>
		public const string RootKey = "GO:/";

		private static bool TryParse(string yaml, out List<string> preamble, out List<Document> documents, out string error)
		{
			preamble = new List<string>();
			documents = new List<Document>();
			error = null;
			if (string.IsNullOrEmpty(yaml))
			{
				error = "empty file";
				return false;
			}
			string[] lines = yaml.Replace("\r\n", "\n").Split('\n');
			int count = lines.Length;
			// A trailing newline leaves one empty element, which is not a line.
			if (count > 0 && lines[count - 1].Length == 0)
			{
				count--;
			}
			Document current = null;
			bool inComponents = false, inChildren = false;
			for (int i = 0; i < count; i++)
			{
				string line = lines[i];
				Match header = Header.Match(line);
				if (header.Success)
				{
					current = new Document
					{
						ClassId = int.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture),
						FileId = long.Parse(header.Groups[2].Value, CultureInfo.InvariantCulture),
						Stripped = header.Groups[3].Success,
					};
					documents.Add(current);
					inComponents = inChildren = false;
					continue;
				}
				if (current == null)
				{
					if (line.StartsWith("---", StringComparison.Ordinal))
					{
						error = $"unrecognised document header '{line}'";
						return false;
					}
					preamble.Add(line);
					continue;
				}
				current.Lines.Add(line);

				if (line == "  m_Component:")
				{
					inComponents = true;
					inChildren = false;
					continue;
				}
				if (line == "  m_Children:" || line.StartsWith("  m_Children: []", StringComparison.Ordinal))
				{
					inChildren = line == "  m_Children:";
					inComponents = false;
					continue;
				}
				if (inComponents || inChildren)
				{
					Match item = ListReference.Match(line);
					if (item.Success)
					{
						current.List.Add(long.Parse(item.Groups[1].Value, CultureInfo.InvariantCulture));
						continue;
					}
					inComponents = inChildren = false;
				}
				Match field = Field.Match(line);
				if (field.Success)
				{
					string value = field.Groups[2].Value;
					switch (field.Groups[1].Value)
					{
						case "m_Name": current.Name = value; break;
						case "m_GameObject": current.GameObject = ReferencedId(value); break;
						case "m_Father": current.Father = ReferencedId(value); break;
						case "m_Script": current.Script = value.Trim(); break;
					}
				}
			}
			if (documents.Count == 0)
			{
				error = "no objects";
				return false;
			}
			return true;
		}

		private static long ReferencedId(string value)
		{
			Match m = LocalReference.Match(value);
			return m.Success ? long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
		}

		private static bool TryAssignKeys(List<Document> documents, out Dictionary<long, string> keys, out string error)
		{
			keys = null;
			error = null;
			var byId = new Dictionary<long, Document>();
			foreach (Document doc in documents)
			{
				if (doc.Stripped || doc.ClassId == 1001)
				{
					error = "nested prefab instances and stripped objects are not supported";
					return false;
				}
				if (!byId.TryAdd(doc.FileId, doc))
				{
					error = $"file ID {doc.FileId} appears twice";
					return false;
				}
			}

			// Each GameObject's transform, and the root.
			var transformOf = new Dictionary<long, Document>();
			Document root = null;
			foreach (Document doc in documents)
			{
				if (doc.ClassId != TransformClass && doc.ClassId != RectTransformClass)
				{
					continue;
				}
				if (doc.GameObject == 0 || !byId.ContainsKey(doc.GameObject) || !transformOf.TryAdd(doc.GameObject, doc))
				{
					error = $"transform {doc.FileId} has no GameObject of its own";
					return false;
				}
				if (doc.Father == 0)
				{
					if (root != null)
					{
						error = "more than one root transform";
						return false;
					}
					root = doc;
				}
			}
			if (root == null)
			{
				error = "no root transform";
				return false;
			}

			// Hierarchy paths, walked down from the root through each transform's children in order.
			var pathOf = new Dictionary<long, string>();
			var pending = new Stack<(Document transform, string path)>();
			pending.Push((root, "/"));
			while (pending.Count > 0)
			{
				(Document transform, string path) = pending.Pop();
				if (pathOf.ContainsKey(transform.GameObject))
				{
					error = "the hierarchy has a cycle";
					return false;
				}
				pathOf[transform.GameObject] = path;
				var seen = new Dictionary<string, int>(StringComparer.Ordinal);
				foreach (long childId in transform.List)
				{
					if (!byId.TryGetValue(childId, out Document child) || (child.ClassId != TransformClass && child.ClassId != RectTransformClass)
						|| !byId.TryGetValue(child.GameObject, out Document childObject))
					{
						error = $"transform {transform.FileId} lists a child {childId} that is not a transform";
						return false;
					}
					string name = childObject.Name ?? string.Empty;
					seen.TryGetValue(name, out int index);
					seen[name] = index + 1;
					string segment = Escape(name) + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
					pending.Push((child, path == "/" ? "/" + segment : path + "/" + segment));
				}
			}

			keys = new Dictionary<long, string>();
			foreach (Document doc in documents)
			{
				if (doc.ClassId != GameObjectClass)
				{
					continue;
				}
				if (!pathOf.TryGetValue(doc.FileId, out string path))
				{
					error = $"GameObject '{doc.Name}' ({doc.FileId}) is not under the root";
					return false;
				}
				keys[doc.FileId] = "GO:" + path;
				var perType = new Dictionary<string, int>(StringComparer.Ordinal);
				foreach (long componentId in doc.List)
				{
					if (!byId.TryGetValue(componentId, out Document component) || component.ClassId == GameObjectClass)
					{
						error = $"GameObject '{doc.Name}' lists a component {componentId} that is not in the file";
						return false;
					}
					string type = component.ClassId.ToString(CultureInfo.InvariantCulture);
					if (component.ClassId == MonoBehaviourClass)
					{
						type += ":" + (component.Script ?? string.Empty);
					}
					perType.TryGetValue(type, out int index);
					perType[type] = index + 1;
					if (keys.ContainsKey(componentId))
					{
						error = $"component {componentId} is listed by two GameObjects";
						return false;
					}
					keys[componentId] = "C:" + path + "|" + type + "#" + index.ToString(CultureInfo.InvariantCulture);
				}
			}
			foreach (Document doc in documents)
			{
				if (!keys.ContainsKey(doc.FileId))
				{
					error = $"object {doc.FileId} (class {doc.ClassId}) belongs to no GameObject";
					return false;
				}
			}
			return true;
		}

		/// <summary>Names may hold the path's own punctuation; escaping keeps two different trees from sharing a path.</summary>
		private static string Escape(string name) => name.Replace("\\", "\\\\").Replace("/", "\\/").Replace("[", "\\[");

		private static string Normalise(string path) => path.Replace('\\', '/');
	}
}
#endif
