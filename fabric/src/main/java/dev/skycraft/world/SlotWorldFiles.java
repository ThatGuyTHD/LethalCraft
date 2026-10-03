package dev.skycraft.world;

import java.io.IOException;
import java.nio.file.*;
import java.nio.file.attribute.BasicFileAttributes;

/** Migration never moves or edits the original world; an interrupted copy is not opened. */
public final class SlotWorldFiles {
    private SlotWorldFiles() {}
    public static boolean validName(String name) {
        return name != null && name.matches("[A-Za-z0-9_-]{1,95}");
    }
    public static boolean importLegacy(Path saves, String legacyName, String targetName) throws IOException {
        if (!validName(legacyName) || !validName(targetName) || legacyName.equals(targetName))
            throw new IOException("Invalid Minecraft world name");
        Path root = saves.toAbsolutePath().normalize();
        Path source = root.resolve(legacyName), target = root.resolve(targetName);
        if (Files.exists(target, LinkOption.NOFOLLOW_LINKS)) return false;
        if (!Files.exists(source, LinkOption.NOFOLLOW_LINKS)) return false;
        if (Files.isSymbolicLink(source) || !Files.isRegularFile(source.resolve("level.dat"), LinkOption.NOFOLLOW_LINKS))
            throw new IOException("Legacy Minecraft world is not a regular saved world");
        Path staging = Files.createTempDirectory(root, ".lethalcraft-import-");
        // On failure the partial folder is retained for diagnosis, never mistaken for a world.
        Files.walkFileTree(source, new SimpleFileVisitor<>() {
            @Override public FileVisitResult preVisitDirectory(Path dir, BasicFileAttributes attrs) throws IOException {
                Files.createDirectories(staging.resolve(source.relativize(dir)));
                return FileVisitResult.CONTINUE;
            }
            @Override public FileVisitResult visitFile(Path file, BasicFileAttributes attrs) throws IOException {
                if (!attrs.isRegularFile()) throw new IOException("Unsupported linked file in legacy world: " + file.getFileName());
                if (!file.getFileName().toString().equals("session.lock"))
                    Files.copy(file, staging.resolve(source.relativize(file)), StandardCopyOption.COPY_ATTRIBUTES);
                return FileVisitResult.CONTINUE;
            }
        });
        try { Files.move(staging, target, StandardCopyOption.ATOMIC_MOVE); }
        catch (AtomicMoveNotSupportedException e) { Files.move(staging, target); }
        return true;
    }
}
