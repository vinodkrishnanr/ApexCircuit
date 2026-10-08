using Godot;

namespace ApexCircuit;

public partial class AiCar : Node3D
{
    const float Wall = TrackCourse.HalfWidth - 0.6f;
    const float MinSpeed = 13f;

    readonly Node3D[] _wheels = new Node3D[4];
    StandardMaterial3D _brakeMat = null!;
    StandardMaterial3D _paintMat = null!;
    Node3D _body = null!;

    const float WheelBase = 2.55f;
    // Chassis, nose, and rear wing in local space, plus a gap so the paint cannot touch.
    // Local +Z is the tail (LookingAt aims local -Z along the nose).
    const float BodyCenterZ = 0.11f;
    public const float BodyHalfLength = 2.40f;
    public const float BodyHalfWidth = 1.22f;
    // Body center stays on asphalt, with enough road left for two cars to run side by side.
    const float LaneLimit = TrackCourse.HalfWidth - BodyHalfWidth - 0.75f;

    float _targetSpeed = 30f;
    float _targetLane;
    float _laneBias;
    float _yield;
    float _yaw;
    float _suspVel;
    int _pathHint;
    Vector3 _pos;
    Vector3 _vel;

    public float Progress { get; private set; }
    public int Laps { get; private set; }
    public float Lane { get; private set; }
    public float Speed { get; private set; }
    public Pace Pace { get; private set; }
    public CarSpec? Car { get; private set; }
    public DriverProfile? Driver { get; private set; }
    public int Index { get; private set; }
    public string CarName { get; private set; } = "";
    public Color Paint { get; private set; }
    public Vector3 FlatForward { get; private set; } = Vector3.Forward;
    public float LiveFocus { get; private set; }
    public float Damage { get; private set; }
    public Vector3 Velocity => _vel;
    public Vector3 FlatRight => new(FlatForward.Z, 0f, -FlatForward.X);
    public Vector3 FlatPosition => new(_pos.X, 0f, _pos.Z);
    public Vector3 BodyCenter => FlatPosition - FlatForward * BodyCenterZ;

    public void Spawn(TrackPath path, int index, string carName, Color paint, CarSpec car, DriverProfile driver, float progress, float lane)
    {
        Index = index;
        CarName = carName;
        Paint = paint;
        Car = car;
        Driver = driver;
        Pace = Pace.From(car, driver);
        LiveFocus = DriverForm.StartingFocus(driver);
        Progress = progress;
        Lane = lane;
        Name = carName.Replace(' ', '_');
        BuildBody();
        Plan(0.05f, path, System.Array.Empty<AiCar>());
        Speed = _targetSpeed;
        Lane = _targetLane;
        TrackFrame frame = path.Sample(Progress);
        _pos = frame.Position + frame.Right * Lane;
        FlatForward = new Vector3(frame.Tangent.X, 0f, frame.Tangent.Z).Normalized();
        _yaw = Mathf.Atan2(FlatForward.X, FlatForward.Z);
        _vel = FlatForward * Speed;
        _pathHint = HintNear(path, Progress);
        Place(path, 0f);
    }

