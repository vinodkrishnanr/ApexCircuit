# Apex Circuit

Top-down 3D circuit race. Eight AI cars drive the lap on their own: they brake for corners, hold a racing line, and move aside to pass.

## Run

Godot **4.7.2 .NET** and the **.NET 8 SDK** are required.

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/bin:$PATH"
godot --path .
```

Press **C** to toggle a higher overhead camera. The mouse wheel changes camera height.

## What is built

- Closed asphalt circuit with curbs, barriers, a finish gantry, grass, trees, and a grandstand
- Eight colored AI cars with numbers, brake lights, and wheel spin
- Live order and speed readout
- Camera stays above the lead battle

## Left

- No player-driven car
- No tire model, fuel, or damage
- Scenery is primitive meshes

## Assets

| Asset | Source |
| --- | --- |
| Track, cars, trees, grandstand, sky | Procedural meshes and materials in `scripts/` |
