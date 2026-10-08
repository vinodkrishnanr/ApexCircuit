using System.Text;
using Godot;

namespace ApexCircuit;

public partial class RaceSim : Node3D
{
    // Slower cars start ahead so the field is still passing each other on camera.
    static readonly int[] StartOrder = { 4, 2, 6, 1, 5, 0, 3, 7 };

    readonly List<AiCar> _cars = new();
    readonly Dictionary<(int, int), float> _hitLock = new();
    float _raceTime;
    TrackPath _path = null!;
    Camera3D _camera = null!;
    RichTextLabel _hud = null!;
    Label _stats = null!;
    Vector3 _camPos;
    Vector3 _camVel;
    Vector3 _camLook;
    Vector3 _camLookVel;
    Vector3 _camForward = Vector3.Forward;
    bool _camReady;
    bool _overhead;
    float _height = 64f;

    public override void _Ready()
    {
        _camera = GetNode<Camera3D>("Camera3D");
        GetNode<Label>("HudLayer/Hud").Visible = false;
        _hud = new RichTextLabel
        {
            Name = "Order",
            BbcodeEnabled = true,
            Position = new Vector2(24f, 18f),
            Size = new Vector2(560f, 460f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ScrollActive = false,
        };
        _hud.AddThemeFontSizeOverride("normal_font_size", 22);
        _hud.AddThemeColorOverride("default_color", new Color(0.96f, 0.96f, 0.94f));
        _hud.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        _hud.AddThemeConstantOverride("outline_size", 6);
        GetNode<CanvasLayer>("HudLayer").AddChild(_hud);
        _path = TrackCourse.Build(this);
        Roster roster = Roster.Load();

        const float spacing = 6.6f;
        float nose = 36f + (StartOrder.Length - 1) * spacing;
        for (int slot = 0; slot < StartOrder.Length; slot++)
        {
            int index = StartOrder[slot];
            TeamEntry team = roster.Teams[index];
            DriverProfile driver = roster.Driver(team.DriverId);
            var car = new AiCar();
            AddChild(car);
            float lane = slot % 2 == 0 ? -1.15f : 1.15f;
            car.Spawn(_path, index, team.Label, team.Color, team.Car, driver, nose - slot * spacing, lane);
            _cars.Add(car);
        }

        AimCamera(1f);
        AddStats();
        WriteHud();
    }

    void AddStats()
    {
        var stats = new Label
        {
            Name = "Stats",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(24f, 18f),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        stats.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        stats.OffsetLeft = -460f;
        stats.OffsetTop = 18f;
        stats.OffsetRight = -20f;
        stats.OffsetBottom = 520f;
        stats.GrowHorizontal = Control.GrowDirection.Begin;
        stats.AddThemeFontSizeOverride("font_size", 12);
        stats.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.86f));
        stats.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        stats.AddThemeConstantOverride("outline_size", 4);
        GetNode<CanvasLayer>("HudLayer").AddChild(stats);
        _stats = stats;
    }

    string StatsText(List<AiCar> order)
    {
        var text = new StringBuilder();
        for (int i = 0; i < order.Count; i++)
        {
            AiCar car = order[i];
            text.Append(i + 1);
            text.Append("  ");
            CarSpec spec = car.Car!;
            DriverProfile driver = car.Driver!;
            text.AppendLine(car.CarName);
            text.Append("  Car  Spd ");
            text.Append(spec.Speed);
            text.Append("  Acc ");
            text.Append(spec.Acceleration);
            text.Append("  Han ");
            text.Append(spec.Handling);
            text.Append("  Brk ");
            text.Append(spec.Braking);
            text.AppendLine();
            text.Append("  ");
            text.Append(driver.Name);
            text.Append("  Atk ");
            text.Append(driver.Attack);
            text.Append("  Def ");
            text.Append(driver.Defend);
            text.Append("  Han ");
            text.Append(driver.Handling);
            text.Append("  Brk ");
            text.Append(driver.Braking);
            text.Append("  Foc ");
            text.Append(Mathf.RoundToInt(car.LiveFocus));
            text.Append("/");
            text.Append(driver.Focus);
            text.Append("  Pot ");
            text.Append(driver.Potential);
            text.Append("  Dmg ");
            text.Append(Mathf.RoundToInt(car.Damage));
            text.AppendLine();
        }
        return text.ToString().TrimEnd();
    }

    public override void _PhysicsProcess(double delta) => Step((float)delta);

    public void Step(float dt)
    {
        _raceTime += dt;
        foreach (AiCar car in _cars)
            car.Plan(dt, _path, _cars);
        foreach (AiCar car in _cars)
            car.Apply(dt, _path);
        Resolve(_cars, _path, _hitLock, ref _raceTime);
    }

    public static void Resolve(IReadOnlyList<AiCar> cars, TrackPath path, Dictionary<(int, int), float> hitLock, ref float raceTime)
    {
        for (int pass = 0; pass < 8; pass++)
        {
            bool any = false;
            for (int i = 0; i < cars.Count; i++)
            {
                for (int j = i + 1; j < cars.Count; j++)
                {
                    if (PushApart(cars[i], cars[j], path, hitLock, ref raceTime, pass == 0))
                        any = true;
                }
            }
            if (!any)
                break;
        }
    }

