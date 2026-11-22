> Original architecture proposal, retained for reference. The agreed [WSL training plan](training-plan.md)
> supersedes its single-model/32-action examples and defines the two models, training stages,
> and explicit use of `~/.dotnet/dotnet` (SDK 10.0.404) in WSL. The Python snippet below is incomplete.

**Yes, an HTTP API would work — but I would not use one HTTP request per card.** With your C# simulator already achieving roughly 33,000 random games per second, that could create a significant bottleneck.

I'd keep your existing .NET 10 Schafkopf engine and use Python only for TensorFlow training and policy inference.

## **1. Proposed architecture**

**C# / .NET 10 — Game simulation**

Thousands of parallel Schafkopf games, rules, legal moves, rewards

Batched observations ↔ selected actions

**Python — TensorFlow**

GPU policy inference, PPO training, neural-network weights

Trained weights / policy updates

**Training coordinator**

Collect trajectories, calculate rewards, update policy, evaluate

There are three realistic ways to connect the two runtimes.

| Approach                 | Complexity  | My assessment                                 |
| ------------------------ | ----------- | --------------------------------------------- |
| Batched HTTP API         | Low         | Best for getting started                      |
| gRPC streaming           | Medium      | Good for continuous, high-throughput training |
| ONNX inference inside C# | Medium–high | Very attractive for maximum throughput        |

The important part is **batching**. You want to send 1,024–4,096 decisions at once, not make thousands of tiny network calls.

## **2. How training would actually work**

Imagine running 4,096 independent games in your .NET engine.

1. Start all games, using your existing simulator.
2. For each game, advance until an AI decision is needed.
3. Collect the 4,096 observations and legal-action masks.
4. Send the batch to TensorFlow.
5. TensorFlow samples a card from the policy for every observation.
6. Send the chosen cards back to .NET, execute them, and repeat.
7. When games finish, collect per-player trajectories and final rewards.
8. Train the neural network with PPO, then generate a fresh set of games.

This lets the GPU evaluate large batches while C# continues to handle the game rules.

**One important detail:** The neural network must participate in choosing the cards during training. Simply running random games and sending the results to TensorFlow will not, by itself, provide the on-policy experience PPO needs.

## **3. A minimal batched HTTP implementation**

For a first version, I'd use FastAPI in Python and `HttpClient` in C#.

### **Python — TensorFlow inference server**

This is a minimal inference endpoint, not yet the complete PPO trainer.

```
import tensorflow as tffrom fastapi import FastAPIfrom pydantic import BaseModelapp = FastAPI()OBS_SIZE = 256  # Replace with your actual observation sizeinputs = tf.keras.Input(shape=(OBS_SIZE,))x = tf.keras.layers.Dense(256, activation="relu")(inputs)x = tf.keras.layers.Dense(256, activation="relu")(x)model = tf.keras.Model(    inputs,    [        tf.keras.layers.Dense(32)(x),  # Card logits        tf.keras.layers.Dense(1)(x)    # State value    ])class ActRequest(BaseModel):    observations: list[list[float]]    legalMasks: list[list[bool]]@tf.function(reduce_retracing=True)def predict(obs, mask):    logits, values = model(obs, training=False)    masked = tf.where(mask, logits, -1e9)    actions = tf.squeeze(        tf.random.categorical(masked, 1), axis=1    )    log_probs = tf.nn.log_softmax(masked)    selected_log_probs = tf.gather(
```

### **C# — Call the policy with an entire batch**

```
using System.Net.Http.Json;

public record ActRequest(
    float[][] Observations,
    bool[][] LegalMasks
);

public record ActResponse(
    int[] Actions,
    float[] LogProbs,
    float[] Values
);

var http = new HttpClient
{
    BaseAddress = new Uri("http://localhost:8000")
};

// All states are from independent games
var request = new ActRequest(
    observations,
    legalMasks
);

var response = await http.PostAsJsonAsync("/act", request);
response.EnsureSuccessStatusCode();

var result =
    await response.Content.ReadFromJsonAsync<ActResponse>();

// Apply result.Actions[i] to game[i]
```

Your C# simulator would be responsible for maintaining the active games, encoding observations, applying actions, and recording trajectories.

For the prototype, JSON is convenient. Later, Protocol Buffers with bidirectional gRPC streaming would be an appropriate upgrade because .NET supports it natively.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://learn.microsoft.com\&sz=32)

Microsoft Learn



## **4. The training data you need to collect**

The C# simulator must record more than just the selected card and outcome.

Each decision should produce something similar to this:

