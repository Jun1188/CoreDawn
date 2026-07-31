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
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

namespace FronkonGames.Artistic.TiltShift
{
  ///------------------------------------------------------------------------------------------------------------------
  /// <summary> Render Pass. </summary>
  /// <remarks> Only available for Universal Render Pipeline. </remarks>
  ///------------------------------------------------------------------------------------------------------------------
  public sealed partial class TiltShift
  {
    [DisallowMultipleRendererFeature]
    private sealed class RenderPass : ScriptableRenderPass
    {
      // Internal use only.
      internal Material material { get; set; }

      private static class ShaderIDs
      {
        internal static readonly int Intensity = Shader.PropertyToID("_Intensity");

        internal static readonly int Angle = Shader.PropertyToID("_Angle");
        internal static readonly int Aperture = Shader.PropertyToID("_Aperture");
        internal static readonly int Offset = Shader.PropertyToID("_Offset");
        internal static readonly int Blur = Shader.PropertyToID("_Blur");
        internal static readonly int BlurCurve = Shader.PropertyToID("_BlurCurve");
        internal static readonly int Distortion = Shader.PropertyToID("_Distortion");
        internal static readonly int DistortionScale = Shader.PropertyToID("_DistortionScale");
        internal static readonly int FocusedBrightness = Shader.PropertyToID("_FocusedBrightness");
        internal static readonly int FocusedContrast = Shader.PropertyToID("_FocusedContrast");
        internal static readonly int FocusedGamma = Shader.PropertyToID("_FocusedGamma");
        internal static readonly int FocusedHue = Shader.PropertyToID("_FocusedHue");
        internal static readonly int FocusedSaturation = Shader.PropertyToID("_FocusedSaturation");
        internal static readonly int UnfocusedBrightness = Shader.PropertyToID("_UnfocusedBrightness");
        internal static readonly int UnfocusedContrast = Shader.PropertyToID("_UnfocusedContrast");
        internal static readonly int UnfocusedGamma = Shader.PropertyToID("_UnfocusedGamma");
        internal static readonly int UnfocusedHue = Shader.PropertyToID("_UnfocusedHue");
        internal static readonly int UnfocusedSaturation = Shader.PropertyToID("_UnfocusedSaturation");

        internal static readonly int Brightness = Shader.PropertyToID("_Brightness");
        internal static readonly int Contrast = Shader.PropertyToID("_Contrast");
        internal static readonly int Gamma = Shader.PropertyToID("_Gamma");
        internal static readonly int Hue = Shader.PropertyToID("_Hue");
        internal static readonly int Saturation = Shader.PropertyToID("_Saturation");
      }

      private static class Keywords
      {
        internal static readonly string QualityFast = "QUALITY_FAST";
        internal static readonly string QualityNormal = "QUALITY_NORMAL";
        internal static readonly string DebugView = "DEBUG_VIEW";
      }

      /// <summary> Render pass constructor. </summary>
      public RenderPass() : base()
      {
        profilingSampler = new ProfilingSampler(Constants.Asset.AssemblyName);
      }

      /// <summary> Destroy the render pass. </summary>
      ~RenderPass() => material = null;

      private void UpdateMaterial(TiltShiftVolume volume)
      {
        material.shaderKeywords = null;

        switch (volume.quality.value)
        {
          case Quality.Fast: material.EnableKeyword(Keywords.QualityFast); break;
          case Quality.Normal: material.EnableKeyword(Keywords.QualityNormal); break;
          case Quality.High: break;
        }

        material.SetFloat(ShaderIDs.Intensity, volume.intensity.value);

#if UNITY_EDITOR
        if (volume.debugView.value == true)
          material.EnableKeyword(Keywords.DebugView);
#endif
        material.SetFloat(ShaderIDs.Angle, Mathf.Deg2Rad * volume.angle.value);
        material.SetFloat(ShaderIDs.Aperture, volume.aperture.value);
        material.SetFloat(ShaderIDs.Offset, volume.offset.value);

        material.SetFloat(ShaderIDs.BlurCurve, volume.blurCurve.value);
        material.SetFloat(ShaderIDs.Blur, volume.blur.value * (int)volume.quality.value);
        material.SetFloat(ShaderIDs.Distortion, volume.distortion.value);
        material.SetFloat(ShaderIDs.DistortionScale, volume.distortionScale.value);

        material.SetFloat(ShaderIDs.FocusedBrightness, volume.focusedBrightness.value);
        material.SetFloat(ShaderIDs.FocusedContrast, volume.focusedContrast.value);
        material.SetFloat(ShaderIDs.FocusedGamma, 1.0f / volume.focusedGamma.value);
        material.SetFloat(ShaderIDs.FocusedHue, volume.focusedHue.value);
        material.SetFloat(ShaderIDs.FocusedSaturation, volume.focusedSaturation.value);

        material.SetFloat(ShaderIDs.UnfocusedBrightness, volume.unfocusedBrightness.value);
        material.SetFloat(ShaderIDs.UnfocusedContrast, volume.unfocusedContrast.value);
        material.SetFloat(ShaderIDs.UnfocusedGamma, 1.0f / volume.unfocusedGamma.value);
        material.SetFloat(ShaderIDs.UnfocusedHue, volume.unfocusedHue.value);
        material.SetFloat(ShaderIDs.UnfocusedSaturation, volume.unfocusedSaturation.value);

        material.SetFloat(ShaderIDs.Brightness, volume.brightness.value);
        material.SetFloat(ShaderIDs.Contrast, volume.contrast.value);
        material.SetFloat(ShaderIDs.Gamma, 1.0f / volume.gamma.value);
        material.SetFloat(ShaderIDs.Hue, volume.hue.value);
        material.SetFloat(ShaderIDs.Saturation, volume.saturation.value);
      }

      /// <inheritdoc/>
      public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
      {
        TiltShiftVolume volume = VolumeManager.instance.stack?.GetComponent<TiltShiftVolume>();
        if (volume == null || volume.IsActive() == false || material == null)
          return;

        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        if (resourceData.isActiveTargetBackBuffer == true)
          return;

        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
        if (cameraData.camera.cameraType == CameraType.SceneView && volume.affectSceneView.value == false || cameraData.postProcessEnabled == false)
          return;

        TextureHandle source = resourceData.activeColorTexture;
        TextureDesc alphaDesc = source.GetDescriptor(renderGraph);
        alphaDesc.colorFormat = QualitySettings.activeColorSpace == ColorSpace.Linear ? UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB : UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm;
        TextureDesc sourceDesc = source.GetDescriptor(renderGraph);

        UpdateMaterial(volume);

        TextureHandle renderTextureHandle0 = renderGraph.CreateTexture(alphaDesc);
        TextureHandle renderTextureHandle1 = renderGraph.CreateTexture(sourceDesc);

        renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, renderTextureHandle0, material, 0), $"{Constants.Asset.AssemblyName}.Pass0");
        renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(renderTextureHandle0, renderTextureHandle1, material, 1), $"{Constants.Asset.AssemblyName}.Pass1");

        resourceData.cameraColor = renderTextureHandle1;
      }
    }
  }
}
