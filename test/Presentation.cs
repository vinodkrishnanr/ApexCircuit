using Godot;

namespace ApexCircuit;

public partial class Presentation : SceneTree
{
    public override void _Initialize()
    {
        var packed = GD.Load<PackedScene>("res://scenes/main.tscn");
        if (packed == null)
        {
            GD.PushError("Missing res://scenes/main.tscn");
            Quit(1);
            return;
        }
        Root.AddChild(packed.Instantiate());
    }
}
