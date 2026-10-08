using Godot;

namespace ApexCircuit;

public readonly struct TrackFrame
{
    public TrackFrame(Vector3 position, Vector3 tangent, Vector3 right)
    {
        Position = position;
        Tangent = tangent;
        Right = right;
    }

    public Vector3 Position { get; }
    public Vector3 Tangent { get; }
    public Vector3 Right { get; }
}

public sealed class TrackPath
{
    public TrackPath(Vector3[] points, float[] cumulative, float length)
    {
        Points = points;
        Cumulative = cumulative;
        Length = length;
    }

    public Vector3[] Points { get; }
    public float[] Cumulative { get; }
    public float Length { get; }

    public Vector3 PointAt(float distance)
    {
        float s = Mathf.PosMod(distance, Length);
        int hi = 1;
        while (hi < Cumulative.Length - 1 && Cumulative[hi] < s)
            hi++;
        int lo = hi - 1;
        float span = Mathf.Max(Cumulative[hi] - Cumulative[lo], 0.0001f);
        float t = (s - Cumulative[lo]) / span;
        return Points[lo].Lerp(Points[hi % Points.Length], t);
    }

    public TrackFrame Sample(float distance)
    {
        Vector3 pos = PointAt(distance);
        // A short chord across the polyline, so heading turns as the car moves
        // instead of staying fixed on each straight piece and then snapping.
        const float look = 2.2f;
        Vector3 tangent = PointAt(distance + look) - PointAt(distance - look);
        if (tangent.LengthSquared() < 0.0001f)
            tangent = Vector3.Forward;
        tangent = tangent.Normalized();
        Vector3 flat = new Vector3(tangent.X, 0f, tangent.Z);
        if (flat.LengthSquared() < 0.0001f)
            flat = Vector3.Forward;
        flat = flat.Normalized();
        Vector3 right = Vector3.Up.Cross(flat).Normalized();
        return new TrackFrame(pos, tangent, right);
    }

    public float Curvature(float distance)
    {
        const float step = 6f;
        Vector3 a = Sample(distance).Tangent;
        Vector3 b = Sample(distance + step).Tangent;
        float cross = a.X * b.Z - a.Z * b.X;
        float dot = a.X * b.X + a.Z * b.Z;
        return Mathf.Atan2(cross, dot) / step;
    }
}

public static class TrackCourse
{
    const float Scale = 1f;
    public const float HalfWidth = 5.7f;

    // A long main straight (first edge), then a run of corners back to the start.
    static readonly Vector3[] Controls =
    {
        new(0f, 0.20f, 0f),
        new(180f, 0.22f, 0f),
        new(214f, 0.40f, -36f),
        new(196f, 0.55f, -78f),
        new(140f, 0.48f, -102f),
        new(70f, 0.42f, -88f),
        new(18f, 0.50f, -56f),
        new(-16f, 0.35f, -12f),
    };

    public static TrackPath Build(Node3D parent)
    {
        var path = Resample(Fillet(Controls, 18f), 0.4f);
        AddGrass(parent, path);
        AddRibbon(parent, path, HalfWidth, 0.02f, new Color(0.28f, 0.28f, 0.3f), 0.9f, "Asphalt");
        AddCurb(parent, path, HalfWidth, HalfWidth + 1.15f, "Curbs");
        AddRibbon(parent, path, HalfWidth - 0.15f, 0.045f, new Color(0.92f, 0.92f, 0.9f), 0.7f, "EdgeLeft", -1f, 0.18f);
        AddRibbon(parent, path, HalfWidth - 0.15f, 0.045f, new Color(0.92f, 0.92f, 0.9f), 0.7f, "EdgeRight", 1f, 0.18f);
        AddWalls(parent, path, HalfWidth + 2.4f);
        AddFinish(parent, path);
        AddScenery(parent, path);
        return path;
    }

    readonly struct Corner
    {
        public Corner(Vector3 pin, Vector3 pout, Vector3 center, float radius, float startAngle, float sweep, bool arc)
        {
            Pin = pin;
            Pout = pout;
            Center = center;
            Radius = radius;
            StartAngle = startAngle;
            Sweep = sweep;
            Arc = arc;
        }

        public Vector3 Pin { get; }
        public Vector3 Pout { get; }
        public Vector3 Center { get; }
        public float Radius { get; }
        public float StartAngle { get; }
        public float Sweep { get; }
        public bool Arc { get; }
    }

