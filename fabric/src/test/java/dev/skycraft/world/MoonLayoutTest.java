package dev.skycraft.world;

import java.nio.file.*;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import static org.junit.jupiter.api.Assertions.*;

class MoonLayoutTest {
    @TempDir Path directory;
    @Test void existingBuildsStayOnTheFirstUpgradedMoon() throws Exception {
        Files.writeString(directory.resolve("level.dat"), "existing inventory and world metadata");
        assertEquals(3, MoonLayout.prepare(directory, 3, 13));
        assertEquals(3, MoonLayout.prepare(directory, 7, 13));
        assertEquals("minecraft:overworld", MoonLayout.dimension(3, MoonLayout.read(directory)));
        assertEquals("skycraft:moon_7", MoonLayout.dimension(7, MoonLayout.read(directory)));
        assertEquals("existing inventory and world metadata", Files.readString(directory.resolve("level.dat")));
    }
    @Test void newWorldsUseDistinctMoonDimensions() throws Exception {
        assertEquals(-1, MoonLayout.prepare(directory, 0, 13));
        assertNotEquals(MoonLayout.dimension(0, -1), MoonLayout.dimension(1, -1));
    }
    @Test void invalidMigrationRecordStopsInsteadOfSharingOldBuilds() throws Exception {
        Files.writeString(directory.resolve(MoonLayout.FILE), "1\n999\n");
        assertThrows(java.io.IOException.class, () -> MoonLayout.prepare(directory, 0, 13));
        assertEquals("1\n999\n", Files.readString(directory.resolve(MoonLayout.FILE)));
    }
    @Test void invalidMoonNeverCreatesAMigrationFile() {
        assertThrows(java.io.IOException.class, () -> MoonLayout.prepare(directory, 13, 13));
        assertFalse(Files.exists(directory.resolve(MoonLayout.FILE)));
    }
}
