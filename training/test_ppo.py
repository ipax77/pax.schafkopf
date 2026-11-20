"""Hand-calculated PPO and per-seat trajectory regression tests."""
import os
os.environ["CUDA_VISIBLE_DEVICES"] = "-1"
os.environ["TF_CPP_MIN_LOG_LEVEL"] = "3"
import json
from pathlib import Path
import tempfile
import unittest
import numpy as np
import tensorflow as tf
from ppo_math import trajectory_targets, normalize_advantages, ppo_terms, summarize
from ppo_rollout import Trajectories
from train_ppo import checkpoint, version_pair, step_functions, compare_evaluations
from train_sample import FEATURES, KINDS, build_model


class PpoTests(unittest.TestCase):
    def test_terminal_payout_has_monte_carlo_return_at_every_own_decision(self):
        rewards, terminal, advantage, returns = trajectory_targets([.1, .3, -.2], 60)
        np.testing.assert_allclose(rewards, [0, 0, .6])
        np.testing.assert_array_equal(terminal, [False, False, True])
        np.testing.assert_allclose(advantage, [.5, .3, .8], atol=1e-7)
        np.testing.assert_allclose(returns, [.6, .6, .6], atol=1e-7)
        _, _, all_pass_advantage, all_pass_returns = trajectory_targets([.3], 0)
        np.testing.assert_allclose(all_pass_returns, [0])
        np.testing.assert_allclose(all_pass_advantage, [-.3])

    def test_terminal_labels_group_seats_and_exclude_nonlearner_decisions(self):
        buffer = Trajectories("bidding")
        def request(decision, seat, learner=True, model="bidding"):
            return dict(episode_id="7", decision=decision, seat=seat, phase=1, learner=learner, model=model)
        x = np.zeros(FEATURES, np.float32)
        mask = np.array([True, True] + [False] * 15)
        buffer.add(request(0, 0), x, mask, 0, -.69, .1)  # passes before somebody else declares
        buffer.add(request(1, 1, False), x, mask, 1, -.69, .2)
        buffer.add(request(2, 2), x, mask, 1, -.69, .3)
        buffer.add(request(5, 2), x, mask, 1, -.69, .4)
        buffer.add(request(6, 3, False, "playing"), x, mask, 0, 0, .5)
        buffer.finish("7", [60, -60, 60, -60])
        data = buffer.arrays()
        np.testing.assert_array_equal(data["seat"], [0, 2, 2])
        np.testing.assert_allclose(data["returns"], [.6, .6, .6], atol=1e-7)
        np.testing.assert_allclose(data["reward"], [.6, 0, .6])
        np.testing.assert_array_equal(data["terminal"], [True, False, True])
        with self.assertRaises(ValueError):
            buffer.finish("7", [60, -60, 60, -60])

    def test_advantage_normalization_ignores_forced_rows(self):
        actual = normalize_advantages(np.array([1., 3., 1000.]), np.array([True, True, False]))
        np.testing.assert_allclose(actual, [-1, 1, 1000])

    def test_ppo_clips_positive_and_negative_advantages_in_correct_direction(self):
        logits = tf.Variable(np.log([[.9, .1], [.1, .9]]).astype(np.float32))
        with tf.GradientTape() as tape:
            loss, totals = ppo_terms(logits, tf.zeros((2, 1)), tf.constant([[True, True], [True, True]]),
                tf.constant([0, 0]), tf.constant(np.log([.5, .5]).astype(np.float32)), tf.constant([2., -2.]),
                tf.zeros(2), tf, entropy_coefficient=0)
        self.assertAlmostEqual(float(loss), -.4, places=6)
        self.assertAlmostEqual(summarize(totals.numpy())["clip_fraction"], 1.)
        np.testing.assert_allclose(tape.gradient(loss, logits).numpy(), 0, atol=1e-7)

    def test_illegal_logits_and_forced_actions_have_no_policy_gradient(self):
        logits = tf.Variable([[1., 2., 1000.], [-5., 1000., 0.]])
        mask = tf.constant([[True, True, False], [True, False, False]])
        with tf.GradientTape() as tape:
            loss, totals = ppo_terms(logits, tf.zeros((2, 1)), mask, tf.constant([1, 0]),
                tf.constant([-np.log1p(np.exp(-1)), 0.], tf.float32), tf.constant([1., 999.]), tf.constant([.6, -.6]), tf)
        gradients = tape.gradient(loss, logits).numpy()
        np.testing.assert_array_equal(gradients[:, 2], [0, 0])
        np.testing.assert_array_equal(gradients[1], [0, 0, 0])
        self.assertEqual(summarize(totals.numpy())["choice_rows"], 1)
        self.assertTrue(np.isfinite(float(loss)))

    def test_forced_only_batches_train_value_without_policy_entropy_or_kl(self):
        loss, totals = ppo_terms(tf.zeros((1, 2)), tf.constant([[.2]]), tf.constant([[True, False]]),
            tf.constant([0]), tf.zeros(1), tf.constant([999.]), tf.constant([.6]), tf)
        metrics = summarize(totals.numpy())
        self.assertAlmostEqual(float(loss), .08, places=6)
        for key in ("policy_loss", "entropy", "approx_kl", "clip_fraction"):
            self.assertEqual(metrics[key], 0)

    def test_invalid_masks_and_nonfinite_statistics_fail(self):
        for mask, old in [([[False, False]], 0.), ([[True, False]], float("nan"))]:
            with self.assertRaises(tf.errors.InvalidArgumentError):
                ppo_terms(tf.zeros((1, 2)), tf.zeros((1, 1)), tf.constant(mask), tf.constant([0]),
                          tf.constant([old]), tf.ones(1), tf.ones(1), tf)

    def test_checkpoint_restores_rng_weights_and_adam_for_identical_next_update(self):
        config = dict(clip=.2, entropy_coefficient=.01, value_coefficient=.5)
        models = {kind: build_model(kind, tf) for kind in KINDS}
        x = np.ones((2, FEATURES), np.float32)
        mask = np.zeros((2, 32), bool); mask[:, :2] = True
        data = (x, mask, np.array([0, 1], np.int32), np.full(2, -np.log(2), np.float32),
                np.array([1, -1], np.float32), np.array([.6, -.6], np.float32))
        train, _ = step_functions(models["playing"], config, tf)
        train(*data)
        frozen = version_pair(models)["bidding"]
        rng = np.random.default_rng(51)
        rng.random(3)
        with tempfile.TemporaryDirectory() as folder:
            run = Path(folder)
            directory = checkpoint(run, models, dict(update=1, next_index=1007, total_decisions=2), rng, config, tf)
            state = json.loads((directory / "state.json").read_text())
            restored_rng = np.random.default_rng(); restored_rng.bit_generator.state = state["rng_state"]
            np.testing.assert_array_equal(rng.random(20), restored_rng.random(20))
            restored = tf.keras.models.load_model(directory / "playing.keras")
            restored_train, _ = step_functions(restored, config, tf)
            train(*data); restored_train(*data)
            for expected, actual in zip(models["playing"].get_weights(), restored.get_weights(), strict=True):
                np.testing.assert_allclose(expected, actual, atol=1e-7)
            self.assertEqual(version_pair(models)["bidding"], frozen)
            self.assertEqual(state["next_index"], 1007)
            self.assertEqual(state["optimizer_steps"]["playing"], 1)

    def test_paired_evaluation_uses_seed_blocks(self):
        report = dict(master_seed=1, start_index=2, seeds=2, blocks=[dict(candidate=10), dict(candidate=-5)])
        self.assertEqual(compare_evaluations(report, report)["mean"], 0)
        self.assertEqual(compare_evaluations(report, report)["independent_seeds"], 2)


if __name__ == "__main__":
    unittest.main()
