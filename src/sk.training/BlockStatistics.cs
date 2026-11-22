namespace sk.training;

public sealed record Estimate(int IndependentSeeds, double Mean, double Lower95, double Upper95);

public static class BlockStatistics
{
    public static Estimate Bootstrap(IReadOnlyList<double> seedMeans, int samples, ulong seed)
    {
        if (seedMeans.Count < 2 || seedMeans.Any(x => !double.IsFinite(x)))
            throw new ArgumentException("At least two finite base-seed means are required.");
        if (samples < 100) throw new ArgumentOutOfRangeException(nameof(samples), "Use at least 100 bootstrap resamples.");
        var random = new StableRandom(seed);
        var means = new double[samples];
        for (int i = 0; i < samples; i++)
        {
            double sum = 0;
            for (int j = 0; j < seedMeans.Count; j++) sum += seedMeans[random.Next(seedMeans.Count)];
            means[i] = sum / seedMeans.Count;
        }
        Array.Sort(means);
        double Quantile(double fraction)
        {
            double position = (samples - 1) * fraction;
            int lower = (int)position;
            return means[lower] + (means[Math.Min(lower + 1, samples - 1)] - means[lower]) * (position - lower);
        }
        return new(seedMeans.Count, seedMeans.Average(), Quantile(0.025), Quantile(0.975));
    }
}
