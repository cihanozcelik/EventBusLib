#if UNITY_5_3_OR_NEWER
using UnityEngine;

namespace Nopnag.EventBusLib
{
  internal static class UnityEventBusLifetime
  {
    // Runs before scene owners prepare, with or without a domain reload.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void BeginSession() => EventBus.ClearAll();

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    static void InstallEditorCleanup()
    {
      UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
      UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(UnityEditor.PlayModeStateChange state)
    {
      // ExitingPlayMode is too early: owners still need their teardown callbacks.
      if (state == UnityEditor.PlayModeStateChange.EnteredEditMode) EventBus.ClearAll();
    }
#endif
  }
}
#endif
