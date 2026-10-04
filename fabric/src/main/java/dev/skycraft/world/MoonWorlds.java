package dev.skycraft.world;

import dev.skycraft.SkyCraft;
import dev.skycraft.link.SkyLink;
import dev.skycraft.net.SkyNet;
import java.util.*;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerLifecycleEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.fabricmc.fabric.api.networking.v1.ServerPlayNetworking;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.dimension.LevelStem;
import net.minecraft.world.level.portal.TeleportTransition;
import net.minecraft.world.level.storage.LevelResource;
import net.minecraft.world.phys.Vec3;

/** One integrated server, one inventory per player, separate saved dimensions for native moons. */
public final class MoonWorlds {
    private static MinecraftServer bound;
    private static int legacy = -1;
    private static final Map<net.minecraft.server.level.ServerPlayer, Integer> notified = new IdentityHashMap<>();
    private static ServerLevel active;
    private MoonWorlds() {}

    public static Map<ResourceKey<LevelStem>, LevelStem> dimensions(Map<ResourceKey<LevelStem>, LevelStem> source) {
        var host = new SkyLink.SkyState();
        var overworld = source.get(LevelStem.OVERWORLD);
        if (!SkyLink.active() || !SkyLink.readSkyState(host) || host.networkRole == 2 || host.worldName.isEmpty() || overworld == null
            || !overworld.type().is(Identifier.fromNamespaceAndPath("skycraft", "mirror"))) return source;
        if (host.moonCount < 1 || host.moonCount > MoonLayout.MAX_MOONS) throw new IllegalStateException("Invalid moon count");
        var expanded = new LinkedHashMap<>(source);
        for (int moon = 0; moon < host.moonCount; moon++) {
            var key = ResourceKey.create(Registries.LEVEL_STEM, Identifier.fromNamespaceAndPath("skycraft", "moon_" + moon));
            expanded.putIfAbsent(key, new LevelStem(overworld.type(), overworld.generator()));
        }
        return Map.copyOf(expanded);
    }

    public static void init() {
        ServerLifecycleEvents.SERVER_STARTED.register(server -> {
            bound = null; active = null; notified.clear();
            try {
                var root = server.getWorldPath(LevelResource.ROOT);
                if (java.nio.file.Files.exists(root.resolve(MoonLayout.FILE))) {
                    legacy = MoonLayout.read(root); bound = server;
                }
            } catch (java.io.IOException e) { throw new IllegalStateException("Cannot read moon layout", e); }
        });
        ServerLifecycleEvents.SERVER_STOPPED.register(server -> { bound = null; active = null; notified.clear(); });
        net.fabricmc.fabric.api.networking.v1.ServerPlayConnectionEvents.DISCONNECT.register((handler, server) -> notified.remove(handler.getPlayer()));
        ServerTickEvents.START_SERVER_TICK.register(MoonWorlds::tick);
    }

    public static ServerLevel active(MinecraftServer server) { return server == bound ? active : null; }

    private static void tick(MinecraftServer server) {
        if (server != bound || !SkyLink.active()) return;
        var host = new SkyLink.SkyState();
        if (!SkyLink.readSkyState(host) || host.worldName.isEmpty() || host.moonId < 0 || host.moonId >= host.moonCount) return;
        if (!server.getWorldPath(LevelResource.ROOT).toAbsolutePath().normalize().getFileName().toString().equals(host.worldName)) return;
        String dimension = MoonLayout.dimension(host.moonId, legacy);
        var key = ResourceKey.create(Registries.DIMENSION, Identifier.parse(dimension));
        var target = server.getLevel(key);
        if (target == null) return; // Client remains suspended instead of putting builds into another moon.
        active = target;
        notified.keySet().removeIf(player -> !server.getPlayerList().getPlayers().contains(player));
        for (var player : List.copyOf(server.getPlayerList().getPlayers())) {
            if (player.level() != target) {
                player.closeContainer();
                player.teleport(new TeleportTransition(target, player.position(), Vec3.ZERO, player.getYRot(), player.getXRot(), TeleportTransition.DO_NOTHING));
                player.resetFallDistance();
            }
            // Rejoining/respawning creates a new player object even when the UUID is unchanged.
            // Re-send the tiny state periodically too, so a client reset cannot leave it suspended.
            if ((!Objects.equals(notified.get(player), host.moonId) || server.getTickCount() % 40 == 0) && ServerPlayNetworking.canSend(player, SkyNet.Moon.TYPE)) {
                ServerPlayNetworking.send(player, new SkyNet.Moon(host.moonId, dimension));
                notified.put(player, host.moonId);
            }
        }
    }
}
