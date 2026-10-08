using UnityEngine;

namespace ZooGame.Platform
{
    /// <summary>
    /// Single place for runtime device settings that differ in practice between phones and the Editor.
    /// Any native platform code (haptics, store, share sheets) must live behind interfaces in this assembly.
    /// </summary>
    public static class RuntimeDeviceSettings
    {
        public static void Apply(int targetFrameRate, bool keepScreenAwake)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFrameRate;
            Screen.sleepTimeout = keepScreenAwake ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;
        }
    }
}
