using System.Collections.Generic;
using System.Linq;

namespace MassifVisualizer.Services.Detection;

public static class Statistics
{
    public static double Spearman(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        int n = x.Count;
        if (n < 3 || y.Count != n) return 0;

        var rx = Rank(x);
        var ry = Rank(y);

        double meanX = rx.Average();
        double meanY = ry.Average();

        double cov = 0, varX = 0, varY = 0;
        for (int i = 0; i < n; i++)
        {
            double dx = rx[i] - meanX;
            double dy = ry[i] - meanY;
            cov += dx * dy;
            varX += dx * dx;
            varY += dy * dy;
        }

        if (varX <= 0 || varY <= 0) return 0;
        return cov / System.Math.Sqrt(varX * varY);
    }

    private static double[] Rank(IReadOnlyList<double> values)
    {
        int n = values.Count;
        var order = Enumerable.Range(0, n).OrderBy(i => values[i]).ToArray();
        var ranks = new double[n];

        int idx = 0;
        while (idx < n)
        {
            int j = idx;
            while (j + 1 < n && values[order[j + 1]] == values[order[idx]])
                j++;

            double avgRank = (idx + j) / 2.0 + 1;
            for (int k = idx; k <= j; k++)
                ranks[order[k]] = avgRank;

            idx = j + 1;
        }

        return ranks;
    }

    public static double MaxDrawdown(IReadOnlyList<double> y, double peak)
    {
        if (peak <= 0) return 0;

        double runningMax = double.NegativeInfinity;
        double worst = 0;
        foreach (var v in y)
        {
            if (v > runningMax) runningMax = v;
            double drawdown = (runningMax - v) / peak;
            if (drawdown > worst) worst = drawdown;
        }

        return worst;
    }

    public static double[] Normalize(IReadOnlyList<long> times)
    {
        int n = times.Count;
        if (n == 0) return [];

        double t0 = times[0];
        double span = times[^1] - t0;

        if (span <= 0)
            return Enumerable.Range(0, n).Select(i => n == 1 ? 0.0 : i / (double)(n - 1)).ToArray();

        return times.Select(t => (t - t0) / span).ToArray();
    }
}
