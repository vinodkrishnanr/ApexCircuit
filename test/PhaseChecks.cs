using Godot;

namespace ApexCircuit;

/// <summary>
/// Headless acceptance checks for every finished phase.
/// Run: godot --headless --path . --quit --script res://test/PhaseChecks.cs
/// </summary>
public partial class PhaseChecks : SceneTree
{
    public override void _Initialize()
    {
        try
        {
            Phase0Spec();
            Phase1Data();
            Phase2Drivers();
            ContactsStayClear();
            FasterCarPasses();
            GD.Print("PHASE CHECKS PASSED");
            Quit(0);
        }
        catch (Exception ex)
        {
            GD.PushError(ex.ToString());
            GD.Print("PHASE CHECKS FAILED");
            Quit(1);
        }
    }

    static void Phase0Spec()
    {
        string plan = File.ReadAllText(Roster.Path("res://PLAN.md"));
        string[] required =
        [
            "Speed",
            "Acceleration",
            "Handling",
            "Braking",
            "Attack",
            "Defend",
            "Focus",
            "Potential",
            "1000",
            "hero-driver",
            "Tiredness",
            "deterministic",
            "fixed recovery",
            "clean-race",
            "100, 50, 20, or 1",
            "Season length",
            "Phase 0",
            "Phase 1",
            "Phase 2",
            "Phase 3",
            "Phase 4",
            "Phase 5",
            "Phase 6",
            "Changelog",
        ];
        foreach (string phrase in required)
        {
            if (!plan.Contains(phrase, StringComparison.Ordinal))
                throw new InvalidOperationException($"PLAN.md is missing '{phrase}'.");
        }
    }

    static void Phase1Data()
    {
        PaceMapCheck.Run();

        string root = Roster.Path("res://");
        string teams = System.IO.Path.Combine(root, "data", "teams.json");
        string drivers = System.IO.Path.Combine(root, "data", "drivers.json");
        string scratch = System.IO.Path.Combine(root, "data", ".phase1-career-scratch.json");
        try
        {
            Roster roster = Roster.LoadFrom(teams, drivers, scratch);
            if (roster.Teams.Count != 8 || roster.Drivers.Count != 8)
                throw new InvalidOperationException("The shipped roster must have eight teams and eight drivers.");

            var seenTeams = new HashSet<string>();
            var seenDrivers = new HashSet<string>();
            foreach (TeamEntry team in roster.Teams)
            {
                if (!seenTeams.Add(team.Id))
                    throw new InvalidOperationException($"Duplicate team '{team.Id}'.");
                if (string.IsNullOrWhiteSpace(team.DriverId) || !seenDrivers.Add(team.DriverId))
                    throw new InvalidOperationException($"Team '{team.Id}' must have its own driver.");
                DriverProfile driver = roster.Driver(team.DriverId);
                InRange(team.Car.Speed, team.Id + " speed");
                InRange(team.Car.Acceleration, team.Id + " acceleration");
                InRange(team.Car.Handling, team.Id + " handling");
                InRange(team.Car.Braking, team.Id + " braking");
                InRange(driver.Attack, driver.Id + " attack");
                InRange(driver.Defend, driver.Id + " defend");
                InRange(driver.Handling, driver.Id + " handling");
                InRange(driver.Braking, driver.Id + " braking");
                InRange(driver.Focus, driver.Id + " focus");
                InRange(driver.Potential, driver.Id + " potential");
                if (driver.RacesDone is < 0 or > 1000)
                    throw new InvalidOperationException($"{driver.Id} races must be 0–1000.");
                if (driver.Tiredness is < 0 or > 100)
                    throw new InvalidOperationException($"{driver.Id} tiredness must be 0–100.");
            }

            DriverProfile sample = roster.Drivers[0];
            int attack = sample.Attack;
            sample.Attack = attack == 100 ? 99 : attack + 1;
            sample.RacesDone = Math.Min(1000, sample.RacesDone + 1);
            Roster.SaveCareer(roster, scratch);

            Roster again = Roster.LoadFrom(teams, drivers, scratch);
            DriverProfile loaded = again.Driver(sample.Id);
            if (loaded.Attack != sample.Attack || loaded.RacesDone != sample.RacesDone)
                throw new InvalidOperationException("Career JSON must restore driver ratings and races done.");

            TeamEntry first = roster.Teams[0];
            int speed = first.Car.Speed;
            first.Car.Speed = speed == 1 ? 2 : 1;
            Roster.SaveCareer(roster, scratch);
            TeamEntry reloaded = Roster.LoadFrom(teams, drivers, scratch).Teams[0];
            if (reloaded.Car.Speed != speed)
                throw new InvalidOperationException("Car stats must stay in teams.json, not career.json.");
        }
        finally
        {
            if (File.Exists(scratch))
                File.Delete(scratch);
        }
    }

