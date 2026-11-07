using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.GameLoop;
using Mafi.Core.Input;
using Mafi.Core.Mods;
using Mafi.Core.Syncers;
using Mafi.Core.World;
using Mafi.Core.World.Entities;
using Mafi.Localization;
using Mafi.Unity.InputControl;
using Mafi.Unity.Ui.Hud;
using Mafi.Unity.Ui.Library;
using Mafi.Unity.Ui.Library.Inspectors;
using Mafi.Unity.UiStatic.Toolbar;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Component.Manipulators;
using Mafi.Unity.UiToolkit.Library;
using Mafi.Unity.UiToolkit.Themes;

namespace OutpostOverview;

/// <summary>
/// The window: a table with one row per repaired world map mine.
///
///   [≡] [icon] Mine name      Storage [████░░] 62%   Reserve   Years   Status      [⏸]
///
/// - Column headers "Mine", "Storage", "Reserve" and "Years" are clickable to sort
///   (1st click: sort, 2nd click: reverse, 3rd click: back to the player's own order).
/// - The handle [≡] at the start of a row allows drag-and-drop; dragging switches to the
///   player's own ("manual") order.
/// - Sort settings and the manual order are remembered per game (OutpostOverviewSettings).
///
/// Live updates use the game's own "Observe(...).Do(...)" mechanism: the game evaluates the
/// observed values once per sync step and calls the Do-handler only when a value changed.
/// </summary>
/// Created on first open by OutpostOverviewWindowController (WindowController.CreateWindow resolves
/// the constructor parameters from DI), so this class needs no [GlobalDependency] itself.
public class OutpostOverviewWindowView : Window {

	// ── Column widths (px). Header and rows use the same values so the columns line up. ──
	private const int COL_DRAG = 20;
	private const int COL_ICON = 26;
	private const int COL_NAME_MIN = 170;
	private const int COL_STORAGE = 150;
	private const int COL_RESERVE = 110;
	private const int COL_YEARS = 60;
	private const int COL_STATUS = 130;
	private const int COL_BUTTONS = 72;   // limits (gear) + pause button
	private const int COL_GAP = 6;

	private const int WINDOW_WIDTH = 858;
	private const int LIST_MAX_HEIGHT = 600;
	private const int ICON_SIZE = 20;
	private const int FONT_SIZE = 14;
	private const int FONT_SIZE_HEADER = 13;

	private const int COLOR_DRAG_BG = 3224115;      // same dark handle background as the game's recipe lists
	private const int COLOR_DRAG_BORDER = 2763306;

	private const string PAUSE_ICON = "Assets/Unity/UserInterface/General/EnableMachine128.png";
	private const string DRAG_ICON = "Assets/Unity/UserInterface/General/Drag.svg";
	private const string LIMITS_ICON = "Assets/Unity/UserInterface/General/Configure.svg";
	private const int LIMITS_SIDE_PADDING = 34; // room for the 30 px slider handles at both ends

	// Mines whose automatic pause/resume sliders are expanded (UI state only, not saved).
	private readonly HashSet<WorldMapMine> m_expanded = new HashSet<WorldMapMine>();

	private readonly WorldMapManager m_worldMapManager;
	private readonly IInputScheduler m_inputScheduler;
	private readonly OutpostOverviewSettings m_settings;

	private readonly Toggle m_reservesToggle;
	private readonly Toggle m_statusToggle;
	private readonly Row m_header;
	private readonly Label m_emptyLabel;
	private readonly ScrollColumn m_list;

	// Clickable header labels (text gets an arrow when the column is the active sort).
	private readonly Dictionary<MineSortColumn, Label> m_headerLabels = new Dictionary<MineSortColumn, Label>();

	// One row component per mine, created once and re-used when the order changes.
	private readonly Dictionary<WorldMapMine, UiComponent> m_rows = new Dictionary<WorldMapMine, UiComponent>();

	// The mines in the order they are currently displayed (needed to translate drag indices).
	private List<WorldMapMine> m_displayed = new List<WorldMapMine>();

	// While the player drags a row, the list must not be rearranged underneath the drag.
	private bool m_isDragging;

