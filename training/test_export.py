import json
import os
os.environ["CUDA_VISIBLE_DEVICES"] = "-1"
os.environ["TF_CPP_MIN_LOG_LEVEL"] = "3"
from pathlib import Path
import struct
import tempfile
import unittest
import numpy as np
import tensorflow as tf
from train_sample import build_model, ENCODER, RULES, FEATURES, KINDS
from export_policy import export


class ExportTests(unittest.TestCase):
    def test_binary_weights_reproduce_both_policy_and_value_heads(self):
        with tempfile.TemporaryDirectory() as folder:
            source = Path(folder)
            models = {kind: build_model(kind, tf) for kind in KINDS}
            for kind, model in models.items():
                model.save(source / f"{kind}.keras")
            (source / "pair.json").write_text(json.dumps(dict(encoder_version=ENCODER, rules_version=RULES,
                feature_count=FEATURES, reward_scale=100, bidding="bidding.keras", playing="playing.keras")))
            output = source / "native"
            export(source, output, tf)
            x = np.random.default_rng(17).normal(size=(4, FEATURES)).astype(np.float32)
            for kind, (actions, width, _) in KINDS.items():
                data = (output / f"{kind}.bin").read_bytes()
                self.assertEqual(struct.unpack("<5i", data[:20]), (0x31504b53, FEATURES, width, width, actions))
                weights = np.frombuffer(data[20:], dtype="<f4")
                offset = 0
                def layer(inputs, outputs, values):
                    nonlocal offset
                    kernel = weights[offset:offset + inputs * outputs].reshape(outputs, inputs)
                    offset += inputs * outputs
                    bias = weights[offset:offset + outputs]
                    offset += outputs
                    return values @ kernel.T + bias
                hidden = np.maximum(layer(FEATURES, width, x), 0)
                hidden = np.maximum(layer(width, width, hidden), 0)
                actual = [layer(width, actions, hidden), layer(width, 1, hidden)]
                for a, b in zip(actual, models[kind](x), strict=True):
                    np.testing.assert_allclose(a, b.numpy(), atol=1e-5)
                self.assertEqual(offset, len(weights))
            with self.assertRaises(ValueError):
                export(source, output, tf)
            original = json.loads((output / "pair.json").read_text())
            manifest = json.loads((source / "pair.json").read_text())
            manifest["bidding_policy"] = "gameplay-v1"
            (source / "pair.json").write_text(json.dumps(manifest))
            export(source, source / "fixed", tf)
            fixed = json.loads((source / "fixed/pair.json").read_text())
            self.assertEqual(fixed["bidding_policy"], "gameplay-v1")
            self.assertEqual(fixed["playing_sha256"], original["playing_sha256"])
            self.assertNotEqual(fixed["pair_id"], original["pair_id"])


if __name__ == "__main__":
    unittest.main()
