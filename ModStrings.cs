using Mafi.Localization;

namespace OutpostOverview;

/// <summary>
/// All player-visible texts that belong to this mod (texts the game doesn't already have).
///
/// How translation works: each text is registered with the game under an ID that starts
/// with the mod ID ("OutpostOverview."). The game then looks the ID up in the mod's
/// Translations\&lt;language&gt;.json file (e.g. Translations\de.json) for the language the
/// player has selected, and falls back to the English text given here.
///
/// Texts the game already translates itself (e.g. "Status", "Paused") are taken
/// directly from the game's Tr class in the UI code instead of being listed here.
///
/// IMPORTANT: The game only accepts translations for texts a mod registers while it is
/// being loaded. That's why OutpostOverviewMod's constructor calls EnsureRegistered().
/// </summary>
public static class ModStrings {

	/// <summary>Window title and toolbar button text.</summary>
	public static readonly LocStr WindowTitle = Loc.Str(
		"OutpostOverview.WindowTitle", "World mines",
		"title of the window that lists all world map mines of the player");

	/// <summary>Label of the checkbox that shows/hides the remaining reserve columns.</summary>
	public static readonly LocStr ShowReserves = Loc.Str(
		"OutpostOverview.ShowReserves", "Show remaining reserves",
		"checkbox label in the world mines window; toggles the columns with the remaining deposit of each mine");

	/// <summary>Label of the checkbox that shows/hides the status column.</summary>
	public static readonly LocStr ShowStatus = Loc.Str(
		"OutpostOverview.ShowStatus", "Show status",
		"checkbox label in the world mines window; toggles the status column (when off, the mine name is colored by its status)");

	/// <summary>Shown when the player has no repaired world map mine yet.</summary>
	public static readonly LocStr NoMines = Loc.Str(
		"OutpostOverview.NoMines", "No world mines yet. Repair a mine on the world map to see it here.",
		"empty-state text in the world mines window");

	/// <summary>Column header: mine name.</summary>
	public static readonly LocStr ColMine = Loc.Str(
		"OutpostOverview.ColMine", "Mine",
		"column header in the world mines window: name of the mine");

	/// <summary>Column header: storage fill level.</summary>
	public static readonly LocStr ColStorage = Loc.Str(
		"OutpostOverview.ColStorage", "Storage",
		"column header in the world mines window: how full the mine's output storage is");

	/// <summary>Column header: remaining deposit.</summary>
	public static readonly LocStr ColReserve = Loc.Str(
		"OutpostOverview.ColReserve", "Reserve",
		"column header in the world mines window: remaining deposit of the mine");

	/// <summary>Column header: estimated years until the deposit is depleted.</summary>
	public static readonly LocStr ColYears = Loc.Str(
		"OutpostOverview.ColYears", "Years",
		"short column header in the world mines window: estimated years until the deposit runs out");

	/// <summary>Shown in the reserve column for mines with an infinite deposit.</summary>
	public static readonly LocStr Unlimited = Loc.Str(
		"OutpostOverview.Unlimited", "unlimited",
		"shown in the reserve column for a world mine whose deposit never runs out");

	/// <summary>Tooltip of the sortable column headers.</summary>
	public static readonly LocStr SortTooltip = Loc.Str(
		"OutpostOverview.SortTooltip", "Click to sort by this column, click again to reverse, a third time for your own order.",
		"tooltip of a sortable column header in the world mines window");

	/// <summary>Tooltip of the drag handle at the start of each row.</summary>
	public static readonly LocStr DragTooltip = Loc.Str(
		"OutpostOverview.DragTooltip", "Drag to arrange the mines in your own order.",
		"tooltip of the drag handle on each row of the world mines window");

	/// <summary>Tooltip of the gear button that expands the automatic pause/resume sliders.</summary>
	public static readonly LocStr LimitsButtonTooltip = Loc.Str(
		"OutpostOverview.LimitsButtonTooltip", "Automatic pause / resume by storage level",
		"tooltip of the button that shows the sliders for automatically pausing and resuming a world mine");

	/// <summary>Title above the sliders.</summary>
	public static readonly LocStr LimitsTitle = Loc.Str(
		"OutpostOverview.LimitsTitle", "Automatic pause / resume",
		"title above the two sliders that pause/resume a world mine depending on how full its storage is");

	/// <summary>Explanation shown as tooltip of the title.</summary>
	public static readonly LocStr LimitsHelp = Loc.Str(
		"OutpostOverview.LimitsHelp", "The mine is paused when its storage reaches the red limit and resumed when it drops below the green limit. Move a slider to its end to turn it off. Pausing or resuming by hand is respected until the next limit is crossed.",
		"tooltip explaining the automatic pause/resume sliders of a world mine");

	/// <summary>Label in the floater of the green (minimum) slider.</summary>
	public static readonly LocStr ResumeBelow = Loc.Str(
		"OutpostOverview.ResumeBelow", "Resume below:",
		"label shown while dragging the slider; the mine is resumed when its storage drops below this quantity");

	/// <summary>Label in the floater of the red (maximum) slider.</summary>
	public static readonly LocStr PauseFrom = Loc.Str(
		"OutpostOverview.PauseFrom", "Pause from:",
		"label shown while dragging the slider; the mine is paused when its storage reaches this quantity");

	/// <summary>Tooltip of the small "30–90 %" hint next to a mine name.</summary>
	public static readonly LocStr LimitsActiveTooltip = Loc.Str(
		"OutpostOverview.LimitsActiveTooltip", "Automatic pause / resume is active (resume below – pause from, storage level)",
		"tooltip of the small limits hint next to a world mine name");

	/// <summary>Tooltip of the pause/resume button on each mine row.</summary>
	public static readonly LocStr PauseTooltip = Loc.Str(
		"OutpostOverview.PauseTooltip", "Pause / resume this mine",
		"tooltip of the pause toggle button next to each world mine");

	/// <summary>
	/// Does nothing by itself -- calling it forces the fields above to be created
	/// (and thereby registered with the game) at mod load time.
	/// </summary>
	public static void EnsureRegistered() { }
}