	public OutpostOverviewWindowView(
		WorldMapManager worldMapManager,
		IInputScheduler inputScheduler,
		OutpostOverviewSettings settings)
		: base(ModStrings.WindowTitle) {
		m_worldMapManager = worldMapManager;
		m_inputScheduler = inputScheduler;
		m_settings = settings;

		// Native window behavior: draggable by the title bar (position remembered locally per user,
		// not in the save), pin button, and the hotkey shown in the title bar.
		// Height Px.Auto = window as tall as its content. (A pixel height here is only a maximum --
		// the game then makes the window nearly screen-sized.) The list itself is capped below.
		WindowSize(WINDOW_WIDTH.px(), Px.Auto);
		MakeMovableAndEnablePositionSaving();
		EnablePinning();
		ShortcutToShow(ModShortcuts.ToggleWindow);

		// Checkboxes: show / hide the reserve + years columns, and the status column.
		m_reservesToggle = NewCheckbox(ModStrings.ShowReserves, value => m_settings.SetShowReserves(value));
		m_statusToggle = NewCheckbox(ModStrings.ShowStatus, value => m_settings.SetShowStatus(value));
		var options = new Row(4.pt());
		options.AlignItemsCenter();
		options.Add(m_reservesToggle, m_statusToggle);

		m_header = BuildHeader();

		m_emptyLabel = new Label(ModStrings.NoMines);
		m_emptyLabel.FontSize(FONT_SIZE).Margin(4.px());

		m_list = new ScrollColumn();
		m_list.MaxHeight(LIST_MAX_HEIGHT.px());
		// The game's "auto" scrollbar is always shown inside stretched panels, so we hide it and
		// only show it when the rows really don't fit (re-checked whenever the sizes change).
		m_list.ScrollerHidden();
		var scrollView = (UnityEngine.UIElements.ScrollView)m_list.RootElement;
		scrollView.contentContainer.RegisterCallback<UnityEngine.UIElements.GeometryChangedEvent>(_ => UpdateScrollbar());
		scrollView.contentViewport.RegisterCallback<UnityEngine.UIElements.GeometryChangedEvent>(_ => UpdateScrollbar());

		// Content goes onto a Panel that fills the window (like the game's own windows) -- added
		// directly to Body, the window frame's "checker plate" texture would show through.
		AddBodySingle(options, m_header, m_emptyLabel, m_list);

		// Load the saved settings as soon as the game is running and apply them to the checkboxes.
		// The window gets narrower for every hidden column.
		this.Observe(() => { m_settings.EnsureLoaded(); return m_settings.ShowReserves; })
			.Observe(() => m_settings.ShowStatus)
			.Do((showReserves, showStatus) => {
				m_reservesToggle.Value(showReserves);
				m_statusToggle.Value(showStatus);
				int width = WINDOW_WIDTH;
				if (!showReserves) width -= COL_RESERVE + COL_YEARS + 2 * COL_GAP;
				if (!showStatus) width -= COL_STATUS + COL_GAP;
				WindowWidth(width.px());
			});

		// Header arrows follow the current sort setting.
		this.Observe(() => m_settings.SortColumn)
			.Observe(() => m_settings.SortDescending)
			.Do((column, descending) => UpdateHeaderTexts(column, descending));

		// The heart of the window: the sorted list of repaired mines. Whenever the order (or the
		// set of mines) changes -- because values changed, a mine got repaired, or the player
		// changed the sorting -- the rows are rearranged.
		this.ObserveEnumerable(() => GetSortedMines()).Do(mines => ApplyOrder(mines));
	}

	private bool m_scrollbarShown;

	/// <summary>Shows the list's scrollbar only when the rows are taller than the visible area.</summary>
	private void UpdateScrollbar() {
		var scrollView = (UnityEngine.UIElements.ScrollView)m_list.RootElement;
		float content = scrollView.contentContainer.resolvedStyle.height;
		float viewport = scrollView.contentViewport.resolvedStyle.height;
		if (float.IsNaN(content) || float.IsNaN(viewport)) return;
		bool needed = content > viewport + 1f;
		if (needed == m_scrollbarShown) return;
		m_scrollbarShown = needed;
		if (needed) m_list.ScrollerAlwaysVisible(); else m_list.ScrollerHidden();
	}

