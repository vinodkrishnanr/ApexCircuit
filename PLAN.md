# ApexCircuit — Requirements and phased build

Foundation already in place: Godot 4 C# project, top-down 3D track with a smooth ribbon mesh, AI cars with physics (grip, weight transfer), a smoothed chase camera, and a race sim loop with positions and timing.

Current sim hook this plan replaces: each grid entry is a name, paint, and a single `Skill` float (`RaceSim.Entry` / `AiCar.Skill`). Later phases map car and driver attributes onto the existing speed, acceleration, grip, and brake behavior.

Every later phase appends a changelog entry at the bottom of this file. Do not delete history.

Every phase is testable on its own. A phase is done only when its acceptance check passes. Finished checks live in `test/PhaseChecks.cs` and run headless, without starting a race:

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/bin:$PATH"
godot --headless --path /home/vinod/my-game --quit --script res://test/PhaseChecks.cs
```

Success prints `PHASE CHECKS PASSED` and exits 0. A failure prints `PHASE CHECKS FAILED` and exits 1. Later phases add their check to that same script. Do not treat "the game window opened" as the phase test.

## Cars

- Each team has exactly one car.
- Attributes, each an integer from 1 to 100:
  - **Speed**
  - **Acceleration**
  - **Handling**
  - **Braking**
- The user picks the car and the driver before the race.
- Every car on the grid is AI-driven. There is no player driving. This is a spectator / manager race.

## Drivers

- Ratings, each an integer from 1 to 100:
  - **Attack**
  - **Defend**
  - **Handling**
  - **Braking**
  - **Focus**
  - **Potential**
- **Potential** is the driver's lifetime growth ceiling. It persists across seasons for the driver's whole career.
- A driver can contest up to **1000 races**. `RacesDone` runs from 0 to 1000. At 1000 they retire.
- Attack, Defend, Handling, Braking, and Focus move up or down according to how that driver actually raced. Potential does not tick up or down from a single race result; it bounds how far the other ratings can grow.

## How car and driver combine

- A bad car and a good driver still limit what the car can do. If the driver delivers more than that car should allow (punching above the car), that driver is treated as great: over-delivery gets a **hero-driver bonus multiplier** on the post-race rating gains.
- A good car and a bad driver who under-delivers is penalized. The raw delta is not enough on its own in either direction; context weighting applies (see post-race rules).

## Focus, tiredness, and in-race fatigue

- **Tiredness** is its own parameter. It sets the driver's starting Focus for the race:
  - Fresh: starting Focus equals the Focus rating (base Focus is retained).
  - Tired: starting Focus is reduced below the rating. The drop scales with how tired the driver is. Focus goes down in proportion to tiredness.
- During the race, Focus decays over race time. The decay rate is scaled by the Focus rating (a higher rating fades more slowly).
- When in-race Focus is low, weak attributes are amplified. A driver who is bad at braking locks up. The same idea applies to running wide (weak Handling) and sloppy overtakes (weak Attack relative to the car and the rival).
- Drama comes from ratings and fatigue, not from dice. Example: strong Attack, Defend, and Focus, but weak Braking. As Focus falls he finishes the race locking up, and Braking is reduced afterward. If the same race went well because he was leading, Braking can improve instead.

## Deterministic drama

- Keep the sim deterministic. Replays of the same race with the same seed must match.
- Each race uses a seeded RNG so any residual variation is reproducible. Outcomes are driven by ratings, tiredness, and race events, not by unseeded randomness.
- Low Handling or Braking produces lock-ups and running wide when Focus is low. Low Attack produces messy overtakes. These are consequences of the attributes, not random events.

## Between races (Focus recovery)

- **Both:** a fixed recovery between races, plus a bonus when the previous race was clean.
- Exact season length is still open. Do not invent a calendar length until that is decided.

## Selection screen

- Top Drives-style cards.
- Car stats and driver ratings are visible as numbers.
- One car per team. The user picks a car and a driver. That selection is the starting grid the sim runs.

## Post-race breakdown

- After each race, show what changed for the driver and why. Do not drift stats silently.
- Examples of the breakdown lines:
  - `Attack +2: 4 overtakes`
  - `Braking −1: 2 lock-ups`
  - `Focus +1: clean race`
- Context weighting:
  - A podium or a win boosts growth.
  - Over-delivery in a bad car gets the hero-driver bonus multiplier.
  - Under-delivery in a good car penalizes the driver.

## Career clock and retirement

- Notify the user when a driver has **100, 50, 20, or 1** races left.
- At that point the user decides what to do. Show the list of drivers available for hire at that time.
- If the user does not hire anyone, a default rookie takes the seat.
- Rookie generation details beyond "default rookie if no hire" stay a placeholder until discussed. Do not auto-fill a full free-agent market until that discussion.

## Persistence

- Seasons and careers survive restarts.
- Store teams, drivers, and career state as JSON under `data/`.
- Ratings, `RacesDone`, tiredness, and season results are part of that saved state.

## Open items

- **Season length** — decide later. No fixed number of races per season yet.
- **Rookie / hire-list generation** — the notification points and "hire or default rookie" choice are decided. How the hire list is generated is not.
- **Exact numeric curves** — attribute-to-physics mapping, Focus decay constants, hero multiplier, and rating delta sizes are set in the phase that implements them, then recorded in the changelog. They must stay inside the rules above.

## Phased build

### Phase 0 — Lock in the spec — done

- This file is the requirements document from the design session.
- Every later phase appends a changelog entry. Never delete history.
- **Test:** `Phase0Spec` reads this file and fails if a required rule is missing (car and driver attributes, 1000-race careers, hero-driver bonus, tiredness, deterministic drama, Focus recovery, retirement warnings, open season length, and phases 0–6).

### Phase 1 — Data layer: cars, drivers, careers — done

- `CarSpec`: Speed, Acceleration, Handling, Braking (1–100), one per team.
- `DriverProfile`: Attack, Defend, Handling, Braking, Focus, Potential; `RacesDone` 0–1000; tiredness.
- JSON under `data/` (`teams.json`, `drivers.json`, career state) so seasons survive restarts.
- Map attributes onto the existing sim constants (top speed, acceleration, grip, brake force), replacing the single `Skill` float.
- **Test:** `Phase1Data` loads the shipped roster (eight teams, one driver each, stats in range), checks the 1–100 pace map, writes a scratch career file, reloads it, and confirms car stats stay in `teams.json`. The scratch file is deleted. The live `data/career.json` is not modified.

### Phase 2 — Focus fatigue and deterministic drama — done

- Focus starts from the rating adjusted by tiredness, then decays over race time at a rate scaled by the rating.
- Low Focus amplifies weak attributes: braking lock-ups, running wide, sloppy overtakes.
- Outcomes are a pure function of the ratings and current Focus, so the same situation always replays the same way. No unseeded RNG.
- Attack is how hard that driver attacks the car ahead. Defend is how hard that driver holds off the car behind. Each uses only that driver's own rating.
- **Test:** `Phase2Drivers` checks fresh versus tired starting Focus, slower decay for a higher Focus rating, a larger corner penalty when Focus is low and Handling or Braking is weak, a harder attack from a higher Attack, and a harder defense from a higher Defend. The same inputs repeat exactly.

### Phase 3 — Team and driver selection

- Top Drives-style cards: car stats and driver ratings visible as numbers, one car per team.
- Godot UI scene. The pick feeds the sim's starting grid.
- **Test (when this phase lands):** a headless UI check that the cards show the numeric car and driver ratings from the roster, that only one car can be picked per team, and that the chosen pair is the entry the sim would start.

### Phase 4 — Post-race rating engine

- After each race, compute rating deltas with reasons (see breakdown examples).
- Context weighting: podium and wins boost growth; bad-car over-delivery gets the hero multiplier; good-car / bad-driver under-delivery penalizes.
- Show the breakdown after the race. Persist deltas to the career JSON.
- Apply between-race Focus recovery: fixed recovery plus a clean-race bonus.
- **Test (when this phase lands):** feed a scripted race result (overtakes, lock-ups, finish position, car quality) and assert the breakdown lines and the signed deltas. A hero result in a weak car gains more than the same result in a strong car. A clean race recovers more Focus than a messy one. Reloading the scratch career shows the new ratings.

### Phase 5 — Season and championship loop

- Multi-race calendar, points table, standings persisted across sessions.
- Drivers age on the 1000-race clock. Ratings move under the Phase 4 rules.
- Retirement warnings at 100, 50, 20, and 1 races remaining. Offer the hire list; otherwise seat a default rookie.
- Season length stays open until decided. The calendar should not hard-code a length that this file has not set.
- **Test (when this phase lands):** run two scripted rounds, reload, and check points and `RacesDone`. Drivers at 900, 950, 980, and 999 races done raise the matching warning. Declining the hire seats the default rookie. The test must not assume a season length.

### Phase 6 — Polish

- Race presentation: position towers, gap timing, battle indicators, commentary-style event feed (for example "Car 7 locks up into Turn 3!").
- Track variety (2–3 course layouts), grid variety, sound.
- Balance pass: a great driver in a bad car can realistically podium, and does not dominate every race.
- **Test (when this phase lands):** a headless race sample reports position, gap, and at least one commentary event. A balance check runs the same seed with a strong driver in a weak car and a weak driver in a strong car and records both results. The strong-driver / weak-car case is allowed to podium and is not required to win every sample.

## Changelog

### 2026-10-08 — Phase 0

- Added this requirements document from the design session: car attributes, driver career ratings, Focus as in-race fatigue modified by tiredness, deterministic drama, hero-driver narrative, post-race breakdown, 1000-race careers, hire-or-rookie retirement warnings, and season persistence.
- Recorded open items: season length, and how the hire list is generated.
- Recorded Focus recovery between races as fixed recovery plus a clean-race bonus.

### 2026-10-08 — Phase 1

- Added `CarSpec` and `DriverProfile` (including tiredness and `RacesDone`). Eight teams and their drivers live in `data/teams.json` and `data/drivers.json`. Mutable ratings, races, and tiredness load from `data/career.json`, which is written on first launch.
- Replaced the single `Skill` float. A rating of 1–100 maps with `(stat - 1) / 99`. Top speed is `lerp(34, 52, car Speed)`. Acceleration is `lerp(7, 18, car Acceleration)`. Braking is `lerp(14, 38, blend)` and grip is `lerp(24, 68, blend)`, where blend is the average of the car stat and the driver stat. Corner speed loss is `lerp(22, 12, handling blend)` (higher handling sheds less speed).
- Attack, Defend, Focus, Potential, and tiredness are stored. They do not change the physics until Phase 2.

### 2026-10-08 — Phases are testable

- Each phase has an acceptance check. Finished checks run from `test/PhaseChecks.cs` with `godot --headless --path . --quit --script res://test/PhaseChecks.cs`.
- Phase 0 checks that this file still contains the agreed rules. Phase 1 checks the roster, the pace map, and a career save/load that does not touch `data/career.json`.
- Phases 2–6 list the check they must pass before they can be marked done. The race scene no longer runs the pace check on startup.

### 2026-10-08 — Phase 2

- Starting Focus is the Focus rating times `(1 - tiredness / 100)`. It then falls at `lerp(0.45, 0.04, focus rating)` points per second.
- In a corner, low Focus adds up to 40% extra speed loss from weak Handling and 30% from weak Braking, and pushes a weak-handling driver toward the outside.
- Attack and Defend are each `lerp(0.15, 1, rating)` from that driver alone. Attack sets how far they move aside and how little they lift for the car ahead. Defend sets how far they cover the car behind.
- Potential still only caps later growth. It does not change this race. The right-hand list follows the live race order and shows Focus as live/rating.
