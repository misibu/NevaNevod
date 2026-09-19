using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace NevaVision.Core;

/// <summary>
/// RF4 reader that follows the confirmed read-only chains. Since Unity/IL2CPP
/// layouts can change, every read is guarded and a failed chain simply produces
/// an empty snapshot instead of touching game memory in any other way.
/// </summary>
public sealed class Rf4ObservationProvider : IDisposable
{
    private readonly ReadOnlyProcessReader _reader = new();
    private readonly Dictionary<string, string> _fishNames;
    private readonly List<ulong> _rootCandidates = new();
    private readonly Dictionary<(ulong Root, int Rod), ulong[]> _recoveredRodChains = new();
    private readonly HashSet<(ulong Root, int Rod)> _rodRecoveryAttempts = new();
    private ulong _rootAddress;
    private string _attachStatus = "Игра не подключена";
    private string _readStatus = string.Empty;

    public Rf4ObservationProvider()
    {
        _fishNames = LoadFishNames();
    }

    public bool IsAttached => _reader.IsAttached && _rootAddress != 0;
    public int? ProcessId => _reader.ProcessId;

    public bool TryAttach(out string status)
    {
        foreach (string processName in new[] { "rf4_x64", "RussianFishing4", "Russian Fishing 4" })
        {
            Process[] candidates;
            try
            {
                candidates = Process.GetProcessesByName(processName);
            }
            catch
            {
                continue;
            }

            foreach (Process process in candidates)
            {
                if (!_reader.Attach(process, out string attachError))
                    continue;

                if (!_reader.TryGetModuleBase("GameAssembly.dll", out ulong gameAssemblyBase))
                {
                    _reader.Detach();
                    continue;
                }

                ulong staticRoot = unchecked(gameAssemblyBase + Rf4Signature.RootRva);
                _rootCandidates.Clear();
                _recoveredRodChains.Clear();
                _rodRecoveryAttempts.Clear();

                // Different RF4 builds expose the same singleton either directly
                // or through the object chain kept in Rf4Signature. Keep both
                // candidates and select the one that produces a valid rod read.
                if (_reader.TryResolveChain(staticRoot, Rf4Signature.RootObjectChain, out ulong resolvedRoot))
                    _rootCandidates.Add(resolvedRoot);
                _rootCandidates.Add(staticRoot);
                _rootAddress = _rootCandidates[0];

                _attachStatus = $"Подключено к RF4, PID {process.Id}, сборка {Rf4Signature.TargetBuild}";
                _readStatus = "Проверка цепочек памяти…";
                status = _attachStatus;
                return true;
            }
        }

        _rootAddress = 0;
        _rootCandidates.Clear();
        _recoveredRodChains.Clear();
        _rodRecoveryAttempts.Clear();
        _readStatus = string.Empty;
        _attachStatus = "RF4 не найден или доступ к памяти ограничен";
        status = _attachStatus;
        return false;
    }

    public ObservationSnapshot Capture()
    {
        if (!IsAttached)
        {
            return new ObservationSnapshot(DateTimeOffset.Now, false, null, _attachStatus, Array.Empty<FishObservation>());
        }

        if (_rootCandidates.Count == 0)
            _rootCandidates.Add(_rootAddress);

        List<FishObservation>? bestObservations = null;
        ulong bestRoot = _rootAddress;
        int bestRodCount = -1;
        int bestPositionCount = 0;

        foreach (ulong root in _rootCandidates)
        {
            (List<FishObservation> rootObservations, int positionCount, bool playerPositionRead) = CaptureAtRoot(root);
            if (rootObservations.Count > bestRodCount ||
                (rootObservations.Count == bestRodCount && positionCount > bestPositionCount))
            {
                bestObservations = rootObservations;
                bestRoot = root;
                bestRodCount = rootObservations.Count;
                bestPositionCount = positionCount;
                _readStatus = $"Цепочка: {positionCount}/{Rf4Signature.RodChains.Length} удочек, игрок: {(playerPositionRead ? "да" : "нет")}";
            }
        }

        _rootAddress = bestRoot;
        var observations = bestObservations ?? new List<FishObservation>();
        string status = $"{_attachStatus}; {_readStatus}";
        return new ObservationSnapshot(DateTimeOffset.Now, true, ProcessId, status, observations);
    }

    private (List<FishObservation> Observations, int PositionCount, bool PlayerPositionRead) CaptureAtRoot(ulong rootAddress)
    {
        (float X, float Y, float Z)? playerPosition = null;
        bool playerPositionRead = false;
        if (_reader.TryResolveChain(rootAddress, Rf4Signature.PlayerPositionChain, out ulong playerPositionAddress) &&
            _reader.TryReadVector3(playerPositionAddress, out (float X, float Y, float Z) player))
        {
            playerPosition = player;
            playerPositionRead = true;
        }

        var observations = new List<FishObservation>(3);
        int positionCount = 0;
        for (int rod = 0; rod < Rf4Signature.RodChains.Length; rod++)
        {
            if (!TryReadRod(rootAddress, rod, playerPosition, out FishObservation? observation))
                continue;

            positionCount++;
            if (observation is not null)
                observations.Add(observation);
        }

        return (observations, positionCount, playerPositionRead);
    }