| Field           | Purpose                                                                  |
| --------------- | ------------------------------------------------------------------------ |
| `observation`   | What this player knew before acting                                      |
| `legalMask`     | Which of the 32 cards were legal                                         |
| `action`        | The chosen card, 0–31                                                    |
| `logProb`       | Probability of that action under the sampling policy, expressed as a log |
| `value`         | Network's estimated return                                               |
| `reward`        | Usually zero until the game finishes                                     |
| `done`          | Whether this player's game episode has ended                             |
| `playerId`      | Associates decisions with their player trajectory                        |
| `policyVersion` | Identifies which model generated the experience                          |

Since each player makes eight card-playing decisions per game, group their observations into **per-player trajectories**. Do not treat successive actions by different players as if they belonged to one agent's trajectory.

For PPO, Python calculates advantages and returns using those sequences. It then optimizes the policy and value heads using the old action log probabilities saved during simulation.

A practical starting configuration would be:

| Parameter                                 | Initial value |
| ----------------------------------------- | ------------- |
| Parallel games                            | 4,096         |
| Inference batch size                      | Up to 4,096   |
| Collected player decisions per PPO update | 65,536        |
| PPO training epochs                       | 4             |
| PPO minibatch size                        | 2,048         |
| Discount factor                           | 1.0           |
| GAE lambda                                | 0.95          |

These are starting hyperparameters to benchmark, not values that are inherently optimal for Schafkopf.

I'd keep PPO training synchronous at first: collect a batch with one fixed policy version, perform the updates, then resume collecting with the new version.

## **5. An alternative I particularly like for your setup: ONNX inside C#**

Instead of having Python choose every card, you can run the neural network **directly in your .NET process**.

**Python — TensorFlow training**

PPO updates the neural network weights

Export ONNX policy snapshot

**C# — ONNX Runtime + Schafkopf engine**

Runs games and batched network inference locally

Transfer completed training trajectories

**Python — Next PPO update**

This is attractive because the game simulation and card selection stay inside C#. You no longer need a round trip to Python at every decision batch.

ONNX Runtime supports .NET inference with CPU and CUDA execution providers, and its newer `OrtValue` API is designed to reduce memory allocation and tensor-copy overhead.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://onnxruntime.ai\&sz=32)

onnxruntime+1



With a compatible current TensorFlow/Keras installation, exporting can be as simple as:

```
model.export("policy.onnx", format="onnx")
```

Exact export support depends on your installed Keras version, so I would test conversion and compare ONNX outputs against TensorFlow before integrating it into the simulator.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://www.tensorflow.org\&sz=32)

TensorFlow



The disadvantage is model synchronization: each training update produces new weights, which the C# workers must load at a safe rollout boundary. That adds development complexity.

## **6. How I'd organize your repository**

```
schafkopf/
│
├── src/
│   ├── Schafkopf.Core/           # Existing game engine
│   ├── Schafkopf.Training/       # Parallel game runner
│   │   ├── GamePool.cs
│   │   ├── ObservationEncoder.cs
│   │   ├── TrajectoryCollector.cs
│   │   └── PolicyClient.cs
│   │
│   └── Schafkopf.Bot/            # Bot for playing against humans
│
├── training/
│   ├── model.py                  # TensorFlow model
│   ├── server.py                 # Batched inference API
│   ├── ppo.py                    # PPO optimization
│   ├── train.py                  # Training coordinator
│   └── evaluate.py               # Fixed benchmark opponents
│
└── models/
    └── checkpoints/
```

Keep `Schafkopf.Core` independent of TensorFlow. That gives you the freedom to experiment with different AI architectures later.

## **7. What I would build first**

Before writing the entire PPO algorithm, I'd implement a benchmark that compares these three configurations on your hardware:

- **Random baseline:** existing C# simulator, no neural network.
- **Batched TensorFlow:** C# simulation with Python inference over HTTP.
- **Local ONNX:** C# simulation with batched ONNX inference.

Run the same game workload with a randomly initialized network. You'll quickly discover whether your main cost is the neural network, serialization, or communication.

Since you already have a fast engine, this measurement is worth doing before investing in additional infrastructure.

**My recommendation:** Start with batched HTTP so you can get the first trained agent working quickly. Once you have PPO learning, benchmark ONNX inside .NET and move inference there if the performance improvement justifies the extra synchronization logic.

One architectural question matters quite a bit: **Does your C# library already support running thousands of independent games concurrently, or is the 1-million-game benchmark just a tight sequential loop?** That determines how I'd implement `GamePool` and the inference batching.
