## [Voxelyze+Unity+ML-Agents] Parallel Multi-Soft-Robot RL Simulation Framework (ver 0.5)

> **WORK IN PROGRESS (WIP)**
> This project is currently under active development and research. 
> The code is being disclosed in advance for the purpose of maintaining a record of prior research and for demonstration purposes only.

<img width="1791" height="1363" alt="screenshot" src="https://github.com/user-attachments/assets/70b7bc57-b812-4900-8225-18621b9f1f2c" />

### Core Features

*   **Core Physics Engine**: Integrates the high-performance C++ Voxelyze soft-body physics engine with Unity's environment and ML-Agents.
*   **Extreme Multi-Threading**: Maximizes HEDT CPUs (e.g., AMD Threadripper) via OpenMP and Windows Thread Affinity (Processor Group pinning) to utilize 64+ cores perfectly.
*   **Nested Parallel Optimization**: Eliminates thread fork-join overhead by utilizing a single persistent OpenMP parallel region with implicit barriers for micro/macro steps.
*   **Zero-Allocation Data Bridge**: Transfers vertex and state data directly from C++ to Unity's GPU via `IntPtr` and the Burst Compiler, entirely bypassing Garbage Collection (Zero GC).
*   **Lock-Free Triple Buffering**: Ensures smooth 60+ FPS rendering in Unity without being bottlenecked or blocked by heavy asynchronous physics computations.
*   **In-Scene Massive Parallelism**: Safely isolates memory buffers in the DLL, allowing dozens of independent soft robots to train simultaneously and asynchronously within a single Unity scene.
*   **Bi-directional Physics Interaction**: Supports two-way continuous collision, force, and torque exchange between Unity PhysX rigidbodies and Voxelyze soft-bodies.
*   **CPG Motor Actuation**: Translates AI commands into Central Pattern Generator (CPG) parameters, ensuring smooth, continuous sine-wave locomotion and preventing physics explosion.

### Quick User Manual
https://neuronomicon.github.io/VoxUM.html

## 1. Introduction
This system was developed to seamlessly integrate the computationally intensive physical simulation of soft-body robots with Reinforcement Learning (RL) training. Because Unity's native physics engine (PhysX) has limitations in calculating soft-body dynamics, **Voxelyze**, a high-performance C++ voxel physics engine, was adopted as the core physical simulator. 
This specification details the architecture of the zero-allocation data communication between the C++ DLL and Unity C#, the parallel physics computation utilizing multi-core processing, and the lock-step synchronization architecture required for stable integration with Unity ML-Agents.

