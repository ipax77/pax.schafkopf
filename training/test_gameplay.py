import copy
import tempfile
from pathlib import Path
import unittest
from gameplay_eval import panels, aggregate, compare_panels, weighted_estimate
from ppo_run import select_best


class GameplayEvaluationTests(unittest.TestCase):
    def reports(self):
        specs = panels(3000000)
        reports = {s["name"]: dict(master_seed=7, start_index=s["eval_start_index"], seeds=2,
            bidder="gameplay", opponent=s["eval_opponent"], contract_action=s["eval_contract"],
            neural_seats=s["eval_neural_seats"], completed_games=64,
            blocks=[dict(index=s["eval_start_index"] + i, candidate=float(i), reference=0., difference=float(i)) for i in range(2)])
            for s in specs}
        return aggregate(reports, specs)

    def test_weights_balance_contract_families_and_include_three_bot_tables(self):
        specs = panels(3000000)
        self.assertAlmostEqual(sum(s["weight"] for s in specs), 1)
        for family in ("ruf", "wenz", "solo"):
            self.assertAlmostEqual(sum(s["weight"] for s in specs if s["family"] == family), 1 / 6)
        self.assertEqual(sum(s["eval_neural_seats"] == 3 for s in specs), 2)
        result = self.reports()
        self.assertAlmostEqual(result["candidate_payout"]["mean"], .5)
        gain, families = compare_panels(result, result)
        self.assertEqual(gain["mean"], 0)
        self.assertTrue(all(f["lower95"] == 0 for f in families.values()))

    def test_comparison_preserves_pairs_and_detects_changed_panels(self):
        baseline = self.reports()
        current = copy.deepcopy(baseline)
        for report in current["panels"].values():
            for block in report["blocks"]:
                block["candidate"] += 2
        gain, _ = compare_panels(baseline, current)
        self.assertAlmostEqual(gain["mean"], 2)
        self.assertAlmostEqual(gain["lower95"], 2)
        next(iter(current["panels"].values()))["blocks"][0]["index"] += 1
        with self.assertRaises(ValueError):
            compare_panels(baseline, current)

    def test_overlapping_panels_and_missing_weights_fail(self):
        report = self.reports()
        values = list(report["panels"].values())
        values[1]["blocks"][0]["index"] = values[0]["blocks"][0]["index"]
        with self.assertRaises(ValueError):
            aggregate(report["panels"], report["panel_specs"])
        with self.assertRaises(ValueError):
            weighted_estimate({"x": [0, 1]}, {"x": .5})

    def test_contract_regression_blocks_checkpoint_selection_even_with_higher_score(self):
        with tempfile.TemporaryDirectory() as folder:
            run = Path(folder)
            self.assertTrue(select_best(run, run / "initial", dict(ppo_update=0, paired_improvement=dict(mean=0))))
            self.assertFalse(select_best(run, run / "candidate", dict(ppo_update=1, paired_improvement=dict(mean=100), selection_eligible=False)))
            self.assertIn("initial", (run / "best.json").read_text())


if __name__ == "__main__":
    unittest.main()
