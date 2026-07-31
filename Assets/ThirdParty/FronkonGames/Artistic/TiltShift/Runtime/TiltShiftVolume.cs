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
using System;
using UnityEngine.Rendering;

namespace FronkonGames.Artistic.TiltShift
{
  /// <summary> Quality modes. </summary>
  public enum Quality
  {
    /// <summary> Quality mode (default), 22 texture samples per pass. </summary>
    High = 1,

    /// <summary> Standard mode, 12 texture samples per pass. </summary>
    Normal = 2,

    /// <summary> Performance mode, 6 texture samples per pass. </summary>
    Fast = 4,
  }

  /// <summary> Tilt Shift volume component. </summary>
  [Serializable, VolumeComponentMenu("Fronkon Games/Artistic/Tilt Shift")]
  public sealed class TiltShiftVolume : VolumeComponent, IPostProcessComponent
  {
    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    #region Common settings.

    [FloatSliderWithReset(1.0f, 0.0f, 1.0f, "Controls the intensity of the effect [0, 1]. If it is 0, the effect will not be active.")]
    public FloatSliderParameterLinear intensity = new(1.0f, 0.0f, 1.0f);

    #endregion
    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    #region Tilt Shift settings.

    [EnumDropdown((int)Quality.High, "Quality modes. Default High.")]
    public EnumParameterNoInterpolation<Quality> quality = new(Quality.High);

    [FloatSliderWithReset(0.0f, -90.0f, 90.0f, "Angle [-90, 90]. Default 0.")]
    public FloatSliderParameterNoInterpolation angle = new(0.0f, -90.0f, 90.0f);

    [FloatSliderWithReset(0.5f, 0.1f, 5.0f, "Effect aperture [0.1, 5]. Default 0.5.")]
    public FloatSliderParameterNoInterpolation aperture = new(0.5f, 0.1f, 5.0f);

    [FloatSliderWithReset(0.0f, -1.5f, 1.5f, "Vertical offset [-1.5, 1.5]. Default 0.")]
    public FloatSliderParameterNoInterpolation offset = new(0.0f, -1.5f, 1.5f);

    [FloatSliderWithReset(3.0f, 1.0f, 10.0f, "Blur curve [1, 10]. Default 3.0.")]
    public FloatSliderParameterNoInterpolation blurCurve = new(3.0f, 1.0f, 10.0f);

    [FloatSliderWithReset(1.0f, 0.0f, 10.0f, "Blur multiplier [0, 10]. Default 1.")]
    public FloatSliderParameterNoInterpolation blur = new(1.0f, 0.0f, 10.0f);

    [FloatSliderWithReset(5.0f, 0.0f, 20.0f, "Distortion force [0, 20]. Default 5.")]
    public FloatSliderParameterNoInterpolation distortion = new(5.0f, 0.0f, 20.0f);

    [FloatSliderWithReset(1.0f, 0.01f, 2.0f, "Distortion scale [0.01, 2]. Default 1.")]
    public FloatSliderParameterNoInterpolation distortionScale = new(1.0f, 0.01f, 2.0f);

    [ToggleWithReset(false, "Debug view, only available in the Editor.")]
    public BoolParameterNoInterpolation debugView = new(false);

    [FloatSliderWithReset(0.0f, -1.0f, 1.0f, "Brightness of the focused area [-1.0, 1.0]. Default 0.")]
    public FloatSliderParameterNoInterpolation focusedBrightness = new(0.0f, -1.0f, 1.0f);

    [FloatSliderWithReset(1.0f, 0.0f, 10.0f, "Contrast of the focused area [0.0, 10.0]. Default 1.")]
    public FloatSliderParameterNoInterpolation focusedContrast = new(1.0f, 0.0f, 10.0f);

    [FloatSliderWithReset(1.0f, 0.1f, 10.0f, "Gamma of the focused area [0.1, 10.0]. Default 1.")]
    public FloatSliderParameterNoInterpolation focusedGamma = new(1.0f, 0.1f, 10.0f);

    [FloatSliderWithReset(0.0f, 0.0f, 1.0f, "The color wheel of the focused area [0.0, 1.0]. Default 0.")]
    public FloatSliderParameterNoInterpolation focusedHue = new(0.0f, 0.0f, 1.0f);

