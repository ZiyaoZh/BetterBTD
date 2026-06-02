using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BetterBTD.Models.Rounds;

namespace BetterBTD.Services.Shared;

public sealed class RoundCatalogService
{
    private static readonly Lazy<RoundCatalogService> InstanceHolder = new(() => new RoundCatalogService());

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    private readonly string _catalogFilePath;
    private readonly object _syncRoot = new();
    private RoundCatalog? _catalog;

    public RoundCatalogService()
        : this(Path.Combine(AppContext.BaseDirectory, "Assets", "Data", "Rounds", "default.json"))
    {
    }

    internal RoundCatalogService(string catalogFilePath)
    {
        _catalogFilePath = catalogFilePath ?? throw new ArgumentNullException(nameof(catalogFilePath));
    }

    public static RoundCatalogService Instance => InstanceHolder.Value;

    public RoundCatalog LoadCatalog()
    {
        lock (_syncRoot)
        {
            if (_catalog is not null)
            {
                return _catalog;
            }

            if (!File.Exists(_catalogFilePath))
            {
                throw new InvalidOperationException($"Round catalog file not found: {_catalogFilePath}");
            }

            var json = File.ReadAllText(_catalogFilePath);
            var catalog = JsonSerializer.Deserialize<RoundCatalog>(json, JsonOptions)
                ?? throw new InvalidOperationException("Round catalog file is empty or invalid.");

            ValidateCatalog(catalog);
            _catalog = catalog;
            return _catalog;
        }
    }

    public int GetMaxRound()
    {
        return LoadCatalog().MaxRound;
    }

    public RoundDefinition GetRound(int round)
    {
        var catalog = LoadCatalog();
        var definition = catalog.Rounds.FirstOrDefault(x => x.Round == round);
        if (definition is null)
        {
            throw new ArgumentOutOfRangeException(nameof(round), round, "Round is outside the catalog range.");
        }

        return definition;
    }

    public RoundRangeSummary CalculateRange(int startRound, int endRound)
    {
        var catalog = LoadCatalog();
        ValidateRange(catalog, startRound, endRound);

        var rounds = new List<RoundDefinition>(endRound - startRound + 1);
        for (var round = startRound; round <= endRound; round++)
        {
            var definition = catalog.Rounds.FirstOrDefault(x => x.Round == round);
            if (definition is null)
            {
                throw new InvalidOperationException($"Round {round} is missing from the catalog.");
            }

            rounds.Add(definition);
        }

        var peakCash = rounds[0];
        var peakRbe = rounds[0];
        var peakDuration = rounds[0];
        var totalCashReward = 0d;
        var totalExperience = 0L;
        var totalRbe = 0L;
        var totalDurationSeconds = 0d;
        var bloonTotals = new Dictionary<RoundBloonIdentity, long>();

        foreach (var round in rounds)
        {
            totalCashReward += round.CashReward;
            totalExperience += round.Experience;
            totalRbe += round.Rbe;
            totalDurationSeconds += round.DurationSeconds;

            if (round.CashReward > peakCash.CashReward)
            {
                peakCash = round;
            }

            if (round.Rbe > peakRbe.Rbe)
            {
                peakRbe = round;
            }

            if (round.DurationSeconds > peakDuration.DurationSeconds)
            {
                peakDuration = round;
            }

            foreach (var bloon in round.Bloons)
            {
                var identity = new RoundBloonIdentity(bloon.Type, bloon.IsCamo, bloon.IsRegrow, bloon.IsFortified);
                bloonTotals[identity] = bloonTotals.TryGetValue(identity, out var currentCount)
                    ? currentCount + bloon.TotalCount
                    : bloon.TotalCount;
            }
        }

        return new RoundRangeSummary
        {
            StartRound = startRound,
            EndRound = endRound,
            RoundCount = rounds.Count,
            TotalCashReward = totalCashReward,
            TotalExperience = totalExperience,
            TotalRbe = totalRbe,
            TotalDurationSeconds = totalDurationSeconds,
            PeakCashRewardRound = new RoundMetricPeak
            {
                Round = peakCash.Round,
                Value = peakCash.CashReward
            },
            PeakRbeRound = new RoundMetricPeak
            {
                Round = peakRbe.Round,
                Value = peakRbe.Rbe
            },
            PeakDurationRound = new RoundMetricPeak
            {
                Round = peakDuration.Round,
                Value = peakDuration.DurationSeconds
            },
            BloonTotals = bloonTotals
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key.Type)
                .ThenBy(x => x.Key.IsCamo)
                .ThenBy(x => x.Key.IsFortified)
                .ThenBy(x => x.Key.IsRegrow)
                .Select(x => new RoundBloonAggregate
                {
                    Type = x.Key.Type,
                    IsCamo = x.Key.IsCamo,
                    IsFortified = x.Key.IsFortified,
                    IsRegrow = x.Key.IsRegrow,
                    TotalCount = x.Value
                })
                .ToArray()
        };
    }

    private static void ValidateCatalog(RoundCatalog catalog)
    {
        if (!string.Equals(catalog.Format, RoundCatalog.FormatId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported round catalog format: {catalog.Format}");
        }

        if (catalog.Version < 2)
        {
            throw new InvalidOperationException($"Unsupported round catalog version: {catalog.Version}");
        }

        if (catalog.Rounds.Count == 0)
        {
            throw new InvalidOperationException("Round catalog does not contain any rounds.");
        }

        var duplicateRound = catalog.Rounds
            .GroupBy(x => x.Round)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicateRound is not null)
        {
            throw new InvalidOperationException($"Round catalog contains duplicate round {duplicateRound.Key}.");
        }
    }

    private static void ValidateRange(RoundCatalog catalog, int startRound, int endRound)
    {
        if (startRound < 1 || endRound < 1 || startRound > endRound || endRound > catalog.MaxRound)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startRound),
                $"Round range must be between 1 and {catalog.MaxRound}, and startRound cannot exceed endRound.");
        }
    }

    private readonly record struct RoundBloonIdentity(
        RoundBloonType Type,
        bool IsCamo,
        bool IsRegrow,
        bool IsFortified);
}
