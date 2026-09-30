// Behavioural harness for PlacementMatcher — the pure logic that decides whether a sign spawn is
// the local player's own fresh placement. Run it with
//
//     dotnet run --project tests/placement-harness
//
// It prints one line per assertion and exits non-zero on any failure. It is NOT part of the mod
// build (see the .csproj for why it can exist at all) and NOT a gate — nothing runs it
// automatically. Run it after touching PlacementMatcher.cs, before the build.
//
// Every check builds its own PlacementMatcher (window 3s, the default), so no state leaks between
// checks — except where a single row's own "Calls" column chains several calls on one instance,
// which is the behaviour under test there (e.g. re-placing on the same tile, or two placements
// live at once).
using System;
using SignLabels;

internal static class Harness
{
    private static int _pass,
        _fail;

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok)
        {
            _pass++;
            Console.WriteLine("  PASS  " + name + (detail.Length > 0 ? "  (" + detail + ")" : ""));
        }
        else
        {
            _fail++;
            Console.WriteLine("  FAIL  " + name + "  " + detail);
        }
    }

    private static void Main()
    {
        {
            var m = new PlacementMatcher();
            bool recorded = m.Observe(100, 5, 7, 0);
            Check("first_observation_records", recorded);
        }

        {
            var m = new PlacementMatcher();
            m.Observe(100, 5, 7, 0);
            bool second = m.Observe(100, 5, 7, 1);
            Check("same_start_tick_is_not_a_new_placement", !second, "second call returned " + second);
        }

        {
            var m = new PlacementMatcher();
            bool recorded = m.Observe(0, 5, 7, 0);
            bool consumed = m.TryConsume(5, 7, 0.5f);
            Check("zero_tick_records_nothing", !recorded && !consumed, "recorded=" + recorded + " consumed=" + consumed);
        }

        {
            var m = new PlacementMatcher();
            m.Observe(100, 5, 7, 0);
            bool consumed = m.TryConsume(5, 7, 2.9f);
            Check("consume_within_window", consumed);
        }

        {
            var m = new PlacementMatcher();
            m.Observe(100, 5, 7, 0);
            bool consumed = m.TryConsume(5, 7, 3.1f);
            Check("consume_after_window", !consumed, "consumed=" + consumed);
        }

        {
            var m = new PlacementMatcher();
            m.Observe(100, 5, 7, 0);
            bool consumed = m.TryConsume(5, 8, 1);
            Check("consume_on_other_tile", !consumed, "consumed=" + consumed);
        }

        {
            var m = new PlacementMatcher();
            m.Observe(100, 5, 7, 0);
            bool first = m.TryConsume(5, 7, 1);
            bool second = m.TryConsume(5, 7, 1.5f);
            Check("consume_only_once", first && !second, "first=" + first + " second=" + second);
        }

        {
            var m = new PlacementMatcher();
            bool consumed = m.TryConsume(5, 7, 0);
            Check("consume_requires_a_recorded_placement", !consumed, "consumed=" + consumed);
        }

        {
            var m = new PlacementMatcher();
            bool a = m.Observe(100, 5, 7, 0);
            bool b = m.TryConsume(5, 7, 1);
            bool c = m.Observe(160, 5, 7, 2);
            bool d = m.TryConsume(5, 7, 2.5f);
            Check("new_start_tick_on_same_tile_records_again", a && b && c && d, "a=" + a + " b=" + b + " c=" + c + " d=" + d);
        }

        {
            var m = new PlacementMatcher();
            m.Observe(100, 1, 1, 0);
            m.Observe(160, 2, 2, 0.5f);
            bool first = m.TryConsume(1, 1, 1);
            bool second = m.TryConsume(2, 2, 1);
            Check("two_placements_both_consumable", first && second, "first=" + first + " second=" + second);
        }

        Console.WriteLine();
        Console.WriteLine(_pass + " passed, " + _fail + " failed");
        Environment.Exit(_fail == 0 ? 0 : 1);
    }
}