    [FloatSliderWithReset(1.0f, 0.0f, 2.0f, "Intensity of a colors of the focused area [0.0, 2.0]. Default 1.")]
    public FloatSliderParameterNoInterpolation focusedSaturation = new(1.0f, 0.0f, 2.0f);

    [FloatSliderWithReset(0.0f, -1.0f, 1.0f, "Brightness of the unfocused area [-1.0, 1.0]. Default 0.")]
    public FloatSliderParameterNoInterpolation unfocusedBrightness = new(0.0f, -1.0f, 1.0f);

    [FloatSliderWithReset(1.0f, 0.0f, 10.0f, "Contrast of the unfocused area [0.0, 10.0]. Default 1.")]
    public FloatSliderParameterNoInterpolation unfocusedContrast = new(1.0f, 0.0f, 10.0f);

    [FloatSliderWithReset(1.0f, 0.1f, 10.0f, "Gamma of the unfocused area [0.1, 10.0]. Default 1.")]
    public FloatSliderParameterNoInterpolation unfocusedGamma = new(1.0f, 0.1f, 10.0f);

    [FloatSliderWithReset(0.0f, 0.0f, 1.0f, "The color wheel of the unfocused area [0.0, 1.0]. Default 0.")]
    public FloatSliderParameterNoInterpolation unfocusedHue = new(0.0f, 0.0f, 1.0f);

    [FloatSliderWithReset(1.0f, 0.0f, 2.0f, "Intensity of a colors of the unfocused area [0.0, 2.0]. Default 1.")]
    public FloatSliderParameterNoInterpolation unfocusedSaturation = new(1.0f, 0.0f, 2.0f);

    #endregion
    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    #region Color settings.

    [FloatSliderWithReset(0.0f, -1.0f, 1.0f, "Brightness [-1.0, 1.0]. Default 0.")]
    public FloatSliderParameterNoInterpolation brightness = new(0.0f, -1.0f, 1.0f);

    [FloatSliderWithReset(1.0f, 0.0f, 10.0f, "Contrast [0.0, 10.0]. Default 1.")]
    public FloatSliderParameterNoInterpolation contrast = new(1.0f, 0.0f, 10.0f);

    [FloatSliderWithReset(1.0f, 0.1f, 10.0f, "Gamma [0.1, 10.0]. Default 1.")]
    public FloatSliderParameterNoInterpolation gamma = new(1.0f, 0.1f, 10.0f);

    [FloatSliderWithReset(0.0f, 0.0f, 1.0f, "The color wheel [0.0, 1.0]. Default 0.")]
    public FloatSliderParameterNoInterpolation hue = new(0.0f, 0.0f, 1.0f);

    [FloatSliderWithReset(1.0f, 0.0f, 2.0f, "Intensity of a colors [0.0, 2.0]. Default 1.")]
    public FloatSliderParameterNoInterpolation saturation = new(1.0f, 0.0f, 2.0f);

    #endregion
    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    #region Advanced settings.

    [ToggleWithReset(false, "Does it affect the Scene View?")]
    public BoolParameterNoInterpolation affectSceneView = new(false);

    [ToggleWithReset(true, "Use scaled time?")]
    public BoolParameterNoInterpolation useScaledTime = new(true);

    #endregion
    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    /// <inheritdoc/>
    public bool IsActive() => intensity.overrideState == true && intensity.value > 0.0f;

    /// <summary> Reset to default values. </summary>
    public void Reset()
    {
      intensity.value = 1.0f;

      quality.value = Quality.High;
      angle.value = 0.0f;
      aperture.value = 0.5f;
      offset.value = 0.0f;
      blurCurve.value = 3.0f;
      blur.value = 1.0f;
      distortion.value = 5.0f;
      distortionScale.value = 1.0f;
      debugView.value = false;
      focusedBrightness.value = 0.0f;
      focusedContrast.value = 1.0f;
      focusedGamma.value = 1.0f;
      focusedHue.value = 0.0f;
      focusedSaturation.value = 1.0f;
      unfocusedBrightness.value = 0.0f;
      unfocusedContrast.value = 1.0f;
      unfocusedGamma.value = 1.0f;
      unfocusedHue.value = 0.0f;
      unfocusedSaturation.value = 1.0f;

      brightness.value = 0.0f;
      contrast.value = 1.0f;
      gamma.value = 1.0f;
      hue.value = 0.0f;
      saturation.value = 1.0f;

      affectSceneView.value = false;
      useScaledTime.value = true;
    }
  }
}
