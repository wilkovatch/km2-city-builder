using System;
using System.Collections.Generic;

class RandomManager {
    public static Random rnd = new Random();
    public static Dictionary<int, Random> rnds = new Dictionary<int, Random>();

    public static Random DetRandom(int seed = 0) {
        return new Random(seed);
    }

    public static Random CalculatorDetRandom(int seed = 0) {
        if (rnds.ContainsKey(seed)) return rnds[seed];
        var res = new Random(seed);
        rnds[seed] = res;
        return res;
    }
}
