using Godot;

namespace ApexCircuit;

public partial class DamageProbe : SceneTree
{
    public override void _Initialize()
    {
        var world = new Node3D();
        Root.AddChild(world);
        TrackPath path = TrackCourse.Build(world);
        Roster roster = Roster.Load();
        var field = new List<AiCar>();
        var locks = new Dictionary<(int, int), float>();
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

        int overlaps = 0;
        const float dt = 1f / 60f;
        for (int frame = 0; frame < 60 * 90; frame++)
        {
            raceTime += dt;
            foreach (AiCar car in field)
                car.Plan(dt, path, field);
            foreach (AiCar car in field)
                car.Apply(dt, path);
            RaceSim.Resolve(field, path, locks, ref raceTime);
            for (int i = 0; i < field.Count; i++)
            {
                for (int j = i + 1; j < field.Count; j++)
                {
                    if (AiCar.Overlap(field[i], field[j], out _, out _))
                        overlaps++;
                }
            }
        }

        foreach (AiCar car in field.OrderByDescending(c => c.Laps * path.Length + c.Progress))
        {
            DriverProfile d = car.Driver!;
            float control = DriverForm.Control(d.Handling, d.Braking, car.LiveFocus);
            GD.Print($"{car.CarName} laps={car.Laps} dmg={car.Damage:0} control={control:0.00} focus={car.LiveFocus:0} careless={DriverForm.CausesAccidents(d.Handling, d.Braking, car.LiveFocus)}");
        }
        GD.Print($"overlaps={overlaps}");
        Quit(0);
    }
}