    public void Plan(float dt, TrackPath path, IReadOnlyList<AiCar> cars)
    {
        float look = Mathf.Clamp(Speed * 0.42f, 8f, 24f);
        float kNow = path.Curvature(Progress);
        float kAhead = path.Curvature(Progress + look);
        float corner = Mathf.Max(Mathf.Abs(kNow), Mathf.Abs(kAhead));
        float target = Pace.TopSpeed / (1f + corner * Pace.Corner);
        DriverProfile driver = Driver!;
        LiveFocus = DriverForm.Tick(LiveFocus, driver.Focus, dt);
        if (corner > 0.004f)
            target *= 1f - DriverForm.CornerPenalty(driver.Handling, driver.Braking, LiveFocus);
        target *= 1f - Mathf.Clamp(Damage, 0f, 100f) * 0.006f;
        target = Mathf.Clamp(target, MinSpeed * 0.55f, Pace.TopSpeed);

        float inside = 0f;
        if (Mathf.Abs(kAhead) > 0.004f)
        {
            inside = Mathf.Clamp(-Mathf.Sign(kAhead) * Mathf.Abs(kAhead) * 38f, -LaneLimit, LaneLimit);
            inside += Mathf.Sign(kAhead) * DriverForm.OutsidePush(driver.Handling, LiveFocus) * LaneLimit;
        }

        if (_yield > 0f)
            _yield = Mathf.Max(0f, _yield - dt);

        float control = DriverForm.Control(driver.Handling, driver.Braking, LiveFocus);
        float gapWant = DriverForm.RacingGap(control);
        bool careless = DriverForm.CausesAccidents(driver.Handling, driver.Braking, LiveFocus);
        float clearance = BodyHalfWidth * 2f + 0.7f;

        AiCar? blocker = null;
        float closest = 22f;
        foreach (AiCar other in cars)
        {
            if (other == this)
                continue;
            float gap = Mathf.PosMod(other.Progress - Progress, path.Length);
            if (gap < 0.6f || gap > closest)
                continue;
            // Already clear in the other lane: this is a pass, not a car to brake for.
            if (Mathf.Abs(other.Lane - Lane) >= BodyHalfWidth * 2f + 0.7f)
                continue;
            closest = gap;
            blocker = other;
        }

        if (blocker != null)
        {
            float commit = DriverForm.AttackEffort(driver.Attack);
            float side = PassSide(blocker, gapWant);
            float desired = Mathf.Clamp(blocker.Lane + side * gapWant, -LaneLimit, LaneLimit);
            bool room = Mathf.Abs(desired - blocker.Lane) >= gapWant - 0.25f;
            float bias = _yield > 0f || (!room && !careless) ? 0f : desired - inside;
            float rate = careless ? 3.4f : Mathf.Lerp(2.4f, 3.6f, commit);
            _laneBias = Mathf.MoveToward(_laneBias, bias, rate * dt);

            float lateral = Mathf.Abs(Lane - blocker.Lane);
            bool linedUp = lateral >= clearance;
            // Directly behind: match speed so the nose does not hit. Once moved across, keep a small edge and go by.
            if (!linedUp && closest < BodyHalfLength * 2f + 1.4f)
            {
                if (careless)
                    target = Mathf.Min(target, blocker.Speed + Mathf.Lerp(8f, 4f, control));
                else if (lateral < 1.6f)
                    target = Mathf.Min(target, Mathf.Max(blocker.Speed, MinSpeed));
                else
                    target = Mathf.Min(target, blocker.Speed + Mathf.Lerp(3f, 6f, commit));
            }
        }
        else if (Threat(path, cars) is AiCar threat)
        {
            float cover = DriverForm.DefendEffort(driver.Defend);
            float lateral = threat.Lane - Lane;
            float sign = Mathf.Abs(lateral) < 0.25f ? (Index % 2 == 0 ? 1f : -1f) : Mathf.Sign(lateral);
            float desired = Mathf.Clamp(threat.Lane - sign * gapWant, -LaneLimit, LaneLimit);
            if (Mathf.Abs(lateral) >= gapWant - 0.15f)
                desired = inside + _laneBias;
            float rate = (careless ? 0.45f : 0.9f) * Mathf.Max(cover, 0.25f);
            _laneBias = Mathf.MoveToward(_laneBias, desired - inside, rate * dt);
        }
        else if (BoxedIn(path, cars))
        {
            // Stay in the lane until the car just passed is clear. Do not cut back across its nose.
        }
        else
        {
            _laneBias = Mathf.MoveToward(_laneBias, 0f, 0.8f * dt);
        }

        float desiredLane = Mathf.Clamp(inside + _laneBias, -LaneLimit, LaneLimit);
        desiredLane = KeepClear(path, cars, desiredLane, Mathf.Max(gapWant, clearance));
        _targetLane = desiredLane;
        _targetSpeed = target;
    }

