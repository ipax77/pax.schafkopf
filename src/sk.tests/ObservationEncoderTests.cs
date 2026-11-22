using sk.bot;
using sk.shared;
using sk.training;
using System.Text.Json;

namespace sk.tests;

[TestClass]
public sealed class ObservationEncoderTests
{
    private static float[] Block(float[] values, string name)
    {
        var block = ObservationEncoder.Blocks.Single(b => b.Name == name);
        return values[block.Offset..(block.Offset + block.Length)];
    }

    [TestMethod]
    public void GoldenOpeningEncodingAndModelActionAdapters()
    {
        var game = new HeadlessGame(); game.Reset(42);
        var observation = game.Observe(0);
        var x = ObservationEncoder.Encode(observation);
        Assert.HasCount(1748, x);
        Assert.AreEqual(1748, ObservationEncoder.Blocks.Sum(b => b.Length));
        // Pinned fixture from the engine's seed-42 deal, including schema offsets.
        int[] hand = [2, 22, 7, 3, 10, 17, 9, 20];
        var golden = new float[1748];
        foreach (int id in hand) { golden[id] = 1; golden[32 + id] = 1; }
        foreach (int index in new[] { 65, 69, 76, 85, 86, 90, 98, 104, 105, 106, 107, 108, 117, 150, 183, 216, 379 }) golden[index] = 1;
        golden[383] = .25f; golden[385] = .25f; golden[387] = .5f;
        CollectionAssert.AreEqual(golden, x);
        CollectionAssert.AreEqual(hand.Order().ToArray(), Block(x, "hand").Select((v, i) => (v, i)).Where(p => p.v == 1).Select(p => p.i).ToArray());
        CollectionAssert.AreEqual(Block(x, "hand"), Block(x, "original_hand"));
        Assert.AreEqual(1f, x[65]); // Bidding1
        Assert.AreEqual(1f, x[69]); // no contract
        Assert.AreEqual(1f, x[85]); // dealer relative seat 3
        Assert.AreEqual(1f, x[98]); // absent declarer
        Assert.AreEqual(1f, x[104]); // absent partner
        CollectionAssert.AreEqual(new float[] { 1, 1, 1, 1 }, Block(x, "hand_counts"));
        Assert.IsTrue(Block(x, "bidding_history").All(v => v == 0));
        Assert.IsTrue(Block(x, "play_history").All(v => v == 0));
        Assert.AreEqual(3u, ObservationEncoder.LegalMask(observation));
        for (int i = 0; i < 17; i++) Assert.AreEqual(i, ObservationEncoder.EngineAction("bidding", i).Id);
        for (int i = 0; i < 32; i++) Assert.AreEqual(i + 17, ObservationEncoder.EngineAction("playing", i).Id);
        Assert.Throws<ArgumentOutOfRangeException>(() => ObservationEncoder.EngineAction("playing", 32));
        Assert.Throws<ArgumentException>(() => ObservationEncoder.LegalMask(observation with { LegalActions = [] }));
    }

    [TestMethod]
    public void RelativeSeatsAreInvariantUnderRotation()
    {
        var game = new HeadlessGame(); game.Reset(42);
        game.Step(1); game.Step(0); game.Step(0); game.Step(0); game.Step(6); game.Step(game.LegalActions()[0]);
        var observation = game.Observe(game.CurrentPlayer);
        int Shift(int seat) => (seat + 1) % 4;
        int[] Rotate(IReadOnlyList<int> values) => [values[3], values[0], values[1], values[2]];
        var rotated = observation with
        {
            Seat = Shift(observation.Seat), Dealer = Shift(observation.Dealer), ActivePlayer = Shift(observation.ActivePlayer),
            LeadingPlayer = Shift(observation.LeadingPlayer), Contract = observation.Contract! with { Player = Shift(observation.Contract.Player) },
            HandCounts = Rotate(observation.HandCounts), Points = Rotate(observation.Points), TrickCounts = Rotate(observation.TrickCounts),
            CurrentTrick = Rotate(observation.CurrentTrick), History = observation.History.Select(h => h with { Player = Shift(h.Player) }).ToArray()
        };
        CollectionAssert.AreEqual(ObservationEncoder.Encode(observation), ObservationEncoder.Encode(rotated));
    }

