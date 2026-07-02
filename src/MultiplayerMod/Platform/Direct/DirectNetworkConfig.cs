using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MultiplayerMod.Platform.Direct;

/// <summary>
/// Configuration for the direct (LAN/IP) transport. Values are resolved, in order, from an environment variable
/// (<c>MP_&lt;KEY&gt;</c>), then a <c>multiplayer.cfg</c> file in the game's persistent data folder, then a default.
/// The file exists because environment variables don't propagate reliably through a Steam launch — the file is a
/// dependable way to point two machines at each other without a Steam lobby, which is what lets cross-machine play
/// work under a single Steam account (or no Steam at all).
///
/// <para><c>multiplayer.cfg</c> (one <c>key=value</c> per line, <c>#</c> comments):</para>
/// <list type="bullet">
///   <item><c>TRANSPORT=direct</c> — select the LAN transport; anything else (or absent) keeps Steam.</item>
///   <item><c>PORT=27100</c> — TCP port the host binds / the client connects to.</item>
///   <item><c>HOST=192.168.1.202</c> — host address the joining client dials.</item>
///   <item><c>NAME=Landon</c> — player name shown to others (default: machine name).</item>
/// </list>
/// </summary>
public static class DirectNetworkConfig {

    public const int DefaultPort = 27100;
    public const string FileName = "multiplayer.cfg";

    private static readonly Lazy<Dictionary<string, string>> fileValues = new(LoadFile);

    public static bool DirectTransportSelected =>
        string.Equals(Get("TRANSPORT"), "direct", StringComparison.OrdinalIgnoreCase);

    /// <summary>When set (<c>SELFTEST=true</c>), runs an automated in-process loopback transport test on
    /// startup and logs the result — lets the transport be validated with no UI, world, or second machine.</summary>
    public static bool SelfTest =>
        string.Equals(Get("SELFTEST"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>When set (<c>AUTOHOST=true</c>), auto-loads the newest save in host mode at the main menu — no
    /// UI. For unattended two-machine testing: this box hosts.</summary>
    public static bool AutoHost =>
        string.Equals(Get("AUTOHOST"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>When set (<c>AUTOJOIN=true</c>), auto-connects to <see cref="Host"/> at the main menu — no UI.
    /// For unattended two-machine testing: this box joins.</summary>
    public static bool AutoJoin =>
        string.Equals(Get("AUTOJOIN"), "true", StringComparison.OrdinalIgnoreCase);

    public static int Port {
        get {
            var raw = Get("PORT");
            return int.TryParse(raw, out var port) && port is > 0 and <= 65535 ? port : DefaultPort;
        }
    }

    public static string Host {
        get {
            var raw = Get("HOST");
            return string.IsNullOrWhiteSpace(raw) ? "127.0.0.1" : raw!.Trim();
        }
    }

    public static string PlayerName {
        get {
            var raw = Get("NAME");
            return string.IsNullOrWhiteSpace(raw) ? SafeMachineName() : raw!.Trim();
        }
    }

    // Environment variable wins over the file so a one-off launch can override persistent config.
    private static string? Get(string key) {
        var fromEnv = Environment.GetEnvironmentVariable("MP_" + key);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;
        return fileValues.Value.TryGetValue(key, out var value) ? value : null;
    }

    private static Dictionary<string, string> LoadFile() {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try {
            var path = Path.Combine(Application.persistentDataPath, FileName);
            if (!File.Exists(path))
                return values;
            foreach (var line in File.ReadAllLines(path)) {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                    continue;
                var separator = trimmed.IndexOf('=');
                if (separator <= 0)
                    continue;
                values[trimmed.Substring(0, separator).Trim()] = trimmed.Substring(separator + 1).Trim();
            }
        } catch (Exception) {
            // Missing/unreadable config simply means "use defaults" (Steam transport).
        }
        return values;
    }

    private static string SafeMachineName() {
        try {
            return string.IsNullOrWhiteSpace(Environment.MachineName) ? "Player" : Environment.MachineName;
        } catch {
            return "Player";
        }
    }

}
