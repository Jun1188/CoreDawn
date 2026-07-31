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
  /// <summary> Float slider parameter with linear interpolation. </summary>
  [Serializable]
  public sealed class FloatSliderParameterLinear : VolumeParameter<float>
  {
    public float min;
    public float max;

    public FloatSliderParameterLinear(float value, float min, float max, bool overrideState = false)
      : base(value, overrideState)
    {
      this.min = min;
      this.max = max;
    }

    public override float value
    {
      get => m_Value;
      set => m_Value = Mathf.Clamp(value, min, max);
    }

    public override void Interp(float from, float to, float t) => m_Value = to * t;
  }

  /// <summary> Float slider parameter with no interpolation. </summary>
  [Serializable]
  public sealed class FloatSliderParameterNoInterpolation : VolumeParameter<float>
  {
    public float min;
    public float max;

    public FloatSliderParameterNoInterpolation(float value, float min, float max, bool overrideState = false)
      : base(value, overrideState)
    {
      this.min = min;
      this.max = max;
    }

    public override float value
    {
      get => m_Value;
      set => m_Value = Mathf.Clamp(value, min, max);
    }

    public override void Interp(float from, float to, float t) => m_Value = to;
  }

  /// <summary> Bool parameter with no interpolation. </summary>
  [Serializable]
  public sealed class BoolParameterNoInterpolation : VolumeParameter<bool>
  {
    public BoolParameterNoInterpolation(bool value, bool overrideState = false) : base(value, overrideState) { }

    public override void Interp(bool from, bool to, float t) => m_Value = to;
  }

  /// <summary> Enum parameter with no interpolation. </summary>
  [Serializable]
  public sealed class EnumParameterNoInterpolation<T> : VolumeParameter<T> where T : Enum
  {
    public EnumParameterNoInterpolation(T value, bool overrideState = false) : base(value, overrideState) { }

    public override void Interp(T from, T to, float t) => m_Value = to;
  }
}
