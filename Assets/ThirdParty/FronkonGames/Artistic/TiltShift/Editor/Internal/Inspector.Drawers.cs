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
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Rendering;

namespace FronkonGames.Artistic.TiltShift.Editor
{
  /// <summary> Custom drawers. </summary>
  public abstract partial class Inspector : VolumeComponentEditor
  {
    protected void DrawFloatSliderWithReset(string name, string label = null)
    {
      SerializedDataParameter parameter = UnpackParameter(name);
      if (parameter == null)
        return;

      var attr = parameter.GetAttribute<FloatSliderWithResetAttribute>();
      if (attr == null)
      {
        EditorGUILayout.PropertyField(parameter.value, new GUIContent(label ?? parameter.displayName));
        return;
      }

      GUIContent displayLabel = new(label ?? parameter.displayName, attr.tooltip);

      EditorGUILayout.BeginHorizontal();
      {
        EditorGUI.showMixedValue = parameter.overrideState.hasMultipleDifferentValues;
        EditorGUI.showMixedValue = false;

        bool isOverridden = parameter.overrideState.boolValue;
        EditorGUI.BeginDisabledGroup(!isOverridden);

        EditorGUILayout.BeginHorizontal();
        {
          float value = parameter.value.floatValue;
          value = EditorGUILayout.Slider(displayLabel, value, attr.min, attr.max);

          EditorGUI.EndDisabledGroup();
          int oldIndentLevel = EditorGUI.indentLevel;
          EditorGUI.indentLevel = 0;
          EditorGUILayout.PropertyField(parameter.overrideState, GUIContent.none, GUILayout.Width(16));
          EditorGUI.indentLevel = oldIndentLevel;
          EditorGUI.BeginDisabledGroup(!isOverridden);

          if (ResetButton(attr.value, value != attr.value) == true)
            value = attr.value;

          parameter.value.floatValue = value;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUI.EndDisabledGroup();
      }
      EditorGUILayout.EndHorizontal();
    }

    protected void DrawIntSliderWithReset(string name, string label = null)
    {
      SerializedDataParameter parameter = UnpackParameter(name);
      if (parameter == null)
        return;

      var attr = parameter.GetAttribute<IntSliderWithResetAttribute>();
      if (attr == null)
      {
        EditorGUILayout.PropertyField(parameter.value, new GUIContent(label ?? parameter.displayName));
        return;
      }

      GUIContent displayLabel = new(label ?? parameter.displayName, attr.tooltip);

      EditorGUILayout.BeginHorizontal();
      {
        EditorGUI.showMixedValue = parameter.overrideState.hasMultipleDifferentValues;
        EditorGUI.showMixedValue = false;

        bool isOverridden = parameter.overrideState.boolValue;
        EditorGUI.BeginDisabledGroup(!isOverridden);

        EditorGUILayout.BeginHorizontal();
        {
          int value = parameter.value.intValue;
          value = EditorGUILayout.IntSlider(displayLabel, value, attr.min, attr.max);

          EditorGUI.EndDisabledGroup();
          int oldIndentLevel = EditorGUI.indentLevel;
          EditorGUI.indentLevel = 0;
          EditorGUILayout.PropertyField(parameter.overrideState, GUIContent.none, GUILayout.Width(16));
          EditorGUI.indentLevel = oldIndentLevel;
          EditorGUI.BeginDisabledGroup(!isOverridden);

          if (ResetButton(attr.value, value != attr.value) == true)
            value = attr.value;

          parameter.value.intValue = value;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUI.EndDisabledGroup();
      }
      EditorGUILayout.EndHorizontal();
    }

    protected void DrawToggleWithReset(string name, bool defaultValue = default)
    {
      SerializedDataParameter parameter = UnpackParameter(name);
      if (parameter == null)
        return;

      GUIContent displayLabel = new(parameter.displayName);

      EditorGUILayout.BeginHorizontal();
      {
        EditorGUI.showMixedValue = parameter.overrideState.hasMultipleDifferentValues;
        EditorGUI.showMixedValue = false;

        bool isOverridden = parameter.overrideState.boolValue;
        EditorGUI.BeginDisabledGroup(!isOverridden);

        EditorGUILayout.BeginHorizontal();
        {
          bool value = parameter.value.boolValue;
          value = EditorGUILayout.Toggle(displayLabel, value);

          EditorGUI.EndDisabledGroup();
          int oldIndentLevel = EditorGUI.indentLevel;
          EditorGUI.indentLevel = 0;
          EditorGUILayout.PropertyField(parameter.overrideState, GUIContent.none, GUILayout.Width(16));
          EditorGUI.indentLevel = oldIndentLevel;
          EditorGUI.BeginDisabledGroup(!isOverridden);

          bool isDefault = EqualityComparer<bool>.Default.Equals(value, defaultValue);
          if (ResetButton(defaultValue, !isDefault) == true)
            value = defaultValue;

          parameter.value.boolValue = value;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUI.EndDisabledGroup();
      }
      EditorGUILayout.EndHorizontal();
    }

    /// <summary> Draws a ColorFieldWithResetAttribute with color field and reset using attribute configuration. </summary>
    protected void DrawColorWithReset(string name, string label = null, Color defaultValue = default)
    {
      SerializedDataParameter parameter = UnpackParameter(name);
      if (parameter == null)
        return;

      GUIContent displayLabel = new(label ?? parameter.displayName);

      EditorGUILayout.BeginHorizontal();
      {
        EditorGUI.showMixedValue = parameter.overrideState.hasMultipleDifferentValues;
        EditorGUI.showMixedValue = false;

        bool isOverridden = parameter.overrideState.boolValue;
        EditorGUI.BeginDisabledGroup(!isOverridden);

        EditorGUILayout.BeginHorizontal();
        {
          Color value = parameter.value.colorValue;

          EditorGUI.BeginChangeCheck();
          value = EditorGUILayout.ColorField(displayLabel, value);

          EditorGUI.EndDisabledGroup();
          int oldIndentLevel = EditorGUI.indentLevel;
          EditorGUI.indentLevel = 0;
          EditorGUILayout.PropertyField(parameter.overrideState, GUIContent.none, GUILayout.Width(16));
          EditorGUI.indentLevel = oldIndentLevel;
          EditorGUI.BeginDisabledGroup(!isOverridden);

          bool isDefault = EqualityComparer<Color>.Default.Equals(value, defaultValue);
          if (ResetButton(defaultValue, !isDefault) == true)
            value = defaultValue;

          parameter.value.colorValue = value;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUI.EndDisabledGroup();
      }
      EditorGUILayout.EndHorizontal();
    }

    /// <summary> Draws a Vector3 field with reset. </summary>
    protected void DrawVector3WithReset(string name, string label = null, Vector3 defaultValue = default)
    {
      SerializedDataParameter parameter = UnpackParameter(name);
      if (parameter == null)
        return;

      GUIContent displayLabel = new(label ?? parameter.displayName);

      Rect rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);

      float buttonsWidth = 16.0f + 20.0f + 2.0f;
      Rect fieldRect = new(rect.x, rect.y, rect.width - buttonsWidth, rect.height);
      Rect overrideRect = new(rect.x + rect.width - buttonsWidth + 2.0f, rect.y, 16.0f, rect.height);
      Rect resetRect = new(rect.x + rect.width - 19.0f, rect.y, 19.0f, rect.height);

      EditorGUI.showMixedValue = parameter.overrideState.hasMultipleDifferentValues;
      EditorGUI.showMixedValue = false;

      bool isOverridden = parameter.overrideState.boolValue;

      EditorGUI.BeginDisabledGroup(!isOverridden);
      Vector3 value = parameter.value.vector3Value;
      EditorGUI.BeginChangeCheck();

      Rect labelRect = new(fieldRect.x, fieldRect.y, EditorGUIUtility.labelWidth, fieldRect.height);
      Rect valueRect = new(fieldRect.x + EditorGUIUtility.labelWidth, fieldRect.y, fieldRect.width - EditorGUIUtility.labelWidth, fieldRect.height);

      EditorGUI.LabelField(labelRect, displayLabel);
      value = EditorGUI.Vector3Field(valueRect, GUIContent.none, value);

      if (EditorGUI.EndChangeCheck() == true)
        parameter.value.vector3Value = value;
      EditorGUI.EndDisabledGroup();

      int oldIndentLevel = EditorGUI.indentLevel;
      EditorGUI.indentLevel = 0;
      EditorGUI.PropertyField(overrideRect, parameter.overrideState, GUIContent.none);
      EditorGUI.indentLevel = oldIndentLevel;

      EditorGUI.BeginDisabledGroup(!isOverridden);
      if (ResetButton(resetRect, defaultValue, value != defaultValue) == true)
        parameter.value.vector3Value = defaultValue;
      EditorGUI.EndDisabledGroup();
    }

    protected void DrawEnumDropdownWithReset<T>(string name, string label = null, T defaultValue = default) where T : Enum
    {
      SerializedDataParameter parameter = UnpackParameter(name);
      if (parameter == null)
        return;

      GUIContent displayLabel = new(label ?? parameter.displayName);

      EditorGUILayout.BeginHorizontal();
      {
        EditorGUI.showMixedValue = parameter.overrideState.hasMultipleDifferentValues;
        EditorGUI.showMixedValue = false;

        bool isOverridden = parameter.overrideState.boolValue;
        EditorGUI.BeginDisabledGroup(!isOverridden);

        EditorGUILayout.BeginHorizontal();
        {
          T currentValue = (T)Enum.ToObject(typeof(T), parameter.value.intValue);

          T newValue = (T)EditorGUILayout.EnumPopup(displayLabel, currentValue);
          parameter.value.intValue = Convert.ToInt32(newValue);

          EditorGUI.EndDisabledGroup();
          int oldIndentLevel = EditorGUI.indentLevel;
          EditorGUI.indentLevel = 0;
          EditorGUILayout.PropertyField(parameter.overrideState, GUIContent.none, GUILayout.Width(16));
          EditorGUI.indentLevel = oldIndentLevel;
          EditorGUI.BeginDisabledGroup(!isOverridden);

          bool isDefault = EqualityComparer<T>.Default.Equals(newValue, defaultValue);
          if (ResetButton(defaultValue, !isDefault) == true)
            parameter.value.intValue = Convert.ToInt32(defaultValue);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUI.EndDisabledGroup();
      }
      EditorGUILayout.EndHorizontal();
    }
  }
}
