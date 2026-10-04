package dev.skycraft.world;

import java.io.IOException;
import java.nio.file.*;

/** Persist the one moon that inherits pre-dimension builds. Original region files stay in place. */
public final class MoonLayout {
    public static final String FILE = "lethalcraft-moons.txt";
    public static final int MAX_MOONS = 128;
    private MoonLayout() {}
    public static int prepare(Path world, int moon, int count) throws IOException {
        if (count < 1 || count > MAX_MOONS || moon < 0 || moon >= count) throw new IOException("Invalid native moon list");
        Path marker = world.resolve(FILE);
        if (Files.exists(marker)) return read(world);
        int legacy = Files.exists(world.resolve("level.dat")) ? moon : -1;
        Files.createDirectories(world);
        // Creation is atomic and never replaces an existing migration decision.
        Path temporary = Files.createTempFile(world, ".moon-layout-", ".tmp");
        try {
            Files.writeString(temporary, "1\n" + legacy + "\n");
            Files.move(temporary, marker, StandardCopyOption.ATOMIC_MOVE);
        } finally { Files.deleteIfExists(temporary); }
        return legacy;
    }
    public static int read(Path world) throws IOException {
        var lines = Files.readAllLines(world.resolve(FILE));
        try {
            if (lines.size() != 2 || !lines.getFirst().equals("1")) throw new NumberFormatException();
            int legacy = Integer.parseInt(lines.get(1));
            if (legacy < -1 || legacy >= MAX_MOONS) throw new NumberFormatException();
            return legacy;
        } catch (NumberFormatException e) { throw new IOException("Invalid moon migration record; restore its backup", e); }
    }
    public static String dimension(int moon, int legacy) {
        if (moon < 0 || moon >= MAX_MOONS) throw new IllegalArgumentException("Invalid moon");
        return moon == legacy ? "minecraft:overworld" : "skycraft:moon_" + moon;
    }
}
