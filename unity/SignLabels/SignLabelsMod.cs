using ModSettingsMenu.Settings;
using PugMod;
using UnityEngine;

namespace SignLabels
{
    /// <summary>
    /// Mod bootstrap. The Pugstorm mod loader instantiates this class on game
    /// start and calls the IMod lifecycle methods. Harmony patch classes are
    /// auto-discovered by the loader — there is no PatchAll() call.
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
