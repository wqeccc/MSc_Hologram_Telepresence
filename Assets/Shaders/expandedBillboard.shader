Shader "Custom/expandedBillboard"
{
    Properties
    {
        // patch size
        _ColorTex("Texture", 2D) = "white" {}
        _DepthTex("TextureD", 2D) = "white" {}
        _BodyIndexTex("TextureB", 2D) = "white" {}

        _SizeIncrement("SizeIncrement", Range(0, 0.01)) = 0.001

        _SizeFilter("SizeFilter", Int) = 2
        _sigmaS("SigmaS", Range(0.1,20)) = 3
        _sigmaL("SigmaL", Range(0.1,20)) = 3

        [Toggle] _calculateNormals("Normals", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off  // render both back and front faces

        Pass
        {
            CGPROGRAM

            #pragma target 5.0
            #pragma vertex VS_Main
            #pragma fragment FS_Main
            #include "UnityCG.cginc" 

            // **************************************************************
            // Data structures                                              *
            // **************************************************************
            struct appdata
            {
                float4 vertex   : POSITION;
                float2 uv0      : TEXCOORD0; // Kinect uv
                float2 uv1      : TEXCOORD1; // quad
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float4 color    : COLOR;
            };

            // **************************************************************
            // Vars                                                         *
            // **************************************************************
            sampler2D _ColorTex;
            sampler2D _DepthTex;
            sampler2D _BodyIndexTex;
            
            int _SizeFilter;
            float _sigmaS;
            float _sigmaL;

            float _calculateNormals;
            int _RemoveBackground;
            float camera_calibration[14];
            float camera_width;
            float camera_height;
            float _SizeIncrement;

            #define EPS 1e-5

            // **************************************************************
			// Aux Functions												*
			// **************************************************************
            int textureToDepth(float x, float y)
            { 
                // TextureFormat.RG16
                float4 d = tex2Dlod(_DepthTex, float4(x, y, 0, 0));
                int dr = d.r * 255;
                int dg = d.g * 255;
                return dr | dg << 8;
            }

            float2 transform_2d_point(float2 uv)
            {
                float cx = camera_calibration[0];
                float cy = camera_calibration[1];
                float fx = camera_calibration[2];
                float fy = camera_calibration[3];
                float k1 = camera_calibration[4];
                float k2 = camera_calibration[5];
                float k3 = camera_calibration[6];
                float k4 = camera_calibration[7];
                float k5 = camera_calibration[8];
                float k6 = camera_calibration[9];
                float codx = camera_calibration[10]; // center of distortion is set to 0 for Brown Conrady model
                float cody = camera_calibration[11];
                float p1 = camera_calibration[12];
                float p2 = camera_calibration[13];

                // error, both must be positive
                if (fx <= 0.f && fy <= 0.f)
				{
					return float2(0,0);
				}

                // correction for radial distortion
                float xp_d = (uv[0] - cx) / fx - codx;
                float yp_d = (uv[1] - cy) / fy - cody;

                float rs = xp_d * xp_d + yp_d * yp_d;
                float rss = rs * rs;
                float rsc = rss * rs;
                float a = 1.f + k1 * rs + k2 * rss + k3 * rsc;
                float b = 1.f + k4 * rs + k5 * rss + k6 * rsc;
                float di = (a != 0.f) ? (1.f / a) * b : 1.f * b;

                float2 xy;
                xy[0] = xp_d * di;
                xy[1] = yp_d * di;

                // approximate correction for tangential params
                float two_xy = 2.f * xy[0] * xy[1];
                float xx = xy[0] * xy[0];
                float yy = xy[1] * xy[1];

                xy[0] -= (yy + 3.f * xx) * p2 + two_xy * p1;
                xy[1] -= (xx + 3.f * yy) * p1 + two_xy * p2;

                // add on center of distortion
                xy[0] += codx;
                xy[1] += cody;

                // return transformation_iterative_unproject(camera_calibration, uv, xy, valid, 20);
                return xy;
            }

            float4 estimateNormal(float x, float y)
            {
                float yScale = 0.1;
                float xzScale = 1;
                float deltax = 1.0 / camera_width;
                float deltay = 1.0 / camera_height;
                float sx = textureToDepth(x < camera_width - deltax ? x + deltax : x, y) - textureToDepth(x > 0 ? x - deltax : x, y);
                float sy = textureToDepth(x, y < camera_height - deltay ? y + deltay : y) - textureToDepth(x, y > 0 ? y - deltay : y);

                float4 n = float4(-sx * yScale, sy * yScale, 2 * xzScale, 1);
                return normalize(n);
            }

            float bilateralFilterDepth(float depth, float x, float y)
            {
                if (_sigmaS == 0 || _sigmaL == 0) return depth;
                float sigS = max(_sigmaS, EPS);
                float sigL = max(_sigmaL, EPS);

                float facS = -1. / (2. * sigS * sigS);
                float facL = -1. / (2. * sigL * sigL);

                float sumW = 0.;
                float sumC = 0.;
                float halfSize = floor(sigS * 2);
                float2 textureSize2 = float2(camera_width, camera_height);
                float2 texCoord = float2(x, y);
                float l = depth;

                for (float i = -halfSize; i <= halfSize; i++) {
                    for (float j = -halfSize; j <= halfSize; j++) {
                        float2 pos = float2(i, j);

                        float2 coords = texCoord + pos / textureSize2;
                        int offsetDepth = textureToDepth(coords.x, coords.y);
                        if(offsetDepth == 0) continue;
                        
                        float distS = length(pos);
                        float distL = offsetDepth - l;

                        float wS = exp(facS * (distS * distS));
                        float wL = exp(facL * (distL * distL));
                        float w = wS * wL;

                        sumW += w;
                        sumC += offsetDepth * w;
                    }
                }
                return sumW > 0.0 ? (sumC / sumW) : depth;
            }

            float medianFilterDepth(int depth, float x, float y)
            {
                if (_SizeFilter <= 0) return depth;
                
                int filterSize = min(_SizeFilter, 4); 
                
                float2 texCoord = float2(x, y);
                float2 textureSize2 = float2(camera_width, camera_height);
                int totalElements = (filterSize * 2 + 1) * (filterSize * 2 + 1);

                int arr[81];

                int k = 0;
                for (int i = -filterSize; i <= filterSize; i++) {
                    for (int j = -filterSize; j <= filterSize; j++) {
                        float2 pos = float2(i, j);
                        float2 coords = texCoord + pos / textureSize2;
                        arr[k] = textureToDepth(coords.x, coords.y);
                        k++;
                    }
                }

                // Insertion sort
                for (int j = 1; j < totalElements; ++j)
                {
                    int key = arr[j];
                    int i = j - 1;
                    while (i >= 0 && arr[i] > key)
                    {
                        arr[i + 1] = arr[i];
                        --i;
                    }
                    arr[i + 1] = key;
                }
                
                int medianIndex = totalElements / 2;
                return arr[medianIndex] > 0 ? arr[medianIndex] : depth;
            }

            // **************************************************************
            // Vertex Shader
            // **************************************************************
            v2f VS_Main(appdata v)
            {
                v2f output = (v2f)0;

                // color & depth
                float4 c = tex2Dlod(_ColorTex, float4(v.uv0.x, v.uv0.y, 0, 0));
                int dValue = textureToDepth(v.uv0.x, v.uv0.y);
                
                if (dValue == 0)
                {
                    output.pos = float4(0, 0, 0, 0);
                    return output;
                }

                if (_RemoveBackground) {
                    float bi = tex2Dlod(_BodyIndexTex, float4(v.uv0.x, v.uv0.y, 0, 0)).a;
                    if (bi == 1) {
                        output.pos = float4(0, 0, 0, 0);
                        return output;
                    }
                }

                // median filtering to remove noise
                dValue = medianFilterDepth(dValue, v.uv0.x, v.uv0.y);
                
                // apply bilateral filtering to smooth the surfaces of the face and body
                float filteredD = bilateralFilterDepth(float(dValue), v.uv0.x, v.uv0.y);
                dValue = int(filteredD);

                if (dValue == 0)
                {
                    output.pos = float4(0, 0, 0, 0);
                    return output;
                }

                // Calculate the 3D position of the point cloud's center point
                float dValue2 = dValue / 1000.0; // mm -> m
                float3 centerPos;
                int x = camera_width * v.uv0.x;
                int y = camera_height * v.uv0.y;
                float vertx = float(x);
                float verty = float(camera_height - y);
                float2 xy = transform_2d_point(float2(vertx, verty));

                centerPos.x = xy.x * dValue2;
                centerPos.y = xy.y * dValue2;
                centerPos.z = dValue2;

                // Handling the rotation vector for orienting a billboard towards the camera
                float3 up = UNITY_MATRIX_IT_MV[1].xyz;
                float3 right = UNITY_MATRIX_IT_MV[0].xyz;
                
                if (_calculateNormals == 1) {
                    float4 nVec = estimateNormal(v.uv0.x, v.uv0.y);
                    float nx = nVec.x; float ny = nVec.y; float nz = nVec.z;
                    float n = sqrt(nx*nx + ny*ny + nz*nz);
                    float h1 = max(nx - n, nx + n);
                    float h2 = ny; float h3 = nz;
                    float h = sqrt(h1*h1 + h2*h2 + h3*h3);
                    right = float3(-2 * h1 * h2 / (h*h), 1 - 2 * (h2*h2) / (h*h), -2 * h2 * h3 / (h*h));
                    up = float3(-2 * h1 * h3 / (h*h), -2 * h2 * h3 / (h*h), 1 - 2 * (h3*h3) / (h*h));
                }
                up = normalize(up);
                right = normalize(right);

                // Calculate the dimensions for the left, right, top, and bottom
                float2 xyL = transform_2d_point(float2(vertx - 1, verty));
                float3 posL = float3(xyL.x * dValue2, xyL.y * dValue2, dValue2);
                float sizeL = _SizeIncrement + (distance(centerPos, posL) / 2);

                float2 xyR = transform_2d_point(float2(vertx + 1, verty));
                float3 posR = float3(xyR.x * dValue2, xyR.y * dValue2, dValue2);
                float sizeR = _SizeIncrement + (distance(centerPos, posR) / 2);

                float2 xyT = transform_2d_point(float2(vertx, verty + 1));
                float3 posT = float3(xyT.x * dValue2, xyT.y * dValue2, dValue2);
                float sizeT = _SizeIncrement + (distance(centerPos, posT) / 2);

                float2 xyD = transform_2d_point(float2(vertx, verty - 1));
                float3 posD = float3(xyD.x * dValue2, xyD.y * dValue2, dValue2);
                float sizeD = _SizeIncrement + (distance(centerPos, posD) / 2);

                float3 finalPos = centerPos;
                
                // expend quad
                finalPos += (v.uv1.x > 0.5) ? (sizeR * right) : (-sizeL * right);
                finalPos += (v.uv1.y > 0.5) ? (sizeT * up) : (-sizeD * up);

                // output to Fragment shader
                output.pos = UnityObjectToClipPos(float4(finalPos, 1.0));
                c.a = 1.0;
                output.color = c;

                return output;
            }

            // Fragment Shader -----------------------------------------------
            float4 FS_Main(v2f input) : SV_Target
            {
                // UNITY_APPLY_FOG(input.fogCoord, col);
                return input.color;
            }

            ENDCG
        }
    }
}