    private bool TryReadRod(ulong rootAddress, int rodIndex, (float X, float Y, float Z)? playerPosition, out FishObservation? observation)
    {
        observation = null;
        var key = (rootAddress, rodIndex);

        if (_recoveredRodChains.TryGetValue(key, out ulong[]? recoveredChain) &&
            TryReadRodWithChain(rootAddress, recoveredChain, rodIndex, playerPosition, out observation))
        {
            return true;
        }

        if (TryReadRodWithChain(rootAddress, Rf4Signature.RodChains[rodIndex], rodIndex, playerPosition, out observation))
            return true;

        // The game has no usable global-metadata.dat, so the exact field layout
        // can move between clients. Try a small, bounded neighbourhood around
        // the known read-only chain once per root/rod instead of scanning the
        // entire process.
        if (!_rodRecoveryAttempts.Add(key))
            return false;

        foreach (ulong[] candidate in BuildRodChainVariants(rodIndex))
        {
            if (!TryReadRodWithChain(rootAddress, candidate, rodIndex, playerPosition, out observation))
                continue;

            _recoveredRodChains[key] = candidate;
            return true;
        }

        return false;
    }

    private bool TryReadRodWithChain(ulong rootAddress, IReadOnlyList<ulong> rodChain, int rodIndex,
        (float X, float Y, float Z)? playerPosition, out FishObservation? observation)
    {
        observation = null;
        if (!_reader.TryResolveChain(rootAddress, rodChain, out ulong objectAddress))
            return false;

        // The source application treats a successful position read as the visible
        // fish transition. This also gives us a stable one-shot event for sound.
        if (!_reader.TryResolveChain(objectAddress, Rf4Signature.PositionChain, out ulong positionAddress) ||
            !_reader.TryReadVector3(positionAddress, out _))
            return false;

        string fishId = string.Empty;
        string rawName = string.Empty;
        if (_reader.TryResolveChain(objectAddress, Rf4Signature.FishNameChain, out ulong nameAddress))
            _reader.TryReadUtf8(nameAddress, 96, out rawName);

        if (string.IsNullOrWhiteSpace(rawName))
            rawName = "Неизвестная рыба";

        fishId = rawName.Trim();
        string fishName = _fishNames.TryGetValue(fishId, out string? translated) ? translated : rawName;

        double? weightKg = null;
        if (_reader.TryResolveChain(objectAddress, Rf4Signature.WeightChain, out ulong weightAddress) &&
            _reader.TryReadInt32(weightAddress, out int grams) && grams >= 0 && grams < 1_000_000)
        {
            weightKg = grams / Rf4Signature.GramsPerKilogram;
        }

        int? slot = null;
        if (_reader.TryResolveChain(objectAddress, Rf4Signature.SlotChain, out ulong slotAddress) &&
            _reader.TryReadInt32(slotAddress, out int slotValue) && slotValue is >= 0 and <= 16)
        {
            slot = slotValue;
        }

        bool isRare = false;
        if (_reader.TryResolveChain(objectAddress, Rf4Signature.RarityChain, out ulong rarityAddress) &&
            _reader.TryReadBytes(rarityAddress, 1, out byte[] rarityBytes))
        {
            isRare = rarityBytes[0] != 0;
        }

        if (_reader.TryResolveChain(objectAddress, Rf4Signature.StateChain, out ulong stateAddress) &&
            _reader.TryReadInt32(stateAddress, out int state) && string.IsNullOrWhiteSpace(fishName))
        {
            // Keep the state read in the same guarded path for builds where the
            // name pointer is unavailable. It is deliberately not used as a
            // rarity guess because RF4 state values are build-dependent.
            fishName = $"Рыба ({state.ToString()})";
        }

        double? distanceMeters = null;
        if (playerPosition is { } player &&
            _reader.TryResolveChain(objectAddress, Rf4Signature.PositionChain, out ulong distancePositionAddress) &&
            _reader.TryReadVector3(distancePositionAddress, out (float X, float Y, float Z) fishPosition))
        {
            // This mirrors the two-axis distance used by the observed overlay.
            double dx = fishPosition.X - player.X;
            double dy = fishPosition.Y - player.Y;
            distanceMeters = Math.Sqrt((dx * dx) + (2.0 * dy * dy));
        }

        observation = new FishObservation(
            rodIndex,
            IsActive: true,
            IsHooked: true,
            FishId: fishId,
            FishName: fishName,
            WeightKg: weightKg,
            DistanceMeters: distanceMeters,
            Slot: slot,
            IsRare: isRare,
            RawName: rawName);
        return true;
    }

    private static IEnumerable<ulong[]> BuildRodChainVariants(int rodIndex)
    {
        ulong[] original = Rf4Signature.RodChains[rodIndex];
        var yielded = new HashSet<string>(StringComparer.Ordinal);

        // The first hop is shared with the working player chain. Vary the
        // following fields only, keeping the search small and deterministic.
        int[] ranges = { 0, 0x100, 0x80, 0x80, 0x80 };
        for (int index = 1; index < original.Length; index++)
        {
            for (int delta = -ranges[index]; delta <= ranges[index]; delta += 8)
            {
                if (delta == 0)
                    continue;

                ulong[] candidate = (ulong[])original.Clone();
                long value = unchecked((long)original[index]) + delta;
                if (value < 0 || value > 0x400)
                    continue;

                candidate[index] = (ulong)value;
                string key = string.Join(",", candidate);
                if (yielded.Add(key))
                    yield return candidate;
            }
        }
    }

    private static Dictionary<string, string> LoadFishNames()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Core", "FishNames.json");
            if (!File.Exists(path))
                path = Path.Combine(AppContext.BaseDirectory, "FishNames.json");

            if (File.Exists(path))
            {
                var values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                if (values is not null)
                    return values;
            }
        }
        catch
        {
            // The reader can still show the raw fish identifier.
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public void Dispose() => _reader.Dispose();
}
