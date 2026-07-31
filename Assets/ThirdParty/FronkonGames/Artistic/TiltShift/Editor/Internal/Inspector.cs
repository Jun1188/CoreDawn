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
using System.Text.RegularExpressions;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEditor;

namespace FronkonGames.Artistic.TiltShift.Editor
{
  /// <summary> Inspector base. </summary>
  public abstract partial class Inspector : VolumeComponentEditor
  {
    public static int IndentLevel
    {
      get => EditorGUI.indentLevel;
      set => EditorGUI.indentLevel = value;
    }

    public static void BeginVertical() => EditorGUILayout.BeginVertical();

    public static void EndVertical() => EditorGUILayout.EndVertical();

    public static void BeginHorizontal() => EditorGUILayout.BeginHorizontal();

    public static void EndHorizontal() => EditorGUILayout.EndHorizontal();

    public static void FlexibleSpace() => GUILayout.FlexibleSpace();

    public static float LabelWidth
    {
      get => EditorGUIUtility.labelWidth;
      set => EditorGUIUtility.labelWidth = value;
    }

    public static float FieldWidth
    {
      get => EditorGUIUtility.fieldWidth;
      set => EditorGUIUtility.fieldWidth = value;
    }

    public static bool EnableGUI
    {
      get => GUI.enabled;
      set => GUI.enabled = value;
    }

    public static bool Changed
    {
      get => GUI.changed;
      set => GUI.changed = value;
    }

    private GUIStyle styleLogo;

    protected readonly Dictionary<string, SerializedDataParameter> parameters = new();

    public override void OnInspectorGUI()
    {
      InitStyles();

      ResetGUI();

      serializedObject.Update();

      BeginVertical();
      {
        if (styleLogo != null)
        {
          EditorGUILayout.BeginHorizontal();
          {
            FlexibleSpace();
            GUILayout.Label(new GUIContent(Constants.Asset.Name, $"Version {Constants.Asset.Version}"), styleLogo);
          }
          EditorGUILayout.EndHorizontal();

          EditorGUILayout.BeginHorizontal();
          {
            FlexibleSpace();
            GUILayout.Label(Constants.Asset.Description, EditorStyles.miniLabel);
          }
          EditorGUILayout.EndHorizontal();

          Separator();

          InspectorGUI();

          Separator();

          if (Foldout("Color") == true)
          {
            IndentLevel++;
            DrawFloatSliderWithReset("brightness");
            DrawFloatSliderWithReset("contrast");
            DrawFloatSliderWithReset("gamma");
            DrawFloatSliderWithReset("hue");
            DrawFloatSliderWithReset("saturation");
            IndentLevel--;
          }

          Separator();

          if (Foldout("Advanced") == true)
          {
            IndentLevel++;
            DrawToggleWithReset("affectSceneView");
            DrawToggleWithReset("useScaledTime", true);
            IndentLevel--;
          }

          CheckForErrors();

          Separator();

          BeginHorizontal();
          {
            if (MiniButton("documentation", "Online documentation") == true)
              Application.OpenURL(Constants.Support.Documentation);

            try
            {
              string lastCheck = EditorPrefs.GetString($"{Constants.Asset.AssemblyName}.LastCheck");
              if (string.IsNullOrEmpty(lastCheck) == false)
              {
                DateTime lastCheckTime = DateTime.Parse(lastCheck);
                if (lastCheckTime < DateTime.Now.AddHours(-24))
                {
                  CheckForUpdate();
                  GUI.changed = true;
                  EditorPrefs.SetString($"{Constants.Asset.AssemblyName}.LastCheck", DateTime.Now.ToString("yyyy-MM-dd"));
                }
              }
              else
                EditorPrefs.SetString($"{Constants.Asset.AssemblyName}.LastCheck", DateTime.Now.ToString("yyyy-MM-dd"));
            }
            catch
            {
              EditorPrefs.SetString($"{Constants.Asset.AssemblyName}.LastCheck", DateTime.Now.ToString("yyyy-MM-dd"));
              updateAvailable = false;
            }

            Separator();

            if (updateAvailable == true)
            {
              if (MiniButton("<color=#FFD700>update available</color>", "A new update is available in the store!") == true)
              {
                Application.OpenURL(Constants.Support.Store);
                updateAvailable = false;
              }
            }
            else if (EditorPrefs.GetBool($"{Constants.Asset.AssemblyName}.Review") == false)
            {
              if (MiniButton("write a review <color=#800000>❤️</color>", "Write a review, thanks!") == true)
              {
                Application.OpenURL(Constants.Support.Store);
                EditorPrefs.SetBool($"{Constants.Asset.AssemblyName}.Review", true);
              }
            }

            FlexibleSpace();

            if (Button("Reset") == true)
            {
              if (EditorUtility.DisplayDialog("Reset", "Are you sure you want to reset the values?", "Yes", "No") == true)
                ResetValues();
            }
          }
          EndHorizontal();
        }
      }
      EndVertical();

      serializedObject.ApplyModifiedProperties();

      if (Changed == true)
        SetTargetDirty();
    }

    protected abstract void InspectorGUI();

    protected abstract void ResetValues();

    protected virtual void CheckForErrors() { }

    protected SerializedDataParameter UnpackParameter(string name)
    {
      if (parameters.TryGetValue(name, out SerializedDataParameter parameter) == false)
      {
        SerializedProperty property = serializedObject.FindProperty(name);
        if (property != null)
        {
          parameter = Unpack(property);
          if (parameter != null)
            parameters.Add(name, parameter);
        }
      }

      return parameter;
    }

    private void InitStyles()
    {
      if (styleLogo == null)
      {
        Font font = null;
        string[] ids = AssetDatabase.FindAssets("FronkonGames-Black");
        for (int i = 0; i < ids.Length; ++i)
        {
          string fontPath = AssetDatabase.GUIDToAssetPath(ids[i]);
          if (fontPath.Contains(".otf") == true)
          {
            font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            break;
          }
        }

        if (font != null)
        {
          styleLogo = new GUIStyle(EditorStyles.boldLabel)
          {
            font = font,
            alignment = TextAnchor.LowerLeft,
            fontSize = 24
          };
        }
      }
    }

    public static string HumanizeName(string text)
    {
      if (string.IsNullOrEmpty(text) == false)
      {
        text = text.Replace("_", " ").Trim();
        text = Regex.Replace(text, "^_", "").Trim();
        text = Regex.Replace(text, "([a-z])([A-Z])", "$1 $2").Trim();
        text = Regex.Replace(text, "([A-Z])([A-Z][a-z])", "$1 $2").Trim();
        text = char.ToUpper(text[0]) + text.Substring(1);
      }

      return text;
    }

    public static void ResetGUI(int indentLevel = 0, float labelWidth = 0.0f, float fieldWidth = 0.0f, bool guiEnabled = true)
    {
      EditorGUI.indentLevel = indentLevel;
      EditorGUIUtility.labelWidth = labelWidth;
      EditorGUIUtility.fieldWidth = fieldWidth;
      GUI.enabled = guiEnabled;
    }

    public void SetTargetDirty() => EditorUtility.SetDirty(target);

    public void SetDirty(UnityEngine.Object obj) => EditorUtility.SetDirty(obj);
  }
}