    // Replace each kink in the control polygon with a circular arc so the
    // mesh edges are curves, not a chain of long straight chords.
    static Vector3[] Fillet(Vector3[] controls, float wantRadius)
    {
        int n = controls.Length;
        var raw = new Vector3[n];
        for (int i = 0; i < n; i++)
            raw[i] = ScalePoint(controls[i]);

        var corners = new Corner[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 prev = raw[(i - 1 + n) % n];
            Vector3 curr = raw[i];
            Vector3 next = raw[(i + 1) % n];
            Vector3 inVec = curr - prev;
            Vector3 outVec = next - curr;
            float inLen = new Vector3(inVec.X, 0f, inVec.Z).Length();
            float outLen = new Vector3(outVec.X, 0f, outVec.Z).Length();
            Vector3 inDir = new Vector3(inVec.X, 0f, inVec.Z) / inLen;
            Vector3 outDir = new Vector3(outVec.X, 0f, outVec.Z) / outLen;
            float phi = Mathf.Acos(Mathf.Clamp(inDir.Dot(outDir), -1f, 1f));
            if (phi < 0.05f || inLen < 0.01f || outLen < 0.01f)
            {
                corners[i] = new Corner(curr, curr, curr, 0f, 0f, 0f, false);
                continue;
            }

            float d = Mathf.Min(wantRadius * Mathf.Tan(phi * 0.5f), 0.46f * Mathf.Min(inLen, outLen));
            float radius = d / Mathf.Tan(phi * 0.5f);
            float yIn = Mathf.Lerp(prev.Y, curr.Y, (inLen - d) / inLen);
            float yOut = Mathf.Lerp(curr.Y, next.Y, d / outLen);
            Vector3 pin = curr - inDir * d;
            Vector3 pout = curr + outDir * d;
            pin.Y = yIn;
            pout.Y = yOut;
            float cross = inDir.X * outDir.Z - inDir.Z * outDir.X;
            Vector3 side = cross >= 0f
                ? new Vector3(-inDir.Z, 0f, inDir.X)
                : new Vector3(inDir.Z, 0f, -inDir.X);
            Vector3 center = new Vector3(pin.X, 0f, pin.Z) + side * radius;
            float a0 = Mathf.Atan2(pin.Z - center.Z, pin.X - center.X);
            float a1 = Mathf.Atan2(pout.Z - center.Z, pout.X - center.X);
            float sweep = Mathf.AngleDifference(a0, a1);
            if (Mathf.Abs(Mathf.Abs(sweep) - phi) > 0.2f)
                sweep += sweep > 0f ? -Mathf.Tau : Mathf.Tau;
            corners[i] = new Corner(pin, pout, center, radius, a0, sweep, true);
        }

        var pts = new List<Vector3>();
        for (int i = 0; i < n; i++)
        {
            Corner corner = corners[i];
            Corner next = corners[(i + 1) % n];
            if (corner.Arc)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(corner.Sweep) / Mathf.DegToRad(3f)));
                for (int s = 0; s <= steps; s++)
                {
                    float u = s / (float)steps;
                    float ang = corner.StartAngle + corner.Sweep * u;
                    float y = Mathf.Lerp(corner.Pin.Y, corner.Pout.Y, u);
                    pts.Add(new Vector3(
                        corner.Center.X + Mathf.Cos(ang) * corner.Radius,
                        y,
                        corner.Center.Z + Mathf.Sin(ang) * corner.Radius));
                }
            }
            else
            {
                pts.Add(corner.Pin);
            }

            Vector3 from = corner.Arc ? corner.Pout : corner.Pin;
            Vector3 to = next.Pin;
            float span = from.DistanceTo(to);
            int straight = Mathf.Max(1, Mathf.CeilToInt(span / 2f));
            for (int s = 1; s < straight; s++)
                pts.Add(from.Lerp(to, s / (float)straight));
        }
        return pts.ToArray();
    }

    static Vector3 ScalePoint(Vector3 p) => new(p.X * Scale, p.Y, p.Z * Scale);

    static TrackPath Resample(Vector3[] dense, float spacing)
    {
        float total = 0f;
        for (int i = 0; i < dense.Length; i++)
            total += dense[i].DistanceTo(dense[(i + 1) % dense.Length]);

        int count = Mathf.Max(8, Mathf.FloorToInt(total / spacing));
        var points = new Vector3[count];
        var cumulative = new float[count + 1];
        float stride = total / count;
        float walked = 0f;
        int seg = 0;
        float segStart = 0f;
        float segLen = dense[0].DistanceTo(dense[1]);
        for (int i = 0; i < count; i++)
        {
            float target = i * stride;
            while (segStart + segLen < target && seg < dense.Length - 1)
            {
                seg++;
                segStart += segLen;
                segLen = dense[seg].DistanceTo(dense[(seg + 1) % dense.Length]);
            }
            float t = segLen < 0.0001f ? 0f : (target - segStart) / segLen;
            points[i] = dense[seg].Lerp(dense[(seg + 1) % dense.Length], Mathf.Clamp(t, 0f, 1f));
            cumulative[i] = target;
            walked = target;
        }
        cumulative[count] = walked + points[count - 1].DistanceTo(points[0]);
        // Recompute cumulative from actual chords so Sample stays consistent.
        cumulative[0] = 0f;
        for (int i = 1; i < count; i++)
            cumulative[i] = cumulative[i - 1] + points[i - 1].DistanceTo(points[i]);
        cumulative[count] = cumulative[count - 1] + points[count - 1].DistanceTo(points[0]);
        return new TrackPath(points, cumulative, cumulative[count]);
    }

    static void AddGrass(Node3D parent, TrackPath path)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetSmoothGroup(0);
        float s = 80f;
        foreach (Vector3 point in path.Points)
            s = Mathf.Max(s, Mathf.Max(Mathf.Abs(point.X), Mathf.Abs(point.Z)));
        s += 48f;
        Vector3[] quad =
        {
            new(-s, -0.05f, -s),
            new(-s, -0.05f, s),
            new(s, -0.05f, s),
            new(s, -0.05f, -s),
        };
        AddQuad(st, quad[0], quad[1], quad[2], quad[3], 0f, 0f, 8f, 8f);
        Commit(parent, st, "Grass", new Color(0.22f, 0.48f, 0.2f), 0.95f);
    }

    static void AddRibbon(Node3D parent, TrackPath path, float halfWidth, float lift, Color color, float rough, string name, float side = 0f, float thickness = 0f)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetSmoothGroup(0);
        int n = path.Points.Length;
        bool strip = thickness > 0.01f;
        float inner = strip ? halfWidth - thickness : halfWidth;
        float outer = halfWidth;
        for (int i = 0; i < n; i++)
        {
            TrackFrame a = FrameAt(path, i);
            TrackFrame b = FrameAt(path, i + 1);
            if (strip)
            {
                float sign = side >= 0f ? 1f : -1f;
                Vector3 a0 = a.Position + a.Right * (sign * inner) + Vector3.Up * lift;
                Vector3 a1 = a.Position + a.Right * (sign * outer) + Vector3.Up * lift;
                Vector3 b0 = b.Position + b.Right * (sign * inner) + Vector3.Up * lift;
                Vector3 b1 = b.Position + b.Right * (sign * outer) + Vector3.Up * lift;
                if (sign > 0f)
                    AddQuad(st, a0, a1, b1, b0, 0f, i, 1f, i + 1f);
                else
                    AddQuad(st, a0, b0, b1, a1, 0f, i, 1f, i + 1f);
            }
            else
            {
                Vector3 aL = a.Position - a.Right * outer + Vector3.Up * lift;
                Vector3 aR = a.Position + a.Right * outer + Vector3.Up * lift;
                Vector3 bL = b.Position - b.Right * outer + Vector3.Up * lift;
                Vector3 bR = b.Position + b.Right * outer + Vector3.Up * lift;
                AddQuad(st, aL, aR, bR, bL, 0f, i * 0.05f, 1f, (i + 1) * 0.05f);
            }
        }
        Commit(parent, st, name, color, rough);
    }

    static void AddCurb(Node3D parent, TrackPath path, float inner, float outer, string name)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetSmoothGroup(0);
        int n = path.Points.Length;
        for (int i = 0; i < n; i++)
        {
            int stripe = Mathf.Max(1, Mathf.RoundToInt(4f / Mathf.Max(path.Length / n, 0.01f)));
            bool red = (i / stripe) % 2 == 0;
            Color color = red ? new Color(0.85f, 0.12f, 0.1f) : new Color(0.92f, 0.92f, 0.9f);
            TrackFrame a = FrameAt(path, i);
            TrackFrame b = FrameAt(path, i + 1);
            AddCurbSide(st, a, b, inner, outer, 1f, color, i);
            AddCurbSide(st, a, b, inner, outer, -1f, color, i);
        }
        var mat = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 0.75f,
        };
        CommitColored(parent, st, name, mat);
    }

    static void AddCurbSide(SurfaceTool st, TrackFrame a, TrackFrame b, float inner, float outer, float sign, Color color, int i)
    {
        Vector3 a0 = a.Position + a.Right * (sign * inner) + Vector3.Up * 0.04f;
        Vector3 a1 = a.Position + a.Right * (sign * outer) + Vector3.Up * 0.04f;
        Vector3 b0 = b.Position + b.Right * (sign * inner) + Vector3.Up * 0.04f;
        Vector3 b1 = b.Position + b.Right * (sign * outer) + Vector3.Up * 0.04f;
        if (sign > 0f)
            AddQuadColored(st, a0, a1, b1, b0, color);
        else
            AddQuadColored(st, a0, b0, b1, a1, color);
        _ = i;
    }

    static void AddWalls(Node3D parent, TrackPath path, float lateral)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetSmoothGroup(0);
        int n = path.Points.Length;
        for (int i = 0; i < n; i++)
        {
            TrackFrame a = FrameAt(path, i);
            TrackFrame b = FrameAt(path, i + 1);
            WallSpan(st, a, b, lateral);
            WallSpan(st, a, b, -lateral);
        }
        Commit(parent, st, "Barriers", new Color(0.55f, 0.56f, 0.58f), 0.65f);
    }

    static void WallSpan(SurfaceTool st, TrackFrame a, TrackFrame b, float lateral)
    {
        Vector3 a0 = a.Position + a.Right * lateral + Vector3.Up * 0.02f;
        Vector3 a1 = a0 + Vector3.Up * 0.85f;
        Vector3 b0 = b.Position + b.Right * lateral + Vector3.Up * 0.02f;
        Vector3 b1 = b0 + Vector3.Up * 0.85f;
        if (lateral >= 0f)
            AddQuad(st, a0, b0, b1, a1, 0f, 0f, 1f, 1f);
        else
            AddQuad(st, a0, a1, b1, b0, 0f, 0f, 1f, 1f);
    }

    static void AddFinish(Node3D parent, TrackPath path)
    {
        TrackFrame a = path.Sample(0f);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetSmoothGroup(uint.MaxValue);
        const int cells = 10;
        float span = HalfWidth * 2f;
        for (int i = 0; i < cells; i++)
        {
            float t0 = -HalfWidth + span * (i / (float)cells);
            float t1 = -HalfWidth + span * ((i + 1) / (float)cells);
            Color color = i % 2 == 0 ? Colors.White : new Color(0.08f, 0.08f, 0.08f);
            Vector3 p0 = a.Position + a.Right * t0 + Vector3.Up * 0.06f;
            Vector3 p1 = a.Position + a.Right * t1 + Vector3.Up * 0.06f;
            Vector3 p2 = p1 + a.Tangent * 1.4f;
            Vector3 p3 = p0 + a.Tangent * 1.4f;
            AddQuadColored(st, p0, p1, p2, p3, color);
        }
        var mat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.6f };
        CommitColored(parent, st, "Finish", mat);

        var postMat = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.12f, 0.12f), Roughness = 0.5f };
        for (int side = -1; side <= 1; side += 2)
        {
            var post = new MeshInstance3D
            {
                Name = side < 0 ? "GantryL" : "GantryR",
                Mesh = new BoxMesh { Size = new Vector3(0.45f, 4.2f, 0.45f) },
                MaterialOverride = postMat,
                Position = a.Position + a.Right * (side * (HalfWidth + 1.6f)) + Vector3.Up * 2.1f,
            };
            parent.AddChild(post);
        }
        var banner = new MeshInstance3D
        {
            Name = "Banner",
            Mesh = new BoxMesh { Size = new Vector3((HalfWidth + 1.6f) * 2f, 0.7f, 0.35f) },
            Position = a.Position + Vector3.Up * 4.3f,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.9f, 0.9f, 0.92f),
                Roughness = 0.55f,
            },
        };
        banner.Basis = Basis.LookingAt(a.Tangent, Vector3.Up);
        parent.AddChild(banner);
    }

    static void AddScenery(Node3D parent, TrackPath path)
    {
        var trunkMat = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.24f, 0.14f), Roughness = 0.9f };
        var leafMat = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.38f, 0.16f), Roughness = 0.85f };
        var standMat = new StandardMaterial3D { AlbedoColor = new Color(0.28f, 0.3f, 0.34f), Roughness = 0.7f };
        var seatMat = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.18f, 0.16f), Roughness = 0.6f };
        var trunk = new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.28f, Height = 1.5f };
        var crown = new SphereMesh { Radius = 1.15f, Height = 2.1f };
        int n = path.Points.Length;
        int stride = Mathf.Max(1, Mathf.RoundToInt(14f / Mathf.Max(path.Length / n, 0.01f)));
        for (int i = 0; i < n; i += stride)
        {
            TrackFrame f = FrameAt(path, i);
            PlaceTree(parent, f.Position + f.Right * 14f, trunk, crown, trunkMat, leafMat, i);
            PlaceTree(parent, f.Position - f.Right * 14f, trunk, crown, trunkMat, leafMat, i + 1);
        }

        TrackFrame start = path.Sample(8f);
        var stand = new MeshInstance3D
        {
            Name = "Grandstand",
            Mesh = new BoxMesh { Size = new Vector3(6f, 2.2f, 18f) },
            MaterialOverride = standMat,
            Position = start.Position - start.Right * 14f + Vector3.Up * 1.1f,
        };
        stand.Basis = Basis.LookingAt(start.Tangent, Vector3.Up);
        parent.AddChild(stand);
        var seats = new MeshInstance3D
        {
            Name = "Seats",
            Mesh = new BoxMesh { Size = new Vector3(5.2f, 0.35f, 16f) },
            MaterialOverride = seatMat,
            Position = stand.Position + Vector3.Up * 1.15f,
            Basis = stand.Basis,
        };
        parent.AddChild(seats);
    }

    static void PlaceTree(Node3D parent, Vector3 at, CylinderMesh trunk, SphereMesh crown, Material trunkMat, Material leafMat, int salt)
    {
        float h = 1.3f + (salt % 5) * 0.18f;
        var stem = new MeshInstance3D
        {
            Mesh = trunk,
            MaterialOverride = trunkMat,
            Position = at + Vector3.Up * (h * 0.5f),
            Scale = new Vector3(1f, h / 1.5f, 1f),
        };
        var top = new MeshInstance3D
        {
            Mesh = crown,
            MaterialOverride = leafMat,
            Position = at + Vector3.Up * (h + 1.1f),
            Scale = new Vector3(1f + (salt % 3) * 0.15f, 1f, 1f),
        };
        parent.AddChild(stem);
        parent.AddChild(top);
    }

    static TrackFrame FrameAt(TrackPath path, int index)
    {
        int n = path.Points.Length;
        int i = ((index % n) + n) % n;
        return path.Sample(path.Cumulative[i]);
    }

    static void AddQuad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u0, float v0, float u1, float v1)
    {
        st.SetUV(new Vector2(u0, v0));
        st.AddVertex(a);
        st.SetUV(new Vector2(u0, v1));
        st.AddVertex(b);
        st.SetUV(new Vector2(u1, v1));
        st.AddVertex(c);
        st.SetUV(new Vector2(u0, v0));
        st.AddVertex(a);
        st.SetUV(new Vector2(u1, v1));
        st.AddVertex(c);
        st.SetUV(new Vector2(u1, v0));
        st.AddVertex(d);
    }

    static void AddQuadColored(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    {
        void Vert(Vector3 p)
        {
            st.SetColor(color);
            st.SetUV(Vector2.Zero);
            st.AddVertex(p);
        }
        Vert(a); Vert(b); Vert(c);
        Vert(a); Vert(c); Vert(d);
    }

    static void Commit(Node3D parent, SurfaceTool st, string name, Color color, float rough)
    {
        st.GenerateNormals();
        st.GenerateTangents();
        var mesh = st.Commit();
        var inst = new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                Roughness = rough,
            },
        };
        parent.AddChild(inst);
    }

    static void CommitColored(Node3D parent, SurfaceTool st, string name, Material material)
    {
        st.GenerateNormals();
        st.GenerateTangents();
        parent.AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = st.Commit(),
            MaterialOverride = material,
        });
    }
}
