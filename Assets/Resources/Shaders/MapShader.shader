Shader "Custom/Map"
{
	Properties
	{
		_MainTex("Texture", 2D) = "white" {}
		_Size("Size", Vector) = (0, 0, 0, 0)
		_Spacing("Grid Spacing", float) = 0.1
		_Spacing2("Grid Spacing 2", float) = 0.01
		_Spacing3("Grid Spacing 3", float) = 0.001
		_Thickness("Line Thickness", float) = 0.5
		_MapOverlay("Map Overlay", float) = 0.5
		_Opacity("Opacity", float) = 1
	}

	/*SubShader{
		Tags { "RenderType" = "Opaque" }
		Offset 1, -2
		CGPROGRAM
		#pragma surface surf Lambert
		struct Input {
			float2 uv_MainTex;
			float3 worldPos;
		};
		sampler2D _MainTex;
		float4 _Size;


		void surf(Input IN, inout SurfaceOutput o) {
			IN.worldPos.y += 1;
			o.Albedo = tex2D(_MainTex, (IN.worldPos.xz + _Size.yw) / _Size.xz).rgb;
		}
		ENDCG
	}*/
	SubShader
	{
		Tags { "RenderType" = "Transparent" "Queue" = "Transparent"}
		LOD 100

		Blend SrcAlpha OneMinusSrcAlpha
		ZWrite Off

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
				float4 posWorld : TEXCOORD2;
			};

			sampler2D _MainTex;
			float4 _MainTex_ST;
			float4 _Size;
			float _Spacing;
			float _Spacing2;
			float _Spacing3;
			float _Thickness;
			float _MapOverlay;
			float _Opacity;

			// Derived from code by Evan Wallace: http://madebyevan.com/shaders/grid/
			float grid(float2 coord)
			{
				float2 grid = abs(frac(coord - 0.5) - 0.5) / fwidth(coord);
				float lin = min(grid.x, grid.y);
				return 1.0 - min(lin, 1.0);
			}

			v2f vert(appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex + float4(0, -0.16, 0, 0));
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				o.posWorld = mul(unity_ObjectToWorld, v.vertex);
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				float2 coord = i.posWorld.xz;
				float2 coord2 = i.posWorld.xz * _Spacing;
				float2 coord3 = i.posWorld.xz * _Spacing2;
				float2 coord4 = i.posWorld.xz * _Spacing3;
				float c = grid(coord);
				float c2 = grid(coord2);
				float c3 = grid(coord3);
				float c4 = grid(coord4);
				fixed4 gridCol = tex2D(_MainTex, (i.posWorld.xz + _Size.yw) / _Size.xz);
				gridCol.a = clamp(max((c + c2 + c3 + c4)* _Thickness, _MapOverlay) * _Opacity, 0, 1);
				return gridCol;
				//fixed4 col = float4(tex2D(_MainTex, (i.posWorld.xz + _Size.yw) / _Size.xz).rgb, max(grid(i.posWorld.xz), 0.5));
				//return col;
			}
			ENDCG
		}
	}
}