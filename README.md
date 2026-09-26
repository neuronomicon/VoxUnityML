# VoxUnityML

### ** Parallel Multi-Soft-Robot Reinforcement Learning in Unity, powered by Voxelyze 3D Voxel Physics Engine **

![Unity](https://img.shields.io/badge/Unity-6-000000?logo=unity&logoColor=white)
![ML-Agents](https://img.shields.io/badge/ML--Agents-PPO-0088cc)
![OpenMP](https://img.shields.io/badge/OpenMP-5.0%20%2F%202.0-0f7a4a)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-1f5fa8)
![Status](https://img.shields.io/badge/status-WIP-c05314)

> **Work in progress.** Released early to document prior research and for demonstration.
> APIs and training configurations are still moving.

---
### Unity Editor Working Screen
<!--<img width="1280" alt="screenshot" src="https://github.com/user-attachments/assets/70b7bc57-b812-4900-8225-18621b9f1f2c" />-->
<img width="1350" height="1089" alt="VoxUnityML_Editor_Shot" src="https://github.com/user-attachments/assets/98a6e573-451c-4ea8-bb13-b359860c5cef" />

---
### Training in Action (Click to view)
&emsp;
<a href="https://youtu.be/SqiNJGC3jWg" target="_blank">
  <img width="300" src="https://github.com/user-attachments/assets/c08db34b-7961-457c-b990-27aa587f3063">
</a> &emsp;&emsp;&emsp;
<a href="https://youtu.be/IXZdLQriIfw" target="_blank">
  <img width="300" src="https://github.com/user-attachments/assets/f59fdcce-14db-432b-b801-082d2bcb4acc">
</a>
<br>
&emsp;&ensp; 33-voxels 4 Arena (i9-12900k 16C/24T)
&emsp;&emsp;&ensp; 516-voxels 15 Arena (Threadripper 3990X 64C/128T)

## What it does

While Unity's built-in physics and surface-based assets excel at visual deformation, they lack the true volumetric, multi-material mechanics (bones/muscles) required for active soft robots. Thus, this project runs **[Voxelyze](https://github.com/jonhiller/Voxelyze)** — Jon Hiller's 3D voxel physics engine — as the dedicated physics core bridged into Unity ML-Agents.Through a custom Zero-Allocation (Zero-GC) architecture, dozens of independent soft robots train in parallel at 1000Hz+ micro-steps. Each operates in isolated DLL memory, allowing Unity to render at 60+ FPS without physics bottlenecks. The result is a true soft-robot RL testbed that scales with CPU core count rather than GPU budget.

## Highlights

- **C++ physics core** — [Voxelyze](https://github.com/jonhiller/Voxelyze) 3D voxel physics, stepped
  from Unity rather than running free. Each voxel carries full 6-DOF state; deformation, collision
  and thermal actuation are computed natively.
- **Nested OpenMP parallelism** — a persistent parallel region parallelises across robots (macro)
  and across voxels within a robot (micro), with no fork-join churn per step.
- **OpenMP 5.0, with a 2.0 fallback** — the DLL builds against either **libomp** (clang-cl,
  `_OPENMP 201811`) or **vcomp** (MSVC, `_OPENMP 200203`). A thin compatibility header selects
  `omp_set_max_active_levels()` or the deprecated `omp_set_nested()` by *OpenMP version* rather than
  by compiler, so both builds behave identically — and a compile-time guard fails the build when
  OpenMP is off, which otherwise yields a silently serial DLL.
- **Physical-core-aware thread allocation** — rather than counting logical threads, the scheduler
  reads the machine's real topology (physical cores, SMT siblings, Windows processor groups) and
  hands each robot an **exclusive contiguous block of physical cores**, one thread per core, leaving
  the SMT siblings free for Unity and Python. Blocks never straddle a processor group, so 64+ core
  HEDT machines (AMD Threadripper class) use the whole chip instead of half of it. Robots never
  share a core, because the macro barrier would propagate one robot's contention to all of them.
- **Zero-GC data bridge** — vertex and state data cross from C++ to Unity as `IntPtr` and are
  consumed by the Burst compiler directly into mesh buffers. No managed allocation per frame.
- **Lock-free triple buffering** — rendering reads a completed snapshot while physics writes the
  next one; heavy simulation never stalls the frame.
- **Bi-directional PhysX coupling** — continuous two-way collision, force and torque exchange
  between Unity rigidbodies and Voxelyze soft bodies.
- **Direct thermal actuation** — the policy sets a target temperature per muscle voxel; a
  first-order actuator model (τ ≈ 50 ms) turns discontinuous commands into smooth deformation
  without a hand-designed gait generator.

---

## Quick start

```bash
# 1. Build the C++ DLL (Visual Studio x64 — "Release" with MSVC, or "ReleaseCL" with clang-cl)
#    and copy it into the Unity project.

# 2. Open SingleArenaScene in Unity 6.

# 3. Start the trainer, then press Play.
mlagents-learn config/VoxBot33.yaml --run-id=VoxBot_01
```

The console should report the wiring:

```
[VxAff] topology: logical=128  physicalCores=64  groups=2
[VoxelEngineCore] Total 2 robots indexed sequentially across 1 training areas.
[VoxelRLManager] 1 robots, actionSize=33
```

Full walkthrough → **[VoxUnityML: First Training Run](https://neuronomicon.github.io/VoxUM.html)**

---

## Architecture

Three layers connected by pointers rather than by serialisation:

| Layer | Language | Responsibility |
|---|---|---|
| Physics | C++ DLL | Voxel dynamics, collision, multi-threading, per-robot memory isolation |
| Bridge & render | C++ / C# | Triple-buffered state and vertex transfer, Burst mesh upload |
| RL & interaction | Unity C# | Observation assembly, action dispatch, lock-step stepping, user input |

### The control loop

Physics runs on a background C++ thread, so Unity cannot step the Academy on a fixed schedule.
`VoxelRLManager` disables automatic stepping and drives the cycle by hand, waiting on a ready flag
before releasing the next batch of actions.

```mermaid
flowchart LR
    A["Unity Update()<br/>polls ready flag"] --> B["EnvironmentStep()<br/>observe → decide"]
    B --> C["Action buffer<br/>N agents, lock-step"]
    C --> D["C++ worker thread<br/>20 × 1 ms steps"]
    D -->|"advances 20 ms, raises ready flag"| A
```

> **Never attach a `Decision Requester`.** It would request decisions on Unity's own schedule,
> independent of the worker thread, and agents would submit actions while physics is mid-flight.
> The manager calls `RequestDecision()` for every agent itself.

Because stepping is driven from `Update()` rather than `FixedUpdate()`, raising `time_scale` does
**not** speed up training — `target_frame_rate: -1` is the knob that matters.

---

## Observation and action space

Body geometry and task objective are separate ScriptableObjects, so one robot body can be reused
across many tasks. Observation and action sizes are **derived** from the body profile and pushed
into `BehaviorParameters` — they are never typed in by hand.

**Observation** — body-local frame, measured relative to the centre of mass:

| Index | Contents | Count |
|---|---|---|
| 0–2 | Target direction (unit x, z) and normalised distance | 3 |
| 3–5 | Centre-of-mass velocity | 3 |
| 6–8 | Mean angular velocity | 3 |
| 9–206 | Per voxel: position and velocity relative to CoM | 6 × 33 |
| 207–239 | Previous action | 33 |

The previous-action block is not optional. Actuators carry a first-order lag, so the next state
depends on commands issued several steps earlier; feeding the last action back restores the Markov
property the policy needs.

**Action** — one continuous value per muscle voxel:

```
a ∈ [-1, +1]  →  target temperature = a × 12
                 contract ←──────────→ expand

current += (target - current) × 0.02   per 1 ms micro-step   (τ ≈ 49.5 ms)
```

The lag is applied inside the micro-step loop, so actuator dynamics stay fixed in wall-clock terms
regardless of the decision rate.

---

## Key files

```
cpp/
  Unity_Voxel_RL_DLL.cpp            RL entry points, reset, lock-step handoff
  Unity_Voxel_RL_Actions_DLL.cpp    actuation modes (direct thermal / legacy CPG)
  Unity_Voxel_FuncDLL.cpp           engine boot, worker thread, OpenMP robot loop
  Voxelyze_Nested.cpp               nested-parallel time step
  VoxCoreAffinity.h / .cpp          physical-core and processor-group assignment
  VoxOmpCompat.h                    OpenMP 5.0 / 2.0 compatibility layer

Assets/Scripts/RL/
  RobotBodyProfile.cs               anatomy, muscle count, observation layout
  RobotTaskProfile.cs               abstract task: rewards, termination, sizes
  TargetTrackingProfile.cs          concrete task implementation
  VoxelRobotAgent.cs                ML-Agents agent (delegates to the profile)
  VoxelRLManager.cs                 lock-step decision cycle

Assets/Scripts/Editor/
  AutoBuilder.cs                    dual graphics + headless server build

config/VoxBot33.yaml                PPO hyperparameters and engine settings
```

## Scaling

| Scene | Arenas | Simulated robots | Physical cores |
|---|---|---|---|
| `SingleArenaScene` | 1 | 2 | 4 |
| `MultiArenaScene04` | 4 | 8 | 16 |
| `MultiArenaScene16` | 16 | 32 | 64 |

Each arena holds one RL robot (`threadCount 3`) plus one non-learning companion body
(`threadCount 1`). Demand is counted in **physical cores, not logical threads** — a 64-core /
128-thread machine has a budget of 64, and the spare SMT siblings are the headroom that keeps
Unity's frame time sane.

```
C = Σ ceil(threadCount_i)                        cores one arena needs    ( [3,1] → 4 )
N = floor((groups × cores_per_group − reserved) / C)        arenas that fit
```

A robot's core block cannot cross a Windows processor group, so keep `C` a **divisor of the
per-group core count** — 32 on a 64-core machine — and nothing is stranded. `VxAff_MaxRepeats()`
answers exactly, and the boot log reports `cores used`, `spare` and `skipped` so you can check
without benchmarking.

Reference configuration on 64 cores / 128 threads: **15 arenas → 30 robots on 60 of 64 cores**,
4 cores spare for Unity, nothing stranded, nested teams intact. Oversubscribing is what makes 16
arenas slower than 4 — every robot is joined by the macro barrier, so contention on one propagates
to all. On a small machine the trade-off inverts: 4 arenas on an 8-core / 16-thread box reports
`[OVERSUBSCRIBED]` and is nonetheless the throughput optimum there, since 16 threads land one per
logical CPU with nothing double-booked.

Keep the training YAML identical across scenes when comparing them. `buffer_size` counts total
agent steps, so the number of policy updates at a given step count is unchanged — which is what
makes the comparison meaningful.

---

## Roadmap

- [ ] Terrain and obstacle task variants beyond target tracking
- [ ] Morphology co-optimisation (evolving the voxel layout alongside the controller)
- [ ] Contact-state observations for gait learning on uneven ground
- [ ] Linux / headless cluster support

## Acknowledgements

Built on [Voxelyze](https://github.com/jonhiller/Voxelyze), the 3D voxel physics engine by
Jonathan Hiller and Hod Lipson (*Dynamic Simulation of Soft Multimaterial 3D-Printed Objects*,
Soft Robotics, 2014).
Observation and action design draws on *Evolution Gym* (Bhatia et al., NeurIPS 2021).

---

#### Copyright (c) 2026 Y.S.Shim, PCU-Game Lab. All rights reserved.
