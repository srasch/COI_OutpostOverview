using System.Collections.Generic;
using Mafi;
using Mafi.Core.Entities;
using Mafi.Core.GameLoop;
using Mafi.Core.Input;
using Mafi.Core.World;
using Mafi.Core.World.Entities;

namespace OutpostOverview;

/// <summary>
/// Automatic pause / resume of world mines based on their storage fill level
/// (limits set with the sliders in the window, see MineLimits / OutpostOverviewSettings).
///
/// Runs on every sync update -- also while the window is closed.
///
/// Behavior: the mod only acts at the moment a limit is crossed ("edge triggered"):
///   - storage reaches the pause limit (red)        -> pause the mine (if it is running)
///   - storage drops below the resume limit (green) -> resume the mine (if it is paused)
/// In between, nothing happens, so pausing/resuming by hand is respected until the next crossing.
/// Changing a mine's limits counts as a fresh start: if the mine is already beyond a limit,
/// the action happens right away.
///
/// Pausing/resuming uses the same ToggleEnabledCmd as the game's own pause button.
/// </summary>
[GlobalDependency(RegistrationMode.AsSelf)]
public class MineAutoPauser {

	private enum Zone { Below, Between, Above }

	/// <summary>Last seen zone per mine, together with the limits it was evaluated against.</summary>
	private readonly Dictionary<int, KeyValuePair<MineLimits, Zone>> m_lastZone =
		new Dictionary<int, KeyValuePair<MineLimits, Zone>>();

	private readonly WorldMapManager m_worldMapManager;
	private readonly IInputScheduler m_inputScheduler;
	private readonly OutpostOverviewSettings m_settings;

	public MineAutoPauser(
		WorldMapManager worldMapManager,
		IInputScheduler inputScheduler,
		OutpostOverviewSettings settings,
		IGameLoopEvents gameLoopEvents) {
		m_worldMapManager = worldMapManager;
		m_inputScheduler = inputScheduler;
		m_settings = settings;
		gameLoopEvents.SyncUpdate.AddNonSaveable(this, _ => Update());
	}

	private void Update() {
		m_settings.EnsureLoaded();
		if (!m_settings.IsLoaded) return;

		foreach (WorldMapMine mine in m_worldMapManager.Mines) {
			int id = mine.Id.Value;
			MineLimits limits = m_settings.GetLimits(id);
			if (!mine.IsRepaired || !limits.IsActive) {
				m_lastZone.Remove(id);
				continue;
			}

			long capacity = mine.Buffer.Capacity.Value;
			if (capacity <= 0) continue;
			long quantity = mine.Buffer.Quantity.Value;

			// Compare "quantity / capacity" with "step / STEPS" without rounding errors.
			Zone zone;
			if (limits.HasMax && quantity * MineLimits.STEPS >= limits.Max * capacity) {
				zone = Zone.Above;
			} else if (limits.HasMin && quantity * MineLimits.STEPS < limits.Min * capacity) {
				zone = Zone.Below;
			} else {
				zone = Zone.Between;
			}

			if (m_lastZone.TryGetValue(id, out var last) && last.Key.Equals(limits) && last.Value == zone) {
				continue; // nothing crossed
			}
			m_lastZone[id] = new KeyValuePair<MineLimits, Zone>(limits, zone);

			if (zone == Zone.Above && !mine.IsPaused) {
				m_inputScheduler.ScheduleInputCmd(new ToggleEnabledCmd(mine.Id));
				Log.Info($"OutpostOverview: auto-paused mine {id} ({mine.Product.Id}), storage {quantity}/{capacity}");
			} else if (zone == Zone.Below && mine.IsPaused) {
				m_inputScheduler.ScheduleInputCmd(new ToggleEnabledCmd(mine.Id));
				Log.Info($"OutpostOverview: auto-resumed mine {id} ({mine.Product.Id}), storage {quantity}/{capacity}");
			}
		}
	}
}
