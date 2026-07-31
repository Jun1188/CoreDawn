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
using UnityEditor;

namespace FronkonGames.Artistic.TiltShift.Editor
{
  /// <summary> Custom VolumeComponentEditor for TiltShiftVolume. </summary>
  [CustomEditor(typeof(TiltShiftVolume))]
  public sealed class TiltShiftVolumeInspector : Inspector
  {
    /// <inheritdoc/>
    protected override void InspectorGUI()
    {
      DrawFloatSliderWithReset("intensity");

      Separator();

      DrawEnumDropdownWithReset("quality", "Quality", Quality.High);
      DrawFloatSliderWithReset("angle");
      DrawFloatSliderWithReset("aperture");
      DrawFloatSliderWithReset("offset");

      DrawFloatSliderWithReset("blur");
      IndentLevel++;
      DrawFloatSliderWithReset("blurCurve", "Curve");
      IndentLevel--;

      DrawFloatSliderWithReset("distortion");
      IndentLevel++;
      DrawFloatSliderWithReset("distortionScale", "Scale");
      IndentLevel--;

      GUILayout.Label("Focused zone");
      IndentLevel++;
      DrawFloatSliderWithReset("focusedBrightness", "Brightness");
      DrawFloatSliderWithReset("focusedContrast", "Contrast");
      DrawFloatSliderWithReset("focusedGamma", "Gamma");
      DrawFloatSliderWithReset("focusedHue", "Hue");
      DrawFloatSliderWithReset("focusedSaturation", "Saturation");
      IndentLevel--;

      GUILayout.Label("Unfocused zone");
      IndentLevel++;
      DrawFloatSliderWithReset("unfocusedBrightness", "Brightness");
      DrawFloatSliderWithReset("unfocusedContrast", "Contrast");
      DrawFloatSliderWithReset("unfocusedGamma", "Gamma");
      DrawFloatSliderWithReset("unfocusedHue", "Hue");
      DrawFloatSliderWithReset("unfocusedSaturation", "Saturation");
      IndentLevel--;

      Separator();

      DrawToggleWithReset("debugView", false);
    }

    protected override void ResetValues() => ((TiltShiftVolume)target).Reset();

    protected override void CheckForErrors()
    {
      if (TiltShift.IsInAnyRenderFeatures() == false)
      {
        Separator();

        EditorGUILayout.HelpBox($"Renderer Feature '{Constants.Asset.Name}' not found. You must add it as a Render Feature.", MessageType.Error);
      }
      else
      {
        TiltShift[] effects = TiltShift.Instances;

        bool anyEnabled = false;
        for (int i = 0; i < effects.Length; i++)
        {
          if (effects[i].isActive == true)
          {
            anyEnabled = true;
            break;
          }
        }

        if (anyEnabled == false)
        {
          Separator();

          EditorGUILayout.HelpBox($"No Renderer Feature '{Constants.Asset.Name}' is active. You must activate it in the Render Features.", MessageType.Warning);
        }
      }
    }
  }
}
