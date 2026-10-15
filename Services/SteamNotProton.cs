using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace GithubLauncher.Services;

public static class SteamNotProton
{
    public static string SteamRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Steam");
    public static List<string> Accounts(string? root = null) => Directory.Exists(Path.Combine(root ?? SteamRoot, "userdata"))
        ? Directory.GetDirectories(Path.Combine(root ?? SteamRoot, "userdata"))
            .Select(Path.GetFileName).OfType<string>().Where(n => n.Length > 0 && n.All(char.IsAsciiDigit)).Order().ToList() : [];

    public static List<string> Tools(string? root = null)
    {
        var directory = Path.Combine(root ?? SteamRoot, "compatibilitytools.d");
        return Directory.Exists(directory) ? Directory.GetDirectories(directory, "notproton*")
            .Where(p => File.Exists(Path.Combine(p, "run")) && File.Exists(Path.Combine(p, "compatibilitytool.vdf")))
            .Select(Path.GetFileName).OfType<string>().Order().ToList() : [];
    }

    public static bool IsSteamRunning() => new[] { "steam_osx", "steam" }.Any(name => Process.GetProcessesByName(name).Length > 0);

    public static void ValidateSetup(string tool, string account, string? root = null)
    {
        if (!Tools(root).Contains(tool)) throw new InvalidOperationException("Open NotProton, prepare CrossOver, and start Steam once. Choose an installed NotProton tool.");
        if (!Accounts(root).Contains(account)) throw new InvalidOperationException("Choose an existing Steam account on this Mac.");
        if (root == null)
        {
            var inner = Path.Combine(SteamRoot, "Steam.AppBundle", "Steam", "Contents", "MacOS", "notproton.dylib");
            if (!File.Exists(inner) && !File.Exists("/Applications/Steam.app/Contents/MacOS/notproton.dylib"))
                throw new InvalidOperationException("NotProton was not detected in Steam. Complete setup in the NotProton application.");
        }
    }

