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
using UnityEngine;
using UnityEngine.Rendering;

namespace FronkonGames.Artistic.TiltShift
{
  /// <summary> Int slider with reset attribute. </summary>
  [AttributeUsage(AttributeTargets.Field)]
  public class IntSliderWithResetAttribute : PropertyAttribute
  {
    public readonly int min;
    public readonly int max;
    public readonly int value;
    public readonly string tooltip;

    public IntSliderWithResetAttribute(int value, int min, int max, string tooltip = "")
    {
      Debug.Assert(value >= min && value <= max, $"Value {value} is out of range [{min}, {max}]");
      Debug.Assert(min <= max, $"{min} must be less than or equal to {max}");

      this.value = value;
      this.min = min;
      this.max = max;
      this.tooltip = tooltip;
    }
  }

  /// <summary> Float slider with reset attribute. </summary>
  [AttributeUsage(AttributeTargets.Field)]
  public class FloatSliderWithResetAttribute : PropertyAttribute
  {
    public readonly float min;
    public readonly float max;
    public readonly float value;
    public readonly string tooltip;

    public FloatSliderWithResetAttribute(float value, float min, float max, string tooltip = "")
    {
      Debug.Assert(value >= min && value <= max, $"Value {value} is out of range [{min}, {max}]");
      Debug.Assert(min <= max, $"{min} must be less than or equal to {max}");

      this.value = value;
      this.min = min;
      this.max = max;
      this.tooltip = tooltip;
    }
  }

  /// <summary> Toggle with reset attribute. </summary>
  [AttributeUsage(AttributeTargets.Field)]
  public class ToggleWithResetAttribute : PropertyAttribute
  {
    public readonly bool value;
    public readonly string tooltip;

    public ToggleWithResetAttribute(bool value, string tooltip = "")
    {
      this.value = value;
      this.tooltip = tooltip;
    }
  }

  /// <summary> Enum dropdown attribute. </summary>
  [AttributeUsage(AttributeTargets.Field)]
  public class EnumDropdownAttribute : PropertyAttribute
  {
    public readonly string tooltip;
    public readonly int value;

    public EnumDropdownAttribute(int value, string tooltip = "")
    {
      this.tooltip = tooltip;
      this.value = value;
    }
  }

  /// <summary> Color with reset attribute. </summary>
  [AttributeUsage(AttributeTargets.Field)]
  public class ColorWithResetAttribute : PropertyAttribute
  {
    public readonly Color color;
    public readonly string tooltip;

    public ColorWithResetAttribute(uint rgba, string tooltip = "")
    {
      this.color = new Color(
        ((rgba >> 24) & 0xFF) / 255.0f,
        ((rgba >> 16) & 0xFF) / 255.0f,
        ((rgba >> 8) & 0xFF) / 255.0f,
        (rgba & 0xFF) / 255.0f
      );
      this.tooltip = tooltip;
    }
  }

  /// <summary> Enum parameter. </summary>
  [Serializable]
  public class EnumParameter<T> : VolumeParameter<T> where T : Enum
  {
    public EnumParameter(T value, bool overrideState = false) : base(value, overrideState) { }
  }
}
