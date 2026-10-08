using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace ApexCircuit;

public sealed class CarSpec
{
    public int Speed { get; set; } = 50;
    public int Acceleration { get; set; } = 50;
    public int Handling { get; set; } = 50;
    public int Braking { get; set; } = 50;

    public void Clamp()
    {
        Speed = Roster.ClampStat(Speed);
        Acceleration = Roster.ClampStat(Acceleration);
        Handling = Roster.ClampStat(Handling);
        Braking = Roster.ClampStat(Braking);
    }
}

public sealed class DriverProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Attack { get; set; } = 50;
    public int Defend { get; set; } = 50;
    public int Handling { get; set; } = 50;
    public int Braking { get; set; } = 50;
    public int Focus { get; set; } = 50;
    public int Potential { get; set; } = 50;
    public int RacesDone { get; set; }
    public int Tiredness { get; set; }

    public void Clamp()
    {
        Attack = Roster.ClampStat(Attack);
        Defend = Roster.ClampStat(Defend);
        Handling = Roster.ClampStat(Handling);
        Braking = Roster.ClampStat(Braking);
        Focus = Roster.ClampStat(Focus);
        Potential = Roster.ClampStat(Potential);
        RacesDone = Math.Clamp(RacesDone, 0, 1000);
        Tiredness = Math.Clamp(Tiredness, 0, 100);
    }
}

public readonly record struct Pace(float TopSpeed, float Accel, float Brake, float Grip, float Corner)
{
    public static Pace From(CarSpec car, DriverProfile driver)
    {
        car.Clamp();
        driver.Clamp();
        float speed = Unit(car.Speed);
        float accel = Unit(car.Acceleration);
        float handling = Unit(Blend(car.Handling, driver.Handling));
        float braking = Unit(Blend(car.Braking, driver.Braking));
        return new Pace(
            Mathf.Lerp(34f, 52f, speed),
            Mathf.Lerp(7f, 18f, accel),
            Mathf.Lerp(14f, 38f, braking),
            Mathf.Lerp(24f, 68f, handling),
            Mathf.Lerp(22f, 12f, handling));
    }

    public static int Blend(int carStat, int driverStat) => (carStat + driverStat) / 2;

    public static float Unit(int stat) => (Roster.ClampStat(stat) - 1) / 99f;
}

public static class DriverForm
{
    public static float StartingFocus(DriverProfile driver)
    {
        driver.Clamp();
        return driver.Focus * (1f - driver.Tiredness / 100f);
    }

    public static float DecayPerSecond(int focusRating) =>
        Mathf.Lerp(0.45f, 0.04f, Pace.Unit(focusRating));

    public static float Tick(float live, int focusRating, float dt) =>
        Mathf.Max(0f, live - DecayPerSecond(focusRating) * dt);

    public static float CornerPenalty(int handling, int braking, float liveFocus)
    {
        float low = 1f - Mathf.Clamp(liveFocus, 0f, 100f) / 100f;
        float wide = (100 - Roster.ClampStat(handling)) / 100f * low;
        float lockup = (100 - Roster.ClampStat(braking)) / 100f * low;
        return 0.40f * wide + 0.30f * lockup;
    }

    public static float OutsidePush(int handling, float liveFocus)
    {
        float low = 1f - Mathf.Clamp(liveFocus, 0f, 100f) / 100f;
        return (100 - Roster.ClampStat(handling)) / 100f * low;
    }

    public static float AttackEffort(int attack) => Effort(attack);

    public static float DefendEffort(int defend) => Effort(defend);

    /// <summary>How well the driver can place the car. Low Focus drags this down.</summary>
    public static float Control(int handling, int braking, float liveFocus)
    {
        float focus = Mathf.Clamp(liveFocus, 0f, 100f) / 100f;
        return (Pace.Unit(handling) + Pace.Unit(braking) + focus) / 3f;
    }

    /// <summary>
    /// A driver causes a crash only when weak braking or handling shows up because Focus is already low.
    /// A normal overtake is not a mistake.
    /// </summary>
    public static bool CausesAccidents(int handling, int braking, float liveFocus)
    {
        float tired = 1f - Mathf.Clamp(liveFocus, 0f, 100f) / 100f;
        float brakeFault = (1f - Pace.Unit(braking)) * tired;
        float handleFault = (1f - Pace.Unit(handling)) * tired;
        return brakeFault >= 0.42f || handleFault >= 0.42f;
    }