    static void Phase2Drivers()
    {
        var fresh = new DriverProfile { Focus = 80, Tiredness = 0, Handling = 40, Braking = 40 };
        var tired = new DriverProfile { Focus = 80, Tiredness = 50, Handling = 40, Braking = 40 };
        if (DriverForm.StartingFocus(fresh) != 80f)
            throw new InvalidOperationException("A fresh driver keeps their Focus rating at the start.");
        if (DriverForm.StartingFocus(tired) >= DriverForm.StartingFocus(fresh))
            throw new InvalidOperationException("Tiredness must lower starting Focus.");

        float sharp = DriverForm.Tick(80f, 90, 20f);
        float dull = DriverForm.Tick(80f, 30, 20f);
        if (sharp >= 80f || dull >= sharp)
            throw new InvalidOperationException("Focus must fall during a race, and a higher rating must fall more slowly.");

        float composed = DriverForm.CornerPenalty(90, 90, 80f);
        float faded = DriverForm.CornerPenalty(36, 36, 20f);
        if (faded <= composed)
            throw new InvalidOperationException("Low Focus must amplify weak handling and braking.");
        if (DriverForm.CornerPenalty(100, 100, 0f) != 0f)
            throw new InvalidOperationException("A perfect handling and braking rating does not lock up or run wide.");

        if (DriverForm.AttackEffort(92) <= DriverForm.AttackEffort(40))
            throw new InvalidOperationException("A higher Attack must press the car ahead harder.");
        if (DriverForm.DefendEffort(92) <= DriverForm.DefendEffort(40))
            throw new InvalidOperationException("A higher Defend must hold off the car behind harder.");

        if (!DriverForm.CausesAccidents(30, 22, 10f) || DriverForm.CausesAccidents(90, 88, 80f))
            throw new InvalidOperationException("Only a tired driver who is weak on handling or braking causes accidents.");
        if (DriverForm.CausesAccidents(36, 36, 80f))
            throw new InvalidOperationException("The same weak driver must stay clean while Focus is still high.");
        if (DriverForm.RacingGap(0.2f) < AiCar.BodyHalfWidth * 2f)
            throw new InvalidOperationException("A pass must leave a gap wider than the two cars.");

        float again = DriverForm.CornerPenalty(36, 36, 20f);
        if (again != faded)
            throw new InvalidOperationException("The same ratings and Focus must produce the same corner penalty.");
    }