	// ─────────────────────────────── Sorting ───────────────────────────────

	private List<WorldMapMine> GetSortedMines() {
		m_settings.EnsureLoaded();
		var mines = m_worldMapManager.Mines.Where(m => m.IsRepaired).ToList();

		switch (m_settings.SortColumn) {
			case MineSortColumn.Name:
				return OrderBy(mines, m => 0.0, alsoByName: true);
			case MineSortColumn.Fill:
				return OrderBy(mines, GetFillRatio);
			case MineSortColumn.Reserve:
				return OrderBy(mines, m => m.QuantityAvailable.HasValue ? m.QuantityAvailable.Value.Value : double.PositiveInfinity);
			case MineSortColumn.Years:
				return OrderBy(mines, m => GetYearsLeft(m) ?? double.PositiveInfinity);
			default:
				return GetManualOrder(mines);
		}
	}

	/// <summary>Sorts by the given key (direction from settings); ties are sorted by name.</summary>
	private List<WorldMapMine> OrderBy(List<WorldMapMine> mines, Func<WorldMapMine, double> key, bool alsoByName = false) {
		bool desc = m_settings.SortDescending;
		var ordered = desc ? mines.OrderByDescending(key) : mines.OrderBy(key);
		return (desc && alsoByName
				? ordered.ThenByDescending(GetSortName)
				: ordered.ThenBy(GetSortName))
			.ThenBy(m => m.Id.Value)
			.ToList();
	}

	/// <summary>The player's own order; mines that are not in it yet go to the end (by name).</summary>
	private List<WorldMapMine> GetManualOrder(List<WorldMapMine> mines) {
		var position = new Dictionary<int, int>();
		var order = m_settings.ManualOrder;
		for (int i = 0; i < order.Count; i++) {
			position[order[i]] = i;
		}
		return mines
			.OrderBy(m => position.TryGetValue(m.Id.Value, out int p) ? p : int.MaxValue)
			.ThenBy(GetSortName)
			.ThenBy(m => m.Id.Value)
			.ToList();
	}

	/// <summary>Header click: 1st click sorts (natural direction), 2nd reverses, 3rd returns to manual order.</summary>
	private void OnHeaderClicked(MineSortColumn column) {
		// "Natural" first direction: fullest storage first; smallest reserve / fewest years first; names A-Z.
		bool firstDescending = column == MineSortColumn.Fill;

		if (m_settings.SortColumn != column) {
			m_settings.SetSort(column, firstDescending);
		} else if (m_settings.SortDescending == firstDescending) {
			m_settings.SetSort(column, !firstDescending);
		} else {
			m_settings.SetSort(MineSortColumn.Manual, false);
		}
	}

	/// <summary>Drag-and-drop finished: store the new order as the player's manual order.</summary>
	private void OnRowDragged(int oldIndex, int newIndex) {
		if (oldIndex < 0 || oldIndex >= m_displayed.Count) return;
		var newOrder = new List<WorldMapMine>(m_displayed);
		var moved = newOrder[oldIndex];
		newOrder.RemoveAt(oldIndex);
		newOrder.Insert(Math.Min(Math.Max(newIndex, 0), newOrder.Count), moved);
		m_displayed = newOrder;
		m_settings.SetManualOrder(newOrder.Select(m => m.Id.Value));
	}

	private void ApplyOrder(Lyst<WorldMapMine> mines) {
		// Skipped during a drag; the drop itself stores a new manual order, which triggers this again.
		if (m_isDragging) return;

		// Create rows for new mines, forget rows of mines that are gone.
		foreach (var mine in mines) {
			if (!m_rows.ContainsKey(mine)) {
				m_rows[mine] = BuildMineRow(mine);
			}
		}
		foreach (var gone in m_rows.Keys.Where(m => !mines.Contains(m)).ToList()) {
			m_rows.Remove(gone);
		}

		m_displayed = mines.ToList();
		bool any = m_displayed.Count > 0;
		m_emptyLabel.SetVisible(!any);
		m_header.SetVisible(any);
		m_list.SetVisible(any);
		m_list.SetChildren(m_displayed.Select(m => m_rows[m]).ToList());
	}

