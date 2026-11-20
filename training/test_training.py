"""Numerical checks for the imitation objective (run with the pinned training Python)."""
import os
os.environ["CUDA_VISIBLE_DEVICES"] = "-1"
os.environ["TF_CPP_MIN_LOG_LEVEL"] = "3"
import unittest
import numpy as np
import tensorflow as tf
from train_sample import loss_terms


class ObjectiveTests(unittest.TestCase):
    def test_illegal_logits_and_forced_moves_do_not_change_policy_loss(self):
        logits = tf.Variable([[1., 2., 1000.], [-5., 1000., -1000.]])
        mask = tf.constant([[True, True, False], [True, False, False]])
        with tf.GradientTape() as tape:
            loss, totals = loss_terms(logits, tf.zeros((2, 1)), mask, tf.constant([1, 0]), tf.constant([.6, -.6]), tf)
        gradients = tape.gradient(loss, logits).numpy()
        expected_ce = np.log1p(np.exp(-1))
        self.assertAlmostEqual(float(totals[0]), expected_ce, places=6)
        self.assertAlmostEqual(float(loss), expected_ce + .5 * .36, places=6)
        np.testing.assert_array_equal(gradients[:, 2], [0, 0])
        np.testing.assert_array_equal(gradients[1], [0, 0, 0])
        self.assertEqual(float(totals[2]), 1)

    def test_empty_mask_and_illegal_label_fail(self):
        for mask, label in [([[False, False]], 0), ([[True, False]], 1)]:
            with self.assertRaises(tf.errors.InvalidArgumentError):
                loss_terms(tf.zeros((1, 2)), tf.zeros((1, 1)), tf.constant(mask), tf.constant([label]), tf.zeros(1), tf)

    def test_all_forced_batch_still_trains_value(self):
        loss, totals = loss_terms(tf.zeros((1, 2)), tf.constant([[.2]]), tf.constant([[True, False]]),
                                 tf.constant([0]), tf.constant([.6]), tf)
        self.assertAlmostEqual(float(loss), .5 * .4**2, places=6)
        self.assertEqual(float(totals[2]), 0)


if __name__ == "__main__":
    unittest.main()