    [TestMethod]
    public void HiddenHandsNeverAffectFeaturesAndCardZeroDiffersFromMissing()
    {
        var game = new Game(); game.ResetEpisode(42);
        var before = ObservationEncoder.Encode(game.Observe(0));
        (game.Table.Players[1].Hand[0], game.Table.Players[2].Hand[0]) = (game.Table.Players[2].Hand[0], game.Table.Players[1].Hand[0]);
        CollectionAssert.AreEqual(before, ObservationEncoder.Encode(game.Observe(0)));
        var observation = game.Observe(0) with { CurrentTrick = [0, -1, -1, -1] };
        var trick = Block(ObservationEncoder.Encode(observation), "current_trick");
        Assert.AreEqual(0f, trick[0]); Assert.AreEqual(1f, trick[1]); Assert.AreEqual(1f, trick[33]);
    }

    [TestMethod]
    public void OriginalHandAndRufRoleUseOnlyOwnHandAndPublicPlays()
    {
        var game = new HeadlessGame(); game.Reset(42);
        int ace = GameAction.ToCardId(new Card { Suit = Suit.Gras, Rank = Rank.Ace });
        var observation = game.Observe(0) with
        {
            Contract = new(1, GameType.Ruf, Suit.Gras, false), PublicTeammate = -1,
            Hand = [0, 1], History = [new ActionEvent(0, GameAction.FromId(17 + ace))]
        };
        var x = ObservationEncoder.Encode(observation);
        Assert.AreEqual(1f, Block(x, "original_hand")[ace]);
        Assert.AreEqual(0f, Block(x, "hand")[ace]);
        Assert.AreEqual(1f, Block(x, "own_role")[2]);
        Assert.AreEqual(1f, Block(x, "public_partner")[4]); // own role does not expose a public partner
        Assert.AreEqual(1f, Block(ObservationEncoder.Encode(observation with { History = [] }), "own_role")[3]);
        Assert.AreEqual(1f, Block(ObservationEncoder.Encode(observation with { Contract = null, History = [] }), "own_role")[0]);
    }

    [TestMethod]
    public void DemonstrationsKeepSeedSplitsSeparateAndAttachTerminalSeatPayouts()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sk-imitation-test-" + Guid.NewGuid());
        try
        {
            ImitationData.Generate(directory, 24, 8, 77);
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "dataset.json")));
            var expected = new Dictionary<(string, string), Queue<(int Action, float Reward)>>();
            var seeds = new HashSet<ulong>();
            foreach (string split in new[] { "training", "validation" })
            {
                foreach (var line in File.ReadLines(Path.Combine(directory, split + "-traces.jsonl")))
                {
                    using var trace = JsonDocument.Parse(line);
                    var root = trace.RootElement;
                    ulong seed = root.GetProperty("seed").GetUInt64();
                    Assert.IsTrue(seeds.Add(seed), "Train/validation deal leakage");
                    var game = new HeadlessGame(); game.Reset(seed, root.GetProperty("dealer").GetInt32());
                    var rewards = root.GetProperty("rewards").EnumerateArray().Select(r => r.GetInt32()).ToArray();
                    foreach (int action in root.GetProperty("actions").EnumerateArray().Select(a => a.GetInt32()))
                    {
                        var o = game.Observe(game.CurrentPlayer);
                        var key = (split, ObservationEncoder.ModelKind(o));
                        if (!expected.TryGetValue(key, out var queue)) expected[key] = queue = new();
                        queue.Enqueue((ObservationEncoder.LocalAction(o, GameAction.FromId(action)), rewards[o.Seat] / 100f));
                        game.Step(action);
                    }
                    Assert.IsTrue(game.IsTerminal);
                }
            }
            foreach (var shard in manifest.RootElement.GetProperty("shards").EnumerateArray())
            {
                var queue = expected[(shard.GetProperty("split").GetString()!, shard.GetProperty("model").GetString()!)];
                using var reader = new BinaryReader(File.OpenRead(Path.Combine(directory, shard.GetProperty("file").GetString()!)));
                for (int i = 0; i < shard.GetProperty("rows").GetInt32(); i++)
                {
                    for (int feature = 0; feature < ObservationEncoder.FeatureCount; feature++) Assert.IsTrue(float.IsFinite(reader.ReadSingle()));
                    uint mask = reader.ReadUInt32(); int action = reader.ReadInt32(); float value = reader.ReadSingle();
                    var label = queue.Dequeue();
                    Assert.AreEqual(label.Action, action); Assert.AreEqual(label.Reward, value);
                    Assert.AreNotEqual(0u, mask & (1u << action));
                }
                Assert.AreEqual(reader.BaseStream.Length, reader.BaseStream.Position);
            }
            Assert.IsTrue(expected.Values.All(q => q.Count == 0));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