	// ─────────────────────────────── Layout ───────────────────────────────

	private Row BuildHeader() {
		var header = new Row(COL_GAP.px());
		header.AlignItemsCenter().PaddingLeftRight(1.pt()).MarginBottom(1.pt());

		header.Add(Spacer(COL_DRAG), Spacer(COL_ICON));
		header.Add(HeaderLabel(MineSortColumn.Name, ModStrings.ColMine).MinWidth(COL_NAME_MIN.px()).FlexGrow(1f));
		header.Add(HeaderLabel(MineSortColumn.Fill, ModStrings.ColStorage).Width(COL_STORAGE.px()));

		var reserve = HeaderLabel(MineSortColumn.Reserve, ModStrings.ColReserve).Width(COL_RESERVE.px());
		var years = HeaderLabel(MineSortColumn.Years, ModStrings.ColYears).Width(COL_YEARS.px());
		reserve.ObserveVisible(header, () => m_settings.ShowReserves);
		years.ObserveVisible(header, () => m_settings.ShowReserves);
		header.Add(reserve, years);

		var status = new Label(Tr.Status);
		status.FontBold().FontSize(FONT_SIZE_HEADER).Width(COL_STATUS.px());
		status.ObserveVisible(header, () => m_settings.ShowStatus);
		header.Add(status, Spacer(COL_BUTTONS));
		return header;
	}

	/// <summary>
	/// A normal "[x] label" checkbox. standalone: true is needed for that layout -- the default
	/// Toggle puts the label left and the box far right (editor style).
	/// </summary>
	private static Toggle NewCheckbox(LocStr label, Action<bool> onChange) {
		var toggle = new Toggle(standalone: true);
		((IComponentWithLabel)toggle).SetLabel(label);
		toggle.Value(true);
		toggle.OnValueChanged(onChange);
		return toggle;
	}

	private Label HeaderLabel(MineSortColumn column, LocStr text) {
		var label = new Label(text);
		label.FontBold().FontSize(FONT_SIZE_HEADER).Tooltip(ModStrings.SortTooltip);
		label.OnClick(() => OnHeaderClicked(column));
		m_headerLabels[column] = label;
		return label;
	}

	private void UpdateHeaderTexts(MineSortColumn active, bool descending) {
		var texts = new Dictionary<MineSortColumn, LocStr> {
			{ MineSortColumn.Name, ModStrings.ColMine },
			{ MineSortColumn.Fill, ModStrings.ColStorage },
			{ MineSortColumn.Reserve, ModStrings.ColReserve },
			{ MineSortColumn.Years, ModStrings.ColYears },
		};
		foreach (var kv in m_headerLabels) {
			string text = texts[kv.Key].TranslatedString;
			if (kv.Key == active) {
				text += descending ? " ▼" : " ▲";
			}
			kv.Value.Value(text.AsLoc());
			kv.Value.Color(kv.Key == active ? Theme.PositiveColor : Theme.DefaultColor);
		}
	}

	private static UiComponent Spacer(int width) {
		var c = new Column();
		c.Width(width.px());
		return c;
	}

