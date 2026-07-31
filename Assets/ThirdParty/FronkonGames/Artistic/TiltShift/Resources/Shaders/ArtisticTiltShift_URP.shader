////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) Martin Bustos @FronkonGames <fronkongames@gmail.com>. All rights reserved.
//
// THIS FILE CAN NOT BE HOSTED IN PUBLIC REPOSITORIES.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
// WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
Shader "Hidden/Fronkon Games/Artistic/Tilt Shift URP"
{
  Properties
  {
    _MainTex("Main Texture", 2D) = "white" {}
  }

  HLSLINCLUDE
  static const float ReferenceResolution = 1080.0;

  #if defined(QUALITY_FAST)
  static const int WeightSamples = 3;
  static const float Weight[WeightSamples] =
  {
    0.413035,
    0.250935,
    0.056270
  };
  #elif defined (QUALITY_NORMAL)
  static const int WeightSamples = 6;
  static const float Weight[WeightSamples] =
  {
    0.165214,
    0.138082,
    0.120098,
    0.080612,
    0.062210,
    0.022508
  };
  #else
  static const int WeightSamples = 11;
  static const float Weight[WeightSamples] =
  {
    0.082607,
    0.080977,
    0.076276,
    0.069041,
    0.060049,
    0.050187,
    0.040306,
    0.031105,
    0.023066,
    0.016436,
    0.011254
  };
  #endif
  ENDHLSL

  SubShader
  {
    Tags
    {
      "RenderType" = "Opaque"
      "RenderPipeline" = "UniversalPipeline"
    }
    LOD 100
    ZTest Always ZWrite Off Cull Off

    Pass
    {
      Name "Fronkon Games Artistic Tilt Shift Pass 0"

      HLSLPROGRAM
      #pragma vertex ArtisticVert
      #pragma fragment ArtisticFrag
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma exclude_renderers d3d9 d3d11_9x ps3 flash
      #pragma multi_compile _ QUALITY_FAST QUALITY_NORMAL

      #include "Artistic.hlsl"

      float _Blur;
      float _BlurCurve;

      float _Angle;
      float _Aperture;
      float _Offset;

      float _Distortion;
      float _DistortionScale;

      half4 ArtisticFrag(ArtisticVaryings input) : SV_Target
      {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        const float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord).xy;
        const half4 color = SAMPLE_MAIN(uv);
        half4 pixel = color;

        float aspectRatio = _ScreenParams.y / _ScreenParams.x;
        float blurMask = 0.0;

        float2 h = uv - float2(0.5, 0.5);
        float r2 = dot(h, h);

        float2 uvAspect = (1.0 + r2 * (_Distortion * sqrt(r2))) * _DistortionScale * h + 0.5;
        uvAspect.y += aspectRatio * 0.5 - 0.5;
        uvAspect.y /= aspectRatio;
        uvAspect = uvAspect * 2.0 - 1.0;
        uvAspect *= _Aperture;

        float2 tiltVector = float2(sin(_Angle), cos(_Angle));
        blurMask = abs(dot(tiltVector, uvAspect) + _Offset);
        blurMask = max(0.0, min(1.0, blurMask));

        pixel.a = blurMask;
        blurMask = SafePositivePow_float(blurMask, _BlurCurve);

        UNITY_BRANCH
        if (blurMask > 0.0)
        {
          float resolutionScale = _ScreenParams.y / ReferenceResolution;
          float uvOffset = TEXEL_SIZE.x * blurMask * _Blur * resolutionScale;
          pixel.rgb *= Weight[0];

          for (int i = 1; i < WeightSamples; i++)
          {
            float sampleOffset = i * uvOffset;

            pixel.rgb += (SAMPLE_MAIN_LOD(uv + float2(sampleOffset, 0.0)).rgb +
                          SAMPLE_MAIN_LOD(uv - float2(sampleOffset, 0.0)).rgb) * Weight[i];
          }
        }

        return lerp(color, pixel, _Intensity);
      }
      ENDHLSL
    }

    Pass
    {
      Name "Fronkon Games Artistic Tilt Shift Pass 1"

      HLSLPROGRAM
      #pragma vertex ArtisticVert
      #pragma fragment ArtisticFrag
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma exclude_renderers d3d9 d3d11_9x ps3 flash
      #pragma multi_compile ___ DEBUG_VIEW
      #pragma multi_compile _ QUALITY_FAST QUALITY_NORMAL

      #include "Artistic.hlsl"

      float _Blur;
      float _BlurCurve;

      float _FocusedBrightness;
      float _FocusedContrast;
      float _FocusedGamma;
      float _FocusedHue;
      float _FocusedSaturation;

      float _UnfocusedBrightness;
      float _UnfocusedContrast;
      float _UnfocusedGamma;
      float _UnfocusedHue;
      float _UnfocusedSaturation;

#if DEBUG_VIEW
      inline half3 ViewBlur(float blur)
      {
        return (0.5 + 0.5 * smoothstep(0.0, 0.1, blur)) * half3(smoothstep(0.5, 0.3, blur),
                                                                 blur < 0.3 ? smoothstep(0.0, 0.3, blur) : smoothstep(1.0, 0.6, blur),
    	                                                           smoothstep(0.4, 0.6, blur));
      }

      inline float3 BlendOverlay(const float3 s, const float3 d)
      {
        return (s > 0.5) ? 1.0 - 2.0 * (1.0 - s) * (1.0 - d) : 2.0 * s * d;
      }
#endif

      half4 ArtisticFrag(ArtisticVaryings input) : SV_Target
      {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        const float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord).xy;
        const half4 color = SAMPLE_MAIN(uv);
        half4 pixel = color;

        float blurMask = SafePositivePow_float(pixel.a, _BlurCurve);
        UNITY_BRANCH
        if (blurMask > 0.0)
        {
          float resolutionScale = _ScreenParams.y / ReferenceResolution;
          float uvOffset = TEXEL_SIZE.y * blurMask * _Blur * resolutionScale;
          pixel.rgb *= Weight[0];

          for (int i = 1; i < WeightSamples; i++)
          {
            float sampleOffset = i * uvOffset;

            pixel.rgb += (SAMPLE_MAIN_LOD(uv + float2(0.0, sampleOffset)).rgb +
                          SAMPLE_MAIN_LOD(uv - float2(0.0, sampleOffset)).rgb) * Weight[i];
          }
        }

        half3 focus = lerp(ColorAdjust(pixel.rgb, _FocusedContrast, _FocusedBrightness, _FocusedHue, _FocusedGamma, _FocusedSaturation), pixel.rgb, blurMask);
        half3 unfocus = lerp(pixel.rgb, ColorAdjust(pixel.rgb, _UnfocusedContrast, _UnfocusedBrightness, _UnfocusedHue, _UnfocusedGamma, _UnfocusedSaturation), blurMask);
        pixel.rgb = lerp(focus, unfocus, blurMask);

        // Color adjust.
        pixel.rgb = ColorAdjust(pixel.rgb, _Contrast, _Brightness, _Hue, _Gamma, _Saturation);

#if DEBUG_VIEW
        pixel.rgb = BlendOverlay(pixel.rgb, ViewBlur(1.0 - pixel.a));
#endif

        return lerp(color, pixel, _Intensity);
      }
      ENDHLSL
    }    
  }
  
  FallBack "Diffuse"
}
