"""Masked PPO objective and complete per-seat trajectory targets. No game rules live here."""
import numpy as np


def trajectory_targets(values, payout, gamma=1.0, lam=1.0):
    values = np.asarray(values, dtype=np.float32)
    if len(values) == 0 or not np.isfinite(values).all() or not np.isfinite(payout):
        raise ValueError("Invalid complete trajectory")
    rewards = np.zeros(len(values), dtype=np.float32)
    rewards[-1] = payout / 100.0
    terminal = np.zeros(len(values), dtype=bool)
    terminal[-1] = True
    advantages = np.empty_like(values)
    following_value = 0.0
    following_advantage = 0.0
    for i in reversed(range(len(values))):
        delta = rewards[i] + gamma * following_value - values[i]
        following_advantage = delta + gamma * lam * following_advantage
        advantages[i] = following_advantage
        following_value = values[i]
    return rewards, terminal, advantages, advantages + values


def normalize_advantages(advantages, choices):
    result = np.asarray(advantages, dtype=np.float32).copy()
    if np.any(choices):
        selected = result[choices]
        result[choices] = (selected - selected.mean()) / max(float(selected.std()), 1e-8)
    return result


def masked_log_probs(logits, mask, tf):
    tf.debugging.assert_all_finite(logits, "Nonfinite policy logits")
    tf.debugging.assert_equal(tf.reduce_all(tf.reduce_any(mask, axis=1)), True, "Empty action mask")
    return tf.nn.log_softmax(tf.where(mask, logits, tf.constant(-1e9, logits.dtype)), axis=1)


def ppo_terms(logits, predicted, mask, actions, old_log_probs, advantages, returns, tf,
              clip=0.2, entropy_coefficient=0.01, value_coefficient=0.5):
    # Forced-only batches have a zero policy denominator: reject corrupt inputs before
    # divide_no_nan can legitimately turn the policy contribution into zero.
    for value in (predicted, old_log_probs, advantages, returns):
        tf.debugging.assert_all_finite(value, "Nonfinite PPO input")
    log_probs = masked_log_probs(logits, mask, tf)
    tf.debugging.assert_equal(tf.reduce_all(tf.gather(mask, actions, axis=1, batch_dims=1)), True, "Illegal rollout action")
    selected = tf.gather(log_probs, actions, axis=1, batch_dims=1)
    log_ratio = selected - old_log_probs
    ratio = tf.exp(log_ratio)
    choices = tf.cast(tf.reduce_sum(tf.cast(mask, tf.int32), axis=1) > 1, tf.float32)
    count = tf.reduce_sum(choices)
    def choice_mean(value):
        return tf.math.divide_no_nan(tf.reduce_sum(value * choices), count)
    surrogate = tf.minimum(ratio * advantages, tf.clip_by_value(ratio, 1 - clip, 1 + clip) * advantages)
    policy_loss = -choice_mean(surrogate)
    entropy = choice_mean(-tf.reduce_sum(tf.exp(log_probs) * log_probs, axis=1))
    value_error = tf.square(tf.squeeze(predicted, axis=1) - returns)
    value_loss = tf.reduce_mean(value_error)
    loss = policy_loss + value_coefficient * value_loss - entropy_coefficient * entropy
    kl = choice_mean((ratio - 1) - log_ratio)
    clip_fraction = choice_mean(tf.cast(tf.abs(ratio - 1) > clip, tf.float32))
    # Sums support correct aggregation across short/forced-only minibatches.
    totals = tf.stack([policy_loss * count, entropy * count, kl * count, clip_fraction * count,
                       count, tf.reduce_sum(value_error), tf.cast(tf.shape(actions)[0], tf.float32)])
    tf.debugging.assert_all_finite(loss, "Nonfinite PPO loss")
    tf.debugging.assert_all_finite(totals, "Nonfinite PPO statistics")
    return loss, totals


def summarize(totals):
    policy, entropy, kl, clipped, choices, value_error, rows = map(float, totals)
    return {"policy_loss": policy / max(choices, 1), "entropy": entropy / max(choices, 1),
            "approx_kl": kl / max(choices, 1), "clip_fraction": clipped / max(choices, 1),
            "value_mse": value_error / max(rows, 1), "rows": int(rows), "choice_rows": int(choices),
            "forced_fraction": 1 - choices / max(rows, 1)}
