package dev.skycraft.client;

import dev.skycraft.SkyCraft;
import dev.skycraft.link.SkyLink;
import dev.skycraft.world.SlotWorldFiles;
import net.minecraft.world.level.storage.LevelResource;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.TitleScreen;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.world.Difficulty;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.LevelSettings;
import net.minecraft.world.level.WorldDataConfiguration;
import net.minecraft.world.level.levelgen.WorldOptions;
import net.minecraft.world.level.levelgen.presets.WorldPreset;

/** Opens (or creates) the void "mirror" world automatically once Skyrim is connected. */
public final class MirrorWorld {
	private static final ResourceKey<WorldPreset> PRESET =
		ResourceKey.create(Registries.WORLD_PRESET, Identifier.fromNamespaceAndPath(SkyCraft.MOD_ID, "mirror"));
	private static boolean attempted;
	private static long lastLog;
	private static String worldName = "";
	private static int session = -1, generation = -1, saveAck, error, savingRequest;
	private static java.util.concurrent.CompletableFuture<Void> saving;
	private static int connectPort, retries;
	private static long retryAfter;
	private static @org.jspecify.annotations.Nullable String pendingNote;
	private MirrorWorld() {}
	// Legacy integrations direct players to the native lobby, which owns the connection.
	public static void joinFriend(Minecraft minecraft, String ignored) {
		pendingNote = "Join your friend's Lethal Company lobby to connect both games.";
	}
	public static @org.jspecify.annotations.Nullable String friendAddress(Minecraft minecraft) { return null; }
	private static void leaveWorld(Minecraft minecraft) {
		attempted = false;
		minecraft.disconnectFromWorld(net.minecraft.client.multiplayer.ClientLevel.DEFAULT_QUIT_MESSAGE);
		minecraft.gui.setScreen(new TitleScreen());  // openWhenReady takes it from the title screen
	}

	/** Every client tick: a note for the player once they're in a world again. */
	public static void tick(Minecraft minecraft) {
		if (pendingNote != null && minecraft.player != null) {
			minecraft.gui.hud.getChat().addClientSystemMessage(net.minecraft.network.chat.Component.literal(pendingNote));
			pendingNote = null;
		}
	}

	public static boolean managedWorld(String name) {
		return SlotWorldFiles.validName(name) && (name.equals(SkyCraft.WORLD_NAME) || name.startsWith("LethalCraft_"));
	}

	public static boolean ready(Minecraft minecraft) {
		return loaded(minecraft) && MoonClient.ready(minecraft);
	}
	private static boolean loaded(Minecraft minecraft) {
		if (error != 0 || worldName.isEmpty() || minecraft.player == null || minecraft.level == null) return false;
		var server = minecraft.getSingleplayerServer();
		if (server == null) return SkyClient.sky().networkRole == 2 && mcAddress(minecraft).equals("127.0.0.1:" + connectPort);
		return server.getWorldPath(LevelResource.ROOT).toAbsolutePath().normalize().getFileName().toString().equals(worldName);
	}

	public static void report(Minecraft minecraft, SkyLink.McState state) {
		state.sessionAck = loaded(minecraft) ? session : 0;
		state.moonAck = MoonClient.ack(minecraft);
		state.saveAck = saveAck;
		state.sessionError = error;
		state.serverPort = MultiplayerClient.port(minecraft);
	}
	private static String mcAddress(Minecraft minecraft) {
		var remote = minecraft.getCurrentServer(); return remote == null ? "" : remote.ip;
	}

	private static void saveWhenRequested(Minecraft minecraft, int request) {
		if (saving != null) {
			if (!saving.isDone()) return;
			try { saving.join(); saveAck = savingRequest; }
			catch (RuntimeException e) { error = 2; SkyCraft.LOG.error("LethalCraft: saving world failed", e); }
			saving = null;
		}
		if (request == saveAck || !ready(minecraft)) return;
		var server = minecraft.getSingleplayerServer();
		if (server == null) return;
		savingRequest = request;
		var completion = new java.util.concurrent.CompletableFuture<Void>();
		saving = completion;
		server.execute(() -> {
			try { server.saveEverything(false, true, true); completion.complete(null); }
			catch (Exception e) { completion.completeExceptionally(e); }
		});
	}

