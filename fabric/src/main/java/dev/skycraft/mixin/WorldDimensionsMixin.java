package dev.skycraft.mixin;

import dev.skycraft.world.MoonWorlds;
import java.util.Map;
import net.minecraft.resources.ResourceKey;
import net.minecraft.world.level.dimension.LevelStem;
import net.minecraft.world.level.levelgen.WorldDimensions;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.ModifyVariable;

@Mixin(WorldDimensions.class)
public abstract class WorldDimensionsMixin {
    @ModifyVariable(method = "<init>(Ljava/util/Map;)V", at = @At("HEAD"), argsOnly = true)
    private static Map<ResourceKey<LevelStem>, LevelStem> lethalcraft$moons(Map<ResourceKey<LevelStem>, LevelStem> dimensions) {
        return MoonWorlds.dimensions(dimensions);
    }
}
