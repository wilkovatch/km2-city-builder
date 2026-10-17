shader_type spatial;
// Port of the runtime gizmo's "Custom/Lines" shader: vertex colored, always drawn on top.
render_mode unshaded, cull_disabled, depth_test_disabled, depth_draw_never, blend_mix, shadows_disabled;

void fragment() {
	ALBEDO = COLOR.rgb;
	ALPHA = COLOR.a;
}
