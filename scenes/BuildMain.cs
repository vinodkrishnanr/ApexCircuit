using Godot;

namespace ApexCircuit;

public partial class BuildMain : SceneTree
{
    public override void _Initialize()
    {
        var temp = new Node();
        var root = new Node3D { Name = "Main" };
        temp.AddChild(root);

        var skyMat = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.28f, 0.48f, 0.82f),
            SkyHorizonColor = new Color(0.72f, 0.8f, 0.86f),
            GroundBottomColor = new Color(0.18f, 0.28f, 0.16f),
            GroundHorizonColor = new Color(0.45f, 0.5f, 0.4f),
        };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = skyMat },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.85f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            GlowEnabled = true,
            GlowIntensity = 0.35f,
            GlowStrength = 0.7f,
            GlowBloom = 0.05f,
        };
        root.AddChild(new WorldEnvironment { Name = "WorldEnvironment", Environment = environment });

        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-52f, 38f, 0f),
            LightEnergy = 1.45f,
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
            DirectionalShadowMaxDistance = 240f,
        };
        root.AddChild(sun);

        var camera = new Camera3D
        {
            Name = "Camera3D",
            Current = true,
            Fov = 58f,
            Position = new Vector3(0f, 70f, 20f),
        };
        root.AddChild(camera);

        var layer = new CanvasLayer { Name = "HudLayer" };
        var hud = new Label
        {
            Name = "Hud",
            Position = new Vector2(24f, 18f),
            Text = "APEX CIRCUIT",
        };
        hud.AddThemeFontSizeOverride("font_size", 22);
        hud.AddThemeColorOverride("font_color", new Color(0.96f, 0.96f, 0.94f));
        hud.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        hud.AddThemeConstantOverride("outline_size", 6);
        layer.AddChild(hud);
        root.AddChild(layer);

        var script = GD.Load<Script>("res://scripts/RaceSim.cs");
        root.SetScript(script);
        root = temp.GetChild<Node3D>(0);

        SetOwnerRecursive(root, root);
        int expected = CountNodes(root);
        var packed = new PackedScene();
        if (packed.Pack(root) != Error.Ok)
        {
            GD.PushError("Pack failed");
            Quit(1);
            return;
        }
        var test = packed.Instantiate();
        int got = CountNodes(test);
        test.Free();
        if (got < expected)
        {
            GD.PushError($"Scene dropped nodes: expected {expected}, saved {got}");
            Quit(1);
            return;
        }
        Error saved = ResourceSaver.Save(packed, "res://scenes/main.tscn");
        if (saved != Error.Ok)
        {
            GD.PushError($"Save failed: {saved}");
            Quit(1);
            return;
        }
        GD.Print($"Saved main.tscn with {got} nodes");
        Quit();
    }

    static void SetOwnerRecursive(Node node, Node owner)
    {
        foreach (Node child in node.GetChildren())
        {
            if (!string.IsNullOrEmpty(child.SceneFilePath))
                continue;
            child.Owner = owner;
            SetOwnerRecursive(child, owner);
        }
    }

    static int CountNodes(Node node)
    {
        int count = 1;
        foreach (Node child in node.GetChildren())
            count += CountNodes(child);
        return count;
    }
}
