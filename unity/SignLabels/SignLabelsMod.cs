using ModSettingsMenu.Settings;
using PugMod;
using UnityEngine;

namespace SignLabels
{
    /// <summary>
    /// Mod bootstrap. Builds the Mod Settings Menu option for the default visibility in
    /// <see cref="Init"/> and drives <see cref="DefaultVisibility.Tick"/> every frame from
    /// <see cref="Update"/>. The labels themselves come from <see cref="SignLabelConverter"/> and
    /// <see cref="LabeledSign"/>, which need nothing from this class. The mod has no Harmony patches.
    /// </summary>
    public sealed class SignLabelsMod : IMod
    {
        public void EarlyInit() { }

        public void Init()
        {
            Debug.Log("[SignLabels] Mod initialized.");

            // The loader logs only the first exception to escape ANY mod's Init, across all mods, so
            // a throw leaving here might never reach the log. The labels themselves do not depend on
            // this — the converter and LabeledSign run regardless — only the placement default does.
            try
            {
                ModSettings
                    .Section(this)
                    .Hint("Visibility new signs start with when you place them.")
                    .Choice(out var defaultVisibility, "defaultVisibility", new[] { Visibility.Off, Visibility.Hover, Visibility.Always }, Visibility.Hover)
                    .Build();
                DefaultVisibility.Bind(defaultVisibility);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SignLabels] Init threw ({ex}); the defaultVisibility option is unavailable and new signs start at Hover");
            }
        }

        public void ModObjectLoaded(Object obj) { }

        public void Shutdown() { }

        public void Update()
        {
            DefaultVisibility.Tick(Time.time);
        }
    }
}