    float KeepClear(TrackPath path, IReadOnlyList<AiCar> cars, float lane, float gapWant)
    {
        foreach (AiCar other in cars)
        {
            if (other == this)
                continue;
            float ahead = Mathf.PosMod(other.Progress - Progress, path.Length);
            float behind = Mathf.PosMod(Progress - other.Progress, path.Length);
            float along = Mathf.Min(ahead, behind);
            // Only a car actually alongside needs a swerve. A car a length ahead is handled by braking.
            if (along > BodyHalfLength * 2f + 0.5f)
                continue;
            if (Mathf.Abs(Lane - other.Lane) < 1.1f && along > BodyHalfLength)
                continue;
            float sign = Lane - other.Lane;
            if (Mathf.Abs(sign) < 0.05f)
                sign = Index % 2 == 0 ? 1f : -1f;
            else
                sign = Mathf.Sign(sign);
            float away = other.Lane + sign * gapWant;
            if (sign > 0f)
                lane = Mathf.Max(lane, away);
            else
                lane = Mathf.Min(lane, away);
        }
        return Mathf.Clamp(lane, -LaneLimit, LaneLimit);
    }

    float PassSide(AiCar blocker, float gapWant)
    {
        float left = Mathf.Clamp(blocker.Lane - gapWant, -LaneLimit, LaneLimit);
        float right = Mathf.Clamp(blocker.Lane + gapWant, -LaneLimit, LaneLimit);
        float leftRoom = Mathf.Abs(left - blocker.Lane);
        float rightRoom = Mathf.Abs(right - blocker.Lane);
        if (Mathf.Abs(leftRoom - rightRoom) > 0.35f)
            return leftRoom > rightRoom ? -1f : 1f;
        return Lane >= blocker.Lane ? 1f : -1f;
    }

    bool BoxedIn(TrackPath path, IReadOnlyList<AiCar> cars)
    {
        foreach (AiCar other in cars)
        {
            if (other == this)
                continue;
            float back = Mathf.PosMod(Progress - other.Progress, path.Length);
            if (back < 0.4f || back > 7f)
                continue;
            if (Mathf.Abs(other.Lane - Lane) < BodyHalfWidth * 2f + 0.8f)
                return true;
        }
        return false;
    }

    AiCar? Threat(TrackPath path, IReadOnlyList<AiCar> cars)
    {
        AiCar? threat = null;
        float nearest = 14f;
        foreach (AiCar other in cars)
        {
            if (other == this)
                continue;
            float back = Mathf.PosMod(Progress - other.Progress, path.Length);
            if (back < 0.8f || back > nearest)
                continue;
            if (Mathf.Abs(other.Lane - Lane) > 4.4f)
                continue;
            nearest = back;
            threat = other;
        }
        return threat;
    }

    public void Apply(float dt, TrackPath path)
    {
        float look = Mathf.Clamp(Speed * 0.38f, 7f, 20f);
        TrackFrame aimFrame = path.Sample(Progress + look);
        Vector3 aim = aimFrame.Position + aimFrame.Right * _targetLane - _pos;
        aim.Y = 0f;
        if (aim.LengthSquared() > 0.04f)
        {
            float want = Mathf.Atan2(aim.X, aim.Z);
            float maxSteer = Mathf.Lerp(0.62f, 0.2f, Mathf.Clamp(Speed / Pace.TopSpeed, 0f, 1f));
            float steer = Mathf.Clamp(Mathf.AngleDifference(_yaw, want), -maxSteer, maxSteer);
            _yaw += Speed * Mathf.Tan(steer) / WheelBase * dt;
        }

        Vector3 heading = new Vector3(Mathf.Sin(_yaw), 0f, Mathf.Cos(_yaw));
        float longSpeed = _vel.Dot(heading);
        Vector3 side = _vel - heading * longSpeed;
        float sideSpeed = side.Length();
        if (sideSpeed > 0.0001f)
        {
            float left = Mathf.Max(0f, sideSpeed - Pace.Grip * dt);
            side *= left / sideSpeed;
        }
        float rate = _targetSpeed + 0.4f < longSpeed ? Pace.Brake : Pace.Accel;
        longSpeed = Mathf.MoveToward(longSpeed, _targetSpeed, rate * dt);
        _vel = heading * longSpeed + side;
        _pos.X += _vel.X * dt;
        _pos.Z += _vel.Z * dt;

        Measure(path);
        if (Mathf.Abs(Lane) > Wall)
        {
            TrackFrame wall = path.Sample(Progress);
            float push = (Mathf.Abs(Lane) - Wall) * 36f;
            _vel -= wall.Right * (Mathf.Sign(Lane) * push);
            _pos -= wall.Right * (Mathf.Sign(Lane) * push * dt);
            Lane = Mathf.Clamp(Lane, -Wall, Wall);
        }

        TrackFrame ground = path.Sample(Progress);
        float road = ground.Position.Y + 0.02f;
        _suspVel += (road - _pos.Y) * 85f * dt;
        _suspVel /= 1f + 14f * dt;
        _pos.Y += _suspVel * dt;
        Speed = longSpeed;
        Place(path, dt);
    }

