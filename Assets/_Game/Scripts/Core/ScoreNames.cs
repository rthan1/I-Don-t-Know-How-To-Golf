// Golf names for a hole score compared to par: birdie, bogey, nonuple bogey, and so on.
public static class ScoreNames
{
    // Index = strokes over par (0 = par, 1 = bogey, ... 12 = duodecuple bogey).
    private static readonly string[] OverPar =
    {
        "PAR", "BOGEY", "DOUBLE BOGEY", "TRIPLE BOGEY", "QUADRUPLE BOGEY", "QUINTUPLE BOGEY",
        "SEXTUPLE BOGEY", "SEPTUPLE BOGEY", "OCTUPLE BOGEY", "NONUPLE BOGEY", "DECUPLE BOGEY",
        "UNDECUPLE BOGEY", "DUODECUPLE BOGEY",
    };

    // The name for a score this far from par (negative = under par).
    public static string ForToPar(int toPar)
    {
        switch (toPar)
        {
            case -4: return "CONDOR";
            case -3: return "ALBATROSS";
            case -2: return "EAGLE";
            case -1: return "BIRDIE";
        }

        if (toPar < -4) return $"{-toPar} UNDER PAR";
        if (toPar < OverPar.Length) return OverPar[toPar];
        return $"{toPar}-TUPLE BOGEY"; // 13 over and beyond
    }

    // Big headline for the end-of-hole screen. Under-par scores get an exclamation mark.
    public static string Headline(int strokes, int par)
    {
        if (strokes == 1) return "HOLE IN ONE!";

        int toPar = strokes - par;
        return ForToPar(toPar) + (toPar < 0 ? "!" : "");
    }
}
