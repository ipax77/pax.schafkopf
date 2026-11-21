using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using sk.shared;

namespace sk.bot;

/// <summary>Immutable CPU inference for the trainer's two-Dense-ReLU policy/value networks.
/// Only private encoded observations enter inference. No game rules or training dependencies.</summary>
public sealed class NeuralPolicy : IGamePolicy
{
    public const string Format = "dense-f32-v1";
    private sealed record Manifest(int SchemaVersion, string NativeFormat, string EncoderVersion, string RulesVersion,
        int FeatureCount, int RewardScale, string Selection, string PairId,
        string Bidding, string Playing, string BiddingSha256, string PlayingSha256, string BiddingPolicy = "neural");
    private sealed record Network(int Width, int Actions, float[] First, float[] FirstBias,
        float[] Second, float[] SecondBias, float[] Policy, float[] PolicyBias, float[] Value, float ValueBias);
    private readonly Network bidding, playing;
    private readonly IGamePolicy? fixedBidder;
    public string Id { get; }

    public NeuralPolicy(string manifestPath)
    {
        var path = Path.GetFullPath(manifestPath);
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Oversized neural manifest");
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), new JsonSerializerOptions
            { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }) ?? throw new InvalidDataException("Missing neural manifest");
        if (manifest.SchemaVersion != 1 || manifest.NativeFormat != Format || manifest.EncoderVersion != ObservationEncoder.Version ||
            manifest.RulesVersion != HeadlessGame.RulesVersion || manifest.FeatureCount != ObservationEncoder.FeatureCount ||
            manifest.RewardScale != 100 || manifest.Selection != "masked-argmax" || string.IsNullOrWhiteSpace(manifest.PairId))
            throw new InvalidDataException("Incompatible neural policy format, rules, encoder or action selection");
        var directory = Path.GetDirectoryName(path)!;
        fixedBidder = manifest.BiddingPolicy switch
        {
            "neural" => null,
            GameplayPolicy.Version => new GameplayPolicy(),
            "heuristic-v1/conservative" => new HeuristicPolicy(BiddingStyle.Conservative),
            _ => throw new InvalidDataException("Unsupported fixed bidding policy version")
        };
        bidding = Load(directory, manifest.Bidding, manifest.BiddingSha256, 128, 17);
        playing = Load(directory, manifest.Playing, manifest.PlayingSha256, 256, 32);
        Id = $"neural/{manifest.PairId}";
    }

    private static Network Load(string directory, string name, string expectedHash, int width, int actions)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name || name.Contains('\\') || name.Contains('/'))
            throw new InvalidDataException("Network must be a file beside its manifest");
        string path = Path.Combine(directory, name);
        int count = ObservationEncoder.FeatureCount * width + width + width * width + width + width * actions + actions + width + 1;
        if (new FileInfo(path).Length != 20L + 4L * count) throw new InvalidDataException("Incorrect network length");
        byte[] bytes = File.ReadAllBytes(path);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Neural network checksum mismatch");
        using var reader = new BinaryReader(new MemoryStream(bytes));
        if (reader.ReadInt32() != 0x31504b53 || reader.ReadInt32() != ObservationEncoder.FeatureCount ||
            reader.ReadInt32() != width || reader.ReadInt32() != width || reader.ReadInt32() != actions)
            throw new InvalidDataException("Incorrect neural network dimensions");
        float[] Read(int length)
        {
            var result = new float[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = reader.ReadSingle();
                if (!float.IsFinite(result[i])) throw new InvalidDataException("Nonfinite neural weight");
            }
            return result;
        }
        return new(width, actions, Read(width * ObservationEncoder.FeatureCount), Read(width), Read(width * width),
            Read(width), Read(actions * width), Read(actions), Read(width), Read(1)[0]);
    }

    private static float Dot(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        var sum = Vector<float>.Zero;
        int i = 0, size = Vector<float>.Count;
        for (; i <= left.Length - size; i += size)
            sum += new Vector<float>(left.Slice(i, size)) * new Vector<float>(right.Slice(i, size));
        float result = Vector.Sum(sum);
        for (; i < left.Length; i++) result += left[i] * right[i];
        return result;
    }

    private static void Layer(ReadOnlySpan<float> input, float[] kernel, float[] bias, Span<float> output, bool relu)
    {
        for (int i = 0; i < output.Length; i++)
        {
            float value = bias[i] + Dot(input, kernel.AsSpan(i * input.Length, input.Length));
            if (!float.IsFinite(value)) throw new InvalidOperationException("Nonfinite neural output");
            output[i] = relu ? Math.Max(0, value) : value;
        }
    }

    public (float[] Logits, float Value) Predict(string model, ReadOnlySpan<float> features)
    {
        var network = model switch { "bidding" => bidding, "playing" => playing, _ => throw new ArgumentException("Unknown model") };
        if (features.Length != ObservationEncoder.FeatureCount) throw new ArgumentException("Incorrect feature count");
        foreach (float feature in features) if (!float.IsFinite(feature)) throw new ArgumentException("Nonfinite feature");
        Span<float> first = stackalloc float[network.Width], second = stackalloc float[network.Width];
        Layer(features, network.First, network.FirstBias, first, true);
        Layer(first, network.Second, network.SecondBias, second, true);
        var logits = new float[network.Actions];
        Layer(second, network.Policy, network.PolicyBias, logits, false);
        float value = Dot(second, network.Value) + network.ValueBias;
        if (!float.IsFinite(value)) throw new InvalidOperationException("Nonfinite value output");
        return (logits, value);
    }

    public GameAction ChooseAction(GameObservation observation)
    {
        if (observation.ActivePlayer != observation.Seat || observation.LegalActions.Count == 0)
            throw new InvalidOperationException("A policy needs an active, nonterminal observation");
        if (observation.LegalActions.Count == 1) return observation.LegalActions[0];
        if (fixedBidder != null && observation.Phase != GameState.Playing) return fixedBidder.ChooseAction(observation);
        var (logits, _) = Predict(ObservationEncoder.ModelKind(observation), ObservationEncoder.Encode(observation));
        // Use only the engine's legal actions, with deterministic lowest-ID tie breaking.
        return observation.LegalActions.OrderByDescending(a => logits[ObservationEncoder.LocalAction(observation, a)])
            .ThenBy(a => a.Id).First();
    }
}
