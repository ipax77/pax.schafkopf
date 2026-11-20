using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using sk.api.Services;
using sk.bot;
using sk.shared;

namespace sk.tests;

[TestClass]
public sealed class NeuralPolicyTests
{
    internal sealed class Fixture : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "schafkopf-native-" + Guid.NewGuid().ToString("N"));
        public string Manifest => Path.Combine(DirectoryPath, "pair.json");
        public Fixture()
        {
            Directory.CreateDirectory(DirectoryPath);
            string Write(string name, int width, int actions)
            {
                var path = Path.Combine(DirectoryPath, name);
                using (var writer = new BinaryWriter(File.Create(path)))
                {
                    foreach (int value in new[] { 0x31504b53, ObservationEncoder.FeatureCount, width, width, actions }) writer.Write(value);
                    void Layer(int inputs, int outputs, float multiplier, float bias)
                    {
                        for (int o = 0; o < outputs; o++)
                            for (int i = 0; i < inputs; i++) writer.Write(i == 0 ? multiplier * (o + 1) : 0);
                        for (int o = 0; o < outputs; o++) writer.Write(bias);
                    }
                    Layer(ObservationEncoder.FeatureCount, width, 1, .25f);
                    Layer(width, width, 2, .5f);
                    Layer(width, actions, .1f, 0);
                    Layer(width, 1, .3f, -.1f);
                }
                return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            }
            string bidHash = Write("bidding.bin", 128, 17), playHash = Write("playing.bin", 256, 32);
            File.WriteAllText(Manifest, JsonSerializer.Serialize(new
            {
                schema_version = 1, native_format = NeuralPolicy.Format, encoder_version = ObservationEncoder.Version,
                rules_version = HeadlessGame.RulesVersion, feature_count = ObservationEncoder.FeatureCount,
                reward_scale = 100, selection = "masked-argmax", pair_id = "test-pair",
                bidding = "bidding.bin", playing = "playing.bin", bidding_sha256 = bidHash, playing_sha256 = playHash
            }));
        }
        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }

    [TestMethod]
    public void NativeForwardMatchesHandCalculatedDenseReluNetwork()
    {
        using var fixture = new Fixture();
        var policy = new NeuralPolicy(fixture.Manifest);
        var features = new float[ObservationEncoder.FeatureCount]; features[0] = 1;
        foreach (var kind in new[] { "bidding", "playing" })
        {
            var (logits, value) = policy.Predict(kind, features);
            for (int i = 0; i < logits.Length; i++) Assert.AreEqual(.3f * (i + 1), logits[i], 1e-5f);
            Assert.AreEqual(.8f, value, 1e-5f);
        }
        features[0] = float.NaN;
        Assert.Throws<ArgumentException>(() => policy.Predict("playing", features));
        Assert.Throws<ArgumentException>(() => policy.Predict("playing", new float[2]));
    }

    [TestMethod]
    public void NativePolicyMasksActionsAndKeepsLoadedPairImmutable()
    {
        using var fixture = new Fixture();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Bots:ModelManifest"] = fixture.Manifest }).Build();
        var policy = new ConfiguredBotPolicy(configuration, NullLogger<ConfiguredBotPolicy>.Instance);
        Assert.IsTrue(policy.Trained);
        File.WriteAllText(Path.Combine(fixture.DirectoryPath, "playing.bin"), "changed on disk");
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var game = new HeadlessGame(); game.Reset(seed);
            int decisions = 0;
            while (!game.IsTerminal)
            {
                var observation = game.Observe(game.CurrentPlayer);
                var action = policy.ChooseAction(observation);
                Assert.Contains(action, observation.LegalActions);
                var result = game.Step(action);
                if (result.Terminal) Assert.AreEqual(0, result.Rewards.Sum());
                Assert.IsLessThanOrEqualTo(40, ++decisions);
            }
        }
        Assert.AreEqual(0L, policy.FallbackCount);
        Assert.Throws<InvalidDataException>(() => new NeuralPolicy(fixture.Manifest));
    }

    [TestMethod]
    public void NativeLoadRejectsWrongEncoderAndTamperedWeights()
    {
        using var fixture = new Fixture();
        string original = File.ReadAllText(fixture.Manifest);
        File.WriteAllText(fixture.Manifest, original.Replace(ObservationEncoder.Version, "another-encoder"));
        Assert.Throws<InvalidDataException>(() => new NeuralPolicy(fixture.Manifest));
        File.WriteAllText(fixture.Manifest, original);
        string weights = Path.Combine(fixture.DirectoryPath, "bidding.bin");
        var bytes = File.ReadAllBytes(weights); bytes[^1] ^= 1; File.WriteAllBytes(weights, bytes);
        Assert.Throws<InvalidDataException>(() => new NeuralPolicy(fixture.Manifest));
    }
}