	/// <summary>
	/// Builds the list entry of one mine: the single-line row, plus the (collapsible) panel with
	/// the automatic pause/resume sliders below it.
	/// </summary>
	private UiComponent BuildMineRow(WorldMapMine mine) {
		// The whole entry is what gets dragged / re-ordered.
		var item = new Column();
		item.AlignItemsStretch().StyleGroup().MarginBottom(2.px());

		var row = new Row(COL_GAP.px());
		row.AlignItemsCenter().PaddingRight(1.pt());

		// Drag handle (same look as the game's reorderable recipe lists).
		var dragHandle = new Column();
		dragHandle.Width(COL_DRAG.px()).AlignSelfStretch().JustifyItemsCenter()
			.Class(Cls.reorderHandle, Cls.reorderHandleAlphaHover)
			.Background(COLOR_DRAG_BG)
			.BorderRight(1.px(), COLOR_DRAG_BORDER)
			.BorderRadiusLeft(4)
			.Padding(1.pt());
		var dragIcon = new Icon(DRAG_ICON);
		dragIcon.Opacity(0.6f).Size(10.px()).AlignSelfCenter();
		dragHandle.Add(dragIcon);
		dragHandle.Tooltip(ModStrings.DragTooltip);

		// Product icon (with the game's product tooltip).
		var productIcon = new Icon(mine.Product);
		productIcon.Size(ICON_SIZE.px());
		var iconCell = new Row();
		iconCell.Width(COL_ICON.px()).JustifyItemsCenter();
		iconCell.Add(productIcon);

		var nameLabel = new Label();
		nameLabel.FontSize(FONT_SIZE);

		// Small "30–90 %" hint when automatic pause/resume is active.
		var limitsHint = new Label();
		limitsHint.FontSize(FONT_SIZE_HEADER).Opacity(0.7f).MarginLeft(4.px()).Tooltip(ModStrings.LimitsActiveTooltip);

		var nameCell = new Row();
		nameCell.AlignItemsCenter().MinWidth(COL_NAME_MIN.px()).FlexGrow(1f);
		nameCell.Add(nameLabel, limitsHint);

		var storageBar = new ProgressBarPercentInline();
		storageBar.Width(COL_STORAGE.px());

		var reserveLabel = new Label();
		reserveLabel.FontSize(FONT_SIZE).Width(COL_RESERVE.px());

		var yearsLabel = new Label();
		yearsLabel.FontSize(FONT_SIZE).Width(COL_YEARS.px()).Tooltip(Tr.WorldMine_ReserveEstimate__Tooltip);

		var statusLabel = new Label();
		statusLabel.FontSize(FONT_SIZE).Width(COL_STATUS.px());

		var pauseBtn = new ButtonIcon(Button.General, PAUSE_ICON)
			.Toggleable()
			.IconSize(15, 18)
			.OnClick(() => m_inputScheduler.ScheduleInputCmd(new ToggleEnabledCmd(mine.Id)))
			.ObserveSelected(() => mine.IsPaused);
		pauseBtn.Tooltip(ModStrings.PauseTooltip);

		var limitsBtn = new ButtonIcon(Button.General, LIMITS_ICON)
			.Toggleable()
			.IconSize(16, 16)
			.OnClick(() => {
				if (!m_expanded.Remove(mine)) m_expanded.Add(mine);
			})
			.ObserveSelected(() => m_expanded.Contains(mine));
		limitsBtn.Tooltip(ModStrings.LimitsButtonTooltip);

		var buttonsCell = new Row(4.px());
		buttonsCell.Width(COL_BUTTONS.px()).JustifyItemsCenter();
		buttonsCell.Add(limitsBtn, pauseBtn);

		row.Add(dragHandle, iconCell, nameCell, storageBar, reserveLabel, yearsLabel, statusLabel, buttonsCell);

		var limitsPanel = BuildLimitsPanel(mine);
		limitsPanel.ObserveVisible(item, () => m_expanded.Contains(mine));

		item.Add(row, limitsPanel);

		row.Observe(() => m_settings.GetLimits(mine.Id.Value)).Do(limits => {
			limitsHint.Value(FormatLimits(limits).AsLoc());
			limitsHint.SetVisible(limits.IsActive);
		});

		// ── Live bindings ──
		row.Observe(() => GetTitle(mine).Value).Do(title => nameLabel.Value(title.AsLoc()));

		row.Observe(() => mine.Buffer.Quantity)
			.Observe(() => mine.Buffer.Capacity)
			.Do((quantity, capacity) => {
				Percent fill = capacity.IsPositive
					? Percent.FromRatio(quantity.Value, capacity.Value)
					: Percent.Zero;
				storageBar.Value(fill);
				storageBar.SetState(fill >= Percent.Hundred ? DisplayState.Danger
					: fill >= Percent.FromInt(90) ? DisplayState.Warning
					: DisplayState.Positive);
				storageBar.Tooltip($"{FormatQuantity(quantity)} / {FormatQuantity(capacity)}".AsLoc());
			});

		row.Observe(() => mine.QuantityAvailable)
			.Observe(() => mine.ProductionStep)
			.Do((left, step) => {
				reserveLabel.Value(GetReserveText(mine, left));
				double? years = GetYearsLeft(mine);
				yearsLabel.Value((years.HasValue ? years.Value.ToString("0.0") : "–").AsLoc());
			});

		// Status: in its own column, or -- when that column is hidden -- as the color of the name
		// (the status text is always available as tooltip of the name).
		row.Observe(() => mine.CurrentState)
			.Observe(() => m_settings.ShowStatus)
			.Do((state, showStatus) => {
				GetStatus(state, out LocStrFormatted text, out ColorRgba color);
				statusLabel.Value(text);
				statusLabel.Color(color);
				nameLabel.Color(showStatus ? Theme.DefaultColor : color);
				nameLabel.Tooltip(text);
			});
		statusLabel.ObserveVisible(row, () => m_settings.ShowStatus);

		reserveLabel.ObserveVisible(row, () => m_settings.ShowReserves);
		yearsLabel.ObserveVisible(row, () => m_settings.ShowReserves);

		// Drag-and-drop via the game's Reorderable manipulator.
		var reorderable = new Reorderable(dragHandle.RootElement);
		reorderable.OnOrderChanged += OnRowDragged;
		reorderable.OnBeginDrag += () => m_isDragging = true;
		reorderable.OnEndDrag += () => m_isDragging = false;
		item.AddManipulator(reorderable);

		return item;
	}

