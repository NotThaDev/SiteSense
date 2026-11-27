namespace SiteSense.Simulator;

public sealed record Fix(long TimestampMs, double Lat, double Lon, double AccuracyM);

public sealed class SimulatedDevice
{
    private readonly PathGraph _graph;
    private readonly Random _random;
    private GeoPoint _position;
    private int _from; // node the current edge departs from
    private int _to;   // node the current edge heads to

    public SimulatedDevice(string deviceId, PathGraph graph, int seed)
    {
        DeviceId = deviceId;
        _graph = graph;
        _random = new Random(seed);
        var start = _random.Next(graph.Nodes.Count);
        SpeedMps = Math.Clamp(1.3 + (_random.NextDouble() * 2 - 1) * 0.3, 0.8, 1.8);
        _position = graph.Nodes[start];
        _from = start;
        _to = ChooseNext(start, -1);
    }

    public string DeviceId { get; }

    public double SpeedMps { get; }

    public GeoPoint TruePosition => _position;

    public Fix Step(long timestampMs, double intervalS)
    {
        var remaining = SpeedMps * intervalS;
        while (true)
        {
            var target = _graph.Nodes[_to];
            var d = _graph.DistanceM(_position, target);
            if (remaining < d)
            {
                var f = remaining / d;
                _position = new GeoPoint(
                    _position.Lon + (target.Lon - _position.Lon) * f,
                    _position.Lat + (target.Lat - _position.Lat) * f);
                break;
            }
            remaining -= d;
            _position = target;
            var next = ChooseNext(_to, _from);
            _from = _to;
            _to = next;
        }

        // Accuracy-driven Gaussian noise; the 2D sigma is half the reported accuracy.
        var accuracy = 3 + _random.NextDouble() * 5;
        var sigma = accuracy / 2;
        var east = sigma * Gaussian();
        var north = sigma * Gaussian();
        return new Fix(
            timestampMs,
            _position.Lat + north / _graph.MetresPerDegLat,
            _position.Lon + east / _graph.MetresPerDegLon,
            accuracy);
    }

    private int ChooseNext(int node, int from)
    {
        var candidates = _graph.Neighbours(node).Where(n => n != from).ToArray();
        if (candidates.Length == 0) return from; // dead end: turn back
        return candidates[_random.Next(candidates.Length)];
    }

    // Box-Muller; u1 is in (0, 1] so Log never sees zero.
    private double Gaussian()
    {
        var u1 = 1.0 - _random.NextDouble();
        var u2 = _random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
