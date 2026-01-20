Shader "Custom/Grass_Rounded_Stable"
{
    Properties
    {
        _TopColor("Color Punta", Color) = (0.2, 0.8, 0.2, 1)
        _BottomColor("Color Base", Color) = (0.1, 0.3, 0.1, 1)
        _BladeHeight("Altura Máxima", Float) = 0.5
        _BladeWidth("Ancho Base", Float) = 0.05
        _TipRoundness("Redondez Punta", Range(0, 1)) = 0.4
        _LeanAmount("Inclinación", Range(0, 0.2)) = 0.05
        _WindSpeed("Velocidad Viento", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _TopColor, _BottomColor;
            float _BladeHeight, _BladeWidth, _TipRoundness, _LeanAmount, _WindSpeed;

            struct v2g { float4 pos : SV_POSITION; };
            struct g2f { float4 pos : SV_POSITION; float4 color : COLOR; };

            float rand(float3 co) {
                return frac(sin(dot(co.xyz, float3(12.9898, 78.233, 45.164))) * 43758.5453);
            }

            float3x3 RotationMatrix(float angleY, float leanX, float leanZ) {
                float sY, cY, sX, cX, sZ, cZ;
                sincos(angleY, sY, cY);
                sincos(leanX, sX, cX);
                sincos(leanZ, sZ, cZ);
                return float3x3(
                    cY, 0, sY,
                    sY*sX, cX, -cY*sX,
                    -sY*cX, sX, cY*cX
                );
            }

            v2g vert (appdata_base v) {
                v2g o;
                o.pos = v.vertex;
                return o;
            }

            [maxvertexcount(4)]
            void geom(triangle v2g IN[3], inout TriangleStream<g2f> triStream) {
                float3 basePos = (IN[0].pos.xyz + IN[1].pos.xyz + IN[2].pos.xyz) / 3.0;
                float r = rand(basePos);
                
                // Rotación y viento
                float3x3 rotMatrix = RotationMatrix(r * 6.28, (r-0.5)*_LeanAmount, (rand(basePos.zyx)-0.5)*_LeanAmount);
                float wind = sin(_Time.y * _WindSpeed + basePos.x) * 0.05;

                float h = _BladeHeight * (0.8 + r * 0.4);
                float w = _BladeWidth;
                float tr = _TipRoundness * w;

                g2f o;

                // CONSTRUCCIÓN MANUAL DE 2 TRIÁNGULOS (QUAD)
                // Usamos un orden de tira (0, 1, 2, 3) que forma un trapecio
                // Vértice 0: Base Izquierda
                float3 p0 = mul(rotMatrix, float3(-w, 0, 0));
                o.pos = UnityObjectToClipPos(float4(basePos + p0, 1));
                o.color = _BottomColor;
                triStream.Append(o);

                // Vértice 1: Base Derecha
                float3 p1 = mul(rotMatrix, float3(w, 0, 0));
                o.pos = UnityObjectToClipPos(float4(basePos + p1, 1));
                o.color = _BottomColor;
                triStream.Append(o);

                // Vértice 2: Punta Izquierda (un poco hacia el centro)
                float3 p2 = mul(rotMatrix, float3(-tr + wind, h, wind));
                o.pos = UnityObjectToClipPos(float4(basePos + p2, 1));
                o.color = _TopColor;
                triStream.Append(o);

                // Vértice 3: Punta Derecha (un poco hacia el centro)
                float3 p3 = mul(rotMatrix, float3(tr + wind, h, wind));
                o.pos = UnityObjectToClipPos(float4(basePos + p3, 1));
                o.color = _TopColor;
                triStream.Append(o);

                triStream.RestartStrip();
            }

            fixed4 frag (g2f i) : SV_Target {
                return i.color;
            }
            ENDCG
        }
    }
}