	public static void openWhenReady(Minecraft minecraft) {
		var host = SkyClient.sky();
		if (session != host.session || generation != SkyLink.generation() || !worldName.equals(host.worldName) || connectPort != host.connectPort) {
			InputBridge.releaseAll();
			if (minecraft.level != null || minecraft.getSingleplayerServer() != null) leaveWorld(minecraft);
			worldName = host.worldName; session = host.session; generation = SkyLink.generation();
			attempted = false; saveAck = 0; error = 0; saving = null; connectPort = host.connectPort; retries = 0; retryAfter = 0;
			SkyCraft.LOG.info("LethalCraft: switched save session {} to {}", session, worldName.isEmpty() ? "main menu" : worldName);
		}
		if (worldName.isEmpty() || error != 0 || System.currentTimeMillis() < retryAfter) return;
		if (!SlotWorldFiles.validName(worldName)) {
			error = 1; SkyCraft.LOG.error("LethalCraft: rejected unsafe world name"); return;
		}
		saveWhenRequested(minecraft, host.saveRequest);
		// Guests retry their host connection; they never fall back to a private world.
		if (minecraft.gui.screen() instanceof net.minecraft.client.gui.screens.DisconnectedScreen && minecraft.level == null) {
			if (host.networkRole == 2) {
				if (++retries > 3) { error = 3; SkyCraft.LOG.error("LethalCraft: host connection failed after retries; check account sign-in and matching mod versions"); return; }
				attempted = false; retryAfter = System.currentTimeMillis() + 5000; minecraft.gui.setScreen(new TitleScreen()); return;
			}
			attempted = false;
			minecraft.gui.setScreen(new TitleScreen());
			return;
		}
		if (attempted && minecraft.level == null && minecraft.gui.screen() != null && System.currentTimeMillis() - lastLog > 5000) {
			lastLog = System.currentTimeMillis();
			SkyCraft.LOG.info("SkyCraft: still not in the mirror world; current screen {}", minecraft.gui.screen().getClass().getName());
		}
		if (attempted || minecraft.level != null || minecraft.gui.overlay() != null) {
			return;
		}
		// Wait for the menu to settle on the title screen; skip any first-launch prompts in front of it.
		if (!(minecraft.gui.screen() instanceof TitleScreen)) {
			if (minecraft.gui.screen() != null && System.currentTimeMillis() - lastLog > 5000) {
				lastLog = System.currentTimeMillis();
				SkyCraft.LOG.info("SkyCraft: waiting on screen {} before opening the mirror world", minecraft.gui.screen().getClass().getName());
			}
			if (minecraft.gui.screen() == null || minecraft.gui.screen().getClass().getName().contains("Onboarding")) {
				minecraft.gui.setScreen(new TitleScreen());
			}
			return;
		}
		TitleScreen title = (TitleScreen) minecraft.gui.screen();
		attempted = true;
		// The native lobby provides a local endpoint for its authenticated Minecraft tunnel.
		String join = host.networkRole == 2 && connectPort > 0 ? "127.0.0.1:" + connectPort : null;
		if (host.networkRole == 2 && join == null) { attempted = false; return; }
		if (join != null) {
			SkyCraft.LOG.info("SkyCraft: joining {}", join);
			pendingNote = "Connected to the crew's shared Minecraft world.";
			net.minecraft.client.gui.screens.ConnectScreen.startConnecting(title, minecraft, net.minecraft.client.multiplayer.resolver.ServerAddress.parseString(join),
				new net.minecraft.client.multiplayer.ServerData("SkyCraft", join, net.minecraft.client.multiplayer.ServerData.Type.OTHER), false, null);
			return;
		}
		if ((host.saveFlags & 1) != 0) {
			try {
				if (SlotWorldFiles.importLegacy(minecraft.gameDirectory.toPath().resolve("saves"), SkyCraft.WORLD_NAME, worldName))
					SkyCraft.LOG.info("LethalCraft: preserved legacy world and copied it to {}", worldName);
			} catch (java.io.IOException e) {
				error = 1; SkyCraft.LOG.error("LethalCraft: legacy import failed; original preserved, not opening an empty replacement", e); return;
			}
		}
		boolean existing = minecraft.getLevelSource().levelExists(worldName);
		try {
			dev.skycraft.world.MoonLayout.prepare(minecraft.gameDirectory.toPath().resolve("saves").resolve(worldName), host.moonId, host.moonCount);
		} catch (java.io.IOException e) {
			error = 4; SkyCraft.LOG.error("LethalCraft: cannot prepare moon dimensions; existing world preserved", e); return;
		}
		if (existing) {
			SkyCraft.LOG.info("SkyCraft: opening mirror world");
			minecraft.createWorldOpenFlows().openWorld(worldName, () -> minecraft.gui.setScreen(title));
			return;
		}
		SkyCraft.LOG.info("SkyCraft: creating mirror world");
		LevelSettings settings = new LevelSettings(
			worldName,
			GameType.SURVIVAL,
			new LevelSettings.DifficultySettings(Difficulty.NORMAL, false, false),
			true,
			WorldDataConfiguration.DEFAULT
		);
		minecraft.createWorldOpenFlows().createFreshLevel(
			worldName,
			settings,
			new WorldOptions(0L, false, false),
			registries -> registries.lookupOrThrow(Registries.WORLD_PRESET).getOrThrow(PRESET).value().createWorldDimensions(),
			title
		);
	}
}
