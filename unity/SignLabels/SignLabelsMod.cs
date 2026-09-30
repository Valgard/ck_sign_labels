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

            ModSettings
                .Section(this)
                .Hint("Visibility new signs start with when you place them.")
                .Choice(out var defaultVisibility, "defaultVisibility", new[] { Visibility.Off, Visibility.Hover, Visibility.Always }, Visibility.Hover)
                .Build();
            DefaultVisibility.Bind(defaultVisibility);
        }

        public void ModObjectLoaded(Object obj) { }

        public void Shutdown() { }

        public void Update()
        {
            DefaultVisibility.Tick(Time.time);
        }
    }
}
