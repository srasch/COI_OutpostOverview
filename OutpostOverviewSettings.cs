using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mafi;
using Mafi.Core.Mods;
using Mafi.Logging;

namespace OutpostOverview;

/// <summary>Which column the mine list is sorted by. Manual = the player's drag-and-drop order.</summary>
public enum MineSortColumn {
	Manual,
	Name,
	Fill,
	Reserve,
	Years,
}

/// <summary>
/// Automatic pause/resume limits of one mine, in slider steps (0..STEPS, one step = 5 %).
/// Min = resume when the storage drops below it (0 = off).
/// Max = pause when the storage reaches it (STEPS = off).
/// Stored in the settings file as percent, so changing STEPS later keeps saved limits intact.
/// </summary>
public readonly struct MineLimits : IEquatable<MineLimits> {
	public const int STEPS = 20;
	public const int PERCENT_PER_STEP = 100 / STEPS;
	public static readonly MineLimits Off = new MineLimits(0, STEPS);

	public readonly int Min;
	public readonly int Max;

	public MineLimits(int min, int max) {
		Min = Math.Max(0, Math.Min(STEPS, min));
		Max = Math.Max(0, Math.Min(STEPS, max));
	}

	public bool HasMin => Min > 0;
	public bool HasMax => Max < STEPS;
	public bool IsActive => HasMin || HasMax;

	public int MinPercent => Min * PERCENT_PER_STEP;
	public int MaxPercent => Max * PERCENT_PER_STEP;

	/// <summary>Creates limits from percent values (rounded to the nearest step).</summary>
	public static MineLimits FromPercent(int minPercent, int maxPercent) {
		return new MineLimits(
			(int)Math.Round(minPercent / (double)PERCENT_PER_STEP),
			(int)Math.Round(maxPercent / (double)PERCENT_PER_STEP));
	}

	public bool Equals(MineLimits other) => Min == other.Min && Max == other.Max;
	public override bool Equals(object obj) => obj is MineLimits other && Equals(other);
	public override int GetHashCode() => Min * 31 + Max;
}

/// <summary>
/// Remembers the window settings (sort column/direction, reserve toggle, manual order) per save game.
///
/// Storage: a small text file per game in the mod folder, e.g.
/// %APPDATA%\Captain of Industry\Mods\OutpostOverview\SaveData\1401930929274373920.txt
/// The number is the game's "Game ID" (stays the same across all saves of one playthrough).
/// Nothing is written into the save game itself, so the mod stays safe to remove.
///
/// File format (one "key=value" per line, unknown keys are ignored):
///   sort=Fill
///   desc=1
///   reserves=1
///   status=1
///   order=12,34,56      (entity ids of the mines in manual order)
///   limits_pct=12:30:90,34:0:85  (entity id : resume below % : pause from %; 0 / 100 = off)
/// </summary>
[GlobalDependency(RegistrationMode.AsSelf)]
public class OutpostOverviewSettings {

	private readonly IGameIdProvider m_gameIdProvider;
	private readonly string m_dataDir;
	private bool m_loaded;

	public MineSortColumn SortColumn { get; private set; } = MineSortColumn.Manual;
	public bool SortDescending { get; private set; }
	public bool ShowReserves { get; private set; } = true;
	public bool ShowStatus { get; private set; } = true;

	/// <summary>Entity ids in the player's manual order. Mines not listed are appended at the end.</summary>
	public IReadOnlyList<int> ManualOrder => m_manualOrder;
	private List<int> m_manualOrder = new List<int>();

	private readonly Dictionary<int, MineLimits> m_limits = new Dictionary<int, MineLimits>();

	/// <summary>True once the settings of the current game have been read (or found missing).</summary>
	public bool IsLoaded => m_loaded;