    public static ulong GameId(string executable, string name)
    {
        uint crc = 0xffffffff;
        foreach (byte value in Encoding.UTF8.GetBytes(Quote(executable) + name))
        {
            crc ^= value;
            for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        return ((ulong)(~crc | 0x80000000u) << 32) | 0x02000000ul;
    }

    public static bool MatchesPreparedExecutable(ulong id, string preparedExecutable, string selectedExecutable, string name) =>
        id != 0 && !string.IsNullOrWhiteSpace(preparedExecutable) &&
        string.Equals(preparedExecutable, selectedExecutable, StringComparison.Ordinal) &&
        id == GameId(selectedExecutable, name);

    public static ulong Register(string name, string executable, string tool, string account, string? root = null)
    {
        if (IsSteamRunning()) throw new InvalidOperationException("Close Steam before preparing the shortcut, then reopen it.");
        ValidateSetup(tool, account, root);
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose an installed Windows executable (.exe).");
        if (executable.Contains('"') || name.Any(c => c is '\0' or '\r' or '\n')) throw new InvalidOperationException("Invalid Steam shortcut name or path.");
        var steam = root ?? SteamRoot;
        var shortcutsPath = Path.Combine(steam, "userdata", account, "config", "shortcuts.vdf");
        var configPath = Path.Combine(steam, "config", "config.vdf");
        if (!File.Exists(configPath)) throw new FileNotFoundException("Open Steam and sign in first.", configPath);
        ulong gameId = GameId(executable, name);
        uint appId = (uint)(gameId >> 32);
        var shortcuts = ReadShortcuts(shortcutsPath);
        var entries = Object(shortcuts, "shortcuts");
        string marker = "GitOpsGarage NotProton";
        // Own entries are matched by name/tag; never overwrite unrelated shortcuts.
        var existing = entries.Values.OfType<Dictionary<string, object>>().FirstOrDefault(e =>
            e.GetValueOrDefault("appname") as string == name && e.TryGetValue("tags", out var tags) &&
            tags is Dictionary<string, object> t && t.Values.Contains(marker));
        var entry = existing ?? new Dictionary<string, object>();
        if (existing == null)
        {
            int index = 0;
            while (entries.ContainsKey(index.ToString())) index++;
            entries[index.ToString()] = entry;
        }
        entry["appid"] = unchecked((int)appId);
        entry["appname"] = name;
        entry["exe"] = Quote(executable);
        entry["StartDir"] = Quote(Path.GetDirectoryName(executable)!);
        entry["LaunchOptions"] = "";
        entry["icon"] = "";
        entry["ShortcutPath"] = "";
        entry["AllowDesktopConfig"] = 1;
        entry["AllowOverlay"] = 1;
        entry["IsHidden"] = 0;
        entry["tags"] = new Dictionary<string, object> { ["0"] = marker };
        // Parse both files before changing either. Preserve unknown shortcut fields.
        var config = SetToolMapping(File.ReadAllText(configPath), appId, tool);
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true)) WriteObject(writer, shortcuts);
        if (IsSteamRunning()) throw new InvalidOperationException("Steam opened during preparation. Close it and try again.");
        var oldConfig = File.ReadAllBytes(configPath);
        var oldShortcuts = File.Exists(shortcutsPath) ? File.ReadAllBytes(shortcutsPath) : null;
        try
        {
            Replace(configPath, Encoding.UTF8.GetBytes(config));
            Replace(shortcutsPath, buffer.ToArray());
        }
        catch
        {
            File.WriteAllBytes(configPath, oldConfig);
            if (oldShortcuts != null) File.WriteAllBytes(shortcutsPath, oldShortcuts);
            else if (File.Exists(shortcutsPath)) File.Delete(shortcutsPath);
            throw;
        }
        return gameId;
    }

    public static ProcessStartInfo LaunchInfo(ulong id, string executable, string name, string tool, string account)
    {
        ValidateSetup(tool, account);
        if (id != GameId(executable, name)) throw new InvalidOperationException("The executable changed. Prepare the Steam shortcut again in this game’s settings.");
        var shortcuts = ReadShortcuts(Path.Combine(SteamRoot, "userdata", account, "config", "shortcuts.vdf"));
        var found = Object(shortcuts, "shortcuts").Values.OfType<Dictionary<string, object>>().Any(e =>
            e.GetValueOrDefault("appid") is int app && unchecked((uint)app) == (uint)(id >> 32) &&
            e.GetValueOrDefault("exe") as string == Quote(executable));
        if (!found) throw new InvalidOperationException("Steam shortcut is missing. Prepare it again in this game’s settings.");
        var mapping = FindMapping(File.ReadAllText(Path.Combine(SteamRoot, "config", "config.vdf")), (uint)(id >> 32));
        if (mapping != tool) throw new InvalidOperationException("The Steam tool assignment changed. Prepare the NotProton shortcut again.");
        var info = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        info.ArgumentList.Add($"steam://rungameid/{id}");
        return info;
    }

    private static string Quote(string value) => $"\"{value}\"";
    private static void Replace(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) File.Copy(path, path + ".githublauncher.bak", true);
        string temporary = path + ".githublauncher.tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, true);
    }

    private static Dictionary<string, object> Object(Dictionary<string, object> parent, string key)
    {
        if (parent.TryGetValue(key, out var value)) return value as Dictionary<string, object> ?? throw new InvalidDataException($"Invalid VDF object: {key}");
        var result = new Dictionary<string, object>(); parent[key] = result; return result;
    }

    internal static Dictionary<string, object> ReadShortcuts(string path)
    {
        if (!File.Exists(path)) return new();
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
        var result = ReadObject(reader);
        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Trailing Steam shortcut data; file preserved.");
        return result;
    }
    private static string ReadString(BinaryReader reader)
    {
        using var bytes = new MemoryStream();
        byte value;
        while ((value = reader.ReadByte()) != 0) bytes.WriteByte(value);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
    private static Dictionary<string, object> ReadObject(BinaryReader reader)
    {
        var result = new Dictionary<string, object>();
        while (true)
        {
            byte type = reader.ReadByte();
            if (type == 8) return result;
            string key = ReadString(reader);
            result.Add(key, type switch { 0 => ReadObject(reader), 1 => ReadString(reader), 2 => reader.ReadInt32(),
                _ => throw new InvalidDataException($"Unsupported Steam VDF type {type}; file preserved.") });
        }
    }
    private static void WriteString(BinaryWriter writer, string value) { writer.Write(Encoding.UTF8.GetBytes(value)); writer.Write((byte)0); }
    private static void WriteObject(BinaryWriter writer, Dictionary<string, object> obj)
    {
        foreach (var (key, value) in obj)
        {
            writer.Write(value is Dictionary<string, object> ? (byte)0 : value is string ? (byte)1 : (byte)2);
            WriteString(writer, key);
            if (value is Dictionary<string, object> nested) WriteObject(writer, nested);
            else if (value is string text) WriteString(writer, text);
            else writer.Write((int)value);
        }
        writer.Write((byte)8);
    }

    // Token spans let us change one mapping while retaining comments and unrelated config exactly.
    private sealed record Node(string Key, string? Value, int Start, int End, int Close, List<Node> Children);
    private static List<Node> Parse(string text)
    {
        var tokens = Regex.Matches(text, "//[^\\r\\n]*|\"(?:\\\\.|[^\"\\\\])*\"|[{}]|[^\\s{}\"]+")
            .Where(m => !m.Value.StartsWith("//")).ToList();
        int index = 0;
        string Decode(string token) => token.StartsWith('"') ? token[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\") : token;
        List<Node> Read(bool nested)
        {
            var nodes = new List<Node>();
            while (index < tokens.Count && tokens[index].Value != "}")
            {
                var key = tokens[index++];
                if (key.Value is "{" || index >= tokens.Count) throw new InvalidDataException("Malformed Steam config; file preserved.");
                var value = tokens[index++];
                if (value.Value == "{")
                {
                    var children = Read(true);
                    if (index >= tokens.Count || tokens[index].Value != "}") throw new InvalidDataException("Unclosed Steam config object.");
                    var close = tokens[index++];
                    nodes.Add(new(Decode(key.Value), null, key.Index, close.Index + close.Length, close.Index, children));
                }
                else
                {
                    if (value.Value == "}" || key.Value.StartsWith('[') || value.Value.StartsWith('['))
                        throw new InvalidDataException("Unsupported conditional Steam config; file preserved.");
                    nodes.Add(new(Decode(key.Value), Decode(value.Value), key.Index, value.Index + value.Length, -1, []));
                }
            }
            if (!nested && index != tokens.Count) throw new InvalidDataException("Unexpected Steam config closing brace.");
            return nodes;
        }
        return Read(false);
    }
    private static Node SteamNode(string config)
    {
        var nodes = Parse(config);
        Node? node = null;
        foreach (var key in new[] { "InstallConfigStore", "Software", "Valve", "Steam" })
        {
            node = nodes.SingleOrDefault(n => n.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && n.Value == null)
                ?? throw new InvalidDataException($"Steam config missing {key}; file preserved.");
            nodes = node.Children;
        }
        return node!;
    }
    internal static string? FindMapping(string config, uint appId) => SteamNode(config).Children
        .FirstOrDefault(n => n.Key.Equals("CompatToolMapping", StringComparison.OrdinalIgnoreCase))?.Children
        .FirstOrDefault(n => n.Key == appId.ToString())?.Children.FirstOrDefault(n => n.Key == "name")?.Value;

    internal static string SetToolMapping(string config, uint appId, string tool)
    {
        if (!Regex.IsMatch(tool, "^notproton[a-zA-Z0-9._-]*$")) throw new ArgumentException("Invalid NotProton tool name.");
        var steam = SteamNode(config);
        var mappings = steam.Children.SingleOrDefault(n => n.Key.Equals("CompatToolMapping", StringComparison.OrdinalIgnoreCase));
        var existing = mappings?.Children.SingleOrDefault(n => n.Key == appId.ToString());
        string entry = $"\"{appId}\"\n{{\n\"name\" \"{tool}\"\n\"config\" \"\"\n\"priority\" \"250\"\n}}";
        if (existing != null) return config[..existing.Start] + entry + config[existing.End..];
        if (mappings != null) return config.Insert(mappings.Close, "\n" + entry + "\n");
        return config.Insert(steam.Close, "\n\"CompatToolMapping\"\n{\n" + entry + "\n}\n");
    }
}