    public override void _Process(double delta)
    {
        AimCamera((float)delta);
        WriteHud();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key && key.Keycode == Key.C)
            _overhead = !_overhead;
        if (@event is InputEventMouseButton { Pressed: true } mouse)
        {
            if (mouse.ButtonIndex == MouseButton.WheelUp)
                _height = Mathf.Max(40f, _height - 4f);
            else if (mouse.ButtonIndex == MouseButton.WheelDown)
                _height = Mathf.Min(110f, _height + 4f);
        }
    }

    public IReadOnlyList<AiCar> Cars => _cars;

    static bool PushApart(AiCar a, AiCar b, TrackPath path, Dictionary<(int, int), float> hitLock, ref float raceTime, bool scoreHit)
    {
        int lo = Math.Min(a.Index, b.Index);
        int hi = Math.Max(a.Index, b.Index);
        (int, int) key = (lo, hi);
        if (!AiCar.Overlap(a, b, out Vector3 axis, out float penetration))
        {
            if (hitLock.TryGetValue(key, out float at) && raceTime - at > 0.8f)
                hitLock.Remove(key);
            return false;
        }

        Vector3 shift = axis * ((penetration + 0.15f) * 0.5f);
        float approach = -(a.Velocity - b.Velocity).Dot(axis);
        bool carelessA = Careless(a);
        bool carelessB = Careless(b);
        // Side-by-side running and a normal pass have little closing speed. Damage is a late brake or a dive from a weak driver.
        bool crash = scoreHit && (carelessA || carelessB) && approach > 5f && !hitLock.ContainsKey(key);
        float damage = 0f;
        if (crash)
        {
            damage = Mathf.Clamp(approach * 0.7f, 5f, 16f);
            hitLock[key] = raceTime;
        }

        // Cancel the closing speed so the cars separate, without throwing them across the road.
        float bleed = crash ? approach : Mathf.Min(Mathf.Max(approach, 0f), 0.8f);
        Vector3 cancel = axis * (bleed * 0.5f);
        float shedA = crash && carelessA ? Mathf.Min(damage * 0.16f, 2.5f) : 0f;
        float shedB = crash && carelessB ? Mathf.Min(damage * 0.16f, 2.5f) : 0f;
        float dmgA = crash && carelessA ? damage : 0f;
        float dmgB = crash && carelessB ? damage : 0f;
        a.Absorb(shift, cancel - a.FlatForward * shedA, dmgA, path);
        b.Absorb(-shift, -cancel - b.FlatForward * shedB, dmgB, path);
        return true;
    }

    static bool Careless(AiCar car)
    {
        DriverProfile driver = car.Driver!;
        return DriverForm.CausesAccidents(driver.Handling, driver.Braking, car.LiveFocus);
    }

    void AimCamera(float dt)
    {
        List<AiCar> order = Ordered();
        AiCar leader = order[0];
        Vector3 focus = Vector3.Zero;
        int counted = 0;
        foreach (AiCar car in order)
        {
            float gap = Mathf.PosMod(leader.Progress - car.Progress, _path.Length);
            if (counted > 0 && gap > 42f)
                break;
            focus += car.GlobalPosition;
            counted++;
        }
        focus /= Mathf.Max(counted, 1);
        Vector3 forward = leader.FlatForward;
        if (!_camReady)
            _camForward = forward;
        else
            _camForward = _camForward.Lerp(forward, 1f - Mathf.Exp(-1.15f * dt)).Normalized();
        float back = _overhead ? 4f : 16f;
        float height = _overhead ? _height + 18f : _height;
        Vector3 desired = focus + Vector3.Up * height - _camForward * back;
        Vector3 look = focus + _camForward * 10f + Vector3.Up * 1.5f;
        if (!_camReady)
        {
            _camPos = desired;
            _camLook = look;
            _camReady = true;
        }
        else
        {
            Spring(ref _camPos, ref _camVel, desired, 0.85f, dt);
            Spring(ref _camLook, ref _camLookVel, look, 1.05f, dt);
        }
        _camera.GlobalPosition = _camPos;
        if (_camLook.DistanceSquaredTo(_camPos) > 0.25f)
            _camera.LookAt(_camLook, Vector3.Up);
    }

    static void Spring(ref Vector3 pos, ref Vector3 vel, Vector3 goal, float frequency, float dt)
    {
        float omega = Mathf.Tau * frequency;
        float decay = Mathf.Exp(-omega * dt);
        Vector3 offset = pos - goal;
        Vector3 temp = (vel + offset * omega) * dt;
        vel = (vel - temp * omega) * decay;
        pos = goal + (offset + temp) * decay;
    }

    void WriteHud()
    {
        List<AiCar> order = Ordered();
        var text = new StringBuilder();
        text.AppendLine("APEX CIRCUIT");
        text.Append("Lap ");
        text.Append(order[0].Laps + 1);
        text.AppendLine();
        for (int i = 0; i < order.Count; i++)
        {
            AiCar car = order[i];
            text.Append("[bgcolor=#");
            text.Append(car.Paint.ToHtml(false));
            text.Append("]    [/bgcolor]  ");
            text.Append(i + 1);
            text.Append("  ");
            text.Append(car.CarName);
            text.Append("   ");
            text.Append(Mathf.RoundToInt(car.Speed * 3.6f));
            text.AppendLine(" km/h");
        }
        text.Append(_overhead ? "Camera: overhead" : "Camera: top-down");
        _hud.Text = text.ToString();
        _stats.Text = StatsText(order);
    }

    List<AiCar> Ordered()
    {
        var order = new List<AiCar>(_cars);
        order.Sort((a, b) =>
        {
            float sa = a.Laps * _path.Length + a.Progress;
            float sb = b.Laps * _path.Length + b.Progress;
            return sb.CompareTo(sa);
        });
        return order;
    }
}