	/// <summary>
	/// The expandable panel with the storage bar and two sliders, built like the import/export
	/// sliders of the game's storages:
	///   green (bottom range) = resume the mine when its storage drops below this level
	///   red   (top range)    = pause the mine when its storage reaches this level
	/// A slider at its end (green at 0 %, red at 100 %) is off.
	/// </summary>
	private UiComponent BuildLimitsPanel(WorldMapMine mine) {
		int id = mine.Id.Value;

		var title = new Label(ModStrings.LimitsTitle);
		title.FontBold().FontSize(FONT_SIZE_HEADER).Tooltip(ModStrings.LimitsHelp);

		var bar = new ProgressBar();
		bar.Height(16.px()).AlignSelfStretch();
		var barColumn = new Column();
		barColumn.AlignItemsStretch().Fill();
		barColumn.Add(bar);

		var resumeSlider = new BufferSlider(alignLeft: true, isInverted: false, flipHandle: false,
			_ => ModStrings.ResumeBelow, MineLimits.STEPS).StyleAsSoilFertility();
		var pauseSlider = new BufferSlider(alignLeft: true, isInverted: true, flipHandle: false,
			_ => ModStrings.PauseFrom, MineLimits.STEPS).StyleAsAnimalSlaughter();
		resumeSlider.ProductIcon(mine.Product);
		pauseSlider.ProductIcon(mine.Product);
		resumeSlider.OppositeSlider = pauseSlider;
		pauseSlider.OppositeSlider = resumeSlider;

		// Same structure as the game's BufferWithSlider: bar + absolutely positioned sliders in one row,
		// with room below the bar for the slider handles.
		var sliderRow = new Row();
		sliderRow.AlignItemsStretch().PaddingBottom(BufferSlider.BOTTOM_MARGIN);
		sliderRow.Add(barColumn, resumeSlider, pauseSlider);

		var percentLabel = new Label();
		percentLabel.FontSize(FONT_SIZE).Width(50.px()).MarginLeft(8.px());

		var barLine = new Row();
		barLine.AlignItemsStart();
		sliderRow.FlexGrow(1f);
		barLine.Add(sliderRow, percentLabel);

		var panel = new Column(2.px());
		panel.AlignItemsStretch().PaddingTop(4.px()).PaddingBottom(2.px())
			.PaddingLeft(LIMITS_SIDE_PADDING.px()).PaddingRight(LIMITS_SIDE_PADDING.px());
		panel.Add(title, barLine);

		// Keep the sliders apart: green must stay below red.
		resumeSlider.OnValueChanged(step => {
			var limits = m_settings.GetLimits(id);
			if (limits.HasMax && step >= limits.Max) step = limits.Max - 1;
			m_settings.SetLimits(id, new MineLimits(step, limits.Max));
			resumeSlider.Value(step, mine.Buffer.Capacity);
		});
		pauseSlider.OnValueChanged(step => {
			var limits = m_settings.GetLimits(id);
			if (limits.HasMin && step <= limits.Min) step = limits.Min + 1;
			m_settings.SetLimits(id, new MineLimits(limits.Min, step));
			pauseSlider.Value(step, mine.Buffer.Capacity);
		});

		panel.Observe(() => mine.Buffer.Quantity)
			.Observe(() => mine.Buffer.Capacity)
			.Observe(() => m_settings.GetLimits(id))
			.Do((quantity, capacity, limits) => {
				Percent fill = capacity.IsPositive ? Percent.FromRatio(quantity.Value, capacity.Value) : Percent.Zero;
				bar.Value(fill);
				percentLabel.Value(fill.ToStringRounded().AsLoc());
				resumeSlider.Value(limits.Min, capacity);
				pauseSlider.Value(limits.Max, capacity);
			});

		return panel;
	}

