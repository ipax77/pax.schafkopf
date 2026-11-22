"""Predeclared, disjoint validation panels. Guidance counts never enter PPO rewards."""
import numpy as np


def panels(start):
    specs = []
    for opponent in ("conservative", "balanced", "gameplay"):
        specs.append((f"natural-{opponent}", "natural", 1 / 12, opponent, None, 1))
    for opponent in ("conservative", "gameplay"):
        specs.append((f"three-bots-{opponent}", "three-bots", 1 / 8, opponent, None, 3))
    for family, actions in (("ruf", (3, 4, 5)), ("wenz", (6,)), ("solo", (8, 10, 12, 14))):
        for action in actions:
            specs.append((f"{family}-{action}", family, 1 / (6 * len(actions)), "conservative", action, 1))
    return [dict(name=name, family=family, weight=weight, eval_opponent=opponent,
                 eval_contract=action, eval_neural_seats=seats, eval_start_index=start + i * 10000)
            for i, (name, family, weight, opponent, action, seats) in enumerate(specs)]


def weighted_estimate(samples, weights):
    """Bootstrap independent seed blocks within each disjoint panel, retaining fixed panel weights."""
    if not samples or set(samples) != set(weights) or not np.isclose(sum(weights.values()), 1):
        raise ValueError("Invalid panel weights")
    rng = np.random.default_rng(712367)
    boot = np.zeros(2000)
    mean = 0.
    for name, values in samples.items():
        values = np.asarray(values, dtype=np.float64)
        if len(values) < 2 or not np.isfinite(values).all():
            raise ValueError("Need at least two finite seed blocks per panel")
        mean += weights[name] * float(values.mean())
        boot += weights[name] * rng.choice(values, (2000, len(values))).mean(axis=1)
    return dict(mean=mean, lower95=float(np.quantile(boot, .025)), upper95=float(np.quantile(boot, .975)),
                independent_seeds=sum(len(values) for values in samples.values()))


def aggregate(reports, specs):
    weights = {s["name"]: s["weight"] for s in specs}
    if set(reports) != set(weights):
        raise ValueError("Missing evaluation panels")
    # Panels use distinct deal indices; overlapping panels invalidate this bootstrap.
    indices = [(r["master_seed"], b["index"]) for r in reports.values() for b in r["blocks"]]
    if len(indices) != len(set(indices)):
        raise ValueError("Evaluation panels overlap")
    def estimate(field):
        return weighted_estimate({name: [b[field] for b in r["blocks"]] for name, r in reports.items()}, weights)
    return dict(evaluation_suite="gameplay-v1", panels=reports, panel_specs=specs,
                candidate_payout=estimate("candidate"), paired_improvement=estimate("difference"),
                completed_games=sum(r["completed_games"] for r in reports.values()), illegal_actions=0,
                selection="25% one-neural-seat natural, 25% three-neural-seat natural, 50% equally weighted Ruf/Wenz/Solo play; validation only")


def compare_panels(baseline, current):
    if baseline["panel_specs"] != current["panel_specs"] or set(baseline["panels"]) != set(current["panels"]):
        raise ValueError("Evaluation suites differ")
    differences = {}
    for name, new in current["panels"].items():
        old = baseline["panels"][name]
        for key in ("master_seed", "start_index", "seeds", "bidder", "opponent", "contract_action", "neural_seats"):
            if old[key] != new[key]:
                raise ValueError("Evaluation panel settings differ")
        if [b["index"] for b in old["blocks"]] != [b["index"] for b in new["blocks"]]:
            raise ValueError("Evaluation seed blocks differ")
        differences[name] = [n["candidate"] - o["candidate"] for o, n in zip(old["blocks"], new["blocks"], strict=True)]
    specs = current["panel_specs"]
    overall = weighted_estimate(differences, {s["name"]: s["weight"] for s in specs})
    families = {}
    for family in ("ruf", "wenz", "solo"):
        names = [s["name"] for s in specs if s["family"] == family]
        families[family] = weighted_estimate({n: differences[n] for n in names}, {n: 1 / len(names) for n in names})
    return overall, families
