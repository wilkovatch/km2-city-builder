//Edit->Snap to grid (G): the placement clicks are snapped to the grid, the gizmo moves, rotates and scales in steps
//(saved in the program settings)
public static class Snapping {
    static bool? enabled = null;

    public static bool Enabled {
        get {
            enabled ??= SettingsManager.Get("snapping", false);
            return enabled.Value;
        }
        set {
            enabled = value;
            SettingsManager.Set("snapping", value);
        }
    }

    public static void Toggle() {
        Enabled = !Enabled;
    }
}
