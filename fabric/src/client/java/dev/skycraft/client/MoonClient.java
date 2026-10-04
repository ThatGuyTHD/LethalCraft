package dev.skycraft.client;

import net.minecraft.client.Minecraft;

public final class MoonClient {
    private static int moon = -1;
    private static String dimension = "";
    private MoonClient() {}
    public static void accept(int id, String key) {
        if (id < 0 || id >= dev.skycraft.world.MoonLayout.MAX_MOONS ||
            !(key.equals("minecraft:overworld") || key.equals("skycraft:moon_" + id))) return;
        moon = id; dimension = key;
    }
    public static void clear() { moon = -1; dimension = ""; }
    public static boolean ready(Minecraft mc) {
        return moon == SkyClient.sky().moonId && mc.player != null && mc.level != null && mc.level.dimension().identifier().toString().equals(dimension);
    }
    public static int ack(Minecraft mc) { return ready(mc) ? moon + 1 : 0; }
}