	/// <summary>"30–90 %", "30 %–" (only resume) or "–90 %" (only pause).</summary>
	private static string FormatLimits(MineLimits limits) {
		string min = limits.HasMin ? limits.MinPercent.ToString() : "";
		string max = limits.HasMax ? limits.MaxPercent.ToString() : "";
		if (limits.HasMin && limits.HasMax) return $"{min}–{max} %";
		if (limits.HasMin) return $"{min} %–";
		if (limits.HasMax) return $"–{max} %";
		return "";
	}

	// ─────────────────────────────── Values ───────────────────────────────

	private static LocStrFormatted GetTitle(WorldMapMine mine) {
		string custom = mine.CustomTitle.ValueOrNull;
		return string.IsNullOrEmpty(custom) ? mine.DefaultTitle : custom.AsLoc();
	}

	private static string GetSortName(WorldMapMine mine) => GetTitle(mine).Value;

	private static double GetFillRatio(WorldMapMine mine) {
		var capacity = mine.Buffer.Capacity;
		return capacity.IsPositive ? mine.Buffer.Quantity.Value / (double)capacity.Value : 0.0;
	}

	/// <summary>"45k (38%)" or "unlimited". The % uses the same reference value as the game's inspector.</summary>
	private static LocStrFormatted GetReserveText(WorldMapMine mine, Quantity? left) {
		if (!left.HasValue) {
			return ModStrings.Unlimited;
		}
		Quantity total = (mine.Prototype.QuantityAvailable ?? left.Value).Max(left.Value);
		string text = FormatQuantity(left.Value);
		if (total.IsPositive) {
			text += $" ({Percent.FromRatio(left.Value.Value, total.Value).ToStringRounded()})";
		}
		return text.AsLoc();
	}

	/// <summary>
	/// Rough "years left" estimate -- same formula as the game's mine inspector (remaining
	/// reserve divided by the yearly output at the current shift count). Null = unlimited or
	/// not producing (0 shifts).
	/// </summary>
	private static double? GetYearsLeft(WorldMapMine mine) {
		Quantity? left = mine.QuantityAvailable;
		if (!left.HasValue) return null;
		if (left.Value == Quantity.Zero) return 0.0;
		int step = mine.ProductionStep;
		if (step <= 0) return null;
		var proto = mine.Prototype;
		double cyclesPerYear = 1.Years().Ticks / (double)proto.ProductionDuration.Ticks;
		double perYear = step * proto.ProducedProductPerStep.Quantity.Value * cyclesPerYear;
		return perYear > 0 ? left.Value.Value / perYear : (double?)null;
	}

	/// <summary>Compact numbers: 950, 9 450, 45k, 1.2M.</summary>
	private static string FormatQuantity(Quantity q) {
		int v = q.Value;
		if (v >= 1_000_000) return (v / 1_000_000.0).ToString("0.#") + "M";
		if (v >= 10_000) return (v / 1000) + "k";
		return v.ToString("#,0").Replace(',', ' ');
	}