## 2. System Architecture Overview
The entire system is modularized into three main layers, connected via high-performance pointer-based communication:
*   **Physics Layer (C++ DLL):** Handles the actual physics calculations (mass-spring-damper models, RK4 integrators), updates the geometric shape of the robots, and manages multi-threading.
*   **Bridge & Render Layer (C++ / C#):** Safely transfers calculated vertex and physics state data to Unity using a triple-buffering mechanism and performs real-time rendering.
*   **RL & Interaction Layer (Unity C#):** Collects the ML-Agents' observations/actions and user interactions, passing them to the C++ command queue to control the simulation stepping.

---

## 3. C++ DLL (Voxelyze) Core Implementation
To encapsulate responsibilities, the C++ codebase is refactored into modular files.

### 3.1. Modular C++ System Structure
*   **Deferred Initialization and Build Logic:**
    *   Caches robot configuration values and voxel arrays sent from the Unity editor into global static arrays inside the C++ engine.
    *   This deferred initialization pattern prevents overhead and file path errors that would occur if files were loaded via traditional I/O operations.
*   **Main Loop and Thread Control Logic:**
    *   Manages the lifecycle of the simulation.
    *   Employs Windows API to maximize CPU utilization by distributing and pinning threads across process groups.
    *   Utilizes nested OpenMP settings to handle parallelization at both the macro-level (multiple robots) and micro-level (voxels within a robot).
*   **Physics Data and External Forces Logic:**
    *   Encapsulates the logic for extracting physical state information required for reinforcement learning and applying external interactive forces (e.g., mouse drag, physical collisions) to voxels.
*   **Rendering Data Processing Logic:**
    *   Processes 3D vertices and normal vectors to match Unity's GPU rendering pipeline.

---

## 4. Inter-Process Synchronization and Rendering Bridge
To prevent race conditions and frame stuttering between the asynchronous C++ physics threads and Unity's main thread, advanced memory communication techniques were implemented.

### 4.1. Triple Buffering and Mutex Locks
*   Upon completing a physics calculation, the updated vertex and physics state data are written to an internal C++ Write Buffer.
*   Once writing is complete, the engine briefly locks a mutex and swaps the pointers of the Write Buffer and the Ready Buffer.
*   Unity accesses only the Read Buffer via pointers, ensuring that the 60 FPS rendering cycle is maintained without waiting for heavy physics computations.

### 4.2. Zero-Allocation C# Rendering
*   The structured data generated by C++ is passed to Unity as an `IntPtr`.
*   The Unity C# side utilizes `unsafe` blocks and the C# Job System (Burst Compiler) to directly copy this pointer data into the Unity Mesh buffer without allocation, completely eliminating Garbage Collector (GC) overhead.
*   Coordinate system discrepancies (Voxelyze is Z-up; Unity is Y-up) are handled during the data mapping process.

### 4.3. Command Queue (Mailbox) Interaction Pattern
*   User inputs (mouse) or Unity rigid-body collision data are stacked in a command queue array in C++.
*   Just before the C++ loop executes the next physical integration step, it fetches commands from this "mailbox" and accumulates the external forces onto the voxels, advancing the simulation safely.

---

## 5. Multi-Agent Reinforcement Learning (ML-Agents) Integration
The most critical implementation is the transition to a **"Unity-driven Lock-step Ping-Pong Model"** for reinforcement learning.

### 5.1. Shifting Simulation Control (FixedUpdate Synchronization)
*   Running the C++ engine as an infinite asynchronous thread during RL training caused severe desynchronization between physics time and the agent's decision steps, leading to performance degradation.
*   To resolve this, the infinite loop thread is stopped, and the C++ physics computation function is forcibly called from Unity's `FixedUpdate()`.
*   The C++ engine only computes the specified micro-steps upon Unity's request and returns control, ensuring perfect 1:1 synchronization.

### 5.2. In-Scene Parallelization (Multi-Agent Training)
*   Multiple training arenas (containing robots and targets) are duplicated within the Unity Scene.
*   A central manager script automatically discovers all robot agents in the scene and assigns them a unique index.
*   This index maps to independent buffer arrays in the C++ DLL, allowing dozens of isolated parallel universes to be simulated thread-safely within a single C++ engine.

### 5.3. Observation and Action Space Formulation
*   **State Observation:** 
    *   Localized relative vector and distance to the target.
    *   The Up vector to determine the robot's orientation.
    *   Local relative position, velocity, and angular velocity data for each voxel. Safe guards based on expected voxel counts are applied to fix the observation size.
*   **Motor Action:** 
    *   The ML-Agents neural network outputs continuous parameter signals to control motor voxels.
    *   These signals are passed to the C++ engine, converted into Central Pattern Generator (CPG) parameters, or used for direct target volume control, generating smooth, sine-wave-like movements.

### 5.4. Centralized Control and Episode Management
*   Each agent operates on independent episode timelines, resetting the environment locally upon failure.
*   Experience data independently gathered across all training arenas are aggregated to simultaneously update a single Proximal Policy Optimization (PPO) neural network via the Unity ML-Agents trainer, significantly enhancing learning speed.

### Summary
This implementation specification outlines an architecture that harmoniously fuses the overwhelming physics computational power of the C++ Voxelyze engine with Unity's flexible ML-Agents framework. By meticulously managing memory via zero-allocation pointers, managing independent training arena buffers, and strictly enforcing lock-step synchronization, the system achieves stable and framerate-drop-free training for complex multi-soft-robot simulations.

#### Copyright (c) 2026 [Y.S.Shim, PCU-Game Lab]. All rights reserved.