    void ContactsStayClear()
    {
        var world = new Node3D();
        Root.AddChild(world);
        TrackPath path = TrackCourse.Build(world);
        Roster roster = Roster.Load();
        var pair = new List<AiCar>();
        var locks = new Dictionary<(int, int), float>();
        float time = 0f;
        for (int i = 0; i < 2; i++)
        {
            TeamEntry team = roster.Teams[i];
            var car = new AiCar();
            world.AddChild(car);
            car.Spawn(path, i, team.Label, team.Color, team.Car, roster.Driver(team.DriverId), 80f, i * 0.1f);
            pair.Add(car);
        }

        if (!AiCar.Overlap(pair[0], pair[1], out _, out _))
            throw new InvalidOperationException("The contact fixture must start overlapped.");

        for (int n = 0; n < 6; n++)
        {
            time += 0.016f;
            RaceSim.Resolve(pair, path, locks, ref time);
        }

        if (AiCar.Overlap(pair[0], pair[1], out _, out float left))
            throw new InvalidOperationException($"Cars still overlap by {left:0.00} m after a hit.");
        if (pair[0].Damage > 0f || pair[1].Damage > 0f)
            throw new InvalidOperationException("Separating two controlled drivers must not count as a crash.");

        var reckless = new DriverProfile
        {
            Id = "reckless",
            Name = "Reckless",
            Attack = 80,
            Defend = 40,
            Handling = 28,
            Braking = 22,
            Focus = 30,
            Potential = 50,
            Tiredness = 70,
        };
        var victim = new AiCar();
        var wrecker = new AiCar();
        world.AddChild(victim);
        world.AddChild(wrecker);
        CarSpec spec = roster.Teams[0].Car;
        victim.Spawn(path, 20, "Victim", Colors.White, spec, roster.Driver(roster.Teams[0].DriverId), 140f, 0f);
        wrecker.Spawn(path, 21, "Wrecker", Colors.Red, spec, reckless, 140f, 0.1f);
        var wreckLocks = new Dictionary<(int, int), float>();
        float wreckTime = 0f;
        for (int n = 0; n < 6; n++)
        {
            wreckTime += 0.016f;
            RaceSim.Resolve(new List<AiCar> { victim, wrecker }, path, wreckLocks, ref wreckTime);
        }

        if (AiCar.Overlap(victim, wrecker, out _, out float wreckLeft))
            throw new InvalidOperationException($"Cars still overlap by {wreckLeft:0.00} m after separation.");

        var field = new List<AiCar>();
        var fieldLocks = new Dictionary<(int, int), float>();
        float raceTime = 0f;
        const float spacing = 6.6f;
        for (int slot = 0; slot < roster.Teams.Count; slot++)
        {
            TeamEntry team = roster.Teams[slot];
            var car = new AiCar();
            world.AddChild(car);
            float lane = slot % 2 == 0 ? -1.15f : 1.15f;
            car.Spawn(path, slot, team.Label, team.Color, team.Car, roster.Driver(team.DriverId), 36f + slot * spacing, lane);
            field.Add(car);
        }

        const float dt = 1f / 60f;
        for (int frame = 0; frame < 60 * 30; frame++)
        {
            raceTime += dt;
            foreach (AiCar entry in field)
                entry.Plan(dt, path, field);
            foreach (AiCar entry in field)
                entry.Apply(dt, path);
            RaceSim.Resolve(field, path, fieldLocks, ref raceTime);
            for (int i = 0; i < field.Count; i++)
            {
                for (int j = i + 1; j < field.Count; j++)
                {
                    if (AiCar.Overlap(field[i], field[j], out _, out float pen))
                        throw new InvalidOperationException($"{field[i].CarName} overlaps {field[j].CarName} by {pen:0.00} m.");
                }
            }
        }
    }

    void FasterCarPasses()
    {
        var world = new Node3D();
        Root.AddChild(world);
        TrackPath path = TrackCourse.Build(world);
        var calm = new DriverProfile
        {
            Id = "calm",
            Name = "Calm",
            Attack = 70,
            Defend = 55,
            Handling = 78,
            Braking = 76,
            Focus = 80,
            Potential = 60,
            Tiredness = 0,
        };
        var slow = new AiCar();
        var fast = new AiCar();
        world.AddChild(slow);
        world.AddChild(fast);
        slow.Spawn(path, 30, "Slow", Colors.White, new CarSpec { Speed = 40, Acceleration = 70, Handling = 70, Braking = 70 }, calm, 120f, 0f);
        fast.Spawn(path, 31, "Fast", Colors.Red, new CarSpec { Speed = 96, Acceleration = 80, Handling = 70, Braking = 70 }, calm, 108f, 0.2f);
        var cars = new List<AiCar> { slow, fast };
        var locks = new Dictionary<(int, int), float>();
        float raceTime = 0f;
        const float dt = 1f / 60f;
        for (int frame = 0; frame < 60 * 25; frame++)
        {
            raceTime += dt;
            foreach (AiCar car in cars)
                car.Plan(dt, path, cars);
            foreach (AiCar car in cars)
                car.Apply(dt, path);
            RaceSim.Resolve(cars, path, locks, ref raceTime);
            if (AiCar.Overlap(slow, fast, out _, out float pen))
                throw new InvalidOperationException($"Pass overlapped by {pen:0.00} m.");
        }

        float slowDist = slow.Laps * path.Length + slow.Progress;
        float fastDist = fast.Laps * path.Length + fast.Progress;
        if (fastDist <= slowDist)
            throw new InvalidOperationException($"Faster car did not pass. fast={fastDist:0} slow={slowDist:0}.");
        if (slow.Damage > 0f || fast.Damage > 0f)
            throw new InvalidOperationException("A clean pass must not damage either car.");
    }

    static void InRange(int value, string label)
    {
        if (value is < 1 or > 100)
            throw new InvalidOperationException($"{label} must be 1–100, got {value}.");
    }
}
