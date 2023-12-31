Shader "Custom/Grid"
{
	Properties
	{
		_Colour("Grid Colour", color) = (0.5, 0.5, 0.5, 0.5)
		_Colour2("Circle Colour", color) = (1.0, 0, 0, 0.8)
		_Center("Circle Center", Vector) = (0, 0, 0, 0)
		_Radius("Circle Radius", float) = 0.1
		_Spacing("Grid Spacing", float) = 0.1
		_Spacing2("Grid Spacing 2", float) = 0.01
		_Spacing3("Grid Spacing 3", float) = 0.001
		_Thickness("Line Thickness", float) = 0.5
	}
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
				float4 vertex : SV_POSITION;
				float4 posWorld : TEXCOORD2;
			};

			fixed4 _Colour;
			fixed4 _Colour2;
			float4 _Center;
			float _Radius;
			float _Spacing;
			float _Spacing2;
			float _Spacing3;
			float _Thickness;

			v2f vert(appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex + float4(0, -0.16, 0, 0));
				o.posWorld = mul(unity_ObjectToWorld, v.vertex);
				return o;
			}

			// Derived from code by Evan Wallace: http://madebyevan.com/shaders/grid/
			float grid(float2 coord)
			{
				float2 grid = abs(frac(coord - 0.5) - 0.5) / fwidth(coord);
				float lin = min(grid.x, grid.y);
				return 1.0 - min(lin, 1.0);
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
				fixed4 gridCol = _Colour.rgba;
				gridCol.a = min((c+c2+c3+c4) * _Thickness, 1);
				fixed4 circle = _Colour2.rgba;
				if (pow(i.posWorld.x - _Center.x, 2) + pow(i.posWorld.z - _Center.z, 2) > pow(_Radius, 2)) {
					circle = fixed4(0, 0, 0, 0);
				}
				return gridCol + circle;
			}
			ENDCG
		}
	}
}