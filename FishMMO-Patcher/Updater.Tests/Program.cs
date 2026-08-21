using FishMMO.Patcher;

// Throwaway harness for FishMMO.Patcher.PathContainment. The NUnit runner in this repo needs
// Unity, and the helper is plain .NET, so it is compiled directly here and the assertions are
// actually executed. Exit code 0 = all pass.

int passed = 0;
int failed = 0;
var failures = new List<string>();

string root = Path.Combine(Path.GetTempPath(), "fishmmo-containment-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Directory.CreateDirectory(Path.Combine(root, "Managed"));
Directory.CreateDirectory(Path.Combine(root, "FishMMO_Data", "Plugins"));

// A sibling that shares a name prefix with the root. The classic prefix-comparison bypass.
string siblingPrefix = root + "-evil";
Directory.CreateDirectory(siblingPrefix);

// A real symlink inside the root, pointing outside it.
string linkDir = Path.Combine(root, "linked");
string outsideDir = Path.Combine(Path.GetTempPath(), "fishmmo-outside-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(outsideDir);
bool symlinkAvailable;
try
{
	Directory.CreateSymbolicLink(linkDir, outsideDir);
	symlinkAvailable = true;
}
catch (Exception ex)
{
	symlinkAvailable = false;
	Console.WriteLine($"NOTE: symlink creation unavailable on this host ({ex.GetType().Name}); symlink cases will be skipped.");
}

void Allow(string name, string relative)
{
	bool ok = PathContainment.TryResolve(root, relative, out string full, out string reason);
	if (ok && full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
	{
		passed++;
		Console.WriteLine($"  PASS  ALLOW  {name,-46} -> {full.Substring(root.Length)}");
	}
	else
	{
		failed++;
		string msg = $"  FAIL  ALLOW  {name,-46} expected allow, got refuse: {reason}";
		failures.Add(msg);
		Console.WriteLine(msg);
	}
}

void Reject(string name, string relative)
{
	bool ok = PathContainment.TryResolve(root, relative, out string full, out string reason);
	if (!ok)
	{
		passed++;
		Console.WriteLine($"  PASS  REJECT {name,-46} ({reason})");
	}
	else
	{
		failed++;
		string msg = $"  FAIL  REJECT {name,-46} expected refuse, got ALLOW -> {full}";
		failures.Add(msg);
		Console.WriteLine(msg);
	}
}

Console.WriteLine($"root = {root}");
Console.WriteLine();

Console.WriteLine("-- benign nested paths (MUST be allowed) --");
Allow("plain file", "version.txt");
Allow("nested file", "Managed/Assembly-CSharp.dll");
Allow("windows separators", "Managed\\UnityEngine.dll");
Allow("deep nesting", "FishMMO_Data/Plugins/x86_64/steam_api.dll");
Allow("dot segment", "./Managed/x.dll");
Allow("interior dot segment", "Managed/./x.dll");
Allow("double separator", "Managed//x.dll");
Allow("name containing dots", "lib.so.1.2.3");
Allow("space in name", "My Folder/My File.dat");
Allow("dot-prefixed name", ".config/settings.json");
Allow("unicode name", "Data/\u00e9\u00e8\u4e2d\u6587.bin");
Allow("name with dashes/underscores", "FishMMO_Data/some-file_v2.bin");

Console.WriteLine();
Console.WriteLine("-- traversal (MUST be refused) --");
Reject("parent escape", "../evil.txt");
Reject("deep parent escape", "../../../../../../etc/passwd");
Reject("interior parent escape", "Managed/../../evil.txt");
Reject("interior parent, net zero depth", "Managed/../Managed/../../evil.txt");
Reject("backslash parent escape", "..\\evil.txt");
Reject("mixed separator escape", "Managed\\..\\..\\evil.txt");
Reject("bare parent", "..");
Reject("trailing parent", "Managed/..");
Reject("dot-dot lookalike is NOT rejected wrongly? (control)", "../..");

Console.WriteLine();
Console.WriteLine("-- absolute / rooted (MUST be refused) --");
Reject("posix absolute", "/etc/cron.d/pwn");
Reject("posix absolute in tmp", Path.Combine(Path.GetTempPath(), "pwn"));
Reject("backslash-rooted", "\\Windows\\System32\\drivers\\etc\\hosts");
Reject("windows drive absolute", "C:\\Windows\\System32\\cmd.exe");
Reject("windows drive forward slash", "C:/Windows/System32/cmd.exe");
Reject("drive-relative", "C:evil.txt");
Reject("root itself", ".");

Console.WriteLine();
Console.WriteLine("-- UNC / device namespace (MUST be refused) --");
Reject("UNC share", "\\\\attacker\\share\\evil.dll");
Reject("UNC forward slashes", "//attacker/share/evil.dll");
Reject("win32 device namespace", "\\\\?\\C:\\Windows\\evil.dll");
Reject("device namespace UNC", "\\\\.\\pipe\\evil");

Console.WriteLine();
Console.WriteLine("-- prefix-collision sibling (MUST be refused) --");
// Resolves to <root>-evil/x, which shares "<root>" as a string prefix but is a different tree.
Reject("sibling sharing a name prefix", ".." + Path.DirectorySeparatorChar + Path.GetFileName(siblingPrefix) + Path.DirectorySeparatorChar + "x");

Console.WriteLine();
Console.WriteLine("-- symlink components (MUST be refused) --");
if (symlinkAvailable)
{
	Reject("write through a symlinked dir", "linked/evil.txt");
	Reject("nested write through symlink", "linked/deeper/evil.txt");
	Reject("the symlink itself", "linked");
}
else
{
	Console.WriteLine("  SKIP  (symlinks unavailable)");
}

Console.WriteLine();
Console.WriteLine("-- malformed / hostile shapes (MUST be refused) --");
Reject("null path", null!);
Reject("empty path", "");
Reject("whitespace path", "   ");
Reject("embedded NUL", "safe.txt\0/../../evil");
Reject("trailing dot segment", "config.");
Reject("trailing space segment", "config ");
Reject("alternate data stream", "version.txt:hidden");
Reject("ADS on a nested name", "Managed/x.dll:evil");

Console.WriteLine();
Console.WriteLine("-- ResolveOrThrow contract --");
try
{
	PathContainment.ResolveOrThrow(root, "../escape");
	failed++;
	failures.Add("  FAIL  ResolveOrThrow did not throw on '../escape'");
	Console.WriteLine("  FAIL  ResolveOrThrow did not throw on '../escape'");
}
catch (PathContainmentException ex)
{
	passed++;
	Console.WriteLine($"  PASS  ResolveOrThrow threw PathContainmentException (OffendingPath='{ex.OffendingPath}')");
}
try
{
	string ok = PathContainment.ResolveOrThrow(root, "Managed/good.dll");
	if (ok.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
	{
		passed++;
		Console.WriteLine("  PASS  ResolveOrThrow returned a contained path for a benign entry");
	}
	else
	{
		failed++;
		failures.Add("  FAIL  ResolveOrThrow returned an uncontained path");
		Console.WriteLine("  FAIL  ResolveOrThrow returned an uncontained path");
	}
}
catch (Exception ex)
{
	failed++;
	failures.Add($"  FAIL  ResolveOrThrow threw on a benign entry: {ex.Message}");
	Console.WriteLine($"  FAIL  ResolveOrThrow threw on a benign entry: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("-- regression proof: what Path.Combine alone would have done --");
foreach (string hostile in new[] { "../../../../etc/cron.d/pwn", "/etc/cron.d/pwn" })
{
	string naive = Path.GetFullPath(Path.Combine(root, hostile));
	bool escapes = !naive.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
	bool refused = !PathContainment.TryResolve(root, hostile, out _, out _);
	if (escapes && refused)
	{
		passed++;
		Console.WriteLine($"  PASS  '{hostile}' escapes via Path.Combine ({naive}) and is refused by PathContainment");
	}
	else
	{
		failed++;
		string msg = $"  FAIL  '{hostile}' escapes={escapes} refused={refused}";
		failures.Add(msg);
		Console.WriteLine(msg);
	}
}

try { Directory.Delete(root, true); } catch { }
try { Directory.Delete(siblingPrefix, true); } catch { }
try { Directory.Delete(outsideDir, true); } catch { }

Console.WriteLine();
Console.WriteLine($"==== {passed} passed, {failed} failed ====");
foreach (string f in failures) Console.WriteLine(f);
return failed == 0 ? 0 : 1;