	/// <summary>Maps the mine state to the game's own (translated) status texts and colors.</summary>
	private static void GetStatus(WorldMapMine.State state, out LocStrFormatted text, out ColorRgba color) {
		switch (state) {
			case WorldMapMine.State.Working:
				text = Tr.EntityStatus__Working; color = Theme.PositiveColor; return;
			case WorldMapMine.State.Paused:
				text = Tr.EntityStatus__Paused; color = Theme.WarningColor; return;
			case WorldMapMine.State.FullStorage:
				text = Tr.EntityStatus__FullStorage; color = Theme.DangerColor; return;
			case WorldMapMine.State.ResourceDepleted:
				text = Tr.EntityStatus__ResourceDepleted; color = Theme.DangerColor; return;
			case WorldMapMine.State.NotEnoughWorkers:
				text = Tr.EntityStatus__NoWorkers; color = Theme.DangerColor; return;
			case WorldMapMine.State.NotEnoughUnity:
				text = Tr.EntityStatus__NoUnity; color = Theme.DangerColor; return;
			case WorldMapMine.State.Broken:
				text = Tr.EntityStatus__Broken; color = Theme.DangerColor; return;
			default:
				text = Tr.EntityStatus__Idle; color = Theme.InactiveColor; return;
		}
	}
}

/// <summary>
/// Adds the toolbar button (and F8 hotkey) and shows / hides the window.
/// The toolbar button is only shown once at least one world mine is repaired;
/// F8 always works (then showing the "no mines yet" hint).
/// </summary>
[GlobalDependency(RegistrationMode.AsEverything)]
public class OutpostOverviewWindowController : WindowController<OutpostOverviewWindowView>, IToolbarItemController {

	private readonly WorldMapManager m_worldMapManager;
	private volatile bool m_hasMines;

	/// <summary>
	/// IMPORTANT (0.8.8+): this means "the toolbar button is shown", NOT "the window is open".
	/// We show the button only once the player owns a world mine.
	/// </summary>
	public bool IsVisible => m_hasMines;

	/// <summary>false = the F8 hotkey keeps working even while the toolbar button is hidden.</summary>
	public bool DeactivateShortcutsIfNotVisible => false;

	public event Action<IToolbarItemController> VisibilityChanged;

	public OutpostOverviewWindowController(
		ControllerContext controllerContext,
		ToolbarHud toolbar,
		WorldMapManager worldMapManager,
		IShortcutsRegistry shortcutsRegistry,
		AllImplementationsOf<IMod> allMods,
		IGameLoopEvents gameLoopEvents)
		: base(controllerContext) {
		m_worldMapManager = worldMapManager;

		// Register the F8 hotkey with the game (must happen before the game finishes initializing --
		// constructors of global dependencies run early enough; IMod.Initialize is too early for the
		// UI-side registry). After this it appears in Settings > Controls and can be rebound.
		IMod ownMod = allMods.Implementations.FirstOrDefault(m => m is OutpostOverviewMod);
		if (ownMod != null) {
			shortcutsRegistry.Add(ownMod, ModShortcuts.ToggleWindow);
		} else {
			Log.Warning("OutpostOverview: own mod instance not found -- hotkey not registered");
		}

		toolbar.AddMainMenuButton(
			ModStrings.WindowTitle,
			this,
			"Assets/Unity/UserInterface/Toolbar/WorldMap.svg",
			525f, // sort order: right next to the game's world map button (520)
			ModShortcuts.ToggleWindow); // F8 by default, rebindable in Settings > Controls

		// Check once per sync step whether the player owns a mine yet (cheap) and show/hide
		// the toolbar button accordingly.
		gameLoopEvents.SyncUpdate.AddNonSaveable(this, _ => UpdateButtonVisibility());

		Log.Info("OutpostOverview: toolbar button and hotkey registered");
	}

	private void UpdateButtonVisibility() {
		bool hasMines = m_worldMapManager.Mines.Any(m => m.IsRepaired);
		if (hasMines != m_hasMines) {
			m_hasMines = hasMines;
			VisibilityChanged?.Invoke(this);
		}
	}

	// Opening/closing (Activate/Deactivate), the close button and Esc are handled by the game's
	// WindowController base class.
}