    /// <summary>Metres between lane centers so a pass clears both bodies.</summary>
    public static float RacingGap(float control) =>
        Mathf.Lerp(3.15f, 3.45f, Mathf.Clamp(control, 0f, 1f));

    static float Effort(int rating) => Mathf.Lerp(0.15f, 1f, Pace.Unit(rating));
}

public sealed class TeamEntry
{
    public string Id { get; set; } = "";
    public string Number { get; set; } = "";
    public string Name { get; set; } = "";
    public float[] Paint { get; set; } = [1f, 1f, 1f];
    public string DriverId { get; set; } = "";
    public CarSpec Car { get; set; } = new();

    public string Label => $"{Number} {Name}";

    public Color Color => new(
        Paint.Length > 0 ? Paint[0] : 1f,
        Paint.Length > 1 ? Paint[1] : 1f,
        Paint.Length > 2 ? Paint[2] : 1f);

    public void Clamp() => Car.Clamp();
}

public sealed class Roster
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public List<TeamEntry> Teams { get; } = new();
    public List<DriverProfile> Drivers { get; } = new();

    public DriverProfile Driver(string id)
    {
        foreach (DriverProfile driver in Drivers)
        {
            if (driver.Id == id)
                return driver;
        }
        throw new InvalidOperationException($"Missing driver '{id}'.");
    }

    public static Roster Load() =>
        LoadFrom(Path("res://data/teams.json"), Path("res://data/drivers.json"), Path("res://data/career.json"));

    public static Roster LoadFrom(string teamsPath, string driversPath, string careerPath)
    {
        var roster = new Roster();
        TeamFile teams = Read<TeamFile>(teamsPath);
        DriverFile drivers = Read<DriverFile>(driversPath);
        roster.Teams.AddRange(teams.Teams);
        roster.Drivers.AddRange(drivers.Drivers);
        foreach (TeamEntry team in roster.Teams)
            team.Clamp();
        foreach (DriverProfile driver in roster.Drivers)
            driver.Clamp();

        if (File.Exists(careerPath))
            ApplyCareer(roster, Read<CareerFile>(careerPath));
        else
            SaveCareer(roster, careerPath);
        return roster;
    }

    public static void SaveCareer(Roster roster) =>
        SaveCareer(roster, Path("res://data/career.json"));

    public static void SaveCareer(Roster roster, string careerPath)
    {
        var file = new CareerFile();
        foreach (DriverProfile driver in roster.Drivers)
        {
            driver.Clamp();
            file.Drivers.Add(new CareerDriver
            {
                Id = driver.Id,
                Attack = driver.Attack,
                Defend = driver.Defend,
                Handling = driver.Handling,
                Braking = driver.Braking,
                Focus = driver.Focus,
                Potential = driver.Potential,
                RacesDone = driver.RacesDone,
                Tiredness = driver.Tiredness,
            });
        }
        File.WriteAllText(careerPath, JsonSerializer.Serialize(file, Json));
    }

    static void ApplyCareer(Roster roster, CareerFile career)
    {
        foreach (CareerDriver saved in career.Drivers)
        {
            DriverProfile? driver = roster.Drivers.Find(item => item.Id == saved.Id);
            if (driver == null)
                continue;
            driver.Attack = saved.Attack;
            driver.Defend = saved.Defend;
            driver.Handling = saved.Handling;
            driver.Braking = saved.Braking;
            driver.Focus = saved.Focus;
            driver.Potential = saved.Potential;
            driver.RacesDone = saved.RacesDone;
            driver.Tiredness = saved.Tiredness;
            driver.Clamp();
        }
    }

    static T Read<T>(string path)
    {
        T? value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json);
        if (value == null)
            throw new InvalidOperationException($"Empty JSON at {path}.");
        return value;
    }

    public static string Path(string resource) => ProjectSettings.GlobalizePath(resource);

    public static int ClampStat(int value) => Math.Clamp(value, 1, 100);

    sealed class TeamFile
    {
        public List<TeamEntry> Teams { get; set; } = new();
    }

    sealed class DriverFile
    {
        public List<DriverProfile> Drivers { get; set; } = new();
    }

    sealed class CareerFile
    {
        public List<CareerDriver> Drivers { get; set; } = new();
    }

    sealed class CareerDriver
    {
        public string Id { get; set; } = "";
        public int Attack { get; set; }
        public int Defend { get; set; }
        public int Handling { get; set; }
        public int Braking { get; set; }
        public int Focus { get; set; }
        public int Potential { get; set; }
        public int RacesDone { get; set; }
        public int Tiredness { get; set; }
    }
}
