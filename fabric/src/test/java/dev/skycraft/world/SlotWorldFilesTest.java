package dev.skycraft.world;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import java.nio.file.*;
import static org.junit.jupiter.api.Assertions.*;

class SlotWorldFilesTest {
    @TempDir Path saves;
    private Path legacy() throws Exception {
        Path source = Files.createDirectories(saves.resolve("LethalCraft"));
        Files.writeString(source.resolve("level.dat"), "player inventory and world state");
        Files.writeString(source.resolve("session.lock"), "old lock");
        Files.createDirectories(source.resolve("region"));
        Files.writeString(source.resolve("region/r.0.0.mca"), "original builds");
        return source;
    }
    @Test void copiesInventoryAndBuildsWithoutChangingOriginalOrCopyingLock() throws Exception {
        Path source = legacy();
        assertTrue(SlotWorldFiles.importLegacy(saves, "LethalCraft", "LethalCraft_A_123"));
        assertEquals("player inventory and world state", Files.readString(saves.resolve("LethalCraft_A_123/level.dat")));
        assertEquals("original builds", Files.readString(saves.resolve("LethalCraft_A_123/region/r.0.0.mca")));
        assertEquals("original builds", Files.readString(source.resolve("region/r.0.0.mca")));
        assertFalse(Files.exists(saves.resolve("LethalCraft_A_123/session.lock")));
        assertTrue(Files.exists(source.resolve("session.lock")));
    }
    @Test void neverOverwritesExistingSlot() throws Exception {
        legacy(); Path target = Files.createDirectory(saves.resolve("LethalCraft_B"));
        Files.writeString(target.resolve("level.dat"), "slot B inventory");
        assertFalse(SlotWorldFiles.importLegacy(saves, "LethalCraft", "LethalCraft_B"));
        assertEquals("slot B inventory", Files.readString(target.resolve("level.dat")));
    }
    @Test void rejectsTraversalAndMissingWorldMetadata() throws Exception {
        assertFalse(SlotWorldFiles.validName("../other"));
        assertThrows(java.io.IOException.class, () -> SlotWorldFiles.importLegacy(saves, "LethalCraft", "../other"));
        Files.createDirectory(saves.resolve("LethalCraft"));
        assertThrows(java.io.IOException.class, () -> SlotWorldFiles.importLegacy(saves, "LethalCraft", "LethalCraft_A"));
        assertFalse(Files.exists(saves.resolve("LethalCraft_A")));
    }
    @Test void noLegacyWorldMeansNormalFreshCreation() throws Exception {
        assertFalse(SlotWorldFiles.importLegacy(saves, "LethalCraft", "LethalCraft_A"));
        assertFalse(Files.exists(saves.resolve("LethalCraft_A")));
    }
}
