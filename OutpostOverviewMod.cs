using Mafi;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Core.Game;
using Mafi.Core.Mods;
using Mafi.Core.Prototypes;

namespace OutpostOverview;

/// <summary>
/// Main mod entry point. The actual window lives in OutpostOverviewWindow.cs
/// (OutpostOverviewWindowView + OutpostOverviewWindowController); both classes are
/// picked up automatically by the game via [GlobalDependency], so nothing needs to be
/// wired up here.
/// </summary>
public sealed class OutpostOverviewMod : IMod {

	public string Name => "OutpostOverview";
	public int Version => 1;
	public bool IsUiOnly => false;

	public ModManifest Manifest { get; }
	public Option<IConfig> ModConfig => Option<IConfig>.None;
	public ModJsonConfig JsonConfig { get; }

	public OutpostOverviewMod(ModManifest manifest) {
		Manifest = manifest;
		JsonConfig = new ModJsonConfig(this); // REQUIRED -- null crashes the game
		// Register the mod's texts now, while the game is loading the mod,
		// so its translation files (Translations\*.json) are applied to them.
		ModStrings.EnsureRegistered();
		ModShortcuts.EnsureCreated();
		Log.Info("OutpostOverview: constructed");
	}

	public void Initialize(DependencyResolver resolver, bool gameWasLoaded) {
		Log.Info("OutpostOverview: Initialize called, gameWasLoaded=" + gameWasLoaded);
		// Note: the hotkey is registered in OutpostOverviewWindowController's constructor, not here --
		// at this point the UI-side IShortcutsRegistry is not created yet.
	}

	public void ChangeConfigs(Lyst<IConfig> configs) { }

	public void RegisterPrototypes(ProtoRegistrator registrator) { }

	public void RegisterDependencies(
		DependencyResolverBuilder depBuilder, ProtosDb protosDb, bool wasLoaded) {
	}

	public void EarlyInit(DependencyResolver resolver) { }

	public void MigrateJsonConfig(VersionSlim savedVersion, Dict<string, object> savedValues) { }

	public void Dispose() { }
}
