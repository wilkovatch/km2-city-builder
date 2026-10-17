// Shared surface code for the Standard shader variants.
// Godot picks the render pipeline (opaque / alpha scissor / alpha blend) from
// which built-ins a shader writes at compile time, so the Standard
// rendering modes are separate shaders instead of a runtime uniform:
//   Standard            - Opaque      (alpha ignored)
//   StandardCutout      - Cutout      (alpha test against _Cutoff)
//   StandardFade        - Fade        (alpha blended, no depth write)
//   StandardTransparent - Transparent (premultiplied, no depth write)
// GameEngine.Materials.SetBlendMode swaps between them.

// Loaded textures use an anisotropy level of 6.
uniform sampler2D _MainTex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
// Texture tiling (xy) and offset (zw), see GameEngine.Materials.SetTextureScale.
uniform vec4 _MainTex_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _Color : source_color = vec4(1.0);
uniform float _Opacity : hint_range(0.0, 1.0) = 1.0;
// Standard defaults (metallic workflow): smoothness 0.5, metallic 0.
uniform float _Glossiness : hint_range(0.0, 1.0) = 0.5;

vec4 standard_surface(vec2 uv) {
	vec4 surface = texture(_MainTex, uv * _MainTex_ST.xy + _MainTex_ST.zw) * _Color;
	surface.a *= _Opacity;
	return surface;
}

float standard_roughness() {
	return 1.0 - _Glossiness;
}
