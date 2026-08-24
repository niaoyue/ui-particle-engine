# UI Particle Engine

A lightweight, allocation-free, uGUI-native particle engine. It does not use Shuriken
(Unity's ParticleSystem) — the simulation runs in a plain C# struct array, particles are
emitted as quads and rendered through the regular canvas pipeline. As a result:

- Sorts naturally with canvas ordering (draws correctly between UI elements in front/behind)
- Is masked by `Mask` and `RectMask2D`
- **Batches into a single draw call** with anything sharing the same texture/material
- Needs no extra camera, RenderTexture or bake cost

The API and inspector follow the same module layout as Unity's ParticleSystem (Shuriken).

## Usage

1. Create an empty GameObject under a Canvas.
2. Add **Add Component → UI → Effects → UI Particle Emitter**.
3. Assign a `Sprite` (a white square is used if left empty) and press Play —
   `Play On Awake` is enabled by default.

### Controlling from code (same pattern as ParticleSystem)

```csharp
emitter.Play();     // Starts; resumes if paused
emitter.Pause();
emitter.Stop();     // Emission stops, alive particles finish their lifetime
emitter.Stop(UIParticleEmitter.StopBehavior.StopEmittingAndClear);
emitter.Emit(30);   // 30 particles immediately
emitter.IsAlive();

var main = emitter.Main;                 // module access, like Shuriken
main.StartLifetime = new RangeF(0.5f, 1f);
main.GravityModifier = 0.5f;
emitter.Emission.RateOverTime = 50f;
emitter.Shape.ShapeType = UIParticleEmitter.ShapeType.Circle;
emitter.ColorOverLifetime.Enabled = true;
```

## Modules

| Module | Contents |
|---|---|
| **Main** | Duration, Looping, Prewarm, StartDelay, StartLifetime/Speed/Size/Rotation (min-max), StartColorA/B, GravityModifier (Physics2D.gravity ×100 px), SimulationSpace (Local/World), SimulationSpeed, UseUnscaledTime, PlayOnAwake, MaxParticles, AutoRandomSeed/RandomSeed |
| **Emission** | RateOverTime, RateOverDistance (while moving in World space), Bursts[] (Time, Count, Cycles, Interval, Probability) |
| **Shape** | Cone (Angle, Radius) / Circle (Radius, RadiusThickness, Arc) / Rect (its own rect) / Point (UI extra: 360° radial), Rotation |
| **Velocity over Lifetime** | Linear (constant added velocity) |
| **Limit Velocity over Lifetime** | Drag (damping) |
| **Color over Lifetime** | Gradient |
| **Size over Lifetime** | AnimationCurve |
| **Rotation over Lifetime** | AngularVelocity (min-max, °/sec) |
| **Texture Sheet Animation** | NumTilesX/Y, CycleCount, RandomStartFrame, FrameOverTime (curve) — grid flipbook |
| **Trails** | Ratio, Lifetime (multiplier of particle lifetime), MinVertexDistance, MaxPoints (fixed zero-alloc buffer), WidthOverTrail (curve), ColorOverTrail (gradient), InheritParticleColor, SizeAffectsWidth |
| **Renderer** | sprite, tint (Graphic.color), material, raycastTarget, maskable |

Deliberate deviations from Shuriken: everything is adapted to 2D/UI (units are canvas pixels),
`StartColor` is a random blend between two colors (instead of MinMaxGradient), and
`GravityModifier` applies Physics2D.gravity at a 100 px/unit scale. Trail ribbons share the
particle's material (they sample the sprite's UV center and draw as flat color); there is no
separate trail material like in Shuriken.

## Architecture

- **`UIParticleEmitter`** — a `MaskableGraphic` subclass. Simulation + mesh generation.
  Particle data lives in a preallocated `Particle[]`; deaths use swap-remove,
  zero GC allocation per frame.
- **`UIParticleUpdater`** — a single hidden MonoBehaviour. Drives all active emitters
  from one `Update()` (no cost of N separate Update dispatches).
- **`RangeF` / `XorShift32`** — serializable min-max range and a fast deterministic RNG.
- **`UIParticleAttractor`** — pulls particles toward a transform (coin→wallet, XP pickup).
  A particle entering `DestinationRadius` is consumed and `OnAttracted` (UnityEvent) fires;
  `Delay`, `MaxSpeed` and `Movement` (Linear/Smooth) are configurable.
- **`UIParticleMigrator`** — editor tool (`Tools ▸ UI Particle Engine ▸ Particle Migrator`) that
  converts existing ParticleSystem prefabs into UIParticleEmitter prefabs: modules mapped,
  world units scaled to canvas pixels, material textures re-imported as sprites, and
  unsupported features (noise, collision, sub-emitters...) listed in a migration report.
- **`UIParticleEmitterEditor`** — Shuriken-style boxed module inspector; Play/Pause/Stop/Clear
  controls in Play mode, **live preview while selected in edit mode**
  (Play/Restart/Stop) and a particle counter. Use `Simulate(dt)` to step manually from code.

## Performance notes

- Geometry **bypasses the uGUI rebuild pipeline entirely**: a compact vertex layout
  (24 bytes/vertex; UIVertex is ~90 bytes) is pushed straight to `CanvasRenderer.SetMesh`.
  Particle updates never dirty the canvas — other UI on the same canvas is not re-batched.
  Index buffers are written once per capacity change; meshes exceeding 65k vertices
  switch to 32-bit indices automatically.
- Measured (editor, Mono, single-frame cost — sim + mesh build + push):
  500 particles ≈ 0.20 ms, 2,000 ≈ 0.76 ms, 8,000 ≈ 2.9 ms;
  500 with trails ≈ 2.0 ms, 1,000 with trails ≈ 4.0 ms. GC = 0 bytes in every scenario.
  End to end ~2–2.5x faster than the old VertexHelper path.
- On device (Snapdragon 732G, IL2CPP ARM64) the same scenarios run faster than in the
  editor — 8,000 particles ≈ 0.04 ms sim + 0.99 ms mesh build — and 8,000 live on-screen
  particles hold the 60 Hz refresh ceiling. A `UIParticleDeviceBenchmark` component in the
  sample scene prints these numbers to logcat on any development build.
- `Max Particles` is your vertex budget: 4 vertices per particle, plus at most
  (MaxPoints-1)×4 per trailed particle. On mobile the real limit is overdraw, not CPU.
- For an additive look, assign a `UI/Additive`-style UI material to the emitter.

## Known limits (v1)

- No noise or collision.
- Edit-mode preview only runs while the emitter is selected in the inspector (like Shuriken).
- Trails die with their particle (no "Die with Particles = false" behavior from Shuriken)
  and count toward the vertex budget: at most (MaxPoints-1) quads per trailed particle.
- `World` simulation space only compensates for position changes (not rotate/scale).