    public void Absorb(Vector3 move, Vector3 velChange, float hitDamage, TrackPath path)
    {
        move.Y = 0f;
        velChange.Y = 0f;
        _pos += move;
        _vel += velChange;
        if (hitDamage > 0f)
        {
            Damage = Mathf.Min(100f, Damage + hitDamage);
            _yield = 3f;
        }
        Measure(path);
        Place(path, 0f);
    }

    public static bool Overlap(AiCar a, AiCar b, out Vector3 axis, out float penetration)
    {
        axis = Vector3.Forward;
        penetration = float.MaxValue;
        Vector3 delta = a.BodyCenter - b.BodyCenter;
        Vector3[] axes = { a.FlatForward, a.FlatRight, b.FlatForward, b.FlatRight };
        foreach (Vector3 raw in axes)
        {
            Vector3 n = new(raw.X, 0f, raw.Z);
            if (n.LengthSquared() < 0.0001f)
                continue;
            n = n.Normalized();
            float dist = delta.Dot(n);
            float overlap = Extent(a, n) + Extent(b, n) - Mathf.Abs(dist);
            if (overlap <= 0f)
                return false;
            if (overlap < penetration)
            {
                penetration = overlap;
                axis = dist < 0f ? -n : n;
            }
        }
        return true;
    }

    static float Extent(AiCar car, Vector3 axis) =>
        BodyHalfLength * Mathf.Abs(car.FlatForward.Dot(axis))
        + BodyHalfWidth * Mathf.Abs(car.FlatRight.Dot(axis));

    static int HintNear(TrackPath path, float distance)
    {
        float s = Mathf.PosMod(distance, path.Length);
        int i = 1;
        while (i < path.Cumulative.Length - 1 && path.Cumulative[i] < s)
            i++;
        return i - 1;
    }

    void Measure(TrackPath path)
    {
        int n = path.Points.Length;
        int best = _pathHint;
        float bestD = FlatDistance(path.Points[best], _pos);
        for (int step = 1; step <= 30; step++)
        {
            int forward = (best + step) % n;
            int back = (best - step + n) % n;
            float df = FlatDistance(path.Points[forward], _pos);
            float db = FlatDistance(path.Points[back], _pos);
            if (df >= bestD && db >= bestD)
                break;
            if (df < db)
            {
                best = forward;
                bestD = df;
            }
            else
            {
                best = back;
                bestD = db;
            }
        }
        _pathHint = best;
        TrackFrame frame = path.Sample(path.Cumulative[best]);
        Vector3 delta = _pos - frame.Position;
        float along = delta.Dot(new Vector3(frame.Tangent.X, 0f, frame.Tangent.Z));
        float next = Mathf.PosMod(path.Cumulative[best] + along, path.Length);
        if (Progress > path.Length * 0.75f && next < path.Length * 0.25f)
            Laps++;
        Progress = next;
        Lane = delta.Dot(frame.Right);
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }

    void Place(TrackPath path, float dt)
    {
        TrackFrame frame = path.Sample(Progress);
        FlatForward = new Vector3(Mathf.Sin(_yaw), 0f, Mathf.Cos(_yaw));
        Position = _pos;
        // Nose is local -Z. Pitch with the road grade so the tires stay on the surface.
        Vector3 nose = new Vector3(FlatForward.X, frame.Tangent.Y, FlatForward.Z);
        if (nose.LengthSquared() > 0.001f)
            Basis = Basis.LookingAt(nose.Normalized(), Vector3.Up);

        float spin = Mathf.Max(Speed, 0f) * dt / 0.34f;
        foreach (Node3D wheel in _wheels)
            wheel.RotateX(spin);

        float sideSpeed = (_vel - FlatForward * _vel.Dot(FlatForward)).Dot(frame.Right);
        float roll = Mathf.Clamp(-sideSpeed * 0.018f, -0.2f, 0.2f);
        float pitch = Mathf.Clamp((Speed - _targetSpeed) * -0.0045f, -0.08f, 0.1f);
        _body.Rotation = new Vector3(pitch, 0f, roll);
        _brakeMat.EmissionEnergyMultiplier = pitch > 0.02f ? 3.5f : 0.2f;
    }

    void BuildBody()
    {
        _body = new Node3D { Name = "Body" };
        AddChild(_body);

        _paintMat = new StandardMaterial3D { AlbedoColor = Paint, Roughness = 0.35f, Metallic = 0.25f };
        StandardMaterial3D paint = _paintMat;
        var glass = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.05f, 0.07f, 0.09f),
            Roughness = 0.12f,
            Metallic = 0.65f,
        };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.08f, 0.09f), Roughness = 0.8f };
        _brakeMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.45f, 0.05f, 0.04f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.08f, 0.05f),
            EmissionEnergyMultiplier = 0.2f,
            Roughness = 0.4f,
        };

        AddBox(_body, "Chassis", new Vector3(1.72f, 0.38f, 4.15f), new Vector3(0f, 0.48f, 0.15f), paint);
        AddBox(_body, "Nose", new Vector3(1.45f, 0.22f, 0.9f), new Vector3(0f, 0.4f, -1.55f), paint);
        AddBox(_body, "Cabin", new Vector3(1.35f, 0.36f, 1.45f), new Vector3(0f, 0.82f, 0.35f), glass);
        AddBox(_body, "Wing", new Vector3(1.7f, 0.08f, 0.28f), new Vector3(0f, 0.95f, 1.85f), paint);
        AddBox(_body, "BrakeL", new Vector3(0.28f, 0.12f, 0.08f), new Vector3(-0.55f, 0.55f, 2.05f), _brakeMat);
        AddBox(_body, "BrakeR", new Vector3(0.28f, 0.12f, 0.08f), new Vector3(0.55f, 0.55f, 2.05f), _brakeMat);

        var wheelMesh = new CylinderMesh { TopRadius = 0.34f, BottomRadius = 0.34f, Height = 0.26f };
        Vector3[] mounts =
        {
            new(-0.86f, 0.34f, -1.28f),
            new(0.86f, 0.34f, -1.28f),
            new(-0.86f, 0.34f, 1.28f),
            new(0.86f, 0.34f, 1.28f),
        };
        for (int i = 0; i < mounts.Length; i++)
        {
            var pivot = new Node3D { Position = mounts[i] };
            var mesh = new MeshInstance3D
            {
                Mesh = wheelMesh,
                MaterialOverride = dark,
                RotationDegrees = new Vector3(0f, 0f, 90f),
            };
            pivot.AddChild(mesh);
            _body.AddChild(pivot);
            _wheels[i] = pivot;
        }

        var tag = new Label3D
        {
            Text = CarName.Length >= 2 ? CarName[..2] : CarName,
            FontSize = 96,
            PixelSize = 0.006f,
            Position = new Vector3(0f, 1.55f, 0f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            OutlineSize = 24,
            Modulate = Colors.White,
            OutlineModulate = new Color(0f, 0f, 0f),
            DoubleSided = true,
        };
        AddChild(tag);
    }

    static void AddBox(Node3D parent, string name, Vector3 size, Vector3 position, Material material)
    {
        parent.AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = new BoxMesh { Size = size },
            Position = position,
            MaterialOverride = material,
        });
    }
}
