using System;
using System.Collections.Generic;
using System.Globalization;

namespace SiteSense.Simulator;

public sealed record SimulatorOptions
{
    private static readonly HashSet<string> ValueOptions = new()
    {
        "--api", "--token-url", "--client", "--secret", "--admin-user", "--admin-password",
        "--site", "--map", "--devices", "--device-prefix", "--interval-s", "--batch-s",
        "--duration-s", "--seed", "--start-ms",
    };

    public string Api { get; init; } = "http://localhost:8080";
    public string TokenUrl { get; init; } = "http://localhost:8081/realms/sitesense/protocol/openid-connect/token";
    public string Client { get; init; } = "gateway-a";
    public string Secret { get; init; } = "gateway-secret-a";
    public string AdminUser { get; init; } = "admin-a";
    public string AdminPassword { get; init; } = "Password1!";
    public string AdminClient { get; init; } = "sitesense-verify";
    public string Site { get; init; } = "data/site.json";
    public string Map { get; init; } = "data/map.geojson";
    public int Devices { get; init; } = 10;
    public string DevicePrefix { get; init; } = "sim-";
    public double IntervalS { get; init; } = 1.0;
    public double BatchS { get; init; } = 5.0;
    public double DurationS { get; init; } = 0.0;
    public int Seed { get; init; } = 42;
    public long? StartMs { get; init; }
    public bool Setup { get; init; } = true;

    public static SimulatorOptions Parse(string[] args, Func<string, string?> env)
    {
        var options = ApplyEnv(new SimulatorOptions(), env);
        for (var i = 0; i < args.Length; i++)
        {
            var name = args[i];
            if (name == "--no-setup")
            {
                options = options with { Setup = false };
                continue;
            }
            if (!ValueOptions.Contains(name)) throw new ArgumentException($"Unknown argument '{name}'.");
            if (i + 1 >= args.Length) throw new ArgumentException($"Missing value for {name}.");
            options = Apply(options, name, args[++i]);
        }
        Validate(options);
        return options;
    }

    private static SimulatorOptions ApplyEnv(SimulatorOptions o, Func<string, string?> env)
    {
        string? Get(string key)
        {
            var value = env(key);
            return string.IsNullOrEmpty(value) ? null : value;
        }

        return o with
        {
            Api = Get("SIM_API") ?? o.Api,
            TokenUrl = Get("SIM_TOKEN_URL") ?? o.TokenUrl,
            Secret = Get("SIM_SECRET") ?? o.Secret,
            AdminPassword = Get("SIM_ADMIN_PASSWORD") ?? o.AdminPassword,
            Site = Get("SIM_SITE") ?? o.Site,
            Map = Get("SIM_MAP") ?? o.Map,
        };
    }

    private static SimulatorOptions Apply(SimulatorOptions o, string name, string value) => name switch
    {
        "--api" => o with { Api = value },
        "--token-url" => o with { TokenUrl = value },
        "--client" => o with { Client = value },
        "--secret" => o with { Secret = value },
        "--admin-user" => o with { AdminUser = value },
        "--admin-password" => o with { AdminPassword = value },
        "--site" => o with { Site = value },
        "--map" => o with { Map = value },
        "--devices" => o with { Devices = ParseInt(name, value) },
        "--device-prefix" => o with { DevicePrefix = value },
        "--interval-s" => o with { IntervalS = ParseDouble(name, value) },
        "--batch-s" => o with { BatchS = ParseDouble(name, value) },
        "--duration-s" => o with { DurationS = ParseDouble(name, value) },
        "--seed" => o with { Seed = ParseInt(name, value) },
        "--start-ms" => o with { StartMs = ParseLong(name, value) },
        _ => throw new ArgumentException($"Unknown argument '{name}'."),
    };

    private static void Validate(SimulatorOptions o)
    {
        if (o.Devices < 1) throw new ArgumentException("Invalid --devices: must be at least 1.");
        if (o.IntervalS <= 0) throw new ArgumentException("Invalid --interval-s: must be greater than 0.");
        if (o.BatchS < o.IntervalS) throw new ArgumentException("Invalid --batch-s: must be at least --interval-s.");
    }

    private static int ParseInt(string name, string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : throw new ArgumentException($"Invalid {name}: expected a whole number, got '{value}'.");

    private static long ParseLong(string name, string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : throw new ArgumentException($"Invalid {name}: expected a whole number, got '{value}'.");

    private static double ParseDouble(string name, string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d)
            ? d
            : throw new ArgumentException($"Invalid {name}: expected a number, got '{value}'.");
}
