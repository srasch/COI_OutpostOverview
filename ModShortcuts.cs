using Mafi.Unity.InputControl;
using UnityEngine;

namespace OutpostOverview;

/// <summary>
/// Key bindings of this mod (since game 0.8.8 mods declare their own key-binding "actions").
///
/// How it works: each action is a KbHandle with an id "&lt;mod id&gt;.&lt;name&gt;" and a default key.
/// It is registered with the game in OutpostOverviewMod.Initialize. After that it shows up in the
/// game's Controls settings (under "Mod: Outpost Overview"), where players can rebind it.
///
/// Building the handle also registers its label text for translation, so -- just like
/// ModStrings -- this class must be touched while the mod is loading (see EnsureCreated()).
/// </summary>
public static class ModShortcuts {

	/// <summary>Opens / closes the world mines window. Default key: F8.</summary>
	public static readonly KbHandle ToggleWindow = KbHandleBuilder
		.Start("OutpostOverview.ToggleWindow", ModStrings.WindowTitle)
		.SetPrimary(KeyCode.F8)
		.SetGroup(KbGroups.Windows)
		.Build();

	/// <summary>Does nothing by itself -- forces the fields above to be created at mod load time.</summary>
	public static void EnsureCreated() { }
}
