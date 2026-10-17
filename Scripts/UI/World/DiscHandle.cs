using System.Collections;
using System.Collections.Generic;
using Godot;

public partial class DiscHandle: Node3D {
    public bool show = false;
    public override void _Process(double delta) {
        Visible = show;
        show = false;
    }
}
