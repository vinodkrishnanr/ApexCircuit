namespace ApexCircuit;

public static class PaceMapCheck
{
    public static void Run()
    {
        var slowCar = new CarSpec { Speed = 1, Acceleration = 1, Handling = 1, Braking = 1 };
        var fastCar = new CarSpec { Speed = 100, Acceleration = 100, Handling = 100, Braking = 100 };
        var weak = Driver(1);
        var strong = Driver(100);
        Pace slow = Pace.From(slowCar, weak);
        Pace fast = Pace.From(fastCar, strong);
        if (fast.TopSpeed <= slow.TopSpeed || fast.Accel <= slow.Accel || fast.Brake <= slow.Brake || fast.Grip <= slow.Grip)
            throw new InvalidOperationException("Higher ratings must raise top speed, acceleration, braking, and grip.");
        if (fast.Corner >= slow.Corner)
            throw new InvalidOperationException("Higher handling must shed less speed in a corner.");

        var car = new CarSpec { Speed = 70, Acceleration = 70, Handling = 70, Braking = 70 };
        Pace timid = Pace.From(car, Driver(handling: 20, braking: 20));
        Pace sharp = Pace.From(car, Driver(handling: 90, braking: 90));
        if (sharp.Grip <= timid.Grip || sharp.Brake <= timid.Brake)
            throw new InvalidOperationException("Driver handling and braking must change grip and brake force on the same car.");

        var messy = new DriverProfile { Attack = 0, Potential = 200, RacesDone = 5000, Tiredness = -4 };
        messy.Clamp();
        if (messy.Attack != 1 || messy.Potential != 100 || messy.RacesDone != 1000 || messy.Tiredness != 0)
            throw new InvalidOperationException("Ratings must clamp to 1–100, races to 0–1000, and tiredness to 0–100.");
    }

    static DriverProfile Driver(int stat) => Driver(stat, stat);

    static DriverProfile Driver(int handling, int braking) => new()
    {
        Handling = handling,
        Braking = braking,
        Attack = 50,
        Defend = 50,
        Focus = 50,
        Potential = 50,
    };
}
