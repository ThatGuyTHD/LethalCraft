package dev.skycraft.client;

import dev.skycraft.SkyCraft;
import dev.skycraft.net.SkyNet;
import java.net.InetAddress;
import java.net.ServerSocket;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import net.fabricmc.fabric.api.client.networking.v1.ClientPlayNetworking;
import net.minecraft.client.Minecraft;
import net.minecraft.client.server.IntegratedServer;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.player.Player;

public final class MultiplayerClient {
    private static final Map<UUID, Boolean> visible = new ConcurrentHashMap<>();
    private static IntegratedServer published;
    private static int port;
    private static long nextMode, nextPublish;
    private MultiplayerClient() {}
    public static void view(UUID player, boolean active) { visible.put(player, active); }
    public static void clear() { visible.clear(); nextMode = 0; }
    public static boolean draw(Entity entity) {
        return !(entity instanceof Player) || entity == Minecraft.getInstance().player || visible.getOrDefault(entity.getUUID(), false);
    }
    public static int port(Minecraft mc) { return mc.getSingleplayerServer() == published && published != null && published.isPublished() ? port : 0; }
    public static void tick(Minecraft mc) {
        var host = SkyClient.sky();
        if (!MirrorWorld.ready(mc)) return;
        if (host.networkRole == 1 && port(mc) == 0 && System.currentTimeMillis() >= nextPublish) {
            nextPublish = System.currentTimeMillis() + 5000;
            var server = mc.getSingleplayerServer();
            if (server != null) try {
                if (server.isPublished()) server.unpublishServer();
                int chosen;
                try (var reserved = new ServerSocket(0, 1, InetAddress.getLoopbackAddress())) { chosen = reserved.getLocalPort(); }
                // Normal Minecraft account verification remains enabled. The listener mixin binds loopback only.
                boolean authenticated = !(net.fabricmc.loader.api.FabricLoader.getInstance().isDevelopmentEnvironment()
                    && Boolean.getBoolean("lethalcraft.verify") && Boolean.getBoolean("lethalcraft.testOffline"));
                server.setUsesAuthentication(authenticated);
                if (server.publishServer(net.minecraft.server.MinecraftServer.MultiplayerScope.LAN, chosen)) {
                    published = server; port = chosen;
                    SkyCraft.LOG.info("LethalCraft: Minecraft relay endpoint ready on loopback port {} (authentication={})", port, authenticated);
                }
            } catch (Exception e) { SkyCraft.LOG.error("LethalCraft: cannot start shared Minecraft world", e); }
        }
        if (System.currentTimeMillis() >= nextMode && ClientPlayNetworking.canSend(SkyNet.Presentation.TYPE)) {
            nextMode = System.currentTimeMillis() + 250;
            ClientPlayNetworking.send(new SkyNet.Presentation(host.inGame() && !host.menuOpen() && !host.loading()));
        }
    }
}