	public OutpostOverviewSettings(IGameIdProvider gameIdProvider, AllImplementationsOf<IMod> allMods) {
		m_gameIdProvider = gameIdProvider;
		IMod ownMod = allMods.Implementations.FirstOrDefault(m => m is OutpostOverviewMod);
		m_dataDir = ownMod != null ? Path.Combine(ownMod.Manifest.RootDirectoryPath, "SaveData") : null;
	}

	/// <summary>
	/// Loads the settings of the current game on first use. Must not be called before the game is
	/// initialized (the Game ID is only known then) -- the window calls it from its sync updates.
	/// </summary>
	public void EnsureLoaded() {
		if (m_loaded || m_gameIdProvider.GameId == 0) return;
		m_loaded = true;
		string path = GetFilePath();
		if (path == null || !File.Exists(path)) return;
		try {
			foreach (string line in File.ReadAllLines(path)) {
				int eq = line.IndexOf('=');
				if (eq <= 0) continue;
				string key = line.Substring(0, eq).Trim();
				string value = line.Substring(eq + 1).Trim();
				switch (key) {
					case "sort":
						if (Enum.TryParse(value, out MineSortColumn col)) SortColumn = col;
						break;
					case "desc":
						SortDescending = value == "1";
						break;
					case "reserves":
						ShowReserves = value != "0";
						break;
					case "status":
						ShowStatus = value != "0";
						break;
					case "order":
						m_manualOrder = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
							.Select(s => int.TryParse(s, out int id) ? id : 0)
							.Where(id => id > 0)
							.ToList();
						break;
					case "limits_pct":
						foreach (string entry in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) {
							string[] parts = entry.Split(':');
							if (parts.Length == 3
								&& int.TryParse(parts[0], out int id)
								&& int.TryParse(parts[1], out int min)
								&& int.TryParse(parts[2], out int max)) {
								var limits = MineLimits.FromPercent(min, max);
								if (id > 0 && limits.IsActive) m_limits[id] = limits;
							}
						}
						break;
				}
			}
		} catch (Exception ex) {
			Log.Warning("OutpostOverview: could not read settings file '" + path + "': " + ex.Message);
		}
	}

	public void SetSort(MineSortColumn column, bool descending) {
		SortColumn = column;
		SortDescending = descending;
		Save();
	}

	public void SetShowReserves(bool show) {
		ShowReserves = show;
		Save();
	}

	public void SetShowStatus(bool show) {
		ShowStatus = show;
		Save();
	}

	/// <summary>Stores a new manual order and switches the list to manual sorting.</summary>
	public void SetManualOrder(IEnumerable<int> entityIds) {
		m_manualOrder = entityIds.ToList();
		SortColumn = MineSortColumn.Manual;
		SortDescending = false;
		Save();
	}

	public MineLimits GetLimits(int entityId) {
		return m_limits.TryGetValue(entityId, out MineLimits limits) ? limits : MineLimits.Off;
	}

	public void SetLimits(int entityId, MineLimits limits) {
		if (limits.IsActive) {
			m_limits[entityId] = limits;
		} else {
			m_limits.Remove(entityId);
		}
		Save();
	}

	private string GetFilePath() {
		if (m_dataDir == null || m_gameIdProvider.GameId == 0) return null;
		return Path.Combine(m_dataDir, m_gameIdProvider.GameId + ".txt");
	}

	private void Save() {
		string path = GetFilePath();
		if (path == null) return;
		try {
			Directory.CreateDirectory(m_dataDir);
			File.WriteAllLines(path, new[] {
				"sort=" + SortColumn,
				"desc=" + (SortDescending ? "1" : "0"),
				"reserves=" + (ShowReserves ? "1" : "0"),
				"status=" + (ShowStatus ? "1" : "0"),
				"order=" + string.Join(",", m_manualOrder),
				"limits_pct=" + string.Join(",", m_limits.Select(kv => $"{kv.Key}:{kv.Value.MinPercent}:{kv.Value.MaxPercent}")),
			});
		} catch (Exception ex) {
			Log.Warning("OutpostOverview: could not write settings file '" + path + "': " + ex.Message);
		}
	}
}